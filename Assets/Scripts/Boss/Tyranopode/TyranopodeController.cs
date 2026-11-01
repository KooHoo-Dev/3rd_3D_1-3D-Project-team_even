using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using TMPro;
using UnityEngine;
using Random = UnityEngine.Random;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 두 번째 보스 Tyranopode 의 두뇌. BossController(체력·피격 이벤트)·BossAttack(패턴 단계)은 그대로 쓰고, 그 위에서
    ///  - 흐름: 등장 연출(TyranopodeIntro: 벽 너머에서 날아와 착지 → 웨이브 몬스터 흡수 → 포효) → 전투(공격 사이 맴돌기) ⇄ 움찔(부위 파괴)·그로기 → 페이즈 전환(무적 포효) → 페이즈 2 → 사망
    ///  - 패턴 선택: 거리 구간 가중치 × 부위 파괴 × 난이도 보정, 같은 패턴 연속은 덜 고른다. 끝나면 확률로 바로 이어 공격(연속)
    ///  - 부위: TyranopodePart 피해를 배율 적용해 보스 체력에 넘기고, 부위 체력이 다하면 파괴
    ///  - 페이즈 2: 체력 Phase2HpRatio 이하 또는 부위 Phase2BrokenParts 개 파괴 중 먼저 오는 쪽, 한 번만
    /// 라운드에서는 RoundBossEncounter 가 Initialize 를 부르고, 씬에 직접 놓으면 Start 에서 스스로 초기화한다
    /// </summary>
    [RequireComponent(typeof(BossController), typeof(TyranopodeView), typeof(TyranopodeLocomotion))]
    public sealed class TyranopodeController : MonoBehaviour, IBossEncounterInitializer, IBossCameraProfile
    {
        /// <summary>이번 실행에서 등장 연출을 본 적이 있나 (두 번째부터 짧은 연출)</summary>
        private static bool introPlayed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => introPlayed = false;

        private const float ComboDelay = 0.15f;
        private const float RetryDelay = 0.25f;
        private const float RepeatWeight = 0.35f;
        private const float AfterGroggyDelay = 0.8f;
        /// <summary>몸통 외 히트박스 레이어 (TyranopodePrefabBuilder 가 만든다)</summary>
        public const string HitboxLayerName = "BossHitbox";

        private enum State { Waiting, Intro, Fight, Flinch, Groggy, Transition, Finale, Dead }

        private sealed class PartState
        {
            public int Hp;
            public bool Broken;
            public readonly List<TyranopodePart> Parts = new List<TyranopodePart>();
        }

        [Header("씬에 직접 놓고 테스트할 때 (라운드에서는 비워 둔다)")]
        [SerializeField] private MonoBehaviour playerSource;

        [Header("참조")]
        [Tooltip("침·알·포자가 나오는 곳")]
        [SerializeField] private Transform mouth;
        [SerializeField] private Material telegraphMaterial;
        [SerializeField] private TyranopodeSpit spitPrefab;
        [SerializeField] private TyranopodeEgg eggPrefab;
        [SerializeField] private SummonBeacon beaconPrefab;

        [Header("연출 (TyranopodeVfxBuilder 가 만든다. 비어 있으면 생성 순간 오류)")]
        [Tooltip("포효: 입에서 뿜는 가스")]
        [SerializeField] private GameObject roarVfx;
        [Tooltip("바닥에 퍼지는 충격파 링 (반지름 10m 기준, 크기를 맞춰 키운다)")]
        [SerializeField] private GameObject shockwaveVfx;
        [Tooltip("내려찍기·벽 충돌 먼지·파편 (반지름 4m 기준)")]
        [SerializeField] private GameObject impactVfx;
        [SerializeField] private GameObject partBreakVfx;
        [Tooltip("포자 파괴 파동 (전장을 휩쓰는 큰 링)")]
        [SerializeField] private GameObject waveVfx;
        [Tooltip("포자 기믹 실패 폭발")]
        [SerializeField] private GameObject beaconFailVfx;
        [SerializeField] private GameObject minionSpawnVfx;
        [SerializeField] private GameObject minionDeathVfx;
        [Tooltip("촉수 폭풍: 늘어나는 촉수·생체 채찍")]
        [SerializeField] private TyranopodeTentacleSweep tentacleSweep;
        [Tooltip("그로기 동안 켜 둘 연출 (보스 자식, 꺼 둔 채)")]
        [SerializeField] private GameObject groggyVfx;

        [Header("경기장 (비우면 보스 위치에서 벽까지 재서 정한다)")]
        [SerializeField] private Transform arenaCenter;
        [SerializeField, Min(0f)] private float arenaRadius;
        [SerializeField, Min(5f)] private float fallbackArenaRadius = 35f;

        [Header("보호막 (포효 소환 중 플레이어가 죽으면 잡몹 에너지를 흡수)")]
        [SerializeField] private TyranopodeShield shield;
        [Tooltip("잡몹 자리에서 보스로 날아가는 에너지 (계속 빛나는 연출)")]
        [SerializeField] private GameObject energyOrbVfx;
        [Tooltip("에너지가 보스에 닿을 때")]
        [SerializeField] private GameObject energyAbsorbVfx;
        [Tooltip("화면에 보일 에너지 최대 개수 (잡몹이 더 많아도 보호막 양은 잡몹 수로 정한다)")]
        [SerializeField, Min(1)] private int maxEnergyOrbs = 60;
        [Tooltip("에너지가 날아오르는 높이(m, 원본 크기 기준)")]
        [SerializeField, Min(0f)] private float energyArcHeight = 10f;

        [Header("포자 파동에 맞은 잡몹")]
        [SerializeField] private TyranopodeMinions.FlingSettings fling = new TyranopodeMinions.FlingSettings
        {
            HorizontalSpeed = 12f, UpSpeed = 10f, Gravity = 22f, SpinSpeed = 540f,
        };

        [Header("포효 소환 중 무적 (외피 경화)")]
        [Tooltip("몸을 감싸는 차가운 막 (꺼 둔 채)")]
        [SerializeField] private TyranopodeImmuneCue immuneCue;
        [Tooltip("무적인 보스를 맞혔을 때 튕기는 불꽃")]
        [SerializeField] private GameObject ricochetVfx;
        [SerializeField, Min(0f)] private float ricochetInterval = 0.08f;
        [Tooltip("경화 중 발광 색 (원래 색에 곱한다)")]
        [SerializeField] private Color immuneEmissionTint = new Color(0.35f, 0.5f, 0.65f);
        [Tooltip("분석 화면의 영문·숫자 글꼴 (Aldrich). 한글은 finaleAssets.Font")]
        [SerializeField] private TMP_FontAsset techFont;
        [Tooltip("포효 무리가 살아 있을 때 플레이어가 죽으면 잡몹이 쓰러지는 시각 범위(초). 한꺼번에가 아니라 하나둘")]
        [SerializeField] private Vector2 collapseDelay = new Vector2(0.3f, 2f);

        [Header("최후 연출 (체력이 다하면: 힘을 모아 거대화 → 궤도 타격 → 게임 클리어)")]
        [SerializeField] private TyranopodeFinaleSettings finale = TyranopodeFinaleSettings.Default;
        [SerializeField] private TyranopodeFinaleAssets finaleAssets;
        [Tooltip("힘을 모으는 동안 발광 색 (원래 색에 곱한다)")]
        [SerializeField] private Color finaleEmissionTint = new Color(2f, 1.3f, 0.9f);
        [Tooltip("궤도 타격 예고 원 반지름(m, 원본 크기 기준)")]
        [SerializeField, Min(1f)] private float finaleThreatRadius = 30f;

        [Header("등장 연출 (벽 너머에서 날아와 착지 → 땅으로 웨이브 몬스터의 에너지를 흡수 → 포효)")]
        [Tooltip("처음 등장")]
        [SerializeField] private TyranopodeIntroSettings introFull = TyranopodeIntroSettings.Full;
        [Tooltip("두 번째 등장부터 (TyranopodeTable.IntroBriefAfterFirst)")]
        [SerializeField] private TyranopodeIntroSettings introBrief = TyranopodeIntroSettings.Brief;
        [Tooltip("보스 발밑에서 경기장 끝까지 퍼지는 빛나는 땅 혈관 (반지름 60m·3초 기준, 맞춰 늘린다)")]
        [SerializeField] private GameObject introVeinsVfx;
        [Tooltip("착지 충격 반경(m, 원본 크기 기준). 이 안의 몬스터는 흡수되지 않고 날아간다")]
        [SerializeField, Min(1f)] private float introLandRadius = 12f;
        [Tooltip("흡수를 마치면 커지는 배율")]
        [SerializeField, Min(1f)] private float introGrowScale = 1.06f;
        [Tooltip("흡수하는 동안 발광 색 (원래 색에 곱한다)")]
        [SerializeField] private Color introEmissionTint = new Color(1.8f, 1.3f, 0.8f);
        [Tooltip("촉수를 땅에 박은 자세에서 멈출 진행도 (TentaclesSmashAttack 0~1)")]
        [SerializeField, Range(0f, 1f)] private float introPlantPose = 0.45f;
        [Tooltip("날아오는 동안 넘길 점프 자세 구간 (JumpTentaclesSmashAttack 진행도: 뛰어오름 ~ 착지)")]
        [SerializeField] private Vector2 introLeapPose = new Vector2(0.1f, 0.47f);

        [Header("흐름")]
        [SerializeField, Min(0f)] private float flinchMaxTime = 1.2f;

        [Header("플레이어 보스전 카메라")]
        [Tooltip("보스전 카메라 거리 배율 (플레이어 인스펙터 값 × 이 값). 몸이 커서 화면에 다 들어오게")]
        [SerializeField, Min(0.5f)] private float cameraDistanceScale = 1.3f;

        [Header("발광")]
        [Tooltip("발광 주머니가 부서진 뒤 남는 발광 세기 (0~1)")]
        [SerializeField, Range(0f, 1f)] private float sacBrokenEmission = 0.2f;
        [Tooltip("페이즈 2 발광 색 (원래 색에 곱한다)")]
        [SerializeField] private Color phase2EmissionTint = new Color(1f, 0.35f, 0.2f);

        private readonly Dictionary<TyranopodePartType, PartState> parts = new Dictionary<TyranopodePartType, PartState>();
        private readonly List<TyranopodeAttack> candidates = new List<TyranopodeAttack>();
        private readonly List<float> candidateWeights = new List<float>();
        private readonly List<GroundTelegraph> telegraphPool = new List<GroundTelegraph>();
        private readonly List<TyranopodeEgg> eggs = new List<TyranopodeEgg>();

        private BossController boss;
        private TyranopodeView view;
        private TyranopodeLocomotion locomotion;
        private TyranopodeAttack[] attacks;
        private TentacleStormAttack storm;
        private IPlayerTarget player;
        private TyranopodeAssist assist;
        private State state;
        private float stateEndTime;
        private float nextAttackTime;
        private bool wasAttacking;
        private TyranopodeAttack currentAttack;
        private TyranopodeAttack lastAttack;
        private TyranopodeAttack forcedAttack;
        private int lastExplosionFrame = -1;
        private GameObject lastExplosionAttacker;

        private sealed class EnergyOrb
        {
            public Transform Body;
            public Vector3 From;
            public Vector3 Control;
            /// <summary>땅을 기어 오는 에너지 (등장 연출): 발밑(Via)까지 땅을 따라 오고 몸을 타고 입까지 오른다</summary>
            public bool Ground;
            public Vector3 Via;
            public float StartTime;
            public float Duration;
            public float Shield;
        }

        private readonly List<EnergyOrb> orbs = new List<EnergyOrb>();
        private readonly List<(float time, Vector3 origin, float radius, bool last)> waves = new List<(float, Vector3, float, bool)>();
        private float quietUntil;
        private bool summonImmune;
        private int immuneHits;
        private float nextRicochetTime;
        private TyranopodeAnalysisHud analysis;
        private Vector3 initialScale = Vector3.one;
        private float pendingShield;
        private float shieldHp;
        private float shieldMax;
        private int shieldLayersLeft;

        private Func<Camera> playerCamera;
        private TyranopodeIntro intro;
        private TyranopodeIntroSettings introSettings;
        private Vector3 spawnScale = Vector3.one;
        private Vector3 introLaunch;
        private Vector3 introLanding;
        private GroundTelegraph introShadow;
        private GameObject introGroundEnergy;
        private bool introLanded;
        private bool introDraining;
        private float introDrainStart;
        private float introDrainTime;
        private int introAbsorbed;
        private float introShieldGiven;

        public TyranopodeDef Def { get; private set; }
        public Transform Target => player?.Root;
        public IDamageable TargetDamageable { get; private set; }
        public TyranopodeMinions Minions { get; private set; }
        public Transform Mouth => mouth;
        public TyranopodeTentacleSweep TentacleSweep => tentacleSweep;
        /// <summary>플레이어 시선 (알을 시선 앞쪽에 떨어뜨릴 때)</summary>
        public Transform PlayerView => player.ViewPoint;
        public TyranopodeSpit SpitPrefab => spitPrefab;
        public TyranopodeEgg EggPrefab => eggPrefab;
        public SummonBeacon BeaconPrefab => beaconPrefab;

        public bool IsAlive => boss != null && boss.IsAlive && state != State.Waiting && state != State.Dead;
        public bool IsPhase2 { get; private set; }
        public bool IsGroggy => state == State.Groggy;
        public int BrokenCount { get; private set; }

        public float DamageScale => IsPhase2 ? Def.Phase2DamageMultiplier : 1f;
        public float WindupScale => IsPhase2 ? Def.Phase2WindupMultiplier : 1f;
        public float SpeedScale => (IsPhase2 ? Def.Phase2SpeedMultiplier : 1f)
                                   * (IsBroken(TyranopodePartType.LegTentacle) ? Def.LegBrokenSpeedMultiplier : 1f);

        public bool IsBroken(TyranopodePartType type) => parts.TryGetValue(type, out var part) && part.Broken;

        /// <summary>등장 연출 중 (라운드는 끝날 때까지 보스전으로 넘어가지 않고 남은 몬스터도 정리하지 않는다)</summary>
        public bool IsEntranceRunning => state == State.Intro;
        public float CameraDistanceScale => cameraDistanceScale;
        /// <summary>
        /// 몸 크기 배율 (원본 모델 = 1, 프리팹 빌더 ModelScale). 인스펙터의 연출 크기·거리 값은 원본 크기 기준이라 이 값을 곱해 쓴다
        /// (게임 판정 거리는 TyranopodeTable 에 실제 값으로 적는다)
        /// </summary>
        public float BodyScale => spawnScale.x;

        private void Awake()
        {
            boss = GetComponent<BossController>();
            view = GetComponent<TyranopodeView>();
            locomotion = GetComponent<TyranopodeLocomotion>();
            attacks = GetComponents<TyranopodeAttack>();
            storm = GetComponent<TentacleStormAttack>();
            ValidateReferences();
            groggyVfx.SetActive(false);
            IgnoreHitboxCollisions();
        }

        /// <summary>빠진 참조가 있으면 전투 중이 아니라 생성 순간에 터뜨린다 (프리팹 빌더를 다시 실행하면 채워진다)</summary>
        private void ValidateReferences()
        {
            var missing = new List<string>();
            void Need(UnityEngine.Object value, string name)
            {
                if (value == null) missing.Add(name);
            }
            Need(mouth, nameof(mouth));
            Need(telegraphMaterial, nameof(telegraphMaterial));
            Need(spitPrefab, nameof(spitPrefab));
            Need(eggPrefab, nameof(eggPrefab));
            Need(beaconPrefab, nameof(beaconPrefab));
            Need(roarVfx, nameof(roarVfx));
            Need(shockwaveVfx, nameof(shockwaveVfx));
            Need(impactVfx, nameof(impactVfx));
            Need(partBreakVfx, nameof(partBreakVfx));
            Need(waveVfx, nameof(waveVfx));
            Need(beaconFailVfx, nameof(beaconFailVfx));
            Need(minionSpawnVfx, nameof(minionSpawnVfx));
            Need(minionDeathVfx, nameof(minionDeathVfx));
            Need(groggyVfx, nameof(groggyVfx));
            Need(tentacleSweep, nameof(tentacleSweep));
            if (tentacleSweep != null && tentacleSweep.HasReferences == false) missing.Add("tentacleSweep 의 촉수 뼈·채찍");
            Need(shield, nameof(shield));
            Need(energyOrbVfx, nameof(energyOrbVfx));
            Need(energyAbsorbVfx, nameof(energyAbsorbVfx));
            Need(immuneCue, nameof(immuneCue));
            if (immuneCue != null && immuneCue.HasReferences == false) missing.Add("immuneCue 의 막");
            Need(ricochetVfx, nameof(ricochetVfx));
            Need(techFont, nameof(techFont));
            Need(introVeinsVfx, nameof(introVeinsVfx));
            if (shield != null && shield.HasReferences == false) missing.Add("shield 의 구·연출");
            if (finaleAssets.IsComplete == false) missing.Add(nameof(finaleAssets));
            if (spitPrefab != null && spitPrefab.HasReferences == false) missing.Add("spitPrefab 의 연출");
            if (eggPrefab != null && eggPrefab.HasReferences == false) missing.Add("eggPrefab 의 연출");
            if (beaconPrefab != null && beaconPrefab.HasReferences == false) missing.Add("beaconPrefab 의 조명·연출");
            if (missing.Count > 0)
                throw new InvalidOperationException($"[Tyranopode] 참조가 비어 있습니다: {string.Join(", ", missing)}. 'Tyranopode 프리팹 만들기'를 다시 실행해주세요.");
        }

        /// <summary>
        /// 히트박스 레이어는 총알·수류탄 판정(레이캐스트·겹침)만 받고 어떤 물체와도 부딪치지 않게 한다.
        /// 바닥에 깔린 촉수에 플레이어가 걸리지 않는다. 몸통(Body)만 기본 레이어로 플레이어를 막는다
        /// </summary>
        private static void IgnoreHitboxCollisions()
        {
            int layer = LayerMask.NameToLayer(HitboxLayerName);
            if (layer < 0) return;
            for (int other = 0; other < 32; other++) Physics.IgnoreLayerCollision(layer, other, true);
        }

        private void OnEnable()
        {
            boss.Died += OnBossDied;
            EventBus<PlayerDiedEvent>.Subscribe(OnPlayerDied);
            EventBus<PlayerRevivedEvent>.Subscribe(OnPlayerRevived);
        }

        private void OnDisable()
        {
            boss.Died -= OnBossDied;
            EventBus<PlayerDiedEvent>.Unsubscribe(OnPlayerDied);
            EventBus<PlayerRevivedEvent>.Unsubscribe(OnPlayerRevived);
        }

        private void Start()
        {
            // 라운드에서 먼저 초기화했다면 다시 하지 않는다
            if (state != State.Waiting) return;

            // 씬에 직접 놓은 테스트용 초기화
            var target = playerSource as IPlayerTarget;
            if (target?.Root == null) ServiceLocator.TryGet(out target);
            var data = DataManager.Tyranopode;
            if (target?.Root == null || data == null || DataManager.Bosses.TryGet(data.BossId, out var bossDef) == false)
            {
                Debug.LogError("[Tyranopode] 플레이어(IPlayerTarget)·TyranopodeTable·BossTable 을 확인해주세요.", this);
                enabled = false;
                return;
            }
            Initialize(bossDef, target);
        }

        public void Initialize(BossDef def, IPlayerTarget target)
        {
            if (state != State.Waiting) return;

            boss.Initialize(def, target);
            // BossMovement(추적)는 쓰지 않는다. 이동은 TyranopodeLocomotion 이 맡는다
            if (TryGetComponent(out BossMovement movement)) movement.Stop();

            Def = DataManager.Tyranopode
                  ?? throw new InvalidOperationException("[Tyranopode] TyranopodeTable 을 읽지 못했습니다 (DataManager 로드 오류 참고).");
            player = target;
            TargetDamageable = target.Root.GetComponentInParent<IDamageable>();
            locomotion.Initialize(target.Root);
            BuildParts();

            assist = new TyranopodeAssist(Def);
            assist.Begin();

            Minions = new TyranopodeMinions(transform, minionSpawnVfx, minionDeathVfx, fling,
                Def.MinionSpeedMultiplier, Def.MinionApproachSpeedMultiplier, Def.MinionApproachRatio);
            // 분석 화면은 플레이어가 지금 보는 카메라로 화면 위치를 잡는다 (1·3인칭 전환 반영)
            var playerController = (PlayerController)target;
            playerCamera = () => playerController.CurrentCameraTransform.GetComponent<Camera>();
            analysis = TyranopodeAnalysisHud.Create(finaleAssets.Font, techFont, playerCamera);
            Vector3 center = arenaCenter != null ? arenaCenter.position : transform.position;
            Minions.SetupArena(center, arenaCenter != null ? arenaRadius : 0f, fallbackArenaRadius);
            locomotion.SetBounds(Minions.Center, Minions.Radius);
            // 몸이 커질수록 벽 여유가 늘어 움직일 공간이 준다 → 맴돌 공간이 모자라면 알린다 (크기 배율·CircleRadius 조정)
            if (locomotion.InnerRadius < Def.CircleRadius)
                Debug.LogWarning($"[Tyranopode] 경기장이 좁습니다: 움직일 수 있는 반지름 {locomotion.InnerRadius:0.#}m < 맴돌기 반경 {Def.CircleRadius:0.#}m", this);

            IsPhase2 = false;
            spawnScale = transform.localScale;
            initialScale = spawnScale;
            ConfigureAttacks();

            BeginIntro(Def.IntroBriefAfterFirst && introPlayed);
            introPlayed = true;

            Debug.Log($"[Tyranopode] 초기화 | HP {boss.CurrentHp}/{boss.MaxHp} | 경기장 반지름 {Minions.Radius:0.#}m", this);
        }

        private void BuildParts()
        {
            parts.Clear();
            foreach (var part in GetComponentsInChildren<TyranopodePart>(true))
            {
                if (parts.TryGetValue(part.Type, out var partState) == false)
                {
                    partState = new PartState { Hp = PartMaxHp(part.Type) };
                    parts.Add(part.Type, partState);
                }
                partState.Parts.Add(part);
                if (part.Type != TyranopodePartType.Body && part.HasStump == false)
                    throw new InvalidOperationException($"[Tyranopode] 부위 {part.name} 에 상처 연출(stumpVfx)이 없습니다. 프리팹 빌더를 다시 실행해주세요.");
            }
            BrokenCount = 0;
        }

        private void ConfigureAttacks()
        {
            foreach (var attack in attacks) attack.Configure(Def, player, WindupScale);
        }

        // ───────────────────────── 흐름 ─────────────────────────

        private void Update()
        {
            if (state == State.Waiting || state == State.Dead) return;

            assist.Tick(Time.deltaTime);
            Minions.Tick();
            TickWaves();
            TickEnergy();
            if (state == State.Finale) return; // TyranopodeFinale 이 이끈다

            switch (state)
            {
                case State.Intro:
                    TickIntro(); // 시간표는 TyranopodeIntro 가 이끈다
                    return;
                case State.Flinch:
                    if (Time.time >= stateEndTime || view.IsFinished("Base Layer.GetHit")) EnterFight(RetryDelay);
                    return;
                case State.Groggy:
                    if (Time.time >= stateEndTime)
                    {
                        view.EndGroggy();
                        groggyVfx.SetActive(false);
                        EnterFight(AfterGroggyDelay);
                    }
                    return;
                case State.Transition:
                    if (Time.time >= stateEndTime) EndTransition();
                    return;
            }

            UpdateFight();
        }

        private void UpdateFight()
        {
            bool attacking = boss.IsAttacking;
            if (wasAttacking && attacking == false) OnAttackFinished();
            wasAttacking = attacking;

            if (attacking)
            {
                if (currentAttack != null) currentAttack.TickWindup(Time.deltaTime);
                return;
            }

            bool quiet = Time.time < quietUntil;
            locomotion.Circle(Def.CircleRadius * (quiet ? Def.EggQuietCircleMultiplier : 1f), Def.CircleSpeed * SpeedScale);
            if (quiet == false && Time.time >= nextAttackTime) TryStartAttack();
        }

        private void EnterFight(float delay)
        {
            state = State.Fight;
            wasAttacking = false;
            currentAttack = null;
            nextAttackTime = Time.time + delay;
        }

        private void OnAttackFinished()
        {
            bool special = currentAttack is SummonRoarAttack || currentAttack is TentacleStormAttack;
            currentAttack = null;

            float combo = (IsPhase2 ? Def.Phase2ComboChance : Def.ComboChance) * assist.ComboMultiplier;
            if (special == false && Random.value < combo)
            {
                nextAttackTime = Time.time + ComboDelay;
                return;
            }
            nextAttackTime = Time.time + Random.Range(Def.CircleTimeMin, Def.CircleTimeMax) * assist.DelayMultiplier;
        }

        /// <summary>seconds 동안 공격하지 않고 멀찍이 맴돈다 (알을 깰 틈)</summary>
        public void BeginQuiet(float seconds)
        {
            quietUntil = Mathf.Max(quietUntil, Time.time + seconds);
        }

        private void TryStartAttack()
        {
            var chosen = forcedAttack != null && forcedAttack.CanStart() ? forcedAttack : PickAttack();
            if (chosen != null && chosen.TryStart())
            {
                if (chosen == forcedAttack) forcedAttack = null;
                locomotion.Stop();
                currentAttack = chosen;
                lastAttack = chosen;
                wasAttacking = true;
                return;
            }
            nextAttackTime = Time.time + RetryDelay;
        }

        private TyranopodeAttack PickAttack()
        {
            Vector3 offset = Target.position - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;

            candidates.Clear();
            candidateWeights.Clear();
            float total = 0f;
            foreach (var attack in attacks)
            {
                if (attack.CanStart() == false) continue;
                float weight = attack.GetWeight(distance);
                if (attack.IsHeavy) weight *= assist.HeavyMultiplier;
                if (attack == lastAttack) weight *= RepeatWeight;
                if (weight <= 0f) continue;
                candidates.Add(attack);
                candidateWeights.Add(weight);
                total += weight;
            }
            if (candidates.Count == 0) return null;

            float roll = Random.value * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= candidateWeights[i];
                if (roll <= 0f) return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private void CancelCurrentAttack()
        {
            if (currentAttack != null) currentAttack.Cancel();
            currentAttack = null;
            wasAttacking = false;
        }

        /// <summary>그로기 (포자 파괴·벽 충돌). 진행 중인 공격은 끝낸다</summary>
        public void EnterGroggy(float seconds)
        {
            if (IsAlive == false || state == State.Transition) return;
            CancelCurrentAttack();
            locomotion.Stop();
            state = State.Groggy;
            stateEndTime = Time.time + seconds;
            view.PlayGroggy();
            groggyVfx.SetActive(true);
            Debug.Log($"[Tyranopode] 그로기 {seconds:0.#}초", this);
        }

        private void EnterFlinch()
        {
            CancelCurrentAttack();
            locomotion.Stop();
            state = State.Flinch;
            stateEndTime = Time.time + flinchMaxTime;
            view.Play("Base Layer.GetHit", 1.2f, 0.08f);
        }

        private void BeginTransition()
        {
            CancelCurrentAttack();
            locomotion.Stop();
            if (state == State.Groggy) view.EndGroggy();
            groggyVfx.SetActive(false);
            state = State.Transition;
            stateEndTime = Time.time + Def.TransitionTime;
            boss.SetInvulnerable(true);
            view.SetEmissionTint(phase2EmissionTint);
            view.Play(TyranopodeView.Roar, 0.9f, 0.2f);
            PlayRoarVfx();
            EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(CameraShakePresets.BossAppear));
            Debug.Log($"[Tyranopode] 페이즈 2 전환 | HP {boss.HpRatio:P0} | 부서진 부위 {BrokenCount}", this);
        }

        private void EndTransition()
        {
            boss.SetInvulnerable(false);
            IsPhase2 = true;
            ConfigureAttacks();
            forcedAttack = storm;
            EnterFight(0.2f);
        }

        private void OnBossDied()
        {
            if (state == State.Waiting) return;
            state = State.Dead;
            EndIntroObjects();
            currentAttack = null;
            groggyVfx.SetActive(false);
            locomotion.Stop();
            Minions.KillAllNow();
            ClearSpawned();
            pendingShield = 0f;
            shieldHp = 0f;
            shield.Hide();
        }

        // ───────────────────────── 부위 ─────────────────────────

        /// <summary>부위 히트박스가 받은 피해. 배율을 곱해 보스 체력에 넘기고 부위 체력을 깎는다</summary>
        public void ApplyPartDamage(TyranopodePart part, in DamageInfo info)
        {
            if (IsAlive == false) return;
            if (summonImmune)
            {
                ImmuneHit(info.HitPoint);
                return;
            }
            if (boss.IsInvulnerable) return;

            // 수류탄 한 발이 여러 히트박스에 겹쳐 맞아도 한 번만
            if (info.Kind == DamageKind.Explosion)
            {
                if (lastExplosionFrame == Time.frameCount && lastExplosionAttacker == info.Attacker) return;
                lastExplosionFrame = Time.frameCount;
                lastExplosionAttacker = info.Attacker;
            }

            float multiplier = PartDamageMultiplier(part.Type)
                               * (state == State.Groggy ? Def.GroggyDamageMultiplier : 1f);
            if (multiplier <= 0f || info.Amount <= 0) return;
            int amount = Mathf.Max(1, Mathf.RoundToInt(info.Amount * multiplier));

            // 보호막이 먼저 받는다. 다 막으면 보스·부위 체력은 그대로
            amount = AbsorbByShield(amount, info);
            if (amount <= 0) return;

            // 마지막 일격은 체력 1을 남기고 최후 연출로 (연출 끝의 궤도 타격이 처치한다)
            if (amount >= boss.CurrentHp)
            {
                int last = boss.CurrentHp - 1;
                if (last > 0) boss.TakeDamage(new DamageInfo(last, info.Kind, info.AttackerFaction, info.Attacker, info.HitPoint, info.Direction, info.SourceDefId));
                BeginFinale();
                return;
            }

            boss.TakeDamage(new DamageInfo(amount, info.Kind, info.AttackerFaction, info.Attacker, info.HitPoint, info.Direction, info.SourceDefId));
            if (state == State.Dead) return;
            view.Flash();

            if (parts.TryGetValue(part.Type, out var partState) && part.Type != TyranopodePartType.Body && partState.Broken == false)
            {
                partState.Hp -= amount;
                if (partState.Hp <= 0) BreakPart(part.Type, partState, info.HitPoint);
            }

            if (IsPhase2 == false && state != State.Transition
                && (boss.HpRatio <= Def.Phase2HpRatio || BrokenCount >= Def.Phase2BrokenParts))
                BeginTransition();
        }

        private void BreakPart(TyranopodePartType type, PartState partState, Vector3 point)
        {
            partState.Broken = true;
            partState.Hp = 0;
            BrokenCount++;
            foreach (var part in partState.Parts) part.Sever();

            PlayVfx(partBreakVfx, point, BodyScale);
            EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(CameraShakePresets.Impact.At(point, 80f)));
            // 발광은 거의 주머니에서 나온다 → 주머니가 부서지면 빛을 잃는다. 다른 부위는 상처 연출(TyranopodePart.stumpVfx)로
            if (type == TyranopodePartType.Sac) view.SetEmissionLevel(sacBrokenEmission);
            Debug.Log($"[Tyranopode] 부위 파괴: {type} ({BrokenCount}개)", this);

            // 페이즈 전환이 곧 시작되거나 기믹·그로기 중이면 움찔하지 않는다
            bool willTransition = IsPhase2 == false && BrokenCount >= Def.Phase2BrokenParts;
            bool special = currentAttack is SummonRoarAttack || currentAttack is TentacleStormAttack;
            if (state == State.Fight && willTransition == false && special == false) EnterFlinch();
        }

        private int PartMaxHp(TyranopodePartType type) => type switch
        {
            TyranopodePartType.FrontTentacle => Def.FrontTentacleHp,
            TyranopodePartType.LegTentacle => Def.LegTentacleHp,
            TyranopodePartType.Sac => Def.SacHp,
            TyranopodePartType.Head => Def.HeadHp,
            _ => 0,
        };

        private float PartDamageMultiplier(TyranopodePartType type) => type switch
        {
            TyranopodePartType.FrontTentacle => Def.FrontTentacleDamageMultiplier,
            TyranopodePartType.LegTentacle => Def.LegTentacleDamageMultiplier,
            TyranopodePartType.Sac => Def.SacDamageMultiplier,
            TyranopodePartType.Head => Def.HeadDamageMultiplier,
            _ => Def.BodyDamageMultiplier,
        };

        // ───────────────────────── 최후 연출 ─────────────────────────

        private void BeginFinale()
        {
            CancelCurrentAttack();
            locomotion.Stop();
            if (state == State.Groggy) view.EndGroggy();
            groggyVfx.SetActive(false);
            state = State.Finale;
            boss.SetInvulnerable(true);

            // 전장 정리: 잡몹·알·보호막·에너지·파동
            Minions.KillAllNow();
            Minions.TakeCollapsedPositions();
            ClearSpawned();
            pendingShield = 0f;
            shieldHp = 0f;
            shield.Hide();

            view.SetEmissionTint(finaleEmissionTint);
            view.Play(TyranopodeView.Roar, 0.5f, 0.3f);
            PlayRoarVfx();

            var threat = RentTelegraph();
            threat.ShowCircle(transform.position, finaleThreatRadius * BodyScale, finale.StrikeTime, new Color(1f, 0.1f, 0.05f, 0.5f));
            new GameObject("TyranopodeFinale").AddComponent<TyranopodeFinale>().Begin(this, finale, finaleAssets, threat);
            Debug.Log("[Tyranopode] 최후 연출 시작", this);
        }

        /// <summary>최후 연출: 힘을 모으며 커진다 (처음 크기 대비 배율)</summary>
        public void FinaleGrow(float scale) => transform.localScale = initialScale * scale;

        /// <summary>최후 연출: 궤도 타격으로 처치 (기존 사망·보상·사운드 흐름을 탄다)</summary>
        public void FinaleKill()
        {
            boss.SetInvulnerable(false);
            boss.TakeDamage(new DamageInfo(boss.CurrentHp, DamageKind.Explosion, Faction.Player, null, transform.position, Vector3.down));
        }

        // ───────────────────────── 등장 연출 (TyranopodeIntro 가 시간표대로 부른다) ─────────────────────────

        private const float VeinsBaseRadius = 60f;
        private float IntroLandRadius => introLandRadius * BodyScale;
        private const float VeinsBaseTime = 3f;

        /// <summary>
        /// 등장 연출 시작: 플레이어 반대편 벽 너머로 옮겨 두고, 씬에 남은 몬스터를 넘겨받아 기절시킨다.
        /// 지금 자리가 착지점이다 (라운드 보스 생성 위치)
        /// </summary>
        private void BeginIntro(bool brief)
        {
            introSettings = brief ? introBrief : introFull;
            state = State.Intro;
            boss.SetInvulnerable(true);
            transform.localScale = spawnScale;
            introLanding = transform.position;
            introLanded = false;
            introDraining = false;
            introAbsorbed = 0;
            introShieldGiven = 0f;

            // 플레이어 반대편 벽 너머에서 뛰어든다
            Vector3 away = introLanding - Target.position;
            away.y = 0f;
            away = away.sqrMagnitude > 1f ? away.normalized : -transform.forward;
            introLaunch = Minions.Center + away * (Minions.Radius + introSettings.LaunchDistance);
            introLaunch.y = introLanding.y;
            locomotion.SetAirborne(true);
            IntroFly(0f);

            int adopted = Minions.AdoptWaveMonsters(introSettings.TotalTime + 1f);
            introShadow = RentTelegraph();
            introShadow.ShowCircle(introLanding, IntroLandRadius, introSettings.LandTime, new Color(1f, 0.3f, 0.05f, 0.45f));

            intro = new GameObject("TyranopodeIntro").AddComponent<TyranopodeIntro>();
            intro.Begin(this, introSettings, introLanding, introLaunch, playerCamera);
            Debug.Log($"[Tyranopode] 등장 연출 ({(brief ? "짧게" : "처음")}) | 넘겨받은 몬스터 {adopted}", this);
        }

        /// <summary>날아오는 중 (k = 0 뛰어오름 ~ 1 착지): 포물선을 따라 옮기고 점프 자세를 넘긴다</summary>
        public void IntroFly(float k)
        {
            transform.position = Vector3.Lerp(introLaunch, introLanding, k) + Vector3.up * introSettings.LeapHeight * BodyScale * 4f * k * (1f - k);
            locomotion.FaceInstant(introLanding + (introLanding - introLaunch));
            view.Pose(TyranopodeView.JumpSmash, Mathf.Lerp(introLeapPose.x, introLeapPose.y, k));
        }

        /// <summary>착지: 땅이 터지고 가까운 몬스터는 날아간다</summary>
        public void IntroLand()
        {
            introLanded = true;
            transform.position = introLanding;
            locomotion.SetAirborne(false);
            view.PlayFrom(TyranopodeView.JumpSmash, introLeapPose.y);
            introShadow.Detonate(new Color(1f, 0.55f, 0.2f, 0.8f));
            PlayImpactVfx(introLanding, IntroLandRadius);
            PlayShockwave(introLanding, IntroLandRadius * 2.5f);
            Minions.WaveKill(introLanding, 40f, IntroLandRadius);
        }

        /// <summary>
        /// 흡수: 촉수를 땅에 박으면 발밑에서 빛나는 혈관이 경기장 끝까지 퍼지고, 혈관이 닿은 몬스터가 쓰러지며
        /// 그 에너지가 땅을 기어 보스에게 모인다 (1마리당 IntroShieldPerMonster 보호막). 보스는 점점 밝아지고 커진다
        /// </summary>
        public void IntroDrain(float duration)
        {
            introDraining = true;
            introDrainStart = Time.time;
            introDrainTime = duration;
            view.Play(TyranopodeView.TentacleSmash, 1f, 0.15f);
            view.SetEmissionTint(introEmissionTint);

            var veins = Instantiate(introVeinsVfx, transform.position + Vector3.up * 0.05f, Quaternion.identity);
            veins.transform.localScale = Vector3.one * (Minions.Radius / VeinsBaseRadius);
            foreach (var ps in veins.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.simulationSpeed = VeinsBaseTime / duration;
            }
            foreach (var ring in veins.GetComponentsInChildren<TyranopodeGroundRing>()) ring.TimeScale = VeinsBaseTime / duration;
            Destroy(veins, duration + 2f);
            introGroundEnergy = Instantiate(finaleAssets.GroundEnergyVfx, transform.position, Quaternion.identity);
            // 혈관이 닿는 순서대로: 가까운 몬스터부터 흡수 시간의 앞 절반에 걸쳐 쓰러진다
            Minions.DrainAdopted(transform.position, duration * 0.1f, duration * 0.45f);
        }

        /// <summary>포효: 분석 화면 이름 카드 (흡수한 수·생체막)</summary>
        public void IntroRoar()
        {
            view.Play(TyranopodeView.Roar, 0.9f, 0.25f);
            PlayRoarVfx();
            StopGroundEnergy();
            analysis.ShowIntroCard(introAbsorbed, introShieldGiven > 0f);
        }

        /// <summary>연출 끝: 전투 시작 (첫 공격까지 delay)</summary>
        public void IntroFinish(float delay)
        {
            intro = null;
            introDraining = false;
            boss.SetInvulnerable(false);
            initialScale = transform.localScale;
            view.SetEmissionTint(IsPhase2 ? phase2EmissionTint : Color.white);
            view.Play(TyranopodeView.Idle, 1f, 0.3f);
            EnterFight(delay);
            Debug.Log($"[Tyranopode] 등장 연출 끝 | 흡수 {introAbsorbed}마리 → 보호막 {introShieldGiven:0}", this);
        }

        private void TickIntro()
        {
            if (introLanded && introDraining == false)
            {
                locomotion.Face(Target.position, 0.6f);
                return;
            }
            if (introDraining == false) return;

            float k = Mathf.Clamp01((Time.time - introDrainStart) / introDrainTime);
            transform.localScale = spawnScale * Mathf.Lerp(1f, introGrowScale, k);
            // 촉수를 땅에 박은 자세로 버틴다 (아주 느리게 떨리듯)
            if (view.NormalizedTime(TyranopodeView.TentacleSmash) >= introPlantPose) view.SetSpeed(0.03f);

            var positions = Minions.TakeCollapsedPositions();
            if (positions.Count == 0) return;
            float amount = DrainShield(positions.Count, Def.IntroShieldPerMonster, Def.IntroShieldMaxRatio * boss.MaxHp, introShieldGiven);
            introShieldGiven += amount;
            introAbsorbed += positions.Count;
            SpawnEnergyOrbs(positions, amount, ground: true, flightTime: introDrainTime * 0.4f);
        }

        /// <summary>쓰러진 count 마리가 주는 보호막: 1마리당 perMonster, 지금까지 준 양(given)과 합쳐 cap 을 넘지 않는다</summary>
        public static float DrainShield(int count, float perMonster, float cap, float given)
            => Mathf.Min(count * perMonster, Mathf.Max(0f, cap - given));

        private void StopGroundEnergy()
        {
            if (introGroundEnergy == null) return;
            foreach (var ps in introGroundEnergy.GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(introGroundEnergy, 2.5f);
            introGroundEnergy = null;
        }

        /// <summary>보스가 사라질 때: 연출 감독·땅 에너지 정리 (감독이 플레이어 입력 차단을 푼다)</summary>
        private void EndIntroObjects()
        {
            if (intro != null) Destroy(intro.gameObject);
            intro = null;
            StopGroundEnergy();
        }

        // ───────────────────────── 포효 소환: 무적·무리 흡수 ─────────────────────────

        /// <summary>포자가 떠 있는 동안: 외피가 굳어 무적. 분석 화면이 무적임과 약점(포자)을 알려 준다</summary>
        public void BeginSummonImmunity(SummonBeacon beacon)
        {
            summonImmune = true;
            immuneHits = 0;
            boss.SetInvulnerable(true);
            immuneCue.Show();
            view.SetEmissionTint(immuneEmissionTint);
            analysis.ShowImmuneBanner();
            analysis.TrackWeakPoint(beacon.transform);
        }

        /// <summary>포자가 사라졌다 (파괴·실패·취소)</summary>
        public void EndSummonImmunity()
        {
            if (summonImmune == false) return;
            summonImmune = false;
            boss.SetInvulnerable(false);
            immuneCue.Hide();
            view.SetEmissionTint(IsPhase2 ? phase2EmissionTint : Color.white);
            analysis.HideBanner();
            analysis.StopTracking();
        }

        /// <summary>무적인 보스를 맞혔다: 막 물결 + 튕김 불꽃 + "IMMUNE" 표시, 일정 횟수마다 분석 안내를 다시</summary>
        private void ImmuneHit(Vector3 point)
        {
            immuneCue.Hit(point);
            if (Time.time >= nextRicochetTime)
            {
                nextRicochetTime = Time.time + ricochetInterval;
                PlayVfx(ricochetVfx, point, 1f);
                analysis.PopImmune(point);
            }
            immuneHits++;
            if (immuneHits % Def.ImmuneHintHits == 0) analysis.ShowImmuneBanner();
        }

        /// <summary>
        /// 포효 무리가 살아 있는 동안(포자가 사라진 뒤라도) 플레이어가 죽으면: 무리가 하나둘 쓰러지고,
        /// 부활 뒤 그 에너지가 보스로 빨려 들어가 보호막이 된다. 포자가 아직 떠 있으면 기믹도 끝낸다
        /// </summary>
        private void OnPlayerDied(PlayerDiedEvent e)
        {
            if (IsAlive == false || state == State.Finale || Minions.CohortAliveCount == 0) return;
            int count = Minions.CollapseCohort(collapseDelay.x, collapseDelay.y);
            QueueEnergyAfterRevive(count);
            if (currentAttack is SummonRoarAttack summon) summon.EndByPlayerDeath();
            Debug.Log($"[Tyranopode] 포효 무리 {count}마리 흡수 대기 (플레이어 사망)", this);
        }

        // ───────────────────────── 포자 파동 ─────────────────────────

        /// <summary>
        /// 포자 파괴: 포자 아래 바닥에서 WaveCount 번 파동. i 번째 파동은 경기장 끝까지의 (i+1)/WaveCount 까지 휩쓸며
        /// 닿는 잡몹을 날려 보낸다 → 가까운 무리부터 차례로 날아간다. 마지막 파동이 가장 크고 세다
        /// </summary>
        public void StartBeaconWaves(Vector3 beaconPoint)
        {
            Vector3 origin = new Vector3(beaconPoint.x, transform.position.y, beaconPoint.z);
            Vector3 offset = origin - Minions.Center;
            offset.y = 0f;
            float cover = Minions.Radius + offset.magnitude;
            for (int i = 0; i < Def.WaveCount; i++)
                waves.Add((Time.time + i * Def.WaveInterval, origin, cover * (i + 1) / Def.WaveCount, i == Def.WaveCount - 1));
        }

        private void TickWaves()
        {
            for (int i = waves.Count - 1; i >= 0; i--)
            {
                var (time, origin, radius, last) = waves[i];
                if (Time.time < time) continue;
                waves.RemoveAt(i);
                Minions.WaveKill(origin, Def.WaveSpeed, radius);
                PlayVfx(waveVfx, origin, radius / WaveBaseRadius);
                var shake = last ? CameraShakePresets.BossAppear : CameraShakePresets.Impact;
                EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(shake));
            }
        }

        // ───────────────────────── 에너지 흡수·보호막 ─────────────────────────

        /// <summary>
        /// 포효 소환 중 플레이어가 죽었다: 잡몹 minionCount 마리가 쓰러지고, 플레이어가 살아나면 그 에너지가 보스로 날아와 보호막이 된다
        /// </summary>
        public void QueueEnergyAfterRevive(int minionCount)
        {
            float cap = Def.ShieldMaxRatio * boss.MaxHp;
            pendingShield = Mathf.Min(cap, pendingShield + minionCount * Def.ShieldPerMinion);
        }

        private void OnPlayerRevived(PlayerRevivedEvent e)
        {
            if (IsAlive == false || pendingShield <= 0f) return;

            float amount = pendingShield;
            pendingShield = 0f;
            SpawnEnergyOrbs(Minions.TakeCollapsedPositions(), amount, ground: false, flightTime: Def.EnergyFlightTime);
        }

        /// <summary>
        /// positions 에서 보스로 에너지가 모여 shield 만큼 보호막이 된다. 화면의 에너지는 maxEnergyOrbs 까지
        /// (잡몹이 많으면 고르게 골라 보여 주고 보호막 양은 나눠 싣는다. 이미 꽉 찼으면 날아가는 중인 에너지에 얹는다).
        /// ground = 공중으로 떠오르지 않고 땅을 기어 온다 (등장 연출)
        /// </summary>
        private void SpawnEnergyOrbs(List<Vector3> positions, float shield, bool ground, float flightTime)
        {
            int count = Mathf.Min(Mathf.Max(0, maxEnergyOrbs - orbs.Count), positions.Count);
            if (count == 0)
            {
                if (orbs.Count > 0) orbs[orbs.Count - 1].Shield += shield;
                else AddShield(shield);
                return;
            }

            float share = shield / count;
            Vector3 feet = transform.position + Vector3.up * 0.5f;
            for (int i = 0; i < count; i++)
            {
                Vector3 from = positions[i * positions.Count / count] + Vector3.up * (ground ? 0.3f : 1f);
                var body = Instantiate(energyOrbVfx, from, Quaternion.identity).transform;
                Vector3 control;
                if (ground)
                {
                    // 땅 위로 구불구불: 가운데를 옆으로 비껴 땅 높이에
                    Vector3 side = Vector3.Cross(Vector3.up, feet - from).normalized * Vector3.Distance(from, feet) * Random.Range(-0.25f, 0.25f);
                    control = Vector3.Lerp(from, feet, 0.5f) + side;
                }
                else control = Vector3.Lerp(from, mouth.position, 0.5f) + Vector3.up * Random.Range(energyArcHeight * 0.6f, energyArcHeight) * BodyScale;

                orbs.Add(new EnergyOrb
                {
                    Body = body,
                    From = from,
                    Control = control,
                    Ground = ground,
                    Via = feet,
                    // 한꺼번에 오지 않고 천천히 빨려 들어가도록 출발을 흩는다
                    StartTime = Time.time + Random.Range(0f, flightTime * (ground ? 0.15f : 0.6f)),
                    Duration = flightTime * Random.Range(0.8f, 1.2f),
                    Shield = share,
                });
            }
        }

        private void TickEnergy()
        {
            for (int i = orbs.Count - 1; i >= 0; i--)
            {
                var orb = orbs[i];
                float t = Mathf.Clamp01((Time.time - orb.StartTime) / orb.Duration);
                // 처음엔 느리게 떠오르다 보스 쪽으로 빨려 든다
                orb.Body.position = orb.Ground ? GroundOrbPosition(orb, t) : Bezier(orb.From, orb.Control, mouth.position, t * t);
                if (t < 1f) continue;

                Destroy(orb.Body.gameObject, 0.5f);
                orbs.RemoveAt(i);
                PlayVfx(energyAbsorbVfx, mouth.position, 1f);
                view.Flash();
                AddShield(orb.Shield);
            }
        }

        private const float GroundShare = 0.75f;

        /// <summary>땅을 기는 에너지: 앞 75% 는 땅을 따라 발밑까지 (점점 빨라짐), 나머지는 몸을 타고 입까지</summary>
        private Vector3 GroundOrbPosition(EnergyOrb orb, float t)
        {
            if (t < GroundShare)
            {
                float g = t / GroundShare;
                return Bezier(orb.From, orb.Control, orb.Via, g * g);
            }
            float climb = (t - GroundShare) / (1f - GroundShare);
            return Vector3.Lerp(orb.Via, mouth.position, climb * climb);
        }

        private static Vector3 Bezier(Vector3 a, Vector3 control, Vector3 b, float t)
            => Vector3.Lerp(Vector3.Lerp(a, control, t), Vector3.Lerp(control, b, t), t);

        private void AddShield(float amount)
        {
            if (amount <= 0f) return;
            shieldHp += amount;
            shieldMax = Mathf.Max(shieldMax, shieldHp); // 겹 크기 기준 = 이번에 쌓인 최대량
            shieldLayersLeft = Def.ShieldLayers;
            shield.Absorb(shieldHp / (Def.ShieldMaxRatio * boss.MaxHp));
            shield.SetRatio(shieldHp / shieldMax);
        }

        /// <summary>보호막이 피해를 먼저 받는다. 반환 = 보호막을 넘은 피해</summary>
        private int AbsorbByShield(int amount, in DamageInfo info)
        {
            if (shieldHp <= 0f) return amount;

            float absorbed = Mathf.Min(shieldHp, amount);
            shieldHp -= absorbed;
            shield.Hit(info.HitPoint);
            shield.SetRatio(shieldHp / shieldMax);
            // 히트마커·타격음은 그대로 (막혀도 맞혔다는 피드백)
            var blocked = new DamageInfo(Mathf.RoundToInt(absorbed), info.Kind, info.AttackerFaction, info.Attacker, info.HitPoint, info.Direction, info.SourceDefId);
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(shield.gameObject, blocked, false));

            int layersLeft = Mathf.CeilToInt(shieldHp / (shieldMax / Def.ShieldLayers) - 0.0001f);
            if (shieldHp <= 0f)
            {
                shieldHp = 0f;
                shieldMax = 0f;
                shieldLayersLeft = 0;
                shield.Shatter();
                EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(CameraShakePresets.Impact.At(transform.position, 80f)));
                Debug.Log("[Tyranopode] 보호막 파괴 → 그로기", this);
                EnterGroggy(Def.ShieldBreakGroggyTime);
            }
            else if (layersLeft < shieldLayersLeft)
            {
                shieldLayersLeft = layersLeft;
                shield.BreakLayer(layersLeft, Def.ShieldLayers);
                view.Flash();
                EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(CameraShakePresets.Light.At(transform.position, 80f)));
            }
            return amount - Mathf.RoundToInt(absorbed);
        }

        public float ShieldHp => shieldHp;
        public float ShieldMax => shieldMax;

        // ───────────────────────── 소환물·연출 ─────────────────────────

        public void RegisterEgg(TyranopodeEgg egg) => eggs.Add(egg);

        public void UnregisterEgg(TyranopodeEgg egg) => eggs.Remove(egg);

        /// <summary>돌진·내려찍기에 휩쓸린 알을 깬다</summary>
        public void BreakEggsNear(Vector3 point, float radius)
        {
            for (int i = eggs.Count - 1; i >= 0; i--)
            {
                if (eggs[i] == null) continue;
                Vector3 offset = eggs[i].transform.position - point;
                offset.y = 0f;
                if (offset.sqrMagnitude <= radius * radius) eggs[i].Break();
            }
        }

        public GroundTelegraph RentTelegraph()
        {
            foreach (var telegraph in telegraphPool)
                if (telegraph != null && telegraph.gameObject.activeSelf == false) return telegraph;

            var created = GroundTelegraph.Create(telegraphMaterial);
            telegraphPool.Add(created);
            return created;
        }


        private const float ShockwaveBaseRadius = 10f;
        private const float ImpactBaseRadius = 4f;
        private const float RoarShockwaveRadius = 15f;

        /// <summary>포효: 입에서 가스 + 발밑 충격파</summary>
        public void PlayRoarVfx()
        {
            PlayVfx(roarVfx, mouth.position, BodyScale);
            PlayShockwave(transform.position, RoarShockwaveRadius);
        }

        /// <summary>바닥 충격파 링 (radius = 퍼지는 반지름 m)</summary>
        public void PlayShockwave(Vector3 point, float radius) => PlayVfx(shockwaveVfx, point, radius / ShockwaveBaseRadius);

        /// <summary>범위 공격이 터진 자리: 모양 안에 넓이에 맞춰 먼지·파편을 흩뿌린다 (번쩍임은 GroundTelegraph.Detonate)</summary>
        public void PlayAreaDust(GroundTelegraph area)
        {
            int count = Mathf.Clamp(Mathf.RoundToInt(area.Area / AreaPerDust), 2, MaxDustPerArea);
            for (int i = 0; i < count; i++) PlayVfx(impactVfx, area.RandomPointInside(), DustScale);
        }

        private const float AreaPerDust = 20f;
        private const int MaxDustPerArea = 14;
        private const float DustScale = 0.6f;

        /// <summary>내려찍기·벽 충돌: 먼지·파편 + 같은 크기 충격파</summary>
        public void PlayImpactVfx(Vector3 point, float radius)
        {
            PlayVfx(impactVfx, point, radius / ImpactBaseRadius);
            PlayShockwave(point, radius);
        }

        /// <summary>포자 파괴 파동: 포자 아래 바닥에서 퍼진다</summary>
        private const float WaveBaseRadius = 60f;

        public void PlayBeaconFailVfx(Vector3 point) => PlayVfx(beaconFailVfx, point, 1f);

        /// <summary>촉수 폭풍: 쓸고 지나가는 촉수 끝의 흙먼지</summary>
        public void PlaySweepDust(Vector3 point)
        {
            point.y = TyranopodeGround.Height(point, point.y);
            PlayVfx(impactVfx, point, DustScale * BodyScale);
        }

        /// <summary>처음 쓰는 촉수 폭풍: 분석 화면이 점프로 넘는 요령을 알려 준다</summary>
        public void ShowStormHint() => analysis.ShowStormHint(JumpKeyName);

        /// <summary>촉수 폭풍 회피 안내 (점프로 넘길 때까지 쓸기마다)</summary>
        public void ShowEvadePrompt() => analysis.ShowEvadePrompt(JumpKeyName);

        public void HideEvadePrompt() => analysis.HideEvadePrompt();

        public void CueJump() => analysis.CueJump();

        public void ShowEvadeLearned() => analysis.ShowEvadeLearned();

        private static string JumpKeyName => KeyName(new PlayerController.InputPlayerSettings().jumpKey);

        private static string KeyName(UnityEngine.InputSystem.Key key)
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null ? keyboard[key].displayName : key.ToString();
        }

        private static void PlayVfx(GameObject prefab, Vector3 point, float scale)
        {
            var vfx = Instantiate(prefab, point, Quaternion.identity);
            vfx.transform.localScale = Vector3.one * scale;
            Destroy(vfx, 6f);
        }

        private void ClearSpawned()
        {
            foreach (var orb in orbs)
                if (orb.Body != null) Destroy(orb.Body.gameObject);
            orbs.Clear();
            waves.Clear();
            for (int i = eggs.Count - 1; i >= 0; i--)
                if (eggs[i] != null) Destroy(eggs[i].gameObject);
            eggs.Clear();
            foreach (var telegraph in telegraphPool)
                if (telegraph != null) telegraph.Hide();
        }

        private void OnDestroy()
        {
            EndIntroObjects();
            if (analysis != null) Destroy(analysis.gameObject);
            assist?.End();
            Minions?.Release();
            ClearSpawned();
            foreach (var telegraph in telegraphPool)
                if (telegraph != null) Destroy(telegraph.gameObject);
            telegraphPool.Clear();
        }

