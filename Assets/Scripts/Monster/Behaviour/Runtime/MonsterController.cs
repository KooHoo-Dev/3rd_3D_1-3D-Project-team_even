using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 1마리의 Unity 진입점 (Facade).
    /// 프리팹에 monsterId 를 넣어 두면 Awake 에서 MonsterDef 를 읽어 체력·이동·공격·브레인을 조립한다.
    /// (기본 IMonsterFactory 는 프리팹을 Instantiate 만 하므로 초기화는 여기서 스스로 한다)
    /// 매 프레임 브레인을 돌리고, 대상 확보·처치·정리 이벤트를 브레인에 전달한다.
    /// </summary>
    [RequireComponent(typeof(MonsterHealth), typeof(SimpleChaseMover))]
    public sealed class MonsterController : MonoBehaviour
    {
        [Tooltip("MonsterTable Id")]
        [SerializeField] private int monsterId;
        [Tooltip("처치 후 회수까지 (s). 처치 연출 시간")]
        [SerializeField, Min(0f)] private float despawnDelay = 2f;

        private MonsterHealth health;
        private SimpleChaseMover mover;
        private bool playerDead;
        private float despawnTime = -1f;
        private bool despawned;

        public MonsterDef Def { get; private set; }
        public MonsterBrain Brain { get; private set; }

        private void Awake()
        {
            health = GetComponent<MonsterHealth>();
            mover = GetComponent<SimpleChaseMover>();

            Def = DataManager.Monsters.Get(monsterId);
            health.Initialize(Def.MaxHp);
            mover.MoveSpeed = Def.MoveSpeed;

            var body = new TransformMonsterBody(transform);
            var attack = MonsterBrainFactory.CreateAttack(Def.Role, AttackSpec.From(SingleBasicAttack(Def)), body, FindPlayerDamageable);
            Brain = new MonsterBrain(mover, body, attack);

            Brain.Died += OnBrainDied;
            health.Depleted += OnHealthDepleted;
        }

        private void OnEnable()
        {
            EventBus<MonsterWipeRequestedEvent>.Subscribe(OnWipeRequested);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<PlayerDiedEvent>.Subscribe(OnPlayerDied);
        }

        private void OnDisable()
        {
            EventBus<MonsterWipeRequestedEvent>.Unsubscribe(OnWipeRequested);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<PlayerDiedEvent>.Unsubscribe(OnPlayerDied);
        }

        private void Update()
        {
            if (Brain.IsDead)
            {
                if (Time.time >= despawnTime) Despawn();
                return;
            }

            AcquireTarget();
            Brain.Update(Time.time, Time.deltaTime);
        }

        /// <summary>대상이 없으면 ServiceLocator 의 플레이어를 브레인과 이동에 같은 값으로 넣는다</summary>
        private void AcquireTarget()
        {
            if (playerDead || Brain.HasTarget) return;
            if (ServiceLocator.TryGet(out IPlayerTarget player) == false) return; // 플레이어 등록 전
            Brain.SetTarget(player);
            mover.Target = player;
        }

        private void OnHealthDepleted() => Brain.Die(true);

        private void OnBrainDied(bool grantReward)
        {
            mover.Target = null;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false; // 시체는 피격 판정 없음
            EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(Def, transform.position, gameObject, grantReward));
            despawnTime = Time.time + despawnDelay;
        }

        /// <summary>보스 등장 공격의 잡몹 정리 (보스 경계)</summary>
        private void OnWipeRequested(MonsterWipeRequestedEvent e) => Brain.Die(e.GrantReward);

        /// <summary>라운드 정리: 처치 이벤트 없이 바로 회수</summary>
        private void OnRoundCleanup(RoundCleanupEvent e) => Despawn();

        private void OnPlayerDied(PlayerDiedEvent e)
        {
            playerDead = true;
            Brain.SetTarget(null);
            mover.Target = null;
        }

        private void Despawn()
        {
            if (despawned) return;
            despawned = true;
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
