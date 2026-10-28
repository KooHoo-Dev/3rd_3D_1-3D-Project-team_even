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
        /// <summary>군중 생성 위치. Herd 는 같은 groupSeed 끼리 한 방향에 모인다. false = 이번 점은 소환 불가 (플레이어 안전 반경 안 등), 다시 요청하면 된다</summary>
        bool TryGetSpawnPose(SpawnPattern pattern, int groupSeed, out Vector3 position, out Quaternion rotation);
        /// <summary>보급 낙하 위치 (BL-018)</summary>
        bool TryGetSupplyDropPosition(out Vector3 position);
    }

    /// <summary>
    /// 소리 재생. 구현: UI·사운드 담당 (SoundService). 기본 구현: NullSoundService.
    /// 동시 재생 수·쿨다운·우선순위·거리 제한은 SoundTable 값으로 구현 쪽이 처리하므로, 호출하는 쪽은 그냥 부르면 된다.
    /// 같은 소리를 코드 직접 호출과 이벤트 구독 양쪽에서 재생하지 않는다 (소리 하나에 재생 경로 하나).
    /// </summary>
    public interface ISoundService
    {
        /// <summary>position 이 없으면 2D 로 재생 (SoundDef.Spatial 이 false 여도 2D)</summary>
        void PlaySfx(string key, Vector3? position = null);
        /// <summary>Def 를 이미 들고 있으면 이쪽 (키 조회 생략). null 이면 무시</summary>
        void PlaySfx(SoundDef sound, Vector3? position = null);
        /// <summary>
        /// follow 를 따라다니며 반복 재생 (날갯짓·이동). follow 가 꺼지거나 파괴되면 자동으로 멈춘다.
        /// MaxInstances 를 넘으면 듣는 위치에서 가까운 것만 실제로 소리가 나고, 나머지는 대기한다.
        /// </summary>
        SoundHandle PlayLoop(SoundDef sound, Transform follow);
        /// <summary>키만 아는 쪽용 (독가스 장판 등). 키가 없으면 default</summary>
        SoundHandle PlayLoop(string key, Transform follow);
        void StopLoop(SoundHandle handle);
        void PlayBgm(string key);
        void StopBgm();

        /// <summary>전체 볼륨 0~1 (옵션 화면). 카테고리 볼륨에 곱해진다</summary>
        float MasterVolume { get; set; }
        /// <summary>옵션 볼륨 0~1 (옵션 화면)</summary>
        float GetVolume(SoundCategory category);
        void SetVolume(SoundCategory category, float volume);
        /// <summary>category 볼륨을 holdTime 초 동안 level(0~1) 배로 낮춘다 (보스 포효 때 군중 소리 줄이기)</summary>
        void Duck(SoundCategory category, float level, float holdTime);
    }

    /// <summary>PlayLoop 가 돌려주는 번호표. default = 재생 안 됨</summary>
    public readonly struct SoundHandle
    {
        public readonly int Id;
        public SoundHandle(int id) { Id = id; }
        public bool IsValid => Id != 0;
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
