using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 게임 이벤트(EventBus) → 소리. 다른 파트 코드를 고치지 않고 소리를 붙이는 곳.
    /// 이 파일은 필드·구독만, 이벤트 처리는 영역별 partial 파일에 있다.
    ///   .Round   게임 상태(강화 화면 스냅샷)·라운드 시작·게임오버
    ///   .Combat  몬스터 이미터 자동 부착, 총기 피격·처치 피드백
    ///   .Boss    보스 경고·등장(BGM)·처치(BGM 정지·클리어·아나운서)
    ///   .Items   보급 투하(투하음·안내 방송)·획득(랜덤 장전음)·다음 라운드 지급, 강화 구매
    ///   .Ui      저체력 경고 반복, 아나운서
    ///
    /// 소리 하나에 재생 경로는 하나만 둔다. 아래 소리는 발행하는 쪽이 이미 직접 재생하므로 여기서 다루지 않는다.
    ///   WeaponFiredEvent·Reload·빈 탄창 (Gun), 폭발 (GunProjectile), 마그마 착탄 (MagmaProjectile)
    /// 객체 자기 몸 소리는 그 객체의 지역 이벤트를 받는 이미터 담당 (전역 이벤트로 받지 않는다):
    ///   플레이어(발소리·점프·착지·피격·사망·숨·투척) PlayerSoundEmitter / 일반 몬스터(울음·루프·공격·기절·사망) MonsterSoundEmitter
    ///   보스(패턴 예고·흡입 루프·타격·사망) BossSoundEmitter
    /// </summary>
    [RequireComponent(typeof(SoundService))]
    public sealed partial class GameSoundRouter : MonoBehaviour
    {
        [Header("SoundTable 키")]
        [SerializeField] private string crowdBgmKey = "bgm_crowd";
        [SerializeField] private string bossBgmKey = "bgm_boss";
        [SerializeField] private string bossWarningKey = "sfx_boss_warning";
        [Tooltip("총알이 몬스터에 맞은 위치에서 나는 타격음 (3D)")]
        [SerializeField] private string hitImpactKey = "sfx_hit_flesh";
        [Tooltip("명중 피드백 (2D). 처치한 발에는 내지 않는다 (처치는 몬스터 사망음 + UI 표시)")]
        [SerializeField] private string hitMarkerKey = "ui_hit_marker";
        [SerializeField] private string gameOverKey = "ui_game_over";
        [SerializeField] private string stageClearKey = "ui_stage_clear";
        [SerializeField] private string upgradeSelectKey = "ui_upgrade_select";
        [Tooltip("보스 클리어 후 다음 라운드 시작 지급 (2D)")]
        [SerializeField] private string roundSupplyKey = "sfx_supply_reload";
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

        [Header("몬스터 근접 울음 (공격 전에도 주변 몬스터를 소리로 알 수 있게)")]
        [Tooltip("플레이어와 이 거리(m) 안의 몬스터는 울음을 자주 내고, 들어오는 순간 곧 한 번 운다. 0 = 끔")]
        [SerializeField] private float monsterNearDistance = 15f;
        [Tooltip("근접 몬스터의 울음 간격(초) 최소 ~ 최대 (평소 간격은 MonsterSoundEmitter 의 4~10초)")]
        [SerializeField] private Vector2 monsterNearIdleInterval = new Vector2(1.5f, 3.5f);

        [Header("덕킹 (보스 경고·등장 포효 동안 몬스터 소리 줄이기)")]
        [Range(0f, 1f)][SerializeField] private float bossDuckLevel = 0.3f;
        [SerializeField] private float bossDuckTime = 3f;

        private SoundService service;

        /// <summary>같은 오브젝트의 SoundService 사용 (RequireComponent)</summary>
        private void Awake() => service = GetComponent<SoundService>();

        /// <summary>씬에 직접 둔 몬스터에 이미터 자동 부착 (생성되는 몬스터는 MonsterSpawnedEvent 에서)</summary>
        private void Start() => AttachEmittersInScene();

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
            EventBus<DamageDealtEvent>.Subscribe(OnDamageDealt);
            EventBus<PlayerHpChangedEvent>.Subscribe(OnPlayerHpChanged);
            EventBus<PlayerDiedEvent>.Subscribe(OnPlayerDied);
            EventBus<BossWarningEvent>.Subscribe(OnBossWarning);
            EventBus<BossAppearedEvent>.Subscribe(OnBossAppeared);
            EventBus<BossDefeatedEvent>.Subscribe(OnBossDefeated);
            EventBus<UpgradePurchasedEvent>.Subscribe(OnUpgradePurchased);
            EventBus<SupplyDroppedEvent>.Subscribe(OnSupplyDropped);
            EventBus<SupplyCollectedEvent>.Subscribe(OnSupplyCollected);
            EventBus<RoundSupplyGrantedEvent>.Subscribe(OnRoundSupplyGranted);
        }

        /// <summary>구독 해제</summary>
        private void OnDisable()
        {
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<GameOverEvent>.Unsubscribe(OnGameOver);
            EventBus<MonsterSpawnedEvent>.Unsubscribe(OnMonsterSpawned);
            EventBus<DamageDealtEvent>.Unsubscribe(OnDamageDealt);
            EventBus<PlayerHpChangedEvent>.Unsubscribe(OnPlayerHpChanged);
            EventBus<PlayerDiedEvent>.Unsubscribe(OnPlayerDied);
            EventBus<BossWarningEvent>.Unsubscribe(OnBossWarning);
            EventBus<BossAppearedEvent>.Unsubscribe(OnBossAppeared);
            EventBus<BossDefeatedEvent>.Unsubscribe(OnBossDefeated);
            EventBus<UpgradePurchasedEvent>.Unsubscribe(OnUpgradePurchased);
            EventBus<SupplyDroppedEvent>.Unsubscribe(OnSupplyDropped);
            EventBus<SupplyCollectedEvent>.Unsubscribe(OnSupplyCollected);
            EventBus<RoundSupplyGrantedEvent>.Unsubscribe(OnRoundSupplyGranted);
        }
    }
}
