using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    // 씬 전역 기능. ServiceLocator 로 주고받는다.
    // 구현이 아직 없어도 기본 구현(Fallback)이 등록되어 있어서 다른 파트가 막히지 않는다.

    /// <summary>
    /// 몬스터 생성/회수. 구현: 몬스터 담당 (풀링 권장 — 다수 몬스터 최적화).
    /// 기본 구현: ResourcesMonsterFactory (Instantiate/Destroy).
    /// 구현체는 생성 시 MonsterSpawnedEvent 를 발행해야 한다.
    /// </summary>
    public interface IMonsterFactory
    {
        GameObject Spawn(MonsterDef def, Vector3 position, Quaternion rotation);
        void Despawn(GameObject monster);
    }

    /// <summary>
    /// 전장의 생성 위치 계산. 구현: 전장 담당 (E6, 스폰 구역/보급 낙하 구역 배치).
    /// 기본 구현: RingSpawnZoneProvider (플레이어 주변 원형 링, 전장 없이도 동작).
    /// </summary>
    public interface ISpawnZoneProvider
    {
        /// <summary>군중 생성 위치. Herd 는 같은 groupSeed 끼리 한 방향에 모인다</summary>
        bool TryGetSpawnPose(SpawnPattern pattern, int groupSeed, out Vector3 position, out Quaternion rotation);
        /// <summary>보급 낙하 위치 (BL-018)</summary>
        bool TryGetSupplyDropPosition(out Vector3 position);
    }

    /// <summary>소리 재생. 구현: UI·사운드 담당. 기본 구현: NullSoundService</summary>
    public interface ISoundService
    {
        void PlaySfx(string key, Vector3? position = null);
        void PlayBgm(string key);
        void StopBgm();
    }

    /// <summary>재화 (BL-029). 기본 구현: CurrencyWallet (값이 바뀌면 CurrencyChangedEvent)</summary>
    public interface ICurrencyWallet
    {
        int Balance { get; }
        void Add(int amount);
        bool TrySpend(int amount);
        void ResetTo(int amount);
    }

    /// <summary>
    /// 구매한 강화 상태 (BL-030, BL-031). 기본 구현: UpgradeLedger.
    /// 무기·수류탄 쪽은 GetTotalValue 로 실제 능력치를 계산하고 UpgradePurchasedEvent 에 반응해 갱신한다.
    /// </summary>
    public interface IUpgradeState
    {
        int GetLevel(UpgradeType type);
        float GetTotalValue(UpgradeType type);
        /// <summary>다음 레벨 강화 정보 (최대 레벨이면 null)</summary>
        UpgradeDef GetNext(UpgradeType type);
        /// <summary>재화를 차감하고 레벨을 올린다. 성공 시 UpgradePurchasedEvent</summary>
        bool TryPurchase(UpgradeType type, ICurrencyWallet wallet);
        /// <summary>처음부터 다시 시작(BL-033) 시 초기화</summary>
        void ResetAll();
    }
}
