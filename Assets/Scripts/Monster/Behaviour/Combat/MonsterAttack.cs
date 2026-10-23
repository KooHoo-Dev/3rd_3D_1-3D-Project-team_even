using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 공격 전략의 기반 (Template Method).
    /// 진행 순서 "예고(TelegraphTime) → 공격(Active) → 회복(RecoveryTime)" 은 이 클래스가 고정하고,
    /// 하위 클래스는 각 단계의 내용(OnWindup / OnActive / OnRecover / OnCancel)만 정한다.
    /// 단계는 Tick 1번에 최대 1개씩 넘어간다.
    /// </summary>
    public abstract class MonsterAttack : IMonsterAttack
    {
        private readonly Func<IPlayerTarget, IDamageable> findDamageable;
        private float phaseEndTime;
        private float nextReadyTime;

        public AttackSpec Spec { get; }
        public SkillPhase Phase { get; private set; } = SkillPhase.Idle;
        public bool IsRunning => Phase != SkillPhase.Idle;
        public AttackResult LastResult { get; private set; }
        public virtual float Range => Spec.Range;
        public float WallHitStunTime => Spec.WallHitStunTime;

        /// <summary>단계 변경 알림 (애니메이션·예고 표시·보스 이벤트). Idle = 종료</summary>
        public event Action<MonsterAttack, SkillPhase> PhaseChanged;

        /// <summary>진행 중인 공격의 대상. 진행 중이 아니면 null</summary>
        protected IPlayerTarget Target { get; private set; }

        /// <param name="findDamageable">대상에게 피해를 줄 IDamageable 을 찾는 방법 (런타임에서 주입)</param>
        protected MonsterAttack(in AttackSpec spec, Func<IPlayerTarget, IDamageable> findDamageable)
        {
            Spec = spec;
            this.findDamageable = findDamageable ?? throw new ArgumentNullException(nameof(findDamageable));
        }

        /// <summary>쿨다운 확인. 하위 클래스가 발동 조건을 더할 수 있다</summary>
        public virtual bool CanStart(float now) => IsRunning == false && now >= nextReadyTime;

        public void Begin(IPlayerTarget target, float now)
        {
            if (IsRunning) throw new InvalidOperationException("이미 진행 중인 공격이다");
            Target = target ?? throw new ArgumentNullException(nameof(target));
            LastResult = AttackResult.None;
            nextReadyTime = now + Spec.Cooldown;
            SetPhase(SkillPhase.Windup, now + Spec.TelegraphTime);
            OnWindup();
        }

        public void Tick(float now, float deltaTime)
        {
            if (IsRunning && !PlayerMovementTarget.IsAlive(Target))
            {
                Cancel();
                return;
            }
            switch (Phase)
            {
                case SkillPhase.Windup:
                    if (now < phaseEndTime) return;
                    SetPhase(SkillPhase.Active, now);
                    return;
                case SkillPhase.Active:
                    if (OnActive(now, deltaTime) == false || Phase != SkillPhase.Active) return;
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

        public void Cancel()
        {
            if (IsRunning == false) return;
            OnCancel();
            Finish(AttackResult.Cancelled);
        }

        // ── 하위 클래스 훅 ──

        /// <summary>예고 시작 (조준 고정, 예고 범위 표시)</summary>
        protected virtual void OnWindup() { }

        /// <summary>실제 공격. true 를 돌려주면 회복으로 넘어간다</summary>
        protected abstract bool OnActive(float now, float deltaTime);

        /// <summary>회복(경직) 시작</summary>
        protected virtual void OnRecover() { }

        /// <summary>처치 등으로 중간에 끊길 때</summary>
        protected virtual void OnCancel() { }

        /// <summary>회복을 건너뛰고 즉시 끝낸다 (돌진 중 벽 충돌 등)</summary>
        protected void EndEarly(AttackResult result) => Finish(result);

        /// <summary>대상에게 Damage × HitCount 피해. 대상이 이미 죽었으면 주지 않는다</summary>
        protected void Hit(IPlayerTarget target, Vector3 from)
        {
            var damageable = findDamageable(target)
                ?? throw new InvalidOperationException("공격 대상에서 IDamageable 을 찾지 못했다");
            if (damageable.CanBeHitBy(Faction.Monster) == false) return;

            Vector3 direction = HitAreas.Flat(target.Position - from);
            direction = direction.sqrMagnitude > 1e-8f ? direction.normalized : Vector3.zero;
            var info = new DamageInfo(Spec.Damage, DamageKind.MonsterAttack, Faction.Monster, null,
                                      target.Position, direction, Spec.SourceDefId);
            for (int i = 0; i < Spec.HitCount && PlayerMovementTarget.IsAlive(target); i++)
                damageable.TakeDamage(in info);
        }

        private void Finish(AttackResult result)
        {
            LastResult = result;
            Target = null;
            SetPhase(SkillPhase.Idle, 0f);
        }

        private void SetPhase(SkillPhase phase, float endTime)
        {
            Phase = phase;
            phaseEndTime = endTime;
            PhaseChanged?.Invoke(this, phase);
        }
    }
}