#if UNITY_EDITOR
        /// <summary>선택하면: 경기장 벽(회색)·몸이 머무는 경계(노랑)·근/원거리 구간(빨강/파랑)·맴돌기 반경(초록)</summary>
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying == false || Def == null) return;
            var handles = UnityEditor.Handles.color;
            UnityEditor.Handles.color = Color.gray;
            UnityEditor.Handles.DrawWireDisc(Minions.Center, Vector3.up, Minions.Radius);
            UnityEditor.Handles.color = Color.yellow;
            UnityEditor.Handles.DrawWireDisc(Minions.Center, Vector3.up, locomotion.InnerRadius);
            UnityEditor.Handles.color = Color.red;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, Def.MeleeRange);
            UnityEditor.Handles.color = Color.blue;
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, Def.FarRange);
            UnityEditor.Handles.color = Color.green;
            UnityEditor.Handles.DrawWireDisc(Target.position, Vector3.up, Def.CircleRadius);
            UnityEditor.Handles.color = handles;
        }
#endif

        // ───────────────────────── 디버그 (인스펙터 우클릭 메뉴·TyranopodeDebugHud) ─────────────────────────

        public string StateName => state.ToString();
        public string CurrentAttackName => currentAttack != null ? currentAttack.GetType().Name : "-";
        public float Struggle => assist?.Struggle ?? 0f;
        public float AssistDelayMultiplier => assist?.DelayMultiplier ?? 1f;
        public BossController Boss => boss;

        /// <summary>부위별 (종류, 남은 체력, 최대 체력, 파괴 여부). 몸통 제외</summary>
        public IEnumerable<(TyranopodePartType type, int hp, int max, bool broken)> PartStatus()
        {
            foreach (var pair in parts)
                if (pair.Key != TyranopodePartType.Body)
                    yield return (pair.Key, pair.Value.Hp, PartMaxHp(pair.Key), pair.Value.Broken);
        }

        /// <summary>다음 공격을 T 로 (가능해지는 즉시)</summary>
        public void DebugForce<T>() where T : TyranopodeAttack => forcedAttack = GetComponent<T>();

        [ContextMenu("Debug/페이즈 2 전환")]
        public void DebugPhase2()
        {
            if (IsAlive && IsPhase2 == false) BeginTransition();
        }

        [ContextMenu("Debug/포효 소환 바로 사용")]
        public void DebugSummon() => forcedAttack = GetComponent<SummonRoarAttack>();

        [ContextMenu("Debug/촉수 폭풍 바로 사용")]
        public void DebugStorm() => forcedAttack = storm;

        /// <summary>플레이어 사망 없이 에너지 흡수 → 보호막을 본다 (보스 둘레에서 최대치만큼)</summary>
        [ContextMenu("Debug/에너지 흡수 보호막")]
        public void DebugShield()
        {
            if (IsAlive == false) return;
            var positions = new List<Vector3>();
            for (int i = 0; i < 30; i++)
                positions.Add(transform.position + Quaternion.Euler(0f, i * 12f, 0f) * Vector3.forward * Random.Range(10f, Minions.Radius * 0.8f));
            SpawnEnergyOrbs(positions, Def.ShieldMaxRatio * boss.MaxHp, ground: false, flightTime: Def.EnergyFlightTime);
        }

        /// <summary>등장 연출 다시 보기: 경기장에 몬스터를 crowd 마리 흩어 놓고(웨이브 몬스터 대신) 지금 자리에 다시 착지한다</summary>
        public void DebugReplayIntro(bool brief, int crowd)
        {
            if (IsAlive == false || state == State.Intro || state == State.Finale || state == State.Transition) return;
            CancelCurrentAttack();
            locomotion.Stop();
            if (state == State.Groggy) view.EndGroggy();
            groggyVfx.SetActive(false);
            EndSummonImmunity();
            Minions.SpawnLoose(Def.SummonMinionIds, crowd);
            BeginIntro(brief);
        }

        [ContextMenu("Debug/최후 연출")]
        public void DebugFinale()
        {
            if (IsAlive && state != State.Finale) BeginFinale();
        }

        [ContextMenu("Debug/그로기")]
        public void DebugGroggy() => EnterGroggy(Def != null ? Def.GroggyTime : 5f);

        [ContextMenu("Debug/앞 촉수 파괴")]
        private void DebugBreakFront() => DebugBreak(TyranopodePartType.FrontTentacle);

        [ContextMenu("Debug/다리 촉수 파괴")]
        private void DebugBreakLeg() => DebugBreak(TyranopodePartType.LegTentacle);

        [ContextMenu("Debug/발광 주머니 파괴")]
        private void DebugBreakSac() => DebugBreak(TyranopodePartType.Sac);

        [ContextMenu("Debug/머리 파괴")]
        private void DebugBreakHead() => DebugBreak(TyranopodePartType.Head);

        public void DebugBreak(TyranopodePartType type)
        {
            if (IsAlive == false || parts.TryGetValue(type, out var partState) == false || partState.Broken) return;
            BreakPart(type, partState, partState.Parts[0].transform.position);
            if (IsPhase2 == false && state != State.Transition && BrokenCount >= Def.Phase2BrokenParts) BeginTransition();
        }
    }
}
