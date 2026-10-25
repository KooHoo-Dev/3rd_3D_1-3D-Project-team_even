using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 1마리의 상태 머신 본체 (상태 패턴의 Owner). Unity 컴포넌트가 아니라 순수 C# 이다.
    /// MonoBehaviour 껍데기가 Update(now, dt) 를 매 프레임 호출하고, 대상·처치 같은 외부 사건을 전달한다.
    /// 이동은 MonsterMovement 의 정지/재개(SetSuspended)와 공격 허가(CanAttack)로만 제어한다.
    /// </summary>
    public sealed class MonsterStateMachine
    {
        private readonly MonsterMovement movement;
        private readonly MonsterBody body;

        public MonsterState Idle { get; }
        public MonsterState Chase { get; }
        public MonsterState Attack { get; }
        public MonsterState Stunned { get; }
        public MonsterState Dead { get; }

        public MonsterState Current { get; private set; }
        public MonsterAttack CurrentAttack { get; }
        public IPlayerTarget Target { get; private set; }

        /// <summary>마지막 Update 시각 (s)</summary>
        public float Now { get; private set; }
        /// <summary>마지막 Update 의 경과 시간 (s)</summary>
        public float DeltaTime { get; private set; }
        /// <summary>마지막 Stun 요청 시간 (s)</summary>
        public float StunDuration { get; private set; }
        /// <summary>Die 때 받은 보상 지급 여부</summary>
        public bool GrantReward { get; private set; }
        public bool IsDead => Current == Dead;

        /// <summary>(previous, current)</summary>
        public event Action<MonsterState, MonsterState> StateChanged;
        /// <summary>DeadState 진입 시 1번. 인자 = 보상 지급 여부</summary>
        public event Action<bool> Died;

        public MonsterStateMachine(MonsterMovement movement, MonsterBody body, MonsterAttack attack)
        {
            this.movement = movement != null ? movement : throw new ArgumentNullException(nameof(movement));
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            CurrentAttack = attack ?? throw new ArgumentNullException(nameof(attack));

            Idle = new IdleState(this);
            Chase = new ChaseState(this);
            Attack = new AttackState(this);
            Stunned = new StunnedState(this);
            Dead = new DeadState(this);

            Current = Idle;
            Current.Enter();
        }

        /// <summary>대상이 있으면 true. 대상 상실(사망 등)은 소유자(MonsterController)가 SetTarget(null)로 알린다</summary>
        public bool HasTarget => Target != null;
        /// <summary>군중 이동의 공격 허가 (슬롯 예약·토큰). 직접 추적 모드는 대상이 있으면 true</summary>
        public bool CanAttack => movement.CanAttack;

        /// <summary>대상까지 수평 거리 ≤ 현재 공격 사거리</summary>
        public bool IsTargetInAttackRange
        {
            get
            {
                if (HasTarget == false) return false;
                Vector3 toTarget = Target.PlayerTrackingInfo.Position - body.Position;
                toTarget.y = 0f;
                float range = CurrentAttack.Range;
                return toTarget.sqrMagnitude <= range * range;
            }
        }

        /// <summary>null = 대상 상실 (플레이어 사망 등)</summary>
        public void SetTarget(IPlayerTarget target)
        {
            if (!ReferenceEquals(Target, target) && CurrentAttack.IsAttacking) CurrentAttack.Cancel();
            Target = target;
            if (!HasTarget)
            {
                if (CurrentAttack.IsAttacking) CurrentAttack.Cancel();
                if (Current == Attack || Current == Chase) ChangeState(Idle);
            }
        }

        /// <summary>판단 1회. 현재 상태의 Update 를 부른다</summary>
        public void Update(float now, float deltaTime)
        {
            Now = now;
            DeltaTime = deltaTime;
            if (!HasTarget && (Current == Attack || Current == Chase)) SetTarget(null);
            Current.Tick();
        }

        /// <summary>같은 상태로의 변경과 Dead 이후의 변경은 무시한다</summary>
        public void ChangeState(MonsterState next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (next == Current || Current == Dead) return;

            var previous = Current;
            previous.Exit();
            Current = next;
            next.Enter();
            StateChanged?.Invoke(previous, next);
        }

        /// <summary>처치·정리. 어느 상태에서든 Dead 로 간다 (진행 중 공격은 AttackState.Exit 가 취소)</summary>
        public void Die(bool grantReward)
        {
            if (IsDead) return;
            GrantReward = grantReward;
            ChangeState(Dead);
        }

        /// <summary>기절. 어느 상태에서든 Stunned 로 간다</summary>
        public void Stun(float seconds)
        {
            if (seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "기절 시간은 0 이상");
            if (IsDead || Current == Stunned) return; // 반복 기절은 최초 종료시각을 유지한다.
            StunDuration = seconds;
            ChangeState(Stunned);
        }

        /// <summary>회수/비활성화: 보상·사망 알림 없이 공격과 이동을 정리한다.</summary>
        public void Cleanup()
        {
            SetTarget(null);
            if (CurrentAttack.IsAttacking) CurrentAttack.Cancel();
            if (!IsDead) ChangeState(Idle);
            movement.SetSuspended(true);
        }

        // ── 상태 전용 ──
        internal void StopMove() => movement.SetSuspended(true);
        /// <summary>공격하려고 멈춘다. 군중 이동의 공격 슬롯 예약은 유지한다</summary>
        internal void StopForAttack() => movement.SetSuspended(true, retainReservation: true);
        internal void ResumeMove() => movement.SetSuspended(false);
        internal void FaceTarget() => body.Face(Target.PlayerTrackingInfo.Position);
        internal void NotifyDied() => Died?.Invoke(GrantReward);
    }
}
