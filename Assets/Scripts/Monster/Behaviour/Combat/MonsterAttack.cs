using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// AttackState 가 쓰는 공격 전략의 기반 (Template Method). 상태 머신은 이 타입으로 공격을 다룬다.
    /// 진행 순서 "예고(TelegraphTime) → 공격(Active) → 회복(RecoveryTime)" 은 이 클래스가 고정하고,
    /// 하위 클래스는 각 단계의 내용(OnWindup / OnActive / OnRecover / OnCancel)만 정한다.
    /// 단계는 Tick 1번에 최대 1개씩 넘어간다.
    /// 공격 한 주기 = Cooldown. Active 는 ActiveDuration(= Cooldown − 예고 − 회복) 동안 유지되고, 판정(OnActive)은 Active 의 HitTimeRatio 지점에서 시작한다.
    /// </summary>
    public abstract class MonsterAttack
    {
        private readonly Func<IPlayerTarget, IDamageable> findDamageable;
        private float phaseEndTime;
        private float nextReadyTime;

        // ── 공격 주기 (Begin 때 고정, 진행 중 값 변경은 다음 Begin 부터) ──
        private float cycleCooldown;      // s
        private float cycleHitTimeRatio;  // 0~1
        private float activeStartTime;    // s
        private bool isActionDone;        // OnActive 가 true 를 돌려줬으면 같은 Active 에서 다시 부르지 않는다

        public AttackSpec Spec { get; }

        // 공격 한 주기 총 시간 (s). 기본값 = Spec.Cooldown. MonsterController 가 인스펙터 값을 넣는다
        public float Cooldown { get; set; }

        // Active 시작부터 판정까지의 비율 (0~1). 0 = Active 첫 틱에 판정
        public float HitTimeRatio { get; set; } = 0.5f;

        // 진행 중(또는 마지막) 주기의 Active 길이 (s). MonsterController 가 Attack 클립 재생 배율 계산에 쓴다
        public float ActiveDuration => cycleCooldown - Spec.TelegraphTime - Spec.RecoveryTime;
        public SkillPhase Phase { get; private set; } = SkillPhase.Idle;

        // 예고·공격·회복 중 하나에 있으면 true
        public bool IsAttacking => Phase != SkillPhase.Idle;

        public AttackResult LastResult { get; private set; }

        // 공격 상태로 들어가는 수평 거리 (m)
        public virtual float Range => Spec.Range;

        // LastResult 가 WallHit 일 때 기절 시간 (s)
        public float WallHitStunTime => Spec.WallHitStunTime;

        // 단계 변경 알림 (애니메이션·예고 표시·보스 이벤트). Idle = 종료
        public event Action<MonsterAttack, SkillPhase> PhaseChanged;

        // 진행 중인 공격의 대상. 진행 중이 아니면 null
        protected IPlayerTarget Target { get; private set; }

        /// <summary>
        /// 공격 값과 피해 대상 탐색 방법을 받는다. 하위 전략의 생성자가 호출한다. Cooldown 은 Spec.Cooldown 으로 시작한다.
        /// </summary>
        /// <param name="spec">공격 1개의 값 묶음</param>
        /// <param name="findDamageable">대상에게 피해를 줄 IDamageable 을 찾는 방법 (런타임에서 주입)</param>
        /// <exception cref="ArgumentNullException">findDamageable 이 null 일 때</exception>
        protected MonsterAttack(in AttackSpec spec, Func<IPlayerTarget, IDamageable> findDamageable)
        {
            if (findDamageable == null) throw new ArgumentNullException(nameof(findDamageable));

            Spec = spec;
            Cooldown = spec.Cooldown;
            this.findDamageable = findDamageable;
        }

        /// <summary>쿨다운 확인. AttackState 가 Begin 전에 부른다. 하위 클래스가 발동 조건을 더할 수 있다</summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <returns>진행 중이 아니고 이전 Begin 으로부터 쿨다운이 지났으면 true</returns>
        public virtual bool CanStart(float now) => IsAttacking == false && now >= nextReadyTime;

        /// <summary>
        /// 공격 한 주기를 시작한다 (예고 진입). AttackState 가 사거리 안·공격 허가·CanStart 일 때 부른다.
        /// 이 주기의 쿨다운·판정 비율을 여기서 고정하므로
        /// 진행 중에 Cooldown / HitTimeRatio 가 바뀌어도 다음 Begin 부터 반영된다.
        /// </summary>
        /// <param name="target">이번 주기의 공격 대상</param>
        /// <param name="now">현재 시각 (s)</param>
        /// <exception cref="InvalidOperationException">이미 진행 중인 공격이 있을 때</exception>
        /// <exception cref="ArgumentNullException">target 이 null 일 때</exception>
        public void Begin(IPlayerTarget target, float now)
        {
            if (IsAttacking) throw new InvalidOperationException("이미 진행 중인 공격이다");
            if (target == null) throw new ArgumentNullException(nameof(target));
            Target = target;
            LastResult = AttackResult.None;
            cycleCooldown = Cooldown;
            cycleHitTimeRatio = HitTimeRatio;
            nextReadyTime = now + cycleCooldown;
            SetPhase(SkillPhase.Windup, now + Spec.TelegraphTime);
            OnWindup();
        }

        /// <summary>
        /// 단계를 진행한다. Active 는 판정 시각(시작 + ActiveDuration × 판정 비율)까지 기다렸다가 OnActive 를 부르고,
        /// OnActive 가 끝났다고 알린 뒤 Active 시간이 다 지나야 회복으로 넘어간다.
        /// AttackState 가 공격 진행 중 매 판단마다 부른다.
        /// </summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <param name="deltaTime">지난 판단부터 경과 시간 (s). OnActive 로 넘긴다</param>
        /// <exception cref="InvalidOperationException">진행 중이 아닌(Idle) 공격을 Tick 할 때</exception>
        public void Tick(float now, float deltaTime)
        {
            switch (Phase)
            {
                case SkillPhase.Windup:
                    if (now < phaseEndTime) return;
                    activeStartTime = now;
                    SetPhase(SkillPhase.Active, now);
                    return;
                case SkillPhase.Active:
                    bool isBeforeHitTime = now < activeStartTime + ActiveDuration * cycleHitTimeRatio;
                    if (isBeforeHitTime) return;

                    if (isActionDone == false) isActionDone = OnActive(now, deltaTime);
                    if (Phase != SkillPhase.Active) return; // OnActive 안에서 EndEarly 로 끝난 경우 (벽 충돌 등)

                    bool isActiveTimeOver = now >= activeStartTime + ActiveDuration;
                    if (isActionDone == false) return;
                    if (isActiveTimeOver == false) return;
                    SetPhase(SkillPhase.Recover, now + Spec.RecoveryTime);
                    OnRecover();
                    return;
                case SkillPhase.Recover:
                    if (now >= phaseEndTime) Finish(AttackResult.Completed);
                    return;
                default:
                    throw new InvalidOperationException("진행 중이 아닌 공격은 Tick 할 수 없다");
            }
        }

        /// <summary>
        /// 진행 중 공격을 중간에 끊는다 (OnCancel → 결과 Cancelled). 진행 중이 아니면 무시한다.
        /// 상태 머신의 대상 변경·정리와 AttackState.Exit 가 부른다.
        /// </summary>
        public void Cancel()
        {
            if (IsAttacking == false) return;
            OnCancel();
            Finish(AttackResult.Cancelled);
        }

        // ── 하위 클래스 훅 ──

        /// <summary>예고 시작 (조준 고정, 예고 범위 표시)</summary>
        protected virtual void OnWindup() { }

        /// <summary>실제 공격 (판정 시각부터 매 틱 호출)</summary>
        /// <param name="now">현재 시각 (s)</param>
        /// <param name="deltaTime">지난 틱부터 경과 시간 (s)</param>
        /// <returns>true = 공격 동작 완료, 이후 다시 부르지 않고 Active 시간이 끝나면 회복으로 넘어간다. false = 다음 틱에 다시 부른다</returns>
        protected abstract bool OnActive(float now, float deltaTime);

        /// <summary>회복(경직) 시작</summary>
        protected virtual void OnRecover() { }

        /// <summary>처치 등으로 중간에 끊길 때</summary>
        protected virtual void OnCancel() { }

        /// <summary>회복을 건너뛰고 즉시 끝낸다 (돌진 중 벽 충돌 등). 하위 클래스의 OnActive 안에서 부른다</summary>
        /// <param name="result">끝난 이유. LastResult 에 남는다 (WallHit 이면 AttackState 가 기절시킨다)</param>
        protected void EndEarly(AttackResult result) => Finish(result);

        /// <summary>대상에게 Damage × HitCount 피해. 대상이 이미 죽었으면 주지 않는다</summary>
        /// <remarks>아직 전투시스템 관련 적용되지 않았음</remarks>
        /// <param name="target">피해를 받을 대상</param>
        /// <param name="from">공격 위치. 피해 방향(수평) 계산에 쓴다</param>
        /// <exception cref="InvalidOperationException">대상에서 IDamageable 을 찾지 못했을 때</exception>
        protected void Hit(IPlayerTarget target, Vector3 from)
        {
            var damageable = findDamageable(target);
            if (damageable == null) throw new InvalidOperationException("공격 대상에서 IDamageable 을 찾지 못했다");
            if (damageable.CanBeHitBy(Faction.Monster) == false) return;

            Vector3 direction = HitAreas.Flat(target.PlayerTrackingInfo.Position - from);
            direction = direction.magnitude > 1e-4f ? direction.normalized : Vector3.zero;
            var info = new DamageInfo(Spec.Damage, DamageKind.MonsterAttack, Faction.Monster, null,
                                      target.PlayerTrackingInfo.Position, direction, Spec.SourceDefId);
            
            // 전투시스템 없어서 직접 피해주기
            for (int i = 0; i < Spec.HitCount; i++)
                damageable.TakeDamage(in info);
        }

        /// <summary>주기를 끝낸다 (완료·취소·조기 종료 공통). 다음 주기를 위해 판정 완료 표시를 되돌린다</summary>
        /// <param name="result">끝난 이유. LastResult 에 남긴다</param>
        private void Finish(AttackResult result)
        {
            LastResult = result;
            Target = null;
            isActionDone = false;
            SetPhase(SkillPhase.Idle, 0f);
        }

        /// <summary>단계를 바꾸고 PhaseChanged 를 발행한다. 모든 단계 변경은 여기를 지난다</summary>
        /// <param name="phase">새 단계</param>
        /// <param name="endTime">이 단계가 끝나는 시각 (s). Windup·Recover 만 쓰고 Active·Idle 은 의미 없음</param>
        private void SetPhase(SkillPhase phase, float endTime)
        {
            Phase = phase;
            phaseEndTime = endTime;
            PhaseChanged?.Invoke(this, phase);
        }
    }
}
