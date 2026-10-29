using System;
using System.Collections.Generic;
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
    /// 체력(부위 없음, 단일 체력)을 직접 가지고(IDamageable), 매 프레임 상태 머신을 돌리고,
    /// 게임 이벤트(보스 정리·라운드 정리·플레이어 사망)를 상태 머신에 전달한다.
    /// </summary>
    /// <remarks>
    /// 조립 순서: Awake = Def·체력 준비 → Start = 공격·상태 머신 조립.
    /// 기본 IMonsterFactory 는 프리팹을 Instantiate 만 하므로 초기화는 이 클래스가 스스로 한다.
    /// 라이플·수류탄은 IDamageable 로 이 클래스를 찾아 피해를 준다.
    /// </remarks>
    [RequireComponent(typeof(MonsterMovement))]
    public sealed class MonsterController : MonoBehaviour, IDamageable, IMonsterSoundSource
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
        [Tooltip("회복이 끝난 뒤 다음 공격 시작까지 기다리는 시간 (s)")]
        [SerializeField, Min(0f)] private float cooldownOverrideSeconds = 1f;
        [Tooltip("Active 구간 최소 유지 시간 (s). 도약은 이 시간 동안 날고, Attack 클립은 이 시간에 맞춰 1번 재생된다. 다음 공격 시작부터 반영")]
        [SerializeField, Min(0.01f)] private float activeSeconds = 0.5f;
        [Tooltip("Active 구간 중 판정 시점 비율. 0 = Active 시작, 1 = Active 끝. 다음 공격 시작부터 반영")]
        [SerializeField, Range(0f, 1f)] private float hitTimeRatio = 0.5f;
        [Tooltip("예고 중 바라볼 지점으로 도는 속도 (도/초). 0 = 돌지 않는다. 다음 프레임부터 반영")]
        [SerializeField, Min(0f)] private float windupTurnSpeed = 360f;

        // Base Controller 의 Attack 상태 원본 클립 이름. Override 인덱서는 원본 클립 이름으로 슬롯을 찾는다
        private const string BaseAttackClipName = "Placeholder_Attack";

        private MonsterMovement movement;
        private bool playerDead;
        private float despawnTime = -1f;   // 회수 예정 시각 (s, Time.time 기준). -1 = 아직 처치 안 됨
        private bool despawned;

        // ── 애니메이션 ──
        private Animator animator;

        public int MonsterId => monsterId;
        public MonsterDef Def { get; private set; }
        public MonsterStateMachine StateMachine { get; private set; }

        // ── 소리 알림 (IMonsterSoundSource. MonsterSoundEmitter 가 구독) ──
        public event Action<MonsterAttackDef, SkillPhase> AttackPhaseChanged;
        public event Action<float> Stunned;
        public event Action<MonsterAttackDef, Vector3> AttackLanded;
        public event Action<MonsterAttackDef, Vector3> AttackContacted;
        public event Action Died;

        // ── 체력 ──
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }
        public Faction Faction => Faction.Monster;
        public bool IsAlive => CurrentHp > 0;

        /// <summary>
        /// 컴포넌트 참조를 잡고, monsterId 로 테이블 Def 를 읽어 체력을 채운다.
        /// </summary>
        /// <remarks>다른 컴포넌트의 초기화 결과는 아직 쓰지 않는다. 조립은 Start 가 맡는다.</remarks>
        private void Awake()
        {
            movement = GetComponent<MonsterMovement>();
            animator = GetComponent<Animator>();

            Def = DataManager.Monsters.Get(monsterId);
            InitializeHealth(Def.MaxHp);
        }

        /// <summary>
        /// Def 로 공격과 상태 머신을 만들고, 상태 머신·공격·게임 이벤트를 구독한다.
        /// </summary>
        /// <remarks>모든 컴포넌트 Awake 이후에 실행되어야 한다. 상태 머신 생성 직후 Idle.Stop 이 이동 실행기를 쓴다.</remarks>
        private void Start()
        {
            AssembleStateMachine();
            Subscribe();
        }

        /// <summary>
        /// 풀에서 다시 꺼낸 몬스터를 처음 소환된 상태로 되살린다.
        /// 풀(팩토리)이 재사용 개체를 켠 직후 1번 부른다. 처음 만든 개체는 Start 가 조립하므로 부르지 않는다.
        /// </summary>
        /// <remarks>
        /// Dead 는 상태 머신의 종료 상태라 상태 머신을 새로 만든다.
        /// 마지막에 enabled = true 로 OnEnable 을 태워 이동을 켜고 게임 이벤트를 다시 구독한다.
        /// Animator 는 GameObject 를 끄고 켤 때 초기화된다 (Keep Animator State On Disable 꺼짐 전제).
        /// </remarks>
        public void Respawn()
        {
            despawned = false;
            despawnTime = -1f;
            playerDead = false;
            InitializeHealth(Def.MaxHp);
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = true; // OnStateMachineDied 가 끈 피격 판정 복구
            AssembleStateMachine();
            enabled = true;
        }

        /// <summary>
        /// Def 로 공격 목록과 상태 머신을 만들고 상태 머신·공격 이벤트를 구독한다.
        /// Start(최초 조립)와 Respawn(재사용 시 재조립)이 호출한다.
        /// </summary>
        private void AssembleStateMachine()
        {
            // 모든 컴포넌트 Awake 이후 조립한다. Brain 생성 직후 Idle.Stop도 실행기를 안전하게 사용한다.
            var body = new MonsterBody(transform, GetComponent<NavMeshMoveExecutor>());
            var attackSettings = new MonsterAttackSettings(GetComponent<PoisonAreaSettings>(),
                                                          GetComponent<PounceSettings>(),
                                                          GetComponent<ChargeSettings>(),
                                                          GetComponent<DiveSettings>());
            var flyingExecutor = GetComponent<FlyingMoveExecutor>(); // 공중 프리팹에만 있다. 없으면 null
            var attacks = new List<MonsterAttack>();
            foreach (MonsterAttackDef attackDef in BasicAttackDefs(Def))
            {
                var attackSpec = AttackSpec.From(attackDef);
                attacks.Add(MonsterAttackFactory.CreateAttack(Def.Role, attackSpec, body, FindPlayerDamageable,
                                                              attackSettings, movement, flyingExecutor));
            }
            movement.SetCombatTuning(Def.MoveSpeed, attacks[0].Range); // 이동의 전투 사거리는 첫 공격 기준
            StateMachine = new MonsterStateMachine(movement, body, attacks);

            StateMachine.Died += OnStateMachineDied;
            StateMachine.StateChanged += OnStateChanged;
            foreach (MonsterAttack attack in StateMachine.Attacks)
            {
                attack.PhaseChanged += OnAttackPhaseChanged;
                attack.BodyLanded += OnAttackBodyLanded;
                attack.BodyContacted += OnAttackBodyContacted;
            }
        }

        /// <summary>
        /// 비활성 후 다시 켜질 때 이동과 게임 이벤트 구독을 되살린다.
        /// </summary>
        /// <remarks>최초 활성화(StateMachine == null)는 건너뛴다. 최초 구독은 Start 가 조립 후 한다.</remarks>
        private void OnEnable()
        {
            if (StateMachine == null) return; // 최초 구독은 Start의 조립이 끝난 뒤 수행한다.
            movement.enabled = true;
            Subscribe();
        }

        /// <summary>
        /// 몬스터가 반응하는 게임 이벤트 4개(보스 정리, 라운드 정리, 플레이어 사망, 플레이어 부활)를 구독한다.
        /// 해제는 OnDisable 이 한다.
        /// </summary>
        private void Subscribe()
        {
            EventBus<MonsterWipeRequestedEvent>.Subscribe(OnWipeRequested);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<PlayerDiedEvent>.Subscribe(OnPlayerDied);
            EventBus<PlayerRevivedEvent>.Subscribe(OnPlayerRevived);
        }

        /// <summary>
        /// 꺼질 때 상태 머신을 정리하고, 이동을 끄고, 게임 이벤트 구독을 해제한다.
        /// </summary>
        private void OnDisable()
        {
            if (StateMachine != null) StateMachine.Cleanup();
            if (movement != null) movement.enabled = false;
            EventBus<MonsterWipeRequestedEvent>.Unsubscribe(OnWipeRequested);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<PlayerDiedEvent>.Unsubscribe(OnPlayerDied);
            EventBus<PlayerRevivedEvent>.Unsubscribe(OnPlayerRevived);
        }

        /// <summary>
        /// 매 프레임 진입점.
        /// 죽었으면 회수 시각까지 기다렸다가 회수한다.
        /// 살아 있으면 대상 확보 → 공격 튜닝 → 상태 갱신 → 이동 애니 순서로 돈다.
        /// </summary>
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
        /// 인스펙터의 쿨다운·Active 길이·판정 비율·예고 회전 속도를 이 몬스터의 모든 공격에 넣는다.
        /// 덮어쓰기가 꺼져 있으면 쿨다운은 각 공격의 데이터 테이블 값을 쓴다.
        /// </summary>
        /// <remarks>쿨다운·Active 길이·판정 비율은 Begin 때 고정하므로 실제 반영은 다음 공격부터다. 예고 회전 속도는 다음 프레임부터 반영된다.</remarks>
        private void ApplyAttackTuning()
        {
            foreach (MonsterAttack attack in StateMachine.Attacks)
            {
                float cooldown = attack.Spec.Cooldown;
                if (useCooldownOverride) cooldown = cooldownOverrideSeconds;

                attack.Cooldown = cooldown;
                attack.ActiveSeconds = activeSeconds;
                attack.HitTimeRatio = hitTimeRatio;
                attack.WindupTurnSpeed = windupTurnSpeed;
            }
        }

        /// <summary>
        /// 상태 머신의 상태가 바뀔 때 Animator 의 Bool/Trigger 를 새 상태에 맞춘다.
        /// Stunned → IsStunned, Dead → IsDead + Death Trigger, Idle/Chase → IsStunned 해제.
        /// </summary>
        /// <remarks>
        /// Attack 신호는 보내지 않는다 (OnAttackPhaseChanged 담당).
        /// Idle/Chase 안의 정지·이동은 UpdateMoveAnimation 이 IsMoving 으로 정한다.
        /// IsDead 가 Any State 전이를 막아 Death 에서 나가지 않는다.
        /// </remarks>
        /// <param name="current">새로 들어간 상태</param>
        private void OnStateChanged(MonsterState previous, MonsterState current)
        {
            animator.speed = 1f;
            animator.SetBool("IsMoving", false);

            if (current == StateMachine.Stunned)
            {
                animator.SetBool("IsStunned", true);
                Stunned?.Invoke(StateMachine.StunDuration);
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
        /// 소리 쪽에 단계 변경을 알리고, 공격 Active 시작에 Attack 클립을 재생한다.
        /// 재생 배율 = 클립 길이 ÷ Active 길이 → 클립이 Active 동안 정확히 1번 재생된다.
        /// </summary>
        /// <remarks>
        /// 예고·회복·종료 단계는 무시한다. Animator 는 Exit Time 으로 이미 Idle 에 돌아가 있다.
        /// 이동 자체가 공격(UsesMoveAnimation, 돌진)이면 Attack 클립을 쓰지 않는다. 이동 클립은 UpdateMoveAnimation 이 맡는다.
        /// </remarks>
        /// <param name="attack">단계가 바뀐 공격. ActiveDuration 을 읽는다</param>
        /// <param name="phase">새 단계. Active 일 때만 반응한다</param>
        private void OnAttackPhaseChanged(MonsterAttack attack, SkillPhase phase)
        {
            AttackPhaseChanged?.Invoke(FindAttackDef(attack), phase); // 소리: 모든 단계를 알린다 (예고음 = Windup)
            if (phase != SkillPhase.Active) return;
            if (attack.UsesMoveAnimation) return;

            animator.SetFloat("AttackSpeed", ReadAttackClipSeconds() / attack.ActiveDuration);
            animator.SetTrigger("Attack");
        }

        /// <summary>도약 착지를 소리 쪽에 알린다</summary>
        private void OnAttackBodyLanded(MonsterAttack attack, Vector3 ground) => AttackLanded?.Invoke(FindAttackDef(attack), ground);

        /// <summary>돌진 명중(몸 충돌)을 소리 쪽에 알린다</summary>
        private void OnAttackBodyContacted(MonsterAttack attack, Vector3 point) => AttackContacted?.Invoke(FindAttackDef(attack), point);

        /// <summary>공격의 테이블 Def (Spec.SourceDefId). 테이블에 없으면 null</summary>
        private static MonsterAttackDef FindAttackDef(MonsterAttack attack) =>
            DataManager.MonsterAttacks.TryGet(attack.Spec.SourceDefId, out var def) ? def : null;

        /// <summary>
        /// Chase 중 실제 이동 속도로 IsMoving 을 매 프레임 넣어 Idle ↔ Move 전이를 일으킨다.
        /// 이동 자체가 공격인 몬스터(돌진)는 Attack 중과 Idle 준비 동작 중에도 공격이 알려준 몸 이동 속도로 같은 일을 한다.
        /// Move 면 재생 배율을 (속도 ÷ 클립 기준 속도) 로 맞춰 발 미끄러짐을 줄인다.
        /// 클립 기준 속도는 Chase 면 moveClipSpeed, 공격 이동 중이면 공격의 MoveClipSpeedFor (돌진 = ChargeSettings.ChargeClipSpeed).
        /// </summary>
        private void UpdateMoveAnimation()
        {
            MonsterAttack currentAttack = StateMachine.CurrentAttack;
            bool isChasing = StateMachine.Current == StateMachine.Chase;
            bool isAttacking = StateMachine.Current == StateMachine.Attack;
            bool isPreparingInIdle = StateMachine.Current == StateMachine.Idle && StateMachine.IsResting;
            bool isAttackMovingBody = currentAttack.UsesMoveAnimation && (isAttacking || isPreparingInIdle);
            if (isChasing == false && isAttackMovingBody == false) return;

            float speed = movement.Velocity.magnitude;
            float clipSpeed = moveClipSpeed;
            if (isAttackMovingBody)
            {
                speed = currentAttack.BodyMoveSpeed;
                clipSpeed = currentAttack.MoveClipSpeedFor(moveClipSpeed);
            }
            bool shouldMove = speed >= idleSpeedThreshold;
            animator.SetBool("IsMoving", shouldMove);

            if (shouldMove) animator.speed = speed / clipSpeed;
            else animator.speed = 1f;
        }

        /// <summary>
        /// 몬스터별 Override Controller 에서 Attack 슬롯 클립 길이를 읽는다.
        /// </summary>
        /// <returns>Attack 슬롯 클립 길이 (s)</returns>
        /// <exception cref="InvalidOperationException">Override Controller 에 Attack 슬롯 클립이 없을 때</exception>
        private float ReadAttackClipSeconds()
        {
            var overrideController = (AnimatorOverrideController)animator.runtimeAnimatorController;
            AnimationClip attackClip = overrideController[BaseAttackClipName];
            if (attackClip == null)
                throw new InvalidOperationException($"{name}: Override Controller 에 {BaseAttackClipName} 슬롯 클립이 없다");
            return attackClip.length;
        }

        /// <summary>
        /// 대상이 없으면 ServiceLocator 의 플레이어를 상태 머신과 이동에 같은 값으로 넣는다.
        /// 플레이어가 죽었거나 아직 등록 전이면 아무것도 하지 않는다.
        /// </summary>
        private void AcquireTarget()
        {
            if (playerDead) return;
            if (StateMachine.HasTarget) return;
            if (ServiceLocator.TryGet(out IPlayerTarget player) == false) return; // 플레이어 등록 전
            StateMachine.SetTarget(player);
            SetMovementTarget(player);
        }

        /// <summary>
        /// 이동(군중)에 목표를 넣는다. 지금 목표와 같으면 다시 넣지 않는다.
        /// </summary>
        /// <remarks>CrowdCoordinator.SetTarget 은 호출마다 군중 전체의 슬롯 예약을 비우므로 중복 호출을 막는다.</remarks>
        /// <param name="target">새 목표. null = 대상 상실 (플레이어 사망 등)</param>
        private void SetMovementTarget(IPlayerTarget target)
        {
            if (ReferenceEquals(movement.Target, target)) return;
            movement.SetTarget(target);
        }

        /// <summary>
        /// 최대 체력과 현재 체력을 같은 값으로 채운다. Awake 에서 1번 호출한다.
        /// </summary>
        /// <param name="maxHp">테이블의 MaxHp. 1 이상</param>
        /// <exception cref="ArgumentOutOfRangeException">maxHp 가 0 이하일 때</exception>
        private void InitializeHealth(int maxHp)
        {
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "최대 체력은 1 이상");
            MaxHp = maxHp;
            CurrentHp = maxHp;
        }

        /// <summary>
        /// 무기가 IDamageable 로 호출한다. 체력을 깎고 맞을 때마다 DamageDealtEvent 를 발행한다.
        /// 체력이 0 이 된 순간 1번 상태 머신을 Dead 로 보낸다 (보상 지급).
        /// 이미 죽었으면 무시한다.
        /// </summary>
        /// <param name="info">피해 정보. Amount 만큼 체력을 깎는다</param>
        /// <exception cref="InvalidOperationException">InitializeHealth 전에 호출됐을 때</exception>
        public void TakeDamage(in DamageInfo info)
        {
            if (MaxHp == 0) throw new InvalidOperationException($"{nameof(InitializeHealth)} 전에 피해를 받았다");
            if (IsAlive == false) return;

            CurrentHp = Mathf.Max(0, CurrentHp - info.Amount);
            bool isKill = IsAlive == false;
            Debug.Log($"{gameObject.name}: {info.Amount} 체력을 깎는다. 현재 체력: {CurrentHp}");
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(gameObject, info, isKill));
            if (isKill) StateMachine.Die(true);
        }

        /// <summary>
        /// 상태 머신이 Dead 에 들어갈 때 호출된다.
        /// 대상을 놓고, 콜라이더를 끄고, 공중 몬스터면 낙하를 시작하고, MonsterDiedEvent 를 발행하고, 회수 시각을 정한다.
        /// </summary>
        /// <param name="grantReward">true = 처치 보상 지급. MonsterDiedEvent 로 그대로 전달한다</param>
        private void OnStateMachineDied(bool grantReward)
        {
            StateMachine.SetTarget(null);
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false; // 시체는 피격 판정 없음
            var flyingExecutor = GetComponent<FlyingMoveExecutor>(); // 공중 프리팹에만 있다. 없으면 null
            if (flyingExecutor != null) flyingExecutor.StartFalling();
            Died?.Invoke();
            EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(Def, transform.position, gameObject, grantReward));
            despawnTime = Time.time + despawnDelay;
        }

        /// <summary>보스 등장 공격이 잡몹을 정리할 때 이 몬스터를 죽인다.</summary>
        /// <param name="e">GrantReward 로 보상 지급 여부를 정한다</param>
        private void OnWipeRequested(MonsterWipeRequestedEvent e) => StateMachine.Die(e.GrantReward);

        /// <summary>라운드 정리: 처치 이벤트 없이 바로 회수한다.</summary>
        private void OnRoundCleanup(RoundCleanupEvent e) => Despawn();

        /// <summary>
        /// 플레이어가 죽으면 상태 머신과 이동의 대상을 비우고, 이후 대상 확보를 멈춘다.
        /// 최종 사망이 아니다: 부활하면 OnPlayerRevived 가 대상 확보를 다시 연다.
        /// </summary>
        private void OnPlayerDied(PlayerDiedEvent e)
        {
            playerDead = true;
            StateMachine.SetTarget(null);
            SetMovementTarget(null);
        }

        /// <summary>
        /// 플레이어가 부활하면 대상 확보를 다시 연다. 실제 대상 지정은 다음 Update 의 AcquireTarget 이 한다.
        /// </summary>
        private void OnPlayerRevived(PlayerRevivedEvent e) => playerDead = false;

        /// <summary>
        /// 상태 머신·이동·이 컴포넌트를 끄고 팩토리에 회수를 맡긴다. 여러 번 불려도 1번만 실행한다.
        /// </summary>
        private void Despawn()
        {
            if (despawned) return;
            despawned = true;
            StateMachine.Cleanup();
            movement.enabled = false;
            enabled = false;
            ServiceLocator.Get<IMonsterFactory>().Despawn(gameObject);
        }

        /// <summary>
        /// 일반 몬스터 Def 에서 기본 공격 정의 전부를 꺼낸다. Start 가 공격 목록을 만들 때 쓴다.
        /// </summary>
        /// <param name="def">일반 몬스터 Def. BasicAttacks 가 1개 이상이어야 한다</param>
        /// <returns>기본 공격 정의 목록 (테이블 순서 그대로)</returns>
        /// <exception cref="InvalidOperationException">BasicAttacks 가 비어 있을 때</exception>
        private static IReadOnlyList<MonsterAttackDef> BasicAttackDefs(MonsterDef def)
        {
            if (def.BasicAttacks.Count == 0)
                throw new InvalidOperationException($"MonsterTable {def.Id}: 일반 몬스터의 기본 공격은 1개 이상이어야 한다");
            return def.BasicAttacks;
        }

        /// <summary>
        /// 공격 판정이 피해를 줄 플레이어 IDamageable 을 찾는다.
        /// Core 의 TryGetDamageable 과 같은 부모 방향 탐색이다.
        /// </summary>
        /// <param name="target">현재 공격 대상</param>
        /// <returns>target.Root 또는 그 부모의 IDamageable. 없으면 null</returns>
        private static IDamageable FindPlayerDamageable(IPlayerTarget target) =>
            target.Root.GetComponentInParent<IDamageable>();
    }
}
