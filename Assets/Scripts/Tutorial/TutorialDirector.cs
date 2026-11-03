using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Player;
using HybridShooter.Supply;
using HybridShooter.Tutorial.Steps;
using HybridShooter.UI.MainMenu;
using HybridShooter.Weapon;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 불시착 후 기본 전투 테스트 (기획: Docs/Design/Tutorial_Proposal_v3.md). TutorialMap 씬에 하나.
    /// 과목: ① 이동·질주·구르기 → ② 조준·사격(보상) → ②-B 무기 교체 → ③ 수류탄 → ④ 보급 → ⑤ 강화 체험 → ⑥ 전장 입장
    ///  - 과목을 통과할 때마다 그 구역의 진행 차단막(TutorialGate)을 연다
    ///  - 지시한 행동만 하게 과목·구간마다 입력을 좁힌다 (Allow → PlayerController.SetAllowedAbilities). 끝나면 전부 푼다
    ///  - 수류탄은 던져도 줄지 않는다 (PlayerItems.ConsumeGrenades = false)
    ///  - 어디로 가야 할 때는 목적지를 표시한다 (ShowDestination: 화면 표식 TutorialWaypointHud)
    ///  - 몬스터는 휴면(MonsterController.SetDormant)으로 소환해 공격하지 않는다. 피해원은 피해 차단막·훈련 충격뿐이고,
    ///    체력이 MinHealthRatio 밑으로 떨어지면 가득 채운다 (튜토리얼에서 죽지 않게)
    ///  - 처치 보상은 씬의 KillRewardCollector 가 지갑에 넣고, 여기서는 CurrencyChangedEvent 로 "+N 생체 자원" 만 띄운다
    ///  - 게이트 통과 또는 Esc 길게(SkipHoldTime) → 완료 기록 · 재화/강화 초기화 · 게임 씬으로
    /// 씬에 RunDirector 가 없으므로 무기 선택 화면·라운드 흐름은 뜨지 않는다.
    /// 씬 조립: Tools > Tutorial > Setup Tutorial Scene
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialDirector : MonoBehaviour
    {
        /// <summary>진행이 없을 때 힌트를 다시 띄우는 간격(초)</summary>
        public const float HintDelay = 20f;
        /// <summary>체력이 최대의 이 비율 밑이면 가득 채운다</summary>
        private const float MinHealthRatio = 0.3f;
        /// <summary>Esc 를 이만큼(초, 실제 시간) 누르고 있으면 건너뛴다</summary>
        private const float SkipHoldTime = 1.5f;
        private const float ExitFadeTime = 0.8f;
        /// <summary>무리를 둘러 세우는 반지름(m)</summary>
        private const float GroupRadius = 2.2f;

        [SerializeField] private TutorialScript script;
        [Tooltip("튜토리얼이 끝나면 갈 게임 씬 (Build Settings 이름)")]
        [SerializeField] private string nextSceneName = "Monster Player Integration";

        [Header("① 이동 · 질주 · 구르기")]
        [SerializeField] private TutorialZone moveTarget;
        [SerializeField] private TutorialDamageBarrier[] rollBarriers = Array.Empty<TutorialDamageBarrier>();
        [SerializeField] private TutorialGate movementGate;

        [Header("② 조준 · 사격")]
        [SerializeField] private Transform[] aimSpawnPoints = Array.Empty<Transform>();
        [SerializeField] private TutorialGate aimGate;

        [Header("③ 수류탄")]
        [SerializeField] private Transform grenadeGroupCenter;
        [SerializeField] private TutorialGate grenadeGate;

        [Header("④ 보급")]
        [SerializeField] private TutorialZone supplyEntrance;
        [SerializeField] private Transform healDropPoint;
        [SerializeField] private Transform grenadeDropPoint;
        [SerializeField] private Transform weaponDropPoint;
        [SerializeField] private Transform weaponGroupCenter;
        [SerializeField] private TutorialGate supplyGate;

        [Header("⑤ 강화 체험")]
        [SerializeField] private TutorialZone upgradeZone;
        [SerializeField] private TutorialGate upgradeGate;

        [Header("⑥ 전장 입장")]
        [SerializeField] private TutorialZone exitZone;

        // ── 단계가 쓰는 공용 참조 ──
        public TutorialScript Script => script;
        public TutorialHud Hud { get; private set; }
        public TutorialWaypointHud Waypoints { get; private set; }
        public PlayerController Player { get; private set; }
        public PlayerItems Items { get; private set; }
        public GunSlot Guns { get; private set; }
        public SupplyDropper Dropper { get; private set; }

        public TutorialZone MoveTarget => moveTarget;
        public IReadOnlyList<TutorialDamageBarrier> RollBarriers => rollBarriers;
        public TutorialGate MovementGate => movementGate;
        public IReadOnlyList<Transform> AimSpawnPoints => aimSpawnPoints;
        public TutorialGate AimGate => aimGate;
        public Transform GrenadeGroupCenter => grenadeGroupCenter;
        public TutorialGate GrenadeGate => grenadeGate;
        public TutorialZone SupplyEntrance => supplyEntrance;
        public Transform HealDropPoint => healDropPoint;
        public Transform GrenadeDropPoint => grenadeDropPoint;
        public Transform WeaponDropPoint => weaponDropPoint;
        public Transform WeaponGroupCenter => weaponGroupCenter;
        public TutorialGate SupplyGate => supplyGate;
        public TutorialZone UpgradeZone => upgradeZone;
        public TutorialGate UpgradeGate => upgradeGate;
        public TutorialZone ExitZone => exitZone;

        private readonly List<TutorialStep> steps = new List<TutorialStep>();
        private TutorialStep current;
        private Coroutine running;
        private float lastProgressTime;
        private float skipHeld;
        private bool finishing;

        #region 유니티 메시지
        private void Awake()
        {
            Hud = GetComponent<TutorialHud>();
            if (Hud == null) Hud = gameObject.AddComponent<TutorialHud>();
            Waypoints = GetComponent<TutorialWaypointHud>();
            if (Waypoints == null) Waypoints = gameObject.AddComponent<TutorialWaypointHud>();
        }

        private void OnEnable() => EventBus<CurrencyChangedEvent>.Subscribe(OnCurrencyChanged);

        private void OnDisable()
        {
            EventBus<CurrencyChangedEvent>.Unsubscribe(OnCurrencyChanged);
            current?.Cleanup();
            ReleaseRestrictions();
        }

        private void Start()
        {
            Player = FindAnyObjectByType<PlayerController>();
            Dropper = FindAnyObjectByType<SupplyDropper>();
            if (script == null || Player == null)
            {
                Debug.LogError($"[Tutorial] {(script == null ? "TutorialScript" : "PlayerController")} 가 없어 시작할 수 없습니다. Tools > Tutorial > Setup Tutorial Scene 을 다시 실행하세요.", this);
                enabled = false;
                return;
            }
            Items = Player.GetComponent<PlayerItems>();
            Guns = Player.GetComponent<GunSlot>();
            ResetServices();
            if (Items != null) Items.ConsumeGrenades = false;
            Allow(PlayerAbility.None); // 첫 대사 동안은 둘러보기만

            steps.Add(new MoveRollStep());
            steps.Add(new AimStep());
            steps.Add(new WeaponSwitchStep());
            steps.Add(new GrenadeStep());
            steps.Add(new SupplyStep());
            steps.Add(new UpgradeStep());
            steps.Add(new ExitStep());
            foreach (var step in steps) step.Bind(this);

            running = StartCoroutine(Run());
        }

        private void Update()
        {
            if (finishing) return;
            KeepAlive();
            UpdateSkip();
        }
        #endregion

        #region 단계가 쓰는 기능
        /// <summary>대사를 띄우고 다 읽을 때까지 기다린다</summary>
        public IEnumerator Say(string lineId)
        {
            float duration = SayNow(lineId);
            if (duration > 0f) yield return new WaitForSeconds(duration);
        }

        /// <summary>대사를 띄우고 바로 돌아온다. 반환: 읽는 시간(초). 대사가 없으면 0</summary>
        public float SayNow(string lineId)
        {
            var line = script.FindLine(lineId);
            if (line == null)
            {
                Debug.LogWarning($"[Tutorial] 대사 '{lineId}' 가 TutorialScript 에 없습니다.", script);
                return 0f;
            }
            return Hud.Say(line);
        }

        /// <summary>목적지 구역을 화면 표식으로 표시한다. labelId = TutorialScript 목표 문구 Id (예: wp_exit)</summary>
        public void ShowDestination(TutorialZone zone, string labelId)
        {
            if (zone != null) ShowDestination(zone.transform, labelId);
        }

        /// <summary>목적지 지점을 화면 표식으로 표시한다 (몬스터 무리 등)</summary>
        public void ShowDestination(Transform point, string labelId)
        {
            if (point == null) return;
            Waypoints.Show(point, script.ObjectiveText(labelId));
        }

        public void HideDestination() => Waypoints.Hide();

        /// <summary>목표 패널 영문 머리말: 훈련 과목이면 "TRAINING 02 / 05", 아니면 "DEPLOY"</summary>
        public string HeaderOf(TutorialStep step)
        {
            if (step.IsTraining == false) return "DEPLOY";
            int index = 0, total = 0;
            foreach (var s in steps)
            {
                if (s.IsTraining == false) continue;
                total++;
                if (s == step) index = total;
            }
            return $"TRAINING {index:00} / {total:00}";
        }

        /// <summary>입력으로 할 수 있는 행동을 abilities 로 좁힌다 (시선·Esc 는 항상 된다)</summary>
        public void Allow(PlayerAbility abilities)
        {
            if (Player != null) Player.SetAllowedAbilities(abilities);
        }

        /// <summary>진행이 있었다 (힌트 타이머를 다시 센다)</summary>
        public void MarkProgress() => lastProgressTime = Time.time;

        /// <summary>done 이 참이 될 때까지 기다린다. HintDelay 동안 진행(MarkProgress)이 없으면 hintId 대사를 띄운다</summary>
        public IEnumerator WaitUntil(Func<bool> done, string hintId)
        {
            MarkProgress();
            while (done() == false)
            {
                if (hintId != null && Time.time - lastProgressTime >= HintDelay)
                {
                    SayNow(hintId);
                    MarkProgress();
                }
                yield return null;
            }
        }

        /// <summary>휴면 개체 하나를 소환한다 (TutorialScript.DormantMonsterId). 실패하면 null</summary>
        public MonsterController SpawnDormant(Vector3 position, Quaternion rotation)
        {
            if (DataManager.Monsters.TryGet(script.DormantMonsterId, out var def) == false)
            {
                Debug.LogError($"[Tutorial] MonsterTable 에 {script.DormantMonsterId} 가 없습니다.", script);
                return null;
            }
            var go = ServiceLocator.Get<IMonsterFactory>()?.Spawn(def, position, rotation);
            if (go == null || go.TryGetComponent(out MonsterController monster) == false) return null;
            monster.SetDormant(true);
            return monster;
        }

        /// <summary>center 둘레에 휴면 개체 count 마리를 원형으로 세운다 (플레이어 쪽을 보게)</summary>
        public List<MonsterController> SpawnDormantGroup(Transform center, int count)
        {
            var group = new List<MonsterController>();
            if (center == null) return group;
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                Vector3 offset = count == 1 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * GroupRadius;
                var monster = SpawnDormant(center.position + offset, center.rotation);
                if (monster != null) group.Add(monster);
            }
            return group;
        }

        public static int CountDead(List<MonsterController> group)
        {
            int dead = 0;
            foreach (var monster in group)
                if (monster == null || monster.IsAlive == false) dead++;
            return dead;
        }

        /// <summary>남은 개체를 보상 없이 회수한다</summary>
        public static void DespawnAlive(List<MonsterController> group)
        {
            if (ServiceLocator.TryGet<IMonsterFactory>(out var factory) == false) return;
            foreach (var monster in group)
                if (monster != null && monster.IsAlive) factory.Despawn(monster.gameObject);
        }
        #endregion

        #region 흐름
        private IEnumerator Run()
        {
            yield return null; // 플레이어·무기 슬롯 Start 다음
            yield return Say("vo_tut_00_wake");
            yield return Say("vo_tut_00_test");
            foreach (var step in steps)
            {
                current = step;
                yield return step.Run();
                step.Cleanup();
                current = null;
            }
            Finish(false);
        }

        private void Finish(bool skipped)
        {
            if (finishing) return;
            finishing = true;
            Debug.Log($"[Tutorial] {(skipped ? "건너뛰기" : "전 과목 통과")} → {nextSceneName}", this);
            if (running != null) StopCoroutine(running);
            current?.Cleanup();
            current = null;
            TutorialProgress.MarkDone();
            ResetServices();
            ReleaseRestrictions();
            Hud.ClearObjective();
            Hud.SetSkipProgress(0f);
            HideDestination();
            Player.SetMenuInputBlocked(true);
            StartCoroutine(Exit(skipped));
        }

        private IEnumerator Exit(bool skipped)
        {
            if (skipped) yield return new WaitForSecondsRealtime(Mathf.Min(2f, SayNow("vo_tut_99_skip")));
            yield return Hud.FadeOut(ExitFadeTime);
            Time.timeScale = 1f;
            SceneTransition.LoadAsync(nextSceneName, 0.3f, destroyCancellationToken).Forget();
        }

        /// <summary>튜토리얼에서 번 재화·산 강화를 비운다 (지갑·강화 장부는 씬을 넘어 남는 서비스다). 시작·끝 모두</summary>
        private static void ResetServices()
        {
            if (ServiceLocator.TryGet<ICurrencyWallet>(out var wallet)) wallet.ResetTo(0);
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades)) upgrades.ResetAll();
        }

        /// <summary>행동 제한·수류탄 무소모를 되돌린다. 종료·건너뛰기·비활성 모두</summary>
        private void ReleaseRestrictions()
        {
            Allow(PlayerAbility.All);
            if (Items != null) Items.ConsumeGrenades = true;
        }

        /// <summary>튜토리얼에서는 죽지 않는다: 체력이 너무 낮으면 가득 채운다</summary>
        private void KeepAlive()
        {
            if (Player.IsAlive && Player.CurrentHealth < Player.MaxHealth * MinHealthRatio) Player.Heal(Player.MaxHealth);
        }

        private void UpdateSkip()
        {
            var keyboard = Keyboard.current;
            bool held = keyboard != null && keyboard.escapeKey.isPressed;
            skipHeld = held ? skipHeld + Time.unscaledDeltaTime : 0f;
            Hud.SetSkipProgress(skipHeld / SkipHoldTime);
            if (skipHeld >= SkipHoldTime) Finish(true);
        }

        private void OnCurrencyChanged(CurrencyChangedEvent e)
        {
            if (e.Delta > 0 && finishing == false) Hud.ShowReward(e.Delta, e.Balance);
        }
        #endregion
    }
}
