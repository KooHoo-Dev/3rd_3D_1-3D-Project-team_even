using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 1마리의 상태 머신 본체 (상태 패턴의 Owner). Unity 컴포넌트가 아니라 순수 C# 이다.
    /// MonoBehaviour 껍데기(MonsterController)가 Update(now, dt) 를 매 프레임 호출하고, 대상·처치 같은 외부 사건을 전달한다.
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
        // 이 몬스터가 가진 공격 전부. 생성 후 불변, 1개 이상
        public IReadOnlyList<MonsterAttack> Attacks { get; }
        // 지금 고른 공격. 생성 시 Attacks[0], 이후 ChooseNextAttack 이 바꾼다
        public MonsterAttack CurrentAttack { get; private set; }
        public IPlayerTarget Target { get; private set; }

        // 마지막 Update 시각 (s)
        public float Now { get; private set; }
        // 마지막 Update 의 경과 시간 (s)
        public float DeltaTime { get; private set; }
        // 마지막 Stun 요청 시간 (s)
        public float StunDuration { get; private set; }
        // Die 때 받은 보상 지급 여부
        public bool GrantReward { get; private set; }
        public bool IsDead => Current == Dead;

        // 인자 = (previous, current)
        public event Action<MonsterState, MonsterState> StateChanged;
        // DeadState 진입 시 1번. 인자 = 보상 지급 여부
        public event Action<bool> Died;

        /// <summary>
        /// 공격 1개짜리 몬스터용. 목록 생성자에 공격 1개짜리 목록으로 넘긴다.
        /// 기존 테스트가 이 형태로 만든다.
        /// </summary>
        /// <param name="movement">이 몬스터의 이동 컴포넌트</param>
        /// <param name="body">이 몬스터의 몸체 (위치·방향)</param>
        /// <param name="attack">이 몬스터가 쓸 공격 전략</param>
        /// <exception cref="ArgumentNullException">movement, body, attack 중 하나가 null 일 때</exception>
        public MonsterStateMachine(MonsterMovement movement, MonsterBody body, MonsterAttack attack)
            : this(movement, body, new[] { attack })
        {
        }

        /// <summary>
        /// 공격 목록을 받고 상태 5개를 만들어 Idle 로 시작한다. MonsterController.Start 가 조립 때 1번 호출한다.
        /// 처음 고른 공격은 attacks[0] 이다.
        /// </summary>
        /// <remarks>생성 중 Idle.Enter 가 이동을 멈추므로 movement 는 Awake 가 끝난 상태여야 한다.</remarks>
        /// <param name="movement">이 몬스터의 이동 컴포넌트</param>
        /// <param name="body">이 몬스터의 몸체 (위치·방향)</param>
        /// <param name="attacks">이 몬스터가 쓸 공격 전략 목록. 1개 이상</param>
        /// <exception cref="ArgumentNullException">movement, body, attacks 또는 그 원소 중 하나가 null 일 때</exception>
        /// <exception cref="ArgumentException">attacks 가 비어 있을 때</exception>
        public MonsterStateMachine(MonsterMovement movement, MonsterBody body, IReadOnlyList<MonsterAttack> attacks)
        {
            if (movement == null) throw new ArgumentNullException(nameof(movement));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (attacks == null) throw new ArgumentNullException(nameof(attacks));
            if (attacks.Count == 0) throw new ArgumentException("공격은 1개 이상이어야 한다", nameof(attacks));
            foreach (MonsterAttack attack in attacks)
            {
                if (attack == null) throw new ArgumentNullException(nameof(attacks), "공격 목록에 null 이 있다");
            }

            this.movement = movement;
            this.body = body;
            Attacks = attacks;
            CurrentAttack = attacks[0];

            Idle = new IdleState(this);
            Chase = new ChaseState(this);
            Attack = new AttackState(this);
            Stunned = new StunnedState(this);
            Dead = new DeadState(this);

            Current = Idle;
            Current.Enter();
        }

        // 대상이 있으면 true. 대상 상실(사망 등)은 소유자(MonsterController)가 SetTarget(null)로 알린다
        public bool HasTarget => Target != null;
        // 군중 이동의 공격 허가 (슬롯 예약·토큰). 직접 추적 모드는 대상이 있으면 true
        public bool CanAttack => movement.CanAttack;

        // 대상까지 수평 거리 ≤ 현재 공격 사거리. 대상이 없으면 false
        public bool IsTargetInAttackRange
        {
            get
            {
                if (HasTarget == false) return false;
                Vector3 toTarget = Target.PlayerTrackingInfo.Position - body.Position;
                toTarget.y = 0f;
                float range = CurrentAttack.Range;
                return toTarget.magnitude <= range;
            }
        }

        /// <summary>
        /// 공격 대상을 바꾼다. MonsterController 가 대상 확보·상실 때 호출한다.
        /// 대상이 바뀌면 진행 중 공격을 취소하고, 대상이 없어지면 Attack/Chase 에서 Idle 로 돌아간다.
        /// </summary>
        /// <param name="target">새 대상. null = 대상 상실 (플레이어 사망 등)</param>
        public void SetTarget(IPlayerTarget target)
        {
            if (ReferenceEquals(Target, target) == false && CurrentAttack.IsAttacking) CurrentAttack.Cancel();
            Target = target;
            if (HasTarget == false)
            {
                if (CurrentAttack.IsAttacking) CurrentAttack.Cancel();
                if (Current == Attack || Current == Chase) ChangeState(Idle);
            }
        }

        /// <summary>
        /// 판단 1회. MonsterController.Update 가 매 프레임 호출한다.
        /// 시각을 기록하고 현재 상태의 Tick 을 부른다. 상태들은 기록된 Now / DeltaTime 을 읽는다.
        /// </summary>
        /// <param name="now">현재 시각 (s, Time.time)</param>
        /// <param name="deltaTime">지난 프레임부터 경과 시간 (s)</param>
        public void Update(float now, float deltaTime)
        {
            Now = now;
            DeltaTime = deltaTime;
            bool hasNoTarget = HasTarget == false;
            bool isInAttackState = Current == Attack;
            bool isInChaseState = Current == Chase;
            if (hasNoTarget && (isInAttackState || isInChaseState)) SetTarget(null);
            Current.Tick();
        }

        /// <summary>
        /// 상태를 전이한다: 이전 상태 Exit → 새 상태 Enter → StateChanged 발행.
        /// 각 상태의 Tick 과 이 클래스의 SetTarget / Die / Stun / Cleanup 이 호출한다.
        /// 같은 상태로의 변경과 Dead 이후의 변경은 무시한다.
        /// </summary>
        /// <param name="next">들어갈 상태 (이 머신의 Idle / Chase / Attack / Stunned / Dead 중 하나)</param>
        /// <exception cref="ArgumentNullException">next 가 null 일 때</exception>
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

        /// <summary>
        /// 처치·정리. 어느 상태에서든 Dead 로 간다 (진행 중 공격은 AttackState.Exit 가 취소).
        /// MonsterController 가 체력 0 이나 보스 정리 이벤트 때 호출한다. 이미 죽었으면 무시한다.
        /// </summary>
        /// <param name="grantReward">true = 처치 보상 지급. Died 이벤트로 그대로 전달된다</param>
        public void Die(bool grantReward)
        {
            if (IsDead) return;
            GrantReward = grantReward;
            ChangeState(Dead);
        }

        /// <summary>
        /// 기절. 어느 상태에서든 Stunned 로 간다. AttackState 가 벽 충돌(WallHit) 결과를 받았을 때 호출한다.
        /// 죽었거나 이미 기절 중이면 무시한다.
        /// </summary>
        /// <param name="seconds">기절 시간 (s). 0 이상</param>
        /// <exception cref="ArgumentOutOfRangeException">seconds 가 0 미만일 때</exception>
        public void Stun(float seconds)
        {
            if (seconds < 0f) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "기절 시간은 0 이상");
            if (IsDead || Current == Stunned) return; // 반복 기절은 최초 종료시각을 유지한다.
            StunDuration = seconds;
            ChangeState(Stunned);
        }

        /// <summary>
        /// 회수/비활성화: 보상·사망 알림 없이 공격과 이동을 정리한다.
        /// MonsterController 의 OnDisable 과 Despawn 이 호출한다.
        /// </summary>
        public void Cleanup()
        {
            SetTarget(null);
            if (CurrentAttack.IsAttacking) CurrentAttack.Cancel();
            if (IsDead == false) ChangeState(Idle);
            movement.SetSuspended(true);
        }

        // ── 상태 전용 ──
        /// <summary>
        /// 다음에 쓸 공격을 골라 CurrentAttack 에 넣는다. AttackState 가 공격 시작 직전에 호출한다.
        /// 지금은 항상 Attacks[0] 을 고른다.
        /// </summary>
        /// <returns>고른 공격 (CurrentAttack 과 같음)</returns>
        internal MonsterAttack ChooseNextAttack()
        {
            CurrentAttack = Attacks[0];
            return CurrentAttack;
        }

        /// <summary>멈춘다. 군중 이동의 등록·슬롯·토큰 예약은 즉시 반환한다</summary>
        internal void StopMove() => movement.SetSuspended(true);
        /// <summary>공격하려고 멈춘다. 군중 이동의 공격 슬롯 예약은 유지한다</summary>
        internal void StopForAttack() => movement.SetSuspended(true, retainReservation: true);
        /// <summary>Chase 진입 때 이동을 재개한다</summary>
        internal void ResumeMove() => movement.SetSuspended(false);
        /// <summary>AttackState 가 공격 시작 전 대상 쪽을 수평으로 바라보게 한다. 대상이 있어야 한다</summary>
        internal void FaceTarget() => body.Face(Target.PlayerTrackingInfo.Position);
        /// <summary>DeadState 진입 때 Died 이벤트를 보상 지급 여부와 함께 발행한다</summary>
        internal void NotifyDied() => Died?.Invoke(GrantReward);
    }
}
