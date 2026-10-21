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
    /// FullReset = false 이면 다음 라운드 진행(BL-032): 강화·재화·수류탄 수는 유지.
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

    public readonly struct PlayerDiedEvent { }

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

    /// <summary>[플레이어 스태미나] → UI(HUD). 값이 바뀔 때마다</summary>
    public readonly struct PlayerStaminaChangedEvent
    {
        public readonly float Current, Max;
        public PlayerStaminaChangedEvent(float current, float max) { Current = current; Max = max; }
        public float Ratio => Max > 0f ? Current / Max : 0f;
    }

    // ── 무기 (E2) ──  [무기] → UI(HUD)·사운드
    public readonly struct AmmoChangedEvent
    {
        public readonly int Magazine, MagazineSize;
        public AmmoChangedEvent(int magazine, int magazineSize) { Magazine = magazine; MagazineSize = magazineSize; }
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

    public readonly struct ReloadFinishedEvent { }

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
    public readonly struct BossAppearedEvent
    {
        public readonly MonsterDef Boss;
        public readonly GameObject Instance;
        public BossAppearedEvent(MonsterDef boss, GameObject instance) { Boss = boss; Instance = instance; }
    }

    /// <summary>공격 단계 변화. 예고 표시·포효·공격음·회복 상태 표현에 사용 (BL-024~028)</summary>
    public readonly struct BossSkillPhaseChangedEvent
    {
        public readonly MonsterAttackDef Attack;
        public readonly SkillPhase Phase;
        public BossSkillPhaseChangedEvent(MonsterAttackDef attack, SkillPhase phase) { Attack = attack; Phase = phase; }
    }

    /// <summary>보스 페이즈 변화 (BossPhaseDef.Phase 기준, 0 = 멀쩡) (BL-028)</summary>
    public readonly struct BossDamageStageChangedEvent
    {
        public readonly int Stage;
        public BossDamageStageChangedEvent(int stage) { Stage = stage; }
    }

    public readonly struct BossDefeatedEvent
    {
        public readonly MonsterDef Boss;
        public readonly Vector3 Position;
        public BossDefeatedEvent(MonsterDef boss, Vector3 position) { Boss = boss; Position = position; }
    }

    // ── 보급 (E7) ──  [보급] → UI(낙하 위치 표시)·사운드
    public readonly struct SupplyDroppedEvent
    {
        public readonly SupplyDef Def;
        public readonly Vector3 Position;
        public SupplyDroppedEvent(SupplyDef def, Vector3 position) { Def = def; Position = position; }
    }

    public readonly struct SupplyCollectedEvent
    {
        public readonly SupplyDef Def;
        public SupplyCollectedEvent(SupplyDef def) { Def = def; }
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

    /// <summary>[UI(강화 선택 화면)] → 코어. 강화 선택 종료, 다음 라운드 시작 요청 (BL-032)</summary>
    public readonly struct RewardSelectionFinishedEvent { }
}
