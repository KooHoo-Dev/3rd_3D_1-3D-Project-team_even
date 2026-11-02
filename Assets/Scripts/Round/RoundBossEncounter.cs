using System;
using HybridShooter.Boss;
using HybridShooter.Boss.Clypeosaurus;
using HybridShooter.Core.Battlefield;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Round
{
    /// <summary>
    /// 한 라운드의 보스를 SpawnPointFinder 가 고른 위치에 생성·초기화하고, 사망을 기존 보상 이벤트에 연결한다.
    /// 보스는 RoundTable.BossId → BossTable 행의 Prefab(Resources 경로)으로 고른다.
    /// 인스펙터 bossId 와 같은 보스는 인스펙터 bossPrefab 을 쓰고, 찾지 못하면 인스펙터 보스로 대신한다.
    /// </summary>
    public sealed class RoundBossEncounter : MonoBehaviour
    {
        [SerializeField] private BossController bossPrefab;
        [Tooltip("자동 위치 선택에 실패했을 때 쓰는 대체 스폰 위치. 비면 이 컴포넌트 위치")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("플레이어 기준으로 보스 스폰 위치를 고르는 탐색기")]
        [SerializeField] private SpawnPointFinder spawnPointFinder;
        [SerializeField] private int bossId = 1;

        private BossController boss;
        private EntranceAttack entrance;
        private IBossEncounterInitializer initializer;
        private BossDef bossDef;
        private IPlayerTarget player;
        private MonsterDef eventBoss;

        public event Action Defeated;
        public bool IsEntranceComplete => boss != null && (entrance == null || entrance.IsRunning == false)
                                          && (initializer == null || initializer.IsEntranceRunning == false);

        public bool Prepare()
        {
            if (bossPrefab == null)
            {
                var prefab = Resources.Load<GameObject>("Boss/Clypeosaurus/Prefab/Clypeosaurus");
                if (prefab != null) bossPrefab = prefab.GetComponent<BossController>();
            }
            if (spawnPointFinder == null)
            {
                Debug.LogError("[RoundBossEncounter] SpawnPointFinder를 연결해주세요.", this);
                return false;
            }
            if (bossPrefab == null || DataManager.Bosses.TryGet(bossId, out bossDef) == false || ServiceLocator.TryGet(out player) == false || player?.Root == null)
            {
                Debug.LogError("[RoundBossEncounter] 보스 프리팹·BossTable 데이터·플레이어 연결을 확인해주세요.", this);
                return false;
            }
            return true;
        }

        public void Begin(MonsterDef legacyBoss) => Begin(legacyBoss, bossId);

        /// <param name="legacyBoss">보스 이벤트에 실을 옛 MonsterDef (null 가능)</param>
        /// <param name="roundBossId">이번 라운드 보스의 BossTable.Id (RoundTable.BossId)</param>
        public void Begin(MonsterDef legacyBoss, int roundBossId)
        {
            Stop();
            eventBoss = legacyBoss;
            if (TryResolveBoss(roundBossId, out var prefab, out var def) == false)
            {
                prefab = bossPrefab;
                def = bossDef;
            }
            Transform point = spawnPoint != null ? spawnPoint : transform;
            boss = Instantiate(prefab, point.position, point.rotation);
            boss.gameObject.SetActive(true);
            MoveToFoundSpawnPoint();
            boss.Died += OnBossDied;
            entrance = boss.GetComponent<EntranceAttack>();
            if (entrance != null) entrance.Impacted += OnEntranceImpacted;

            initializer = boss.GetComponent<IBossEncounterInitializer>();
            if (initializer != null) initializer.Initialize(def, player);
            else if (boss.TryGetComponent<ClypeosaurusController>(out var controller)) controller.Initialize(def, player);
            else boss.Initialize(def, player);

            // 스스로 등장 연출을 하는 보스는 남은 몬스터를 연출 안에서 정리한다
            if (entrance == null && (initializer == null || initializer.IsEntranceRunning == false)) RequestMonsterWipe(boss.transform.position);
            // 보스 이벤트의 MonsterDef는 현재 테이블 이전 중이라 null일 수 있다.
            EventBus<BossAppearedEvent>.Publish(new BossAppearedEvent(eventBoss, boss.gameObject));
        }
        
        private bool TryResolveBoss(int id, out BossController prefab, out BossDef def)
        {
            prefab = null;
            if (DataManager.Bosses.TryGet(id, out def) == false) return false;
            if (id == bossId && bossPrefab != null)
            {
                prefab = bossPrefab;
                return true;
            }
            var loaded = string.IsNullOrEmpty(def.Prefab) ? null : Resources.Load<GameObject>(def.Prefab);
            if (loaded != null) prefab = loaded.GetComponent<BossController>();
            if (prefab == null) Debug.LogWarning($"[RoundBossEncounter] BossTable {id} 의 Prefab '{def.Prefab}' 을 찾지 못해 인스펙터 보스로 대신합니다.", this);
            return prefab != null;
        }

        /// <summary>BossTable 에서 보스와 프리팹을 찾는다. 인스펙터 보스와 같은 Id 면 인스펙터 프리팹을 쓴다</summary>
        private bool TryResolveBoss(int id, out BossController prefab, out BossDef def)
        {
            prefab = null;
            if (DataManager.Bosses.TryGet(id, out def) == false) return false;
            if (id == bossId && bossPrefab != null)
            {
                prefab = bossPrefab;
                return true;
            }
            var loaded = string.IsNullOrEmpty(def.Prefab) ? null : Resources.Load<GameObject>(def.Prefab);
            if (loaded != null) prefab = loaded.GetComponent<BossController>();
            if (prefab == null) Debug.LogWarning($"[RoundBossEncounter] BossTable {id} 의 Prefab '{def.Prefab}' 을 찾지 못해 인스펙터 보스로 대신합니다.", this);
            return prefab != null;
        }

        /// <summary>
        /// 방금 생성한 보스를 SpawnPointFinder 가 플레이어 기준으로 고른 위치로 옮기고, 플레이어를 바라보게 한다.
        /// Begin 이 Initialize·입장 연출 구독 전에 부른다. 못 찾으면 에러를 남기고 생성 위치(대체 스폰 위치)에 그대로 둔다.
        /// </summary>
        /// <remarks>보스 루트의 몸통 Collider 크기를 쓰므로 활성화된 인스턴스에서만 부른다.</remarks>
        private void MoveToFoundSpawnPoint()
        {
            Vector3 playerPosition = player.Root.position;
            Collider bossCollider = boss.GetComponent<Collider>();

            bool isFound = spawnPointFinder.TryFindSpawnPoint(playerPosition, bossCollider, out Vector3 foundPoint);
            if (isFound == false)
            {
                Debug.LogError("[RoundBossEncounter] 보스를 놓을 평탄한 위치를 찾지 못해 대체 스폰 위치를 씁니다.", this);
                return;
            }

            WarpAgent(foundPoint);
            boss.transform.SetPositionAndRotation(foundPoint, FacePlayer(foundPoint, playerPosition));
        }

        /// <summary>
        /// 보스에 NavMeshAgent 가 있으면 찾은 위치로 Warp 한다. NavMeshAgent 는 transform 만 옮기면 내부 위치가 어긋나기 때문이다.
        /// 생성 위치(대체 스폰 위치)가 NavMesh 밖이어도 Warp 는 목적지 기준이라 시도한다. 목적지 근처에 NavMesh 가 없으면 에러를 남긴다.
        /// 꺼진 NavMeshAgent 는 Warp 할 수 없으니 건너뛴다 (켜는 쪽이 그때 위치를 잡는다).
        /// </summary>
        /// <param name="foundPoint">SpawnPointFinder 가 찾은 위치</param>
        private void WarpAgent(Vector3 foundPoint)
        {
            if (boss.TryGetComponent<NavMeshAgent>(out var agent) == false) return;
            if (agent.enabled == false) return;

            bool isWarped = agent.Warp(foundPoint);
            if (isWarped == false) Debug.LogError($"[RoundBossEncounter] 보스 위치 {foundPoint} 근처에 NavMesh 가 없어 NavMeshAgent 를 옮기지 못했습니다. NavMesh 베이크 범위를 확인해주세요.", this);
        }

        /// <summary>보스 위치에서 플레이어를 수평으로 바라보는 회전</summary>
        /// <param name="bossPosition">보스 위치</param>
        /// <param name="playerPosition">플레이어 위치</param>
        /// <returns>회전. 두 점이 수평으로 겹치면 지금 보스 회전 그대로</returns>
        private Quaternion FacePlayer(Vector3 bossPosition, Vector3 playerPosition)
        {
            Vector3 flatDirectionToPlayer = playerPosition - bossPosition;
            flatDirectionToPlayer.y = 0f;
            if (flatDirectionToPlayer == Vector3.zero) return boss.transform.rotation;

            return Quaternion.LookRotation(flatDirectionToPlayer);
        }

        private void OnEntranceImpacted(BossAttack attack, Vector3 position) => RequestMonsterWipe(position);

        private static void RequestMonsterWipe(Vector3 position) => EventBus<MonsterWipeRequestedEvent>.Publish(new MonsterWipeRequestedEvent(position, false));

        private void OnBossDied()
        {
            if (boss == null) return;
            Vector3 position = boss.transform.position;
            Stop();
            EventBus<BossDefeatedEvent>.Publish(new BossDefeatedEvent(eventBoss, position));
            Defeated?.Invoke();
        }

        public void Stop()
        {
            if (boss == null) return;
            var previous = boss;
            boss = null;
            if (entrance != null) entrance.Impacted -= OnEntranceImpacted;
            entrance = null;
            initializer = null;
            previous.Died -= OnBossDied;
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
        }

        private void OnDisable() => Stop();
    }
}
