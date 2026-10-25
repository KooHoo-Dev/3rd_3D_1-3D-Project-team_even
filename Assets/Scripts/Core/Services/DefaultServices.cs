using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Core.Services
{
    /// <summary>
    /// 플레이 시작 시 기본 구현(Fallback)을 등록. 실제 구현이 Register 되면 그쪽이 우선.
    /// (SubsystemRegistration 에서 ServiceLocator 가 초기화된 "뒤"인 BeforeSceneLoad 에 실행)
    /// </summary>
    internal static class CoreBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterFallbacks()
        {
            ServiceLocator.SetFallback<ISoundService>(new NullSoundService());
            ServiceLocator.SetFallback<IMonsterFactory>(new ResourcesMonsterFactory());
            ServiceLocator.SetFallback<ISpawnZoneProvider>(new RingSpawnZoneProvider());
            ServiceLocator.SetFallback<ICurrencyWallet>(new CurrencyWallet());
            ServiceLocator.SetFallback<IUpgradeState>(new UpgradeLedger());
        }
    }

    /// <summary>사운드 담당 구현 전까지 쓰는 빈 구현</summary>
    public sealed class NullSoundService : ISoundService
    {
        public void PlaySfx(string key, Vector3? position = null) { }
        public void PlaySfx(SoundDef sound, Vector3? position = null) { }
        public SoundHandle PlayLoop(SoundDef sound, Transform follow) => default;
        public void StopLoop(SoundHandle handle) { }
        public void PlayBgm(string key) { }
        public void StopBgm() { }
    }

    /// <summary>몬스터 담당 풀링 구현 전까지 쓰는 기본 팩토리. def.Prefab 을 Instantiate</summary>
    public sealed class ResourcesMonsterFactory : IMonsterFactory
    {
        public GameObject Spawn(MonsterDef def, Vector3 position, Quaternion rotation)
        {
            var go = Object.Instantiate(def.Prefab, position, rotation);
            EventBus<MonsterSpawnedEvent>.Publish(new MonsterSpawnedEvent(def, go));
            return go;
        }

        public void Despawn(GameObject monster)
        {
            if (monster != null) Object.Destroy(monster);
        }
    }

    /// <summary>
    /// 전장 담당 구현 전까지 쓰는 기본 생성 위치: 플레이어 주변 원형 링.
    /// Config.SpawnRingRadius 거리에서 생성하므로 "시작 직후 플레이어 주변은 비우기"(BL-015)도 기본 충족.
    /// </summary>
    public sealed class RingSpawnZoneProvider : ISpawnZoneProvider
    {
        private const float AirHeight = 8f;

        public bool TryGetSpawnPose(SpawnPattern pattern, int groupSeed, out Vector3 position, out Quaternion rotation)
        {
            GetPlayer(out var center, out var forward);
            float radius = DataManager.Config.GetFloat(GameConfig.SpawnRingRadius, 35f);
            float baseAngle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float angle;

            switch (pattern)
            {
                case SpawnPattern.Flank:   // 시야 밖: 측면~후방
                    angle = baseAngle + (Random.value < 0.5f ? 1f : -1f) * Random.Range(100f, 170f);
                    break;
                case SpawnPattern.Herd:    // 같은 groupSeed 는 한 방향에 밀집
                    angle = new System.Random(groupSeed).Next(0, 360) + Random.Range(-8f, 8f);
                    radius += Random.Range(0f, 5f);
                    break;
                default:                   // Surround, Air
                    angle = Random.Range(0f, 360f);
                    break;
            }

            float rad = angle * Mathf.Deg2Rad;
            position = center + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * radius;
            if (pattern == SpawnPattern.Air) position.y += AirHeight;

            Vector3 look = center - position;
            look.y = 0f;
            rotation = look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look) : Quaternion.identity;
            return true;
        }

        /// <summary>플레이어 기준 XZ 거리 SupplyDropMinDistance ~ SupplyDropMaxDistance 링 안. y 는 플레이어 높이 (지면 탐색은 보급 쪽에서)</summary>
        public bool TryGetSupplyDropPosition(out Vector3 position)
        {
            GetPlayer(out var center, out _);
            float min = DataManager.Config.GetFloat(GameConfig.SupplyDropMinDistance, 30f);
            float max = Mathf.Max(min, DataManager.Config.GetFloat(GameConfig.SupplyDropMaxDistance, 150f));
            // 면적 기준 균등 분포 (반지름을 그대로 Random 하면 안쪽에 몰린다)
            float distance = Mathf.Sqrt(Random.Range(min * min, max * max));
            float rad = Random.Range(0f, Mathf.PI * 2f);
            position = center + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * distance;
            return true;
        }

        private static void GetPlayer(out Vector3 position, out Vector3 forward)
        {
            if (ServiceLocator.TryGet<IPlayerTarget>(out var player) && player.Root != null)
            {
                position = player.Root.position;
                forward = player.ViewPoint != null ? player.ViewPoint.forward : player.Root.forward;
            }
            else
            {
                position = Vector3.zero;
                forward = Vector3.forward;
            }
        }
    }

    /// <summary>재화 지갑 기본 구현 (BL-029). 값이 바뀌면 CurrencyChangedEvent 발행</summary>
    public sealed class CurrencyWallet : ICurrencyWallet
    {
        public int Balance { get; private set; }

        public void Add(int amount)
        {
            if (amount <= 0) return;
            Balance += amount;
            EventBus<CurrencyChangedEvent>.Publish(new CurrencyChangedEvent(Balance, amount));
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Balance < amount) return false;
            Balance -= amount;
            EventBus<CurrencyChangedEvent>.Publish(new CurrencyChangedEvent(Balance, -amount));
            return true;
        }

        public void ResetTo(int amount)
        {
            int delta = amount - Balance;
            Balance = amount;
            EventBus<CurrencyChangedEvent>.Publish(new CurrencyChangedEvent(Balance, delta));
        }
    }

    /// <summary>강화 상태 기본 구현 (BL-030, BL-031). 레벨만 기억하고 수치는 UpgradeTrackDef 에서 계산</summary>
    public sealed class UpgradeLedger : IUpgradeState
    {
        private readonly Dictionary<UpgradeType, int> levels = new Dictionary<UpgradeType, int>();

        public int GetLevel(UpgradeType type) => levels.TryGetValue(type, out var lv) ? lv : 0;

        public float GetTotalValue(UpgradeType type)
            => DataManager.UpgradeTracks.TryGet(type, out var track) ? track.GetTotalValue(GetLevel(type)) : 0f;

        public UpgradeDef GetNext(UpgradeType type)
            => DataManager.UpgradeTracks.TryGet(type, out var track) ? track.GetNext(GetLevel(type)) : null;

        public bool TryPurchase(UpgradeType type, ICurrencyWallet wallet)
        {
            var next = GetNext(type);
            if (next == null || wallet == null || wallet.TrySpend(next.Cost) == false) return false;
            levels[type] = next.Level;
            EventBus<UpgradePurchasedEvent>.Publish(new UpgradePurchasedEvent(next));
            return true;
        }

        public void ResetAll() => levels.Clear();
    }
}
