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
    /// <summary>한 라운드의 보스를 생성·초기화하고, 사망을 기존 보상 이벤트에 연결한다.</summary>
    public sealed class RoundBossEncounter : MonoBehaviour
    {
        [SerializeField] private BossController bossPrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private int bossId = 1;

        private BossController boss;
        private BossDef bossDef;
        private IPlayerTarget player;
        private MonsterDef eventBoss;

        public event Action Defeated;

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

        public void Begin(MonsterDef legacyBoss)
        {
            Stop();
            eventBoss = legacyBoss;
            Transform point = spawnPoint != null ? spawnPoint : transform;
            boss = Instantiate(bossPrefab, point.position, point.rotation);
            boss.gameObject.SetActive(true);
            boss.Initialize(bossDef, player);

            var data = DataManager.Clypeosaurus;
            if (boss.TryGetComponent<StompAttack>(out var stomp)) stomp.Initialize(data, player);
            if (boss.TryGetComponent<MagmaBarrageAttack>(out var barrage)) barrage.Initialize(data, player);
            if (boss.TryGetComponent<SuctionAttack>(out var suction)) suction.Initialize(data, player);

            boss.Died += OnBossDied;
            EventBus<MonsterWipeRequestedEvent>.Publish(new MonsterWipeRequestedEvent(boss.transform.position, false));
            // 보스 이벤트의 MonsterDef는 현재 테이블 이전 중이라 null일 수 있다.
            EventBus<BossAppearedEvent>.Publish(new BossAppearedEvent(eventBoss, boss.gameObject));
        }

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
            previous.Died -= OnBossDied;
            previous.gameObject.SetActive(false);
            Destroy(previous.gameObject);
        }

        private void OnDisable() => Stop();
    }
}
