using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 급강하 (Air 역할). 순항 고도에서 플레이어 쪽으로 포물선을 그리며 내려가 스치고, 반대편으로 직진 이탈한다.
    /// 강하 중 몸은 포물선 접선 방향으로 기울고, 이탈하면 수평으로 돌아온다.
    /// 스칠 때 가까우면 1회 피해. 끝나면 Chase 에서 선회 시간과 고도 회복이 끝날 때까지 다시 공격하지 않는다.
    /// 추적 이동은 하지 않는다 (Chase 의 MeleePolicy + FlyingMoveExecutor 가 맡는다).
    /// </summary>
    public sealed class DiveAttack : MonsterAttack
    {
        // 바로 머리 위에서 시작할 때 쓰는 최소 시작 거리 (m). 0 나눗셈 방지와 최대 위치 보정량. 랜덤 보정한 급강하 시작 거리의 하한이기도 하다
        private const float MinStartDistance = 0.5f;

        /// <summary>급강하 진행 단계. Diving → Leaving</summary>
        private enum DiveStage { Diving, Leaving }

        private readonly MonsterMovement movement;
        private readonly DiveSettings settings;
        private readonly FlyingMoveExecutor flyer;
        private readonly float entryDistance; // m, 이 개체의 급강하 시작 거리. 생성 때 랜덤 보정을 더해 고정한다

        // ── 이번 주기 (OnWindup 에서 초기화) ──
        private DiveStage stage;
        private Vector3 diveCenter;        // P, 예고 시작 때 플레이어 위치 (월드)
        private Vector3 diveDirection;     // ĥ, 몬스터 → P 수평 단위 방향
        private float startOffset;         // s₀ (m, 음수). 시작 위치의 ĥ 축 좌표
        private float startHeight;         // y₀ (월드 y, m). 시작 높이
        private float travelled;           // s (m). 지금 위치의 ĥ 축 좌표, s₀ 에서 −s₀ 까지
        private bool hasHit;               // 이번 주기에 피해를 줬나 (주기당 1회)

        // ── 재정비 ──
        private float leaveEndTime = float.NegativeInfinity; // 이탈을 마친 시각 (s)

        public override AfterAttack After => AfterAttack.RepositionInChase;
        public override float Range => entryDistance;
        public override bool UsesMoveAnimation => true;
        public override float BodyMoveSpeed => settings.DiveSpeed;

        /// <summary>
        /// MonsterAttackFactory 가 Air 역할에 만든다. 급강하 시작 거리는 여기서 개체별 랜덤 보정을 더해 1회 고정한다.
        /// MonsterController 가 Start·Respawn 마다 공격을 새로 만드므로 풀 재사용 때 다시 뽑힌다.
        /// </summary>
        /// <param name="spec">공격 값 묶음. Range = 스침 피해 거리(3D)</param>
        /// <param name="body">공격자 몸체</param>
        /// <param name="findDamageable">대상에서 피해를 줄 IDamageable 을 찾는 방법</param>
        /// <param name="movement">몸을 직접 옮기는 이동 컴포넌트 (MoveDuringAttack). 몸 기울기 회전도 이 컴포넌트의 Transform 에 쓴다</param>
        /// <param name="settings">급강하 프리팹의 튜닝 값</param>
        /// <param name="flyer">비행 실행기. 재정비 판정에 순항 고도를 읽는다</param>
        /// <exception cref="ArgumentNullException">movement, settings, flyer 중 하나가 null 일 때 (프리팹에 DiveSettings·FlyingMoveExecutor 가 없을 때 등). body 는 기반 생성자가 던진다</exception>
        public DiveAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable,
                          MonsterMovement movement, DiveSettings settings, FlyingMoveExecutor flyer)
            : base(spec, body, findDamageable)
        {
            if (movement == null) throw new ArgumentNullException(nameof(movement));
            if (settings == null) throw new ArgumentNullException(nameof(settings), "공중 몬스터 프리팹에 DiveSettings 가 없다");
            if (flyer == null) throw new ArgumentNullException(nameof(flyer), "공중 몬스터 프리팹에 FlyingMoveExecutor 가 없다");

            this.movement = movement;
            this.settings = settings;
            this.flyer = flyer;

            float randomRange = settings.EntryDistanceRandomRange;
            float randomOffset = UnityEngine.Random.Range(-randomRange, randomRange);
            entryDistance = Mathf.Max(MinStartDistance, settings.EntryDistance + randomOffset);
        }

        /// <summary>
        /// 다시 급강하해도 되는가. 이탈을 마친 뒤 선회 시간이 지났고 순항 고도를 회복했으면 true.
        /// ChaseState 가 매 틱 부른다. 거리 이탈은 TickLeaving 의 종료 조건이 이미 보장한다.
        /// </summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <returns>재공격 가능하면 true</returns>
        public override bool IsRepositioned(IPlayerTarget target, float now)
        {
            bool turnedAwayLongEnough = now - leaveEndTime >= settings.TurnAwayTime;
            bool isAtCruiseAltitude = Mathf.Abs(Body.Position.y - flyer.CruiseAltitude) <= settings.AltitudeTolerance;
            return turnedAwayLongEnough && isAtCruiseAltitude;
        }

        /// <summary>
        /// 급강하 높이 함수. ĥ 축 좌표 s 에서의 월드 높이. 시작(s₀)과 출구(−s₀)는 y₀, 중앙(0)은 yMin 인 포물선이다.
        /// </summary>
        /// <param name="s">지금 ĥ 축 좌표 (m)</param>
        /// <param name="startOffset">s₀ (m, 0 이 아니어야 한다)</param>
        /// <param name="startHeight">y₀ 시작 높이 (월드 y, m)</param>
        /// <param name="minHeight">yMin 최저 높이 (월드 y, m)</param>
        /// <returns>yMin + (y₀ − yMin)·(s/s₀)²</returns>
        public static float DiveHeight(float s, float startOffset, float startHeight, float minHeight)
        {
            float ratio = s / startOffset;
            return minHeight + (startHeight - minHeight) * ratio * ratio;
        }

        /// <summary>
        /// 급강하 기울기. DiveHeight 를 s 로 미분한 값으로, 몸을 포물선 접선 방향으로 기울일 때 TickDiving 이 쓴다.
        /// 시작 쪽은 음수(내려감), 중앙은 0(수평), 출구 쪽은 양수(올라감)다.
        /// </summary>
        /// <param name="s">지금 ĥ 축 좌표 (m)</param>
        /// <param name="startOffset">s₀ (m, 0 이 아니어야 한다)</param>
        /// <param name="startHeight">y₀ 시작 높이 (월드 y, m)</param>
        /// <param name="minHeight">yMin 최저 높이 (월드 y, m)</param>
        /// <returns>2·(y₀ − yMin)·s / s₀². 높이 변화 m / ĥ 축 이동 m</returns>
        public static float DiveSlope(float s, float startOffset, float startHeight, float minHeight)
        {
            return 2f * (startHeight - minHeight) * s / (startOffset * startOffset);
        }

        /// <summary>
        /// 예고 시작: 급강하 중심 P, 방향 ĥ, 시작 좌표 s₀, 시작 높이 y₀ 를 고정한다. 플레이어 쪽 회전은 기반 Tick 이 예고 내내 한다.
        /// 바로 머리 위(수평 0.5m 미만)면 ĥ = 몸 정면, s₀ = −0.5m 로 둔다.
        /// </summary>
        protected override void OnWindup()
        {
            diveCenter = Target.PlayerTrackingInfo.Position;
            Vector3 toCenter = HitAreas.Flat(diveCenter - Body.Position);
            float horizontalDistance = toCenter.magnitude;
            if (horizontalDistance < MinStartDistance)
            {
                diveDirection = HitAreas.Flat(Body.Forward).normalized;
                startOffset = -MinStartDistance;
            }
            else
            {
                diveDirection = toCenter / horizontalDistance;
                startOffset = -horizontalDistance;
            }

            startHeight = Body.Position.y;
            travelled = startOffset;
            stage = DiveStage.Diving;
            hasHit = false;
        }

        /// <summary>
        /// 급강하 1틱. 단계에 따라 급강하 또는 이탈을 진행한다. 기반 Tick 이 Active 동안 true 를 받을 때까지 매 틱 부른다.
        /// </summary>
        /// <param name="now">현재 시각 (s). 이탈 종료 시각 기록용</param>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        /// <returns>true = 이탈을 마쳤다</returns>
        protected override bool OnActive(float now, float deltaTime)
        {
            if (stage == DiveStage.Diving) TickDiving(deltaTime);
            if (stage == DiveStage.Leaving) return TickLeaving(now, deltaTime);
            return false;
        }

        /// <summary>끊길 때 아무것도 하지 않는다. 위치 제어는 이동 정지 해제(Apply)가 다시 맡는다</summary>
        protected override void OnCancel() { }

        /// <summary>
        /// 급강하 1틱: ĥ 축을 따라 앞으로 가며 높이를 포물선에 맞추고, 몸을 그 지점의 포물선 접선 방향으로 기울인다.
        /// 가까우면 피해를 주고, 출구(−s₀)에 닿으면 이탈로 넘어간다.
        /// </summary>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        private void TickDiving(float deltaTime)
        {
            travelled = Mathf.Min(travelled + settings.DiveSpeed * deltaTime, -startOffset);
            float minimumY = diveCenter.y + settings.MinHeight;
            Vector3 slopeDirection = diveDirection + Vector3.up * DiveSlope(travelled, startOffset, startHeight, minimumY);
            movement.transform.rotation = Quaternion.LookRotation(slopeDirection);
            Vector3 goal = diveCenter + diveDirection * travelled;
            goal.y = DiveHeight(travelled, startOffset, startHeight, minimumY);
            movement.MoveDuringAttack(goal - Body.Position);
            HitIfClose();

            bool reachedExit = travelled >= -startOffset;
            if (reachedExit) stage = DiveStage.Leaving;
        }

        /// <summary>
        /// 이탈 1틱: 몸을 수평으로 서서히 되돌리고 ĥ 방향으로 높이를 유지한 채 직진한다. P 와 수평 거리가 이탈 거리 이상이면 종료 시각을 기록한다.
        /// 출구에서는 위로 크게 기울어 있으므로, 한 틱에 수평으로 바꾸지 않고 levelOutSpeed 로 지수 보간한다 (MonsterMovement.ApplyLookRotation 과 같은 공식).
        /// </summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        /// <returns>true = 이탈 거리에 닿았다</returns>
        private bool TickLeaving(float now, float deltaTime)
        {
            Quaternion levelRotation = Quaternion.LookRotation(diveDirection);
            float blend = 1f - Mathf.Exp(-settings.LevelOutSpeed * deltaTime);
            movement.transform.rotation = Quaternion.Slerp(movement.transform.rotation, levelRotation, blend);
            movement.MoveDuringAttack(diveDirection * settings.DiveSpeed * deltaTime);
            float distanceFromCenter = HitAreas.Flat(Body.Position - diveCenter).magnitude;
            if (distanceFromCenter < settings.LeaveDistance) return false;

            leaveEndTime = now;
            return true;
        }

        /// <summary>지금 몸과 플레이어의 3D 거리가 Spec.Range 이하면 피해를 준다. 주기당 1회. TickDiving 이 부른다</summary>
        private void HitIfClose()
        {
            if (hasHit) return;
            float distanceToTarget = Vector3.Distance(Body.Position, Target.PlayerTrackingInfo.Position);
            if (distanceToTarget > Spec.Range) return;

            Hit(Target, Body.Position);
            hasHit = true;
        }
    }
}
