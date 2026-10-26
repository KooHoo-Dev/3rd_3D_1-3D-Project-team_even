using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HybridShooter.Weapon;
using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 애니메이션 떨림 자동 측정 (디버그 전용). 메뉴 Tools/HybridShooter/Animation Jitter Test 가 Play 중인 Player 에 붙인다.
    /// 키보드·마우스 입력을 막고, 정해 둔 이동(시나리오)을 같은 조건으로 반복하면서 AnimJitterProbe 로 잰다
    ///  - 실행 하나 = 순간이동 → 멈춰서 안정(settle) → 이동 시작 후 안정(warm) → 기록(record)
    ///  - 설정: Baseline / AimIK 끔 / 왼손 IK 끔 / Grounder 끔 / Upper Pose 레이어 끔
    ///  - 바닥: Flat(멀리 위에 만든 평평한 판) / Scene(시작 자리의 실제 지형)
    ///  - 묶음(Full): 평지 IK 비교 전부, 실제 지형 Baseline, 144fps, 1인칭. Quick 은 평지 Baseline 만
    /// 결과: OutputFolder 에 실행별 CSV + summary.md. 끝나면 Completed(폴더) 를 알리고 모든 설정을 되돌린 뒤 스스로 지운다.
    /// 스태미나는 매 프레임 채운다 (바닥나서 전력질주가 끊기는 것을 떨림으로 세지 않게)
    /// 실행 순서 10: PlayerController.Update(입력) 뒤, PlayerAnimationDriver(50) 앞
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(10)]
    public class AnimJitterScenarioRunner : MonoBehaviour
    {
        public enum Config { Baseline, NoAimIK, NoLeftHandIK, NoGrounder, NoUpperPose }
        public enum Surface { Flat, Scene }

        [Serializable]
        public struct Scenario
        {
            public string name;
            /// <summary>몸 기준 입력 (x 오른쪽, y 앞). 크기 1 = 키보드 한 방향</summary>
            public Vector2 input;
            public bool sprint;
            public bool aim;
            /// <summary>시선 yaw 회전 속도(도/초). 입력은 시선 기준이라 몸과 함께 돈다</summary>
            public float yawRate;

            public Scenario(string name, Vector2 input, bool sprint = false, bool aim = false, float yawRate = 0f)
            {
                this.name = name;
                this.input = input;
                this.sprint = sprint;
                this.aim = aim;
                this.yawRate = yawRate;
            }
        }

        private struct RunSpec
        {
            public string pass;
            public Surface surface;
            public ViewMode view;
            public int fps;
            public Scenario scenario;
            public Config config;
        }

        private enum Phase { Boot, Settle, Warm, Record, Done }

        /// <summary>측정이 끝나면 결과 폴더 경로 (에디터 메뉴가 Play 를 끝낸다)</summary>
        public static event Action<string> Completed;

        public static readonly Scenario[] Scenarios =
        {
            new Scenario("Idle", Vector2.zero),
            new Scenario("Forward", new Vector2(0f, 1f)),
            new Scenario("Backward", new Vector2(0f, -1f)),
            new Scenario("StrafeLeft", new Vector2(-1f, 0f)),
            new Scenario("StrafeRight", new Vector2(1f, 0f)),
            new Scenario("DiagForwardLeft", new Vector2(-1f, 1f).normalized),
            new Scenario("SprintForward", new Vector2(0f, 1f), sprint: true),
            new Scenario("SprintDiagonal", new Vector2(-1f, 1f).normalized, sprint: true),
            new Scenario("ForwardTurning", new Vector2(0f, 1f), yawRate: 90f),
            new Scenario("AimForward", new Vector2(0f, 1f), aim: true),
            new Scenario("AimStrafe", new Vector2(1f, 0f), aim: true),
        };

        [Tooltip("true = 평지 Baseline 만 (약 1분), false = 전체 (약 5분)")]
        public bool quick;
        [Tooltip("결과 폴더. 비우면 <프로젝트>/Logs/AnimJitter/<시각>")]
        public string outputFolder;
        [SerializeField] private float bootTime = 1f;
        [SerializeField] private float settleTime = 0.6f;
        [SerializeField] private float warmTime = 0.8f;
        [SerializeField] private float recordTime = 3f;
        [Tooltip("튐 = 가속도 > 중앙값 × 이 값")]
        [SerializeField] private float popRatio = 4f;
        [Tooltip("튐 최소 가속도(m/s²). 거의 멈춰 있을 때 작은 떨림까지 세지 않게")]
        [SerializeField] private float popFloor = 5f;

        private PlayerController player;
        private PlayerAnimationDriver driver;
        private PlayerBodyIK bodyIK;
        private GunSlot gunSlot;
        private AimIK aimIK;
        private GrounderFBBIK grounder;
        private AnimJitterProbe probe;
        private bool aimIKWasEnabled, grounderWasEnabled;
        private int savedTargetFrameRate, savedVSync;
        private ViewMode savedView;
        private Vector3 startPosition;
        private float startYaw;
        private GameObject flatGround;
        private Vector3 flatSpawn;

        private readonly List<RunSpec> runs = new List<RunSpec>();
        private readonly List<AnimJitterReport.RunResult> results = new List<AnimJitterReport.RunResult>();
        private int runIndex = -1;
        private Phase phase = Phase.Boot;
        private float phaseStart;
        private float moveStart;
        private string gunName = "-";

        private void Start()
        {
            player = GetComponent<PlayerController>();
            driver = GetComponent<PlayerAnimationDriver>();
            bodyIK = GetComponent<PlayerBodyIK>();
            gunSlot = GetComponent<GunSlot>();
            aimIK = GetComponentInChildren<AimIK>(true);
            grounder = GetComponentInChildren<GrounderFBBIK>(true);
            if (player == null || driver == null)
            {
                Debug.LogError($"{name}: AnimJitterScenarioRunner 는 PlayerController·PlayerAnimationDriver 가 있는 Player 에 붙여야 합니다", this);
                Destroy(this);
                return;
            }

            probe = GetComponent<AnimJitterProbe>();
            if (probe == null) probe = gameObject.AddComponent<AnimJitterProbe>();
            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "AnimJitter", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            Directory.CreateDirectory(outputFolder);

            aimIKWasEnabled = aimIK != null && aimIK.enabled;
            grounderWasEnabled = grounder != null && grounder.enabled;
            savedTargetFrameRate = Application.targetFrameRate;
            savedVSync = QualitySettings.vSyncCount;
            savedView = player.CurrentViewMode;
            startPosition = player.transform.position;
            startYaw = player.LookYaw;
            CreateFlatGround();
            BuildRuns();

            player.DebugBlockInput = true;
            QualitySettings.vSyncCount = 0;
            phase = Phase.Boot;
            phaseStart = Time.time;
            Debug.Log($"[AnimJitter] 측정 시작: {runs.Count}회 (약 {runs.Count * (settleTime + warmTime + recordTime) / 60f:0.0}분) → {outputFolder}");
        }

        private void BuildRuns()
        {
            runs.Clear();
            foreach (Config config in Enum.GetValues(typeof(Config)))
            {
                if (quick && config != Config.Baseline) continue;
                foreach (Scenario s in Scenarios) Add("IKCompare", Surface.Flat, ViewMode.ThirdPerson, 60, s, config);
            }
            if (quick) return;
            foreach (Scenario s in Scenarios) Add("SceneGround", Surface.Scene, ViewMode.ThirdPerson, 60, s, Config.Baseline);
            foreach (string name in new[] { "Forward", "SprintForward", "ForwardTurning" })
                Add("Fps144", Surface.Flat, ViewMode.ThirdPerson, 144, Find(name), Config.Baseline);
            foreach (string name in new[] { "Forward", "ForwardTurning", "AimForward" })
                Add("FirstPerson", Surface.Flat, ViewMode.FirstPerson, 60, Find(name), Config.Baseline);
        }

        private void Add(string pass, Surface surface, ViewMode view, int fps, Scenario scenario, Config config) =>
            runs.Add(new RunSpec { pass = pass, surface = surface, view = view, fps = fps, scenario = scenario, config = config });

        private static Scenario Find(string name) => Array.Find(Scenarios, s => s.name == name);

        /// <summary>시작 자리 위 높은 곳에 평평한 판 (씬 지형·벽과 섞이지 않게). 레이어 = 바닥 판정(GroundMask)에 들어가는 첫 레이어</summary>
        private void CreateFlatGround()
        {
            flatGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flatGround.name = "AnimJitter_FlatGround";
            int mask = player.GroundMask.value;
            int layer = 0;
            while (layer < 31 && (mask & (1 << layer)) == 0) layer++;
            flatGround.layer = (mask & (1 << layer)) != 0 ? layer : 0;
            flatGround.transform.position = startPosition + Vector3.up * 400f;
            flatGround.transform.localScale = new Vector3(80f, 1f, 80f);
            flatSpawn = flatGround.transform.position + Vector3.up * 0.55f;
        }

        private void Update()
        {
            if (phase == Phase.Done || probe == null) return;
            float elapsed = Time.time - phaseStart;

            switch (phase)
            {
                case Phase.Boot:
                    if (elapsed >= bootTime && probe.Ready) StartRun(runIndex + 1);
                    return;
                case Phase.Settle:
                    if (elapsed >= settleTime) { SetPhase(Phase.Warm); moveStart = Time.time; }
                    break;
                case Phase.Warm:
                    if (elapsed >= warmTime) { SetPhase(Phase.Record); probe.BeginRecording(); }
                    break;
                case Phase.Record:
                    if (elapsed >= recordTime)
                    {
                        FinishRun();
                        StartRun(runIndex + 1);
                        return;
                    }
                    break;
            }
            DriveInput();
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStart = Time.time;
        }

        /// <summary>시선·이동 입력을 매 프레임 넣는다 (PlayerController.Update 를 막아 두었으므로 여기가 유일한 입력)</summary>
        private void DriveInput()
        {
            RunSpec run = runs[runIndex];
            bool moving = phase != Phase.Settle;
            float yaw = RunYaw(run) + (moving ? run.scenario.yawRate * (Time.time - moveStart) : 0f);
            player.DebugSetLook(yaw, 0f);
            Vector2 input = moving ? run.scenario.input : Vector2.zero;
            player.SetMoveInput(Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y), moving && run.scenario.sprint);
            player.RestoreStamina();
        }

        private float RunYaw(RunSpec run) => run.surface == Surface.Scene ? startYaw : 0f;

        private void StartRun(int index)
        {
            runIndex = index;
            if (runIndex >= runs.Count)
            {
                Finish();
                return;
            }

            RunSpec run = runs[runIndex];
            ApplyConfig(run.config);
            Application.targetFrameRate = run.fps;
            if (player.CurrentViewMode != run.view) player.SwitchViewMode(run.view, 0f);
            driver.DebugAimOverride = run.scenario.aim;
            player.Teleport(run.surface == Surface.Flat ? flatSpawn : startPosition);
            player.SetMoveInput(Vector3.zero, false);
            player.DebugSetLook(RunYaw(run), 0f);
            SetPhase(Phase.Settle);
        }

        private void ApplyConfig(Config config)
        {
            if (aimIK != null) aimIK.enabled = aimIKWasEnabled && config != Config.NoAimIK;
            if (bodyIK != null) bodyIK.DebugDisableLeftHand = config == Config.NoLeftHandIK;
            if (grounder != null) grounder.enabled = grounderWasEnabled && config != Config.NoGrounder;
            driver.DebugUpperPoseOff = config == Config.NoUpperPose;
        }

        private void FinishRun()
        {
            probe.EndRecording();
            RunSpec run = runs[runIndex];
            AnimJitterReport.RunResult result = AnimJitterReport.Analyze(probe.Samples, popRatio, popFloor);
            result.pass = run.pass;
            result.surface = run.surface.ToString();
            result.view = run.view == ViewMode.FirstPerson ? "1P" : "3P";
            result.targetFps = run.fps;
            result.scenario = run.scenario.name;
            result.config = run.config.ToString();
            results.Add(result);
            if (gunSlot != null && gunSlot.CurrentGun != null) gunName = gunSlot.CurrentGun.name;

            try
            {
                AnimJitterReport.WriteCsv(Path.Combine(outputFolder, AnimJitterReport.CsvName(results.Count - 1, result)), probe.Samples);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AnimJitter] CSV 저장 실패: {e.Message}");
            }
        }

        private void Finish()
        {
            phase = Phase.Done;
            string header =
                $"- 측정 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}\n" +
                $"- 총: {gunName}, 물리 박자: {Time.fixedDeltaTime * 1000f:0.#}ms, 실행 {results.Count}회 (안정 {settleTime}s + 출발 {warmTime}s + 기록 {recordTime}s)\n" +
                $"- 바닥: Flat = 시작 자리 400m 위 평평한 판, Scene = 시작 자리 실제 지형\n" +
                (File.Exists(Path.Combine(outputFolder, "clip_loop.md")) ? "- 클립 루프 검사: [clip_loop.md](clip_loop.md)\n" : "");
            string summaryPath = Path.Combine(outputFolder, "summary.md");
            try
            {
                File.WriteAllText(summaryPath, AnimJitterReport.Summary(results, header, popRatio, popFloor), new UTF8Encoding(false));
                Debug.Log($"[AnimJitter] 측정 끝: {summaryPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AnimJitter] summary.md 저장 실패: {e.Message}");
            }

            Restore();
            Completed?.Invoke(outputFolder);
            Destroy(probe);
            Destroy(this);
        }

        private void Restore()
        {
            ApplyConfig(Config.Baseline);
            if (aimIK != null) aimIK.enabled = aimIKWasEnabled;
            if (grounder != null) grounder.enabled = grounderWasEnabled;
            driver.DebugAimOverride = null;
            Application.targetFrameRate = savedTargetFrameRate;
            QualitySettings.vSyncCount = savedVSync;
            if (player.CurrentViewMode != savedView) player.SwitchViewMode(savedView, 0f);
            player.SetMoveInput(Vector3.zero, false);
            player.Teleport(startPosition);
            player.DebugSetLook(startYaw, 0f);
            player.DebugBlockInput = false;
            if (flatGround != null) Destroy(flatGround);
        }

        private void OnDestroy()
        {
            // 중간에 Play 를 끝내도 다음 Play 에 영향은 없다 (런타임 값뿐). 측정 중에 컴포넌트만 지운 경우 입력을 되돌린다
            if (phase != Phase.Done && player != null)
            {
                player.DebugBlockInput = false;
                if (driver != null) { driver.DebugAimOverride = null; driver.DebugUpperPoseOff = false; }
                if (bodyIK != null) bodyIK.DebugDisableLeftHand = false;
                if (flatGround != null) Destroy(flatGround);
            }
        }

        private void OnGUI()
        {
            if (phase == Phase.Done || runIndex < 0 || runIndex >= runs.Count) return;
            RunSpec run = runs[runIndex];
            string text = $"Animation Jitter Test {runIndex + 1}/{runs.Count}  {run.pass} · {run.surface} · {(run.view == ViewMode.FirstPerson ? "1P" : "3P")} · {run.fps}fps · {run.scenario.name} · {run.config}  [{phase}]\n" +
                          "측정 중에는 키보드·마우스를 건드리지 마세요";
            GUI.Box(new Rect(10, Screen.height - 60, 760, 48), text);
        }
    }
}
