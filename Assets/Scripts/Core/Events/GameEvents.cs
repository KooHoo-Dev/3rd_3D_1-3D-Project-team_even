using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Core.Events
{
    // ─────────────────────────────────────────────────────────────
    // 파트 간 이벤트 목록.  [발행자] → [주요 구독자]  (관련 백로그)
    // 새 이벤트를 추가하면 Docs/Architecture/02-SystemInterfaces.md 표도 갱신한다.
    // ─────────────────────────────────────────────────────────────

    // ── 게임 흐름 (E9) ──  [코어(RoundDirector)] → 전원
    public readonly struct GameStateChangedEvent
    {
        public readonly GameState Previous, Current;
        public GameStateChangedEvent(GameState previous, GameState current) { Previous = previous; Current = current; }
    }

    public readonly struct RoundStartedEvent
    {
        public readonly int Round;
        public readonly RoundDef Def;
        public RoundStartedEvent(int round, RoundDef def) { Round = round; Def = def; }
    }

    /// <summary>
    /// 라운드 정리 요청. 받으면 각 파트는 자기 몬스터·투사체·보급·예약된 코루틴/타이머를 전부 정리한다.
    /// FullReset = true 이면 처음부터 재시작(BL-033): 플레이어 위치·체력, 강화, 재화까지 초기화.
    /// FullReset = false 이면 다음 라운드 진행(BL-032): 강화·재화·수류탄 수는 유지. 총 탄약은 시작값으로, 보급 무기는 사라진다 (둘 다 공통).
    /// </summary>
    public readonly struct RoundCleanupEvent
    {
        public readonly bool FullReset;
        public RoundCleanupEvent(bool fullReset) { FullReset = fullReset; }
    }

    /// <summary>[코어] → 보스, UI·사운드(지진음·괴성·조명). 보스 등장 직전 짧은 신호 (BL-021)</summary>
    public readonly struct BossWarningEvent
    {
        public readonly MonsterDef Boss;
        public BossWarningEvent(MonsterDef boss) { Boss = boss; }
    }

    public readonly struct GameOverEvent
    {
        public readonly int ReachedRound;
        public GameOverEvent(int reachedRound) { ReachedRound = reachedRound; }
    }

    /// <summary>
    /// [최종 보스 최후 연출(Tyranopode)] → 코어(RoundSequencer). 최후 연출이 시작됨 = 이 보스를 처치하면 게임 클리어.
    /// 코어는 보급 투하를 멈추고, 보스가 죽어도 보상 단계(Reward)로 가지 않는다 (연출이 클리어 화면 → 메인 메뉴로 이끈다)
    /// </summary>
    public readonly struct BossFinaleStartedEvent { }

    /// <summary>[최종 보스 최후 연출(Tyranopode)] → 게임 클리어. 클리어 화면은 연출이 직접 띄운다 (다른 파트가 기록·사운드 등에 쓸 수 있게 알림만)</summary>
    public readonly struct GameClearedEvent { }

    /// <summary>[코어(RunDirector)] → 무기 해금 등. 보스를 처치하고 보상 단계(Reward)에 들어감 = 스테이지 클리어. 라운드마다 한 번</summary>
    public readonly struct StageClearedEvent
    {
        public readonly int Round;
        public StageClearedEvent(int round) { Round = round; }
    }

    // ── 플레이어 (E1) ──  [플레이어] → UI·사운드, 코어
    public readonly struct PlayerHpChangedEvent
    {
        public readonly int Current, Max;
        public PlayerHpChangedEvent(int current, int max) { Current = current; Max = max; }
        public float Ratio => Max > 0 ? (float)Current / Max : 0f;
    }

    /// <summary>피격 연출용 (BL-007: 화면 가장자리·피격음·조준 흔들림)</summary>
    public readonly struct PlayerDamagedEvent
    {
        public readonly DamageInfo Info;
        public PlayerDamagedEvent(in DamageInfo info) { Info = info; }
    }

    /// <summary>
    /// 사망 (체력 0). 최종 사망이 아니다: 부활 대기 시간 안에 부활(PlayerRevivedEvent)할 수 있다.
    /// 몬스터는 대상 확보를 멈추고, UI·사운드는 사망 연출. 게임오버는 PlayerFinalDeathEvent 로 판단한다.
    /// </summary>
    public readonly struct PlayerDiedEvent { }

    /// <summary>[플레이어] → 몬스터, UI·사운드. 부활 연출이 끝나 다시 조작할 수 있게 된 순간. 몬스터는 다시 대상을 잡는다</summary>
    public readonly struct PlayerRevivedEvent { }

    /// <summary>[플레이어] → 코어(게임오버), UI. 부활 대기 시간 안에 부활하지 못해 확정된 최종 사망. 이 뒤로는 부활하지 않는다</summary>
    public readonly struct PlayerFinalDeathEvent { }

    /// <summary>[플레이어] → UI. 남은 목숨(지금 목숨 포함) / 최대 목숨 변화. 시작할 때 한 번, 바뀔 때마다</summary>
    public readonly struct PlayerLivesChangedEvent
    {
        public readonly int Current, Max;
        public PlayerLivesChangedEvent(int current, int max) { Current = current; Max = max; }
    }

    /// <summary>
    /// [플레이어 카메라] → 보스, 코어, UI. 시점 전환 시작/완료 (BL-022).
    /// 보스는 IsComplete == true 를 받은 뒤에 등장 공격을 시작한다 (BL-023).
    /// </summary>
    public readonly struct CameraModeChangedEvent
    {
        public readonly CameraMode Mode;
        public readonly bool IsComplete;
        public CameraModeChangedEvent(CameraMode mode, bool isComplete) { Mode = mode; IsComplete = isComplete; }
    }

    /// <summary>
    /// [누구나] → 플레이어 카메라. 시점 전환 요청. Duration &lt; 0 이면 ConfigTable 의 CameraTransitionTime 사용, 0 이면 즉시.
    /// (보스 등장은 GameStateChangedEvent 로 자동 처리되므로, 이 이벤트는 연출/디버그용)
    /// </summary>
    public readonly struct CameraModeRequestedEvent
    {
        public readonly CameraMode Mode;
        public readonly float Duration;
        public CameraModeRequestedEvent(CameraMode mode, float duration = -1f) { Mode = mode; Duration = duration; }
    }

    /// <summary>
    /// [누구나(보스 등장·걸음·공격)] → 플레이어 카메라. 화면 흔들림 요청 (값은 CameraShakePresets 참고).
    /// Duration 0 이하(계속 흔들기)는 멈출 방법이 없어 무시한다 → 그런 흔들림은 PlayerController.Shake / StopShake 를 직접 쓴다
    /// </summary>
    public readonly struct CameraShakeRequestedEvent
    {
        public readonly CameraShakeSettings Settings;
        public CameraShakeRequestedEvent(CameraShakeSettings settings) { Settings = settings; }
    }

    /// <summary>[플레이어 스태미나] → UI(HUD). 값이 바뀔 때마다</summary>
    public readonly struct PlayerStaminaChangedEvent
    {
        public readonly float Current, Max;
        public PlayerStaminaChangedEvent(float current, float max) { Current = current; Max = max; }
        public float Ratio => Max > 0f ? Current / Max : 0f;
    }

    // ── 무기 (E2) ──  [무기] → UI(HUD)·사운드
    /// <summary>
    /// 지금 든 총의 탄약. 탄창 안 탄약(Magazine / MagazineSize)과 예비 탄창 개수(SpareMagazines / MaxMagazines)는 별개.
    /// InfiniteMagazines 면 예비 탄창이 줄지 않는다 (권총 등)
    /// </summary>
    public readonly struct AmmoChangedEvent
    {
        public readonly int Magazine, MagazineSize;
        public readonly int SpareMagazines, MaxMagazines;
        public readonly bool InfiniteMagazines;
        public AmmoChangedEvent(int magazine, int magazineSize, int spareMagazines, int maxMagazines, bool infiniteMagazines)
        {
            Magazine = magazine; MagazineSize = magazineSize;
            SpareMagazines = spareMagazines; MaxMagazines = maxMagazines; InfiniteMagazines = infiniteMagazines;
        }
    }

    public readonly struct WeaponFiredEvent
    {
        public readonly WeaponDef Weapon;
        public readonly Vector3 Muzzle;
        public WeaponFiredEvent(WeaponDef weapon, Vector3 muzzle) { Weapon = weapon; Muzzle = muzzle; }
    }

    /// <summary>재장전 시작. UI 는 Duration 으로 진행 표시 (BL-004)</summary>
    public readonly struct ReloadStartedEvent
    {
        public readonly float Duration;
        public ReloadStartedEvent(float duration) { Duration = duration; }
    }

    /// <summary>재장전 종료. 완료(탄창 충전)와 취소(구르기·피격 등, 탄·예비 탄창 변화 없음) 공통</summary>
    public readonly struct ReloadFinishedEvent { }

    /// <summary>예비 탄창이 없어 재장전할 수 없을 때 (UI 경고용)</summary>
    public readonly struct ReloadDeniedEvent { }

    /// <summary>[플레이어] → UI. 회복 팩 보유 개수 변화 (BL-019)</summary>
    public readonly struct HealPackCountChangedEvent
    {
        public readonly int Current, Max;
        public HealPackCountChangedEvent(int current, int max) { Current = current; Max = max; }
    }

    public readonly struct GrenadeCountChangedEvent
    {
        public readonly int Current, Max;
        public GrenadeCountChangedEvent(int current, int max) { Current = current; Max = max; }
    }

    /// <summary>[수류탄] → 몬스터(돌진형 유인 아이디어 BL-014), UI·사운드</summary>
    public readonly struct GrenadeExplodedEvent
    {
        public readonly GrenadeDef Grenade;
        public readonly Vector3 Position;
        public GrenadeExplodedEvent(GrenadeDef grenade, Vector3 position) { Grenade = grenade; Position = position; }
    }

    /// <summary>[피해를 준 쪽] → UI(히트마커), 몬스터 피격 연출</summary>
    public readonly struct DamageDealtEvent
    {
        public readonly GameObject Target;
        public readonly DamageInfo Info;
        public readonly bool IsKill;
        public DamageDealtEvent(GameObject target, in DamageInfo info, bool isKill) { Target = target; Info = info; IsKill = isKill; }
    }

    // ── 몬스터 (E3, E4) ──  [몬스터] → 코어(스폰 카운트), 보상(재화), UI·사운드
    public readonly struct MonsterSpawnedEvent
    {
        public readonly MonsterDef Def;
        public readonly GameObject Instance;
        public MonsterSpawnedEvent(MonsterDef def, GameObject instance) { Def = def; Instance = instance; }
    }

    /// <summary>
    /// 몬스터 처치. GrantReward = false 는 보스 등장 공격으로 정리된 경우 등 (보상 지급 여부는 BL-029 담당이 판단)
    /// </summary>
    public readonly struct MonsterDiedEvent
    {
        public readonly MonsterDef Def;
        public readonly Vector3 Position;
        public readonly GameObject Instance;
        public readonly bool GrantReward;
        public MonsterDiedEvent(MonsterDef def, Vector3 position, GameObject instance, bool grantReward = true)
        { Def = def; Position = position; Instance = instance; GrantReward = grantReward; }
    }

    /// <summary>[보스 등장 공격] → 몬스터. 살아있는 일반 몬스터 전부 처치 연출 후 제거 (BL-023)</summary>
    public readonly struct MonsterWipeRequestedEvent
    {
        public readonly Vector3 Origin;
        public readonly bool GrantReward;
        public MonsterWipeRequestedEvent(Vector3 origin, bool grantReward) { Origin = origin; GrantReward = grantReward; }
    }

    // ── 보스 (E5) ──  [보스] → 코어, UI·사운드, 플레이어(카메라 흔들림 등)
    // 패턴 단계·타격 같은 보스 몸 연출은 전역 이벤트가 아니라 보스 오브젝트의 지역 이벤트(BossAttack.PhaseChanged·Impacted, BossController.Died)로 받는다
    public readonly struct BossAppearedEvent
    {
        public readonly MonsterDef Boss;
        public readonly GameObject Instance;
        public BossAppearedEvent(MonsterDef boss, GameObject instance) { Boss = boss; Instance = instance; }
    }

    public readonly struct BossDefeatedEvent
    {
        public readonly MonsterDef Boss;
        public readonly Vector3 Position;
        public BossDefeatedEvent(MonsterDef boss, Vector3 position) { Boss = boss; Position = position; }
    }

    // ── 보급 (E7) ──  [보급] → UI(낙하 위치 표시)·사운드(투하음·안내 방송)·빛기둥
    public readonly struct SupplyDroppedEvent
    {
        public readonly SupplyDef Def;
        /// <summary>착지 예정 위치</summary>
        public readonly Vector3 Position;
        /// <summary>투하된 상자 (빛기둥 교체 등). 발행 직후에는 항상 살아 있다</summary>
        public readonly GameObject Crate;
        public SupplyDroppedEvent(SupplyDef def, Vector3 position, GameObject crate) { Def = def; Position = position; Crate = crate; }
    }

    // [보급] → UI(획득 알림)·사운드(랜덤 장전음)·분석
    public readonly struct SupplyCollectedEvent
    {
        public readonly SupplyDef Def;
        /// <summary>실제로 늘어난 아이템(수류탄·회복 팩) 개수. 가득 차 있었으면 0. 보급 무기(SupplyType.Weapon) 는 장착했으면 1, 같은 무기라 탄창만 받았으면 0</summary>
        public readonly int ItemAmount;
        /// <summary>슬롯의 모든 총에 실제로 채운 예비 탄창 합. 모두 가득 차 있었으면 0</summary>
        public readonly int MagazinesAdded;
        public readonly Vector3 Position;
        public SupplyCollectedEvent(SupplyDef def, int itemAmount, int magazinesAdded, Vector3 position)
        {
            Def = def; ItemAmount = itemAmount; MagazinesAdded = magazinesAdded; Position = position;
        }
    }

    /// <summary>[보급(RoundStartSupply)] → UI(지급 알림)·사운드. 보스 클리어 후 다음 라운드 시작 지급. 값은 실제로 늘어난 양 (하나도 안 늘면 발행 안 함)</summary>
    public readonly struct RoundSupplyGrantedEvent
    {
        public readonly int Round;
        public readonly int Grenades, HealPacks, MagazinesAdded;
        public RoundSupplyGrantedEvent(int round, int grenades, int healPacks, int magazinesAdded)
        {
            Round = round; Grenades = grenades; HealPacks = healPacks; MagazinesAdded = magazinesAdded;
        }
    }

    // ── 보상·강화 (E8) ──  [보상] → UI, 무기(능력치 갱신)
    public readonly struct CurrencyChangedEvent
    {
        public readonly int Balance, Delta;
        public CurrencyChangedEvent(int balance, int delta) { Balance = balance; Delta = delta; }
    }

    public readonly struct UpgradePurchasedEvent
    {
        public readonly UpgradeDef Upgrade;
        public UpgradePurchasedEvent(UpgradeDef upgrade) { Upgrade = upgrade; }
    }

    /// <summary>
    /// [보상(UpgradeLedger.ResetAll)] → 무기·수류탄·플레이어. 처음부터 재시작으로 모든 강화가 0 이 됨 (BL-033).
    /// RoundCleanupEvent(FullReset) 구독 순서와 상관없이 강화가 빠진 능력치로 다시 계산하게 한다
    /// </summary>
    public readonly struct UpgradesResetEvent { }

    /// <summary>[UI(강화 선택 화면)] → 코어. 강화 선택 종료, 다음 라운드 시작 요청 (BL-032)</summary>
    public readonly struct RewardSelectionFinishedEvent { }

    // ── 상호작용·보급 무기 ──  [플레이어] → UI(상호작용 프롬프트·슬롯 HUD), 사운드, 분석
    /// <summary>상호작용 대상(조준 중인 상자 등)이 바뀌었거나 프롬프트 내용이 바뀜. HasTarget = false 면 프롬프트 숨김</summary>
    public readonly struct InteractFocusChangedEvent
    {
        public readonly bool HasTarget;
        public readonly InteractPrompt Prompt;
        public InteractFocusChangedEvent(bool hasTarget, InteractPrompt prompt) { HasTarget = hasTarget; Prompt = prompt; }
    }

    /// <summary>
    /// 보급 칸(4번) 무기가 바뀜. Id 0 = 비어 있음 (처음부터 재시작 시 New = 0).
    /// Depleted = 탄을 모두 써서 사라짐 (라운드 정리·다른 보급 무기로 교체와 구분. 보급 확률이 무기 상자를 올리는 데 쓴다)
    /// </summary>
    public readonly struct SupplyWeaponChangedEvent
    {
        public readonly int PreviousWeaponId, NewWeaponId;
        public readonly bool Depleted;
        public SupplyWeaponChangedEvent(int previousWeaponId, int newWeaponId, bool depleted = false)
        {
            PreviousWeaponId = previousWeaponId; NewWeaponId = newWeaponId; Depleted = depleted;
        }
    }

    /// <summary>[코어(WeaponUnlockLedger)] → UI(해금 알림). 무기가 영구 해금됨 (다음 출발 때 무기 선택 화면에서 고를 수 있다)</summary>
    public readonly struct WeaponUnlockedEvent
    {
        public readonly WeaponDef Weapon;
        public WeaponUnlockedEvent(WeaponDef weapon) { Weapon = weapon; }
    }
}
