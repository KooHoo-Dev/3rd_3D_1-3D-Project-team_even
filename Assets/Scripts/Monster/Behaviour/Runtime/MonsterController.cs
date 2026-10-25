using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 1마리의 Unity 진입점 (Facade).
    /// Awake에서 Def/체력을 준비하고 모든 Awake 이후 Start에서 이동·공격·브레인을 조립한다.
    /// (기본 IMonsterFactory 는 프리팹을 Instantiate 만 하므로 초기화는 여기서 스스로 한다)
    /// 매 프레임 브레인을 돌리고, 대상 확보·처치·정리 이벤트를 브레인에 전달한다.
    /// 체력(부위 없음, 단일 체력)도 직접 가진다. 라이플·수류탄이 IDamageable 로 찾아 피해를 준다.
    /// </summary>
    [RequireComponent(typeof(MonsterMovement), typeof(NavMeshMoveExecutor))]
    public sealed class MonsterController : MonoBehaviour, IDamageable
    {
        [Tooltip("MonsterTable Id")]
        [SerializeField] private int monsterId;
        [Tooltip("처치 후 회수까지 (s). 처치 연출 시간")]
        [SerializeField, Min(0f)] private float despawnDelay = 2f;

        [Header("애니메이션")]
        [Tooltip("Move 클립을 1배속으로 재생할 때의 이동 속도 (m/s)")]
        [SerializeField, Min(0.01f)] private float moveClipSpeed = 3f;
        [Tooltip("Chase 중 이 속도 미만이면 Idle 애니를 재생 (m/s)")]
        [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.1f;

        [Header("공격")]
        [Tooltip("켜면 데이터 테이블 Cooldown 대신 아래 값을 쓴다. 다음 공격 시작부터 반영")]
        [SerializeField] private bool useCooldownOverride;
        [Tooltip("공격 한 주기 총 시간 (s). 예고+회복보다 커야 한다 (같거나 작으면 예외)")]
        [SerializeField, Min(0f)] private float cooldownOverrideSeconds = 1f;
        [Tooltip("Active 구간 중 판정 시점 비율. 0 = Active 시작, 1 = Active 끝. 다음 공격 시작부터 반영")]
        [SerializeField, Range(0f, 1f)] private float hitTimeRatio = 0.5f;

        /// <summary>Base Controller 의 Attack 상태 원본 클립 이름. Override 인덱서는 원본 클립 이름으로 찾는다</summary>
        private const string BaseAttackClipName = "Placeholder_Attack";

        private MonsterMovement movement;
        private bool playerDead;
        private float despawnTime = -1f;
        private bool despawned;

        // ── 애니메이션 ──
        private Animator animator;

        public MonsterDef Def { get; private set; }
        public MonsterStateMachine StateMachine { get; private set; }

        // ── 체력 ──
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }
        public Faction Faction => Faction.Monster;
        public bool IsAlive => CurrentHp > 0;

        private void Awake()
        {
            movement = GetComponent<MonsterMovement>();
            animator = GetComponent<Animator>();

            Def = DataManager.Monsters.Get(monsterId);
            InitializeHealth(Def.MaxHp);
        }

        private void Start()
        {
            // 모든 컴포넌트 Awake 이후 조립한다. Brain 생성 직후 Idle.Stop도 실행기를 안전하게 사용한다.
            var body = new MonsterBody(transform);
            var attackSpec = AttackSpec.From(SingleBasicAttack(Def));
            var attack = MonsterAttackFactory.CreateAttack(Def.Role, attackSpec, body, FindPlayerDamageable);
            movement.SetCombatTuning(Def.MoveSpeed, attack.Range);
            StateMachine = new MonsterStateMachine(movement, body, attack);

            StateMachine.Died += OnStateMachineDied;
            StateMachine.StateChanged += OnStateChanged;
            StateMachine.CurrentAttack.PhaseChanged += OnAttackPhaseChanged;
            Subscribe();
        }

        private void OnEnable()
        {
            if (StateMachine == null) return; // 최초 구독은 Start의 조립이 끝난 뒤 수행한다.
            movement.enabled = true;
            Subscribe();
        }

        private void Subscribe()
        {
            EventBus<MonsterWipeRequestedEvent>.Subscribe(OnWipeRequested);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<PlayerDiedEvent>.Subscribe(OnPlayerDied);
        }

        private void OnDisable()
        {
            StateMachine?.Cleanup();
            if (movement != null) movement.enabled = false;
            EventBus<MonsterWipeRequestedEvent>.Unsubscribe(OnWipeRequested);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<PlayerDiedEvent>.Unsubscribe(OnPlayerDied);
        }

        private void Update()
        {
            if (StateMachine.IsDead)
            {
                if (Time.time >= despawnTime) Despawn();
                return;
            }

            AcquireTarget();
            ApplyAttackTuning();
            StateMachine.Update(Time.time, Time.deltaTime);
            UpdateMoveAnimation();
        }

        /// <summary>
        /// 인스펙터의 쿨다운(덮어쓰기 꺼짐 = 데이터 값)과 판정 비율을 매 프레임 공격에 넣는다.
        /// 공격은 Begin 때 값을 고정하므로 실제 반영은 다음 공격부터다. 쿨다운이 예고+회복 이하면 Active 길이가 없으므로 예외.
        /// </summary>
        private void ApplyAttackTuning()
        {
            MonsterAttack attack = StateMachine.CurrentAttack;
            float cooldown = attack.Spec.Cooldown;
            if (useCooldownOverride) cooldown = cooldownOverrideSeconds;

            float windupAndRecoverySeconds = attack.Spec.TelegraphTime + attack.Spec.RecoveryTime;
            if (cooldown <= windupAndRecoverySeconds)
                throw new InvalidOperationException($"{name}: 쿨다운({cooldown}s)은 예고+회복({windupAndRecoverySeconds}s)보다 커야 한다");

            attack.Cooldown = cooldown;
            attack.HitTimeRatio = hitTimeRatio;
        }

        /// <summary>
        /// 상태가 바뀔 때 Animator 의 Bool/Trigger 를 맞춘다. Attack 신호는 여기서 보내지 않는다(OnAttackPhaseChanged 담당).
        /// Stunned 진입/이탈은 IsStunned, Dead 진입은 IsDead + Death Trigger. IsDead 가 Any State 전이를 막아 Death 에서 나가지 않는다.
        /// Idle/Chase 안의 정지·이동은 UpdateMoveAnimation 이 IsMoving 으로 정한다.
        /// </summary>
        private void OnStateChanged(MonsterState previous, MonsterState current)
        {
            animator.speed = 1f;
            animator.SetBool("IsMoving", false);

            if (current == StateMachine.Stunned)
            {
                animator.SetBool("IsStunned", true);
                return;
            }
            if (current == StateMachine.Dead)
            {
                animator.SetBool("IsStunned", false);
                animator.SetBool("IsDead", true);
                animator.SetTrigger("Death");
                return;
            }
            if (current == StateMachine.Attack) return;

            // 여기부터 Idle / Chase 진입: 기절에서 나왔으면 Stunned → Idle 전이가 일어난다
            animator.SetBool("IsStunned", false);
        }

        /// <summary>
        /// 공격 Active 시작에 Attack 클립을 재생한다. 배율 = 클립 길이 ÷ Active 길이 → 클립이 Active 동안 정확히 1번 재생된다.
        /// 예고·회복·종료 단계에서는 아무것도 하지 않는다 (Animator 는 Exit Time 으로 Idle 에 돌아가 있다).
        /// </summary>
        private void OnAttackPhaseChanged(MonsterAttack attack, SkillPhase phase)
        {
            if (phase != SkillPhase.Active) return;

            animator.SetFloat("AttackSpeed", ReadAttackClipSeconds() / attack.ActiveDuration);
            animator.SetTrigger("Attack");
        }

        /// <summary>
        /// Chase 중 실제 이동 속도로 IsMoving 을 매 프레임 넣어 Idle ↔ Move 전이를 일으킨다.
        /// Move 면 재생 배율을 속도 / moveClipSpeed 로 맞춰 발 미끄러짐을 줄인다.
        /// </summary>
        private void UpdateMoveAnimation()
        {
            if (StateMachine.Current != StateMachine.Chase) return;

            float speed = movement.Velocity.magnitude;
            bool shouldMove = speed >= idleSpeedThreshold;
            animator.SetBool("IsMoving", shouldMove);

            if (shouldMove) animator.speed = speed / moveClipSpeed;
            else animator.speed = 1f;
        }

        /// <summary>몬스터별 Override Controller 의 Attack 슬롯 클립 길이 (s). 클립이 없으면 예외</summary>
        private float ReadAttackClipSeconds()
        {
            var overrideController = (AnimatorOverrideController)animator.runtimeAnimatorController;
            AnimationClip attackClip = overrideController[BaseAttackClipName];
            if (attackClip == null)
                throw new InvalidOperationException($"{name}: Override Controller 에 {BaseAttackClipName} 슬롯 클립이 없다");
            return attackClip.length;
        }

        /// <summary>대상이 없으면 ServiceLocator 의 플레이어를 브레인과 이동에 같은 값으로 넣는다</summary>
        private void AcquireTarget()
        {
            if (playerDead || StateMachine.HasTarget) return;
            if (ServiceLocator.TryGet(out IPlayerTarget player) == false) return; // 플레이어 등록 전
            StateMachine.SetTarget(player);
            SetMovementTarget(player);
        }

        /// <summary>
        /// 이동(군중)에 목표를 넣는다. null = 대상 상실(사망 등).
        /// 군중 전체가 같은 목표를 공유하고 CrowdCoordinator.SetTarget 은 호출마다 슬롯 예약을 비우므로, 같은 값이면 다시 설정하지 않는다.
        /// </summary>
        private void SetMovementTarget(IPlayerTarget target)
        {
            if (ReferenceEquals(movement.Target, target)) return;
            movement.SetTarget(target);
        }

        /// <summary>Awake 에서 테이블 MaxHp 로 체력을 채운다</summary>
        private void InitializeHealth(int maxHp)
        {
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "최대 체력은 1 이상");
            MaxHp = maxHp;
            CurrentHp = maxHp;
        }

        /// <summary>
        /// 무기가 IDamageable 로 호출한다. 맞을 때마다 DamageDealtEvent 를 발행하고,
        /// 체력이 0 이 된 순간 1번 브레인을 Dead 로 보낸다 (보상 지급).
        /// </summary>
        public void TakeDamage(in DamageInfo info)
        {
            if (MaxHp == 0) throw new InvalidOperationException($"{nameof(InitializeHealth)} 전에 피해를 받았다");
            if (IsAlive == false) return;

            CurrentHp = Mathf.Max(0, CurrentHp - info.Amount);
            bool isKill = IsAlive == false;
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(gameObject, info, isKill));
            if (isKill) StateMachine.Die(true);
        }

        private void OnStateMachineDied(bool grantReward)
        {
            StateMachine.SetTarget(null);
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false; // 시체는 피격 판정 없음
            EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(Def, transform.position, gameObject, grantReward));
            despawnTime = Time.time + despawnDelay;
        }

        /// <summary>보스 등장 공격의 잡몹 정리 (보스 경계)</summary>
        private void OnWipeRequested(MonsterWipeRequestedEvent e) => StateMachine.Die(e.GrantReward);

        /// <summary>라운드 정리: 처치 이벤트 없이 바로 회수</summary>
        private void OnRoundCleanup(RoundCleanupEvent e) => Despawn();

        private void OnPlayerDied(PlayerDiedEvent e)
        {
            playerDead = true;
            StateMachine.SetTarget(null);
            SetMovementTarget(null);
        }

        private void Despawn()
        {
            if (despawned) return;
            despawned = true;
            StateMachine.Cleanup();
            movement.enabled = false;
            enabled = false;
            ServiceLocator.Get<IMonsterFactory>().Despawn(gameObject);
        }

        private static MonsterAttackDef SingleBasicAttack(MonsterDef def)
        {
            if (def.BasicAttacks.Count != 1)
                throw new InvalidOperationException($"MonsterTable {def.Id}: 일반 몬스터의 기본 공격은 1개여야 한다 (현재 {def.BasicAttacks.Count})");
            return def.BasicAttacks[0];
        }

        /// <summary>플레이어 루트에서 IDamageable 을 찾는다 (Core 의 TryGetDamageable 과 같은 부모 방향 탐색)</summary>
        private static IDamageable FindPlayerDamageable(IPlayerTarget target) =>
            target.Root.GetComponentInParent<IDamageable>();
    }
}
