using System;
using HybridShooter.Boss;
using HybridShooter.Boss.Clypeosaurus;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Round
{
    /// <summary>
    /// 한 라운드의 보스를 생성·초기화하고, 사망을 기존 보상 이벤트에 연결한다.
    /// 보스는 RoundTable.BossId → BossTable 행의 Prefab(Resources 경로)으로 고른다.
    /// 인스펙터 bossId 와 같은 보스는 인스펙터 bossPrefab 을 쓰고, 찾지 못하면 인스펙터 보스로 대신한다.
    /// </summary>
    public sealed class RoundBossEncounter : MonoBehaviour
    {
        [SerializeField] private BossController bossPrefab;
        [SerializeField] private Transform spawnPoint;
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
