using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 게임 이벤트(EventBus) → 소리. 다른 파트 코드를 고치지 않고 소리를 붙이는 곳.
    /// 이 파일은 필드·구독만, 이벤트 처리는 영역별 partial 파일에 있다.
    ///   .Round   게임 상태(강화 화면 스냅샷)·라운드 시작·게임오버
    ///   .Combat  몬스터 생성·사망, 총기 피격·처치, 플레이어 피격
    ///   .Boss    보스 경고·등장·공격·페이즈·처치
    ///   .Items   강화 구매
    ///   .Ui      저체력 경고 반복, 아나운서
    ///
    /// 소리 하나에 재생 경로는 하나만 둔다. 아래 소리는 발행하는 쪽이 이미 직접 재생하므로 여기서 다루지 않는다.
    ///   WeaponFiredEvent·Reload (Gun), GrenadeExplodedEvent (GunProjectile), SupplyDroppedEvent (SupplyDropper)
    /// 플레이어 몸 소리(발소리·점프·착지·사망·숨·투척)는 PlayerSoundEmitter 담당.
    /// 일반 몬스터의 공격·울음·루프는 MonsterSoundEmitter 담당.
    /// </summary>
    [RequireComponent(typeof(SoundService))]
    public sealed partial class GameSoundRouter : MonoBehaviour
    {
        [Header("SoundTable 키")]
        [SerializeField] private string crowdBgmKey = "bgm_crowd";
        [SerializeField] private string bossBgmKey = "bgm_boss";
        [SerializeField] private string bossWarningKey = "sfx_boss_warning";
        [SerializeField] private string playerHitKey = "sfx_player_hit";
        [Tooltip("총알이 몬스터에 맞은 위치에서 나는 타격음 (3D)")]
        [SerializeField] private string hitImpactKey = "sfx_hit_flesh";
        [Tooltip("명중 피드백 (2D)")]
        [SerializeField] private string hitMarkerKey = "ui_hit_marker";
        [Tooltip("처치 피드백 (2D). 처치 시에는 히트마커 대신 재생")]
        [SerializeField] private string killConfirmKey = "ui_kill_confirm";
        [SerializeField] private string gameOverKey = "ui_game_over";
        [SerializeField] private string stageClearKey = "ui_stage_clear";
        [SerializeField] private string upgradeSelectKey = "ui_upgrade_select";
        [SerializeField] private string healthWarningKey = "ui_health_warning";

        [Header("아나운서 (최소 사용: 라운드 시작·보스 처치·게임오버)")]
        [SerializeField] private bool useAnnouncer = true;
        [SerializeField] private string announceFightKey = "ann_fight";
        [SerializeField] private string announceVictoryKey = "ann_victory";
        [SerializeField] private string announceGameOverKey = "ann_game_over";
        [Tooltip("효과음(게임오버·스테이지 클리어)과 겹치지 않도록 아나운서를 늦추는 시간(초)")]
        [SerializeField] private float announcerDelay = 0.8f;

        [Header("저체력 경고")]
        [Tooltip("체력 비율이 ConfigTable.LowHpRatio 이하인 동안 이 간격(초)으로 경고음 반복")]
        [SerializeField] private float healthWarningInterval = 2f;

        [Header("덕킹 (보스 경고·등장 포효 동안 몬스터 소리 줄이기)")]
        [Range(0f, 1f)][SerializeField] private float bossDuckLevel = 0.3f;
        [SerializeField] private float bossDuckTime = 3f;

        private SoundService service;

        /// <summary>같은 오브젝트의 SoundService 사용 (RequireComponent)</summary>
        private void Awake() => service = GetComponent<SoundService>();

        /// <summary>저체력 경고 반복·예약된 아나운서 재생 (강화 화면 timeScale 0 에서도 진행)</summary>
        private void Update()
        {
            UpdateHealthWarning();
            UpdateAnnouncer();
        }

        /// <summary>이벤트 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<RoundStartedEvent>.Subscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<GameOverEvent>.Subscribe(OnGameOver);
            EventBus<MonsterSpawnedEvent>.Subscribe(OnMonsterSpawned);
            EventBus<MonsterDiedEvent>.Subscribe(OnMonsterDied);
            EventBus<DamageDealtEvent>.Subscribe(OnDamageDealt);
            EventBus<PlayerDamagedEvent>.Subscribe(OnPlayerDamaged);
            EventBus<PlayerHpChangedEvent>.Subscribe(OnPlayerHpChanged);
            EventBus<PlayerDiedEvent>.Subscribe(OnPlayerDied);
            EventBus<BossWarningEvent>.Subscribe(OnBossWarning);
            EventBus<BossAppearedEvent>.Subscribe(OnBossAppeared);
            EventBus<BossSkillPhaseChangedEvent>.Subscribe(OnBossSkillPhaseChanged);
            EventBus<BossDamageStageChangedEvent>.Subscribe(OnBossDamageStageChanged);
            EventBus<BossDefeatedEvent>.Subscribe(OnBossDefeated);
            EventBus<UpgradePurchasedEvent>.Subscribe(OnUpgradePurchased);
        }

        /// <summary>구독 해제</summary>
        private void OnDisable()
        {
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<GameOverEvent>.Unsubscribe(OnGameOver);
            EventBus<MonsterSpawnedEvent>.Unsubscribe(OnMonsterSpawned);
            EventBus<MonsterDiedEvent>.Unsubscribe(OnMonsterDied);
            EventBus<DamageDealtEvent>.Unsubscribe(OnDamageDealt);
            EventBus<PlayerDamagedEvent>.Unsubscribe(OnPlayerDamaged);
            EventBus<PlayerHpChangedEvent>.Unsubscribe(OnPlayerHpChanged);
            EventBus<PlayerDiedEvent>.Unsubscribe(OnPlayerDied);
            EventBus<BossWarningEvent>.Unsubscribe(OnBossWarning);
            EventBus<BossAppearedEvent>.Unsubscribe(OnBossAppeared);
            EventBus<BossSkillPhaseChangedEvent>.Unsubscribe(OnBossSkillPhaseChanged);
            EventBus<BossDamageStageChangedEvent>.Unsubscribe(OnBossDamageStageChanged);
            EventBus<BossDefeatedEvent>.Unsubscribe(OnBossDefeated);
            EventBus<UpgradePurchasedEvent>.Unsubscribe(OnUpgradePurchased);
        }
    }
}
