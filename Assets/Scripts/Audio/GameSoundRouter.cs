using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 게임 이벤트(EventBus) → 소리. 다른 파트 코드를 고치지 않고 소리를 붙이는 곳.
    ///
    /// 소리 하나에 재생 경로는 하나만 둔다. 아래 소리는 발행하는 쪽이 이미 직접 재생하므로 여기서 다루지 않는다.
    ///   WeaponFiredEvent·Reload (Gun), GrenadeExplodedEvent (GunProjectile), SupplyDroppedEvent (SupplyDropper)
    /// 일반 몬스터의 공격·울음·루프는 MonsterSoundEmitter(애니메이션 이벤트) 담당. 보스 공격은 여기서 BossSkillPhaseChangedEvent 로 재생.
    ///
    ///   몬스터 생성        MonsterSpawnedEvent         → 프리팹의 MonsterSoundEmitter 에 MonsterDef 연결
    ///   몬스터 사망        MonsterDiedEvent             → DeathSfx (죽은 위치, 보스 제외)
    ///   총기 피격·처치     DamageDealtEvent (Player→)   → 타격음(3D, Bullet 만) + 히트마커 / 처치 확인음(2D)
    ///   플레이어 피격      PlayerDamagedEvent           → sfx_player_hit (2D)
    ///   보스 경고·등장     BossWarningEvent / BossAppearedEvent → 경고음 + 몬스터 소리 덕킹 / 보스 BGM
    ///   보스 공격          BossSkillPhaseChangedEvent(Windup) → 공격 SfxKey (등장 공격이면 포효 + 덕킹)
    ///   보스 페이즈 전환   BossDamageStageChangedEvent  → HurtSfx
    ///   BGM               RoundStarted → 군중, BossAppeared → 보스, BossDefeated·GameOver → 정지
    ///   강화 화면          GameStateChangedEvent(Reward) → 믹서 Reward 스냅샷 (효과음·몬스터 먹먹하게), 벗어나면 Default
    /// </summary>
    [RequireComponent(typeof(SoundService))]
    public sealed class GameSoundRouter : MonoBehaviour
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

        [Header("덕킹 (보스 경고·등장 포효 동안 몬스터 소리 줄이기)")]
        [Range(0f, 1f)][SerializeField] private float bossDuckLevel = 0.3f;
        [SerializeField] private float bossDuckTime = 3f;

        private SoundService service;
        private MonsterDef boss;
        private GameObject bossInstance;

        /// <summary>같은 오브젝트의 SoundService 사용 (RequireComponent)</summary>
        private void Awake() => service = GetComponent<SoundService>();

        /// <summary>이벤트 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<RoundStartedEvent>.Subscribe(OnRoundStarted);
            EventBus<GameOverEvent>.Subscribe(OnGameOver);
            EventBus<MonsterSpawnedEvent>.Subscribe(OnMonsterSpawned);
            EventBus<MonsterDiedEvent>.Subscribe(OnMonsterDied);
            EventBus<DamageDealtEvent>.Subscribe(OnDamageDealt);
            EventBus<PlayerDamagedEvent>.Subscribe(OnPlayerDamaged);
            EventBus<BossWarningEvent>.Subscribe(OnBossWarning);
            EventBus<BossAppearedEvent>.Subscribe(OnBossAppeared);
            EventBus<BossSkillPhaseChangedEvent>.Subscribe(OnBossSkillPhaseChanged);
            EventBus<BossDamageStageChangedEvent>.Subscribe(OnBossDamageStageChanged);
            EventBus<BossDefeatedEvent>.Subscribe(OnBossDefeated);
        }

        /// <summary>구독 해제</summary>
        private void OnDisable()
        {
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<GameOverEvent>.Unsubscribe(OnGameOver);
            EventBus<MonsterSpawnedEvent>.Unsubscribe(OnMonsterSpawned);
            EventBus<MonsterDiedEvent>.Unsubscribe(OnMonsterDied);
            EventBus<DamageDealtEvent>.Unsubscribe(OnDamageDealt);
            EventBus<PlayerDamagedEvent>.Unsubscribe(OnPlayerDamaged);
            EventBus<BossWarningEvent>.Unsubscribe(OnBossWarning);
            EventBus<BossAppearedEvent>.Unsubscribe(OnBossAppeared);
            EventBus<BossSkillPhaseChangedEvent>.Unsubscribe(OnBossSkillPhaseChanged);
            EventBus<BossDamageStageChangedEvent>.Unsubscribe(OnBossDamageStageChanged);
            EventBus<BossDefeatedEvent>.Unsubscribe(OnBossDefeated);
        }

        /// <summary>강화 화면 동안만 Reward 스냅샷</summary>
        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            if (e.Current == GameState.Reward) service.TransitionToSnapshot(SoundService.RewardSnapshot);
            else if (e.Previous == GameState.Reward) service.TransitionToSnapshot(SoundService.DefaultSnapshot);
        }

        private void OnRoundStarted(RoundStartedEvent e)
        {
            boss = null;
            bossInstance = null;
            service.PlayBgm(crowdBgmKey);
        }

        private void OnGameOver(GameOverEvent e) => service.StopBgm();

        /// <summary>프리팹(자식 포함)의 MonsterSoundEmitter 에 Def 연결 → 울음·루프 시작</summary>
        private void OnMonsterSpawned(MonsterSpawnedEvent e)
        {
            if (e.Instance == null) return;
            var emitter = e.Instance.GetComponentInChildren<MonsterSoundEmitter>(true);
            if (emitter != null) emitter.Bind(e.Def);
        }

        /// <summary>보스는 BossDefeatedEvent 에서 처리 (보스가 MonsterDied 도 보내면 두 번 나므로)</summary>
        private void OnMonsterDied(MonsterDiedEvent e)
        {
            if (e.Def == null || e.Def.IsBoss) return;
            service.PlaySfx(e.Def.DeathSfx, e.Position);
        }

        /// <summary>
        /// 플레이어가 준 피해만. 타격음은 총알(Bullet)만 (폭발은 폭발음이 이미 덮음), 피드백은 처치면 처치 확인음 아니면 히트마커.
        /// 한 프레임 여러 발(샷건·폭발 범위)은 SoundTable 의 Cooldown·MaxInstances 가 걸러준다
        /// </summary>
        private void OnDamageDealt(DamageDealtEvent e)
        {
            if (e.Info.AttackerFaction != Faction.Player) return;
            if (e.Info.Kind == DamageKind.Bullet) service.PlaySfx(hitImpactKey, e.Info.HitPoint);
            service.PlaySfx(e.IsKill ? killConfirmKey : hitMarkerKey);
        }

        private void OnPlayerDamaged(PlayerDamagedEvent e) => service.PlaySfx(playerHitKey);

        private void OnBossWarning(BossWarningEvent e)
        {
            service.PlaySfx(bossWarningKey);
            service.Duck(SoundCategory.Monster, bossDuckLevel, bossDuckTime);
        }

        private void OnBossAppeared(BossAppearedEvent e)
        {
            boss = e.Boss;
            bossInstance = e.Instance;
            service.PlayBgm(bossBgmKey);
        }

        /// <summary>예고(Windup) 시작에 공격 소리 → 플레이어가 소리로 피할 타이밍을 안다</summary>
        private void OnBossSkillPhaseChanged(BossSkillPhaseChangedEvent e)
        {
            if (e.Phase != SkillPhase.Windup || e.Attack == null) return;
            if (e.Attack.IsEntrance) service.Duck(SoundCategory.Monster, bossDuckLevel, bossDuckTime);
            service.PlaySfx(e.Attack.Sfx, BossPosition());
        }

        /// <summary>Stage 0 = 멀쩡 (처음 상태 통지)이므로 1 이상에서만</summary>
        private void OnBossDamageStageChanged(BossDamageStageChangedEvent e)
        {
            if (e.Stage <= 0 || boss == null) return;
            service.PlaySfx(boss.HurtSfx, BossPosition());
        }

        private void OnBossDefeated(BossDefeatedEvent e)
        {
            service.PlaySfx(e.Boss?.DeathSfx, e.Position);
            service.StopBgm();
            boss = null;
            bossInstance = null;
        }

        /// <summary>보스 위치. 인스턴스를 모르면 null → 2D 재생 (보스 소리는 어디서든 들려야 하므로)</summary>
        private Vector3? BossPosition() => bossInstance != null ? bossInstance.transform.position : (Vector3?)null;
    }
}
