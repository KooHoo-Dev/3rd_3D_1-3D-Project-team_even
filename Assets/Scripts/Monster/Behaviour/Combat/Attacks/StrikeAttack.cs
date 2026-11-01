using System;
using HybridShooter.Core.Contracts;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 제자리 타격 (기본 근접형). 예고 중 움직이는 대상을 따라 회전 속도만큼 돌고(MonsterAttack.Tick),
    /// 판정 구간 동안 대상이 판정 범위 안에 있을 때만 피해를 준다 → 예고 중 범위 밖으로 피하면 맞지 않는다.
    /// 판정 구간 = Active 의 [HitTimeRatio, HitEndRatio]. 구간 안에서는 최대 MaxHitsPerAttack 번, HitIntervalSeconds 간격으로 맞힌다.
    /// HitEndRatio 가 HitTimeRatio 이하면 판정 시작 1틱만 검사한다 (기존 1회 판정).
    /// Melee 모양은 판정 거리 = 진입 거리(Spec.Range) × HitRangeRatio 다. 예고 동안 같은 방향으로 도망간 거리를 판정 거리로 메운다.
    /// </summary>
    public sealed class StrikeAttack : MonsterAttack
    {
        private enum StrikeStage
        {
            BeforeFirstCheck, // 이번 주기 아직 한 번도 판정하지 않음. 첫 판정은 구간이 이미 지났어도 한다
            InHitWindow,      // 첫 판정 뒤. 판정 구간 안에서만 판정한다
        }

        private readonly IHitArea area; // 생성 때 1번 만든 판정 범위. Melee 면 판정 전에 반경을 HitRangeRatio 로 맞춘다

        // ── 이번 주기 판정 상태 (OnWindup 에서 한 번에 비운다) ──
        private StrikeStage stage;
        private int hitsDealt;      // 이번 주기에 맞힌 횟수
        private float nextHitTime;  // s. 이 시각 전에는 다시 맞히지 않는다

        /// <summary>
        /// MonsterAttackFactory 가 Melee 역할에 만든다. spec 의 Shape 로 판정 범위를 1번 만들어 둔다.
        /// </summary>
        /// <param name="spec">공격 값 묶음</param>
        /// <param name="body">공격자 몸체. 예고 중 회전과 판정 기준 위치·방향</param>
        /// <param name="findDamageable">대상에서 피해를 줄 IDamageable 을 찾는 방법</param>
        /// <exception cref="ArgumentNullException">body 가 null 일 때 (기반 생성자가 던진다)</exception>
        public StrikeAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable)
            : base(spec, body, findDamageable)
        {
            area = HitAreas.From(spec);
        }

        /// <summary>예고 시작: 이번 주기 판정 상태를 비운다. 대상 쪽 회전은 기반 Tick 이 예고 내내 한다</summary>
        protected override void OnWindup()
        {
            stage = StrikeStage.BeforeFirstCheck;
            hitsDealt = 0;
            nextHitTime = 0f;
        }

        /// <summary>
        /// 판정 1틱. 기반 Tick 이 판정 시작 시각부터 true 를 받을 때까지 매 틱 부른다.
        /// 첫 호출은 늦게 불려도 반드시 판정하고(기존 1회 판정과 같음), 이후에는 판정 구간 끝(HitEndTime) 전까지만 판정한다.
        /// </summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <returns>true = 판정 구간이 끝나 동작 완료. false = 다음 틱에 다시 판정한다</returns>
        protected override bool OnActive(float now, float deltaTime)
        {
            bool isFirstCheck = stage == StrikeStage.BeforeFirstCheck;
            bool isHitWindowOver = now >= HitEndTime;
            stage = StrikeStage.InHitWindow;

            if (isFirstCheck || isHitWindowOver == false) TryHit(now);
            return isHitWindowOver;
        }

        /// <summary>
        /// 횟수가 남았고 간격이 지났고 대상이 범위 안이면 Hit 으로 피해를 주고 다음 명중 가능 시각을 정한다. OnActive 가 판정할 틱마다 부른다.
        /// 범위는 매 틱 지금 몸 위치·정면 기준이고, 대상은 조준점이 아닌 실제 플레이어 위치로 판정한다.
        /// </summary>
        /// <param name="now">현재 시각 (s)</param>
        private void TryHit(float now)
        {
            if (hitsDealt >= MaxHitsPerAttack) return;
            if (now < nextHitTime) return;

            // ApplyHitRangeRatio();
            bool isInArea = area.Contains(Body.Position, Body.Forward, Target.PlayerTrackingInfo.Position);
            if (isInArea == false) 
                return;
            NotifyMonsterHit(area, Body.Position, Body.Forward, isInArea);
            Hit(Target, Body.Position);
            hitsDealt++;
            nextHitTime = now + HitIntervalSeconds;
        }

        /// <summary>
        /// Melee 범위의 반경을 진입 거리(Spec.Range) × HitRangeRatio 로 맞춘다. TryHit 이 범위 검사 직전에 부른다.
        /// HitRangeRatio 는 생성 뒤 MonsterController 가 넣고 인스펙터에서 바뀔 수 있어 판정 때마다 맞춘다. 다른 모양은 그대로 둔다.
        /// </summary>
        private void ApplyHitRangeRatio()
        {
            if (area is MeleeArea meleeArea) meleeArea.Range = Spec.Range * HitRangeRatio;
        }
    }
}
