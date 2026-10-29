using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 1인칭 점프 가림 자동 측정 (디버그 전용). 메뉴 Tools/HybridShooter/First Person Aim Test/Run Jump 가 Play 중인 Player 에 붙인다.
    /// 점프 동작이 몸(목·쇄골·가슴)을 1인칭 눈 높이까지 올려 화면을 가리는지, 몸 내리기(PlayerFirstPersonIK.minNeckBelowEye)가 막는지 잰다
    ///  - 실행 하나 = 평평한 판 위에서 안정(settle, 지상 값 기록) → 점프 → 착지 뒤 landRecordTime 까지 기록
    ///  - 설정: Baseline = 몸 내리기 끔 / Current = 지금 인스펙터 값
    ///  - 조합: 설정 × (제자리, 앞으로 걷기) × pitch(정면, 아래 30°)
    ///  - 측정값(프레임마다, 모든 IK·목 접기가 끝난 뒤, 눈 높이 - 뼈 높이 cm. 작을수록 카메라에 가깝다):
    ///    목(몸 내리기 전·최종), 쇄골·위팔 중 가장 높은 것, 가슴, 골반 상승(루트 기준), 몸 내린 거리, 손 오차, 조준기 오차
    /// 결과: OutputFolder 에 samples.csv + summary.md (맨 위 결론, 권장 minNeckBelowEye). 끝나면 Completed(폴더) 를 알리고 설정을 되돌린 뒤 스스로 지운다
    /// 실행 순서 10003: LateUpdate 는 모든 IK·목 접기(10002) 뒤
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(10003)]
    public class FirstPersonJumpScenarioRunner : MonoBehaviour
    {
        public enum Config { Baseline, Current }

        private struct RunSpec
        {
            public bool walk;
            public float pitch;
            public Config config;
        }

        private struct Sample
        {
            public int run;
            public float t;
            public bool air;
            public float neckPreCm, neckCm, shoulderCm, chestCm, hipRiseCm, dropCm, handCm, sightCm;
        }

        private enum Phase { Boot, Settle, Jump, Done }

        /// <summary>측정이 끝나면 결과 폴더 경로 (에디터 메뉴가 Play 를 끝낸다)</summary>
        public static event Action<string> Completed;

        public static readonly float[] Pitches = { 0f, 30f };

        [Tooltip("결과 폴더. 비우면 <프로젝트>/Logs/FirstPersonJump/<시각>")]
        public string outputFolder;
        [SerializeField] private float bootTime = 1f;
        [SerializeField] private float settleTime = 0.8f;
        [SerializeField] private float landRecordTime = 0.5f;
        [SerializeField] private float jumpTimeout = 3f;

        [Header("판정")]
        [Tooltip("공중에서 눈이 목보다 최소 이만큼(cm) 위에 있어야 한다")]
        [SerializeField] private float neckPassCm = 8f;
        [Tooltip("공중에서 눈이 쇄골·위팔보다 최소 이만큼(cm) 위에 있어야 한다")]
        [SerializeField] private float shoulderPassCm = 8f;
        [Tooltip("조준기 오차(cm) 최대")]
        [SerializeField] private float sightPassCm = 1f;

        private PlayerController player;
        private PlayerAnimationDriver driver;
        private PlayerBodyIK bodyIK;
        private PlayerFirstPersonIK fpIK;
        private PlayerFirstPersonNeck fpNeck;
        private Transform hips, neck, chest;
        private Transform[] shoulders;
        private ViewMode savedView;
        private Vector3 startPosition;
        private float startYaw;
        private GameObject flatGround;
        private Vector3 flatSpawn;

        private readonly List<RunSpec> runs = new List<RunSpec>();
        private readonly List<Sample> samples = new List<Sample>();
        private int runIndex = -1;
        private Phase phase = Phase.Boot;
        private float phaseStart;
        private bool jumped;
        private float landedTime = -1f;

        private void Start()
        {
            player = GetComponent<PlayerController>();
            driver = GetComponent<PlayerAnimationDriver>();
            bodyIK = GetComponent<PlayerBodyIK>();
            fpIK = GetComponent<PlayerFirstPersonIK>();
            fpNeck = GetComponent<PlayerFirstPersonNeck>();
            Animator animator = driver != null && driver.Animator != null ? driver.Animator : GetComponentInChildren<Animator>();
            if (player == null || fpIK == null || animator == null || animator.isHuman == false)
            {
                Debug.LogError($"{name}: FirstPersonJumpScenarioRunner 는 휴머노이드 몸과 PlayerController·PlayerFirstPersonIK 가 있는 Player 에 붙여야 합니다", this);
                Destroy(this);
                return;
            }

            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            if (neck == null) neck = animator.GetBoneTransform(HumanBodyBones.Head);
            chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            shoulders = new[] { HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm }
                .Select(animator.GetBoneTransform).Where(t => t != null).ToArray();

            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "FirstPersonJump", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            Directory.CreateDirectory(outputFolder);

            savedView = player.CurrentViewMode;
            startPosition = player.transform.position;
            startYaw = player.LookYaw;
            CreateFlatGround();
            foreach (Config config in Enum.GetValues(typeof(Config)))
                foreach (bool walk in new[] { false, true })
                    foreach (float pitch in Pitches)
                        runs.Add(new RunSpec { walk = walk, pitch = pitch, config = config });

            player.Jumped += OnJumped;
            player.Landed += OnLanded;
            player.DebugBlockInput = true;
            if (player.CurrentViewMode != ViewMode.FirstPerson) player.SwitchViewMode(ViewMode.FirstPerson, 0f);
            player.Teleport(flatSpawn);
            phaseStart = Time.time;
            Debug.Log($"[FirstPersonJump] 측정 시작: {runs.Count}회 → {outputFolder}");
        }

        /// <summary>시작 자리 위 높은 곳에 평평한 판 (씬 지형·천장과 섞이지 않게)</summary>
        private void CreateFlatGround()
        {
            flatGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flatGround.name = "FirstPersonJump_FlatGround";
            int mask = player.GroundMask.value;
            int layer = 0;
            while (layer < 31 && (mask & (1 << layer)) == 0) layer++;
            flatGround.layer = (mask & (1 << layer)) != 0 ? layer : 0;
            flatGround.transform.position = startPosition + Vector3.up * 400f;
            flatGround.transform.localScale = new Vector3(80f, 1f, 80f);
            flatSpawn = flatGround.transform.position + Vector3.up * 0.55f;
        }

        private void OnJumped() => jumped = true;

        private void OnLanded()
        {
            if (phase == Phase.Jump && jumped && landedTime < 0f) landedTime = Time.time;
        }

        private void Update()
        {
            if (phase == Phase.Done) return;
            float elapsed = Time.time - phaseStart;
            player.RestoreStamina();
            if (phase == Phase.Boot)
            {
                player.SetMoveInput(Vector3.zero, false);
                if (elapsed >= bootTime) StartRun(runIndex + 1);
                return;
            }

            RunSpec run = runs[runIndex];
            player.SetMoveInput(run.walk ? Vector3.forward : Vector3.zero, false);
            player.DebugSetLook(0f, run.pitch);
            switch (phase)
            {
                case Phase.Settle:
                    if (elapsed >= settleTime && player.IsOnGround && player.CameraChanging == false)
                    {
                        jumped = false;
                        landedTime = -1f;
                        player.RequestJump();
                        SetPhase(Phase.Jump);
                    }
                    break;
                case Phase.Jump:
                    if ((landedTime >= 0f && Time.time - landedTime >= landRecordTime) || elapsed >= jumpTimeout)
                        StartRun(runIndex + 1);
                    break;
            }
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStart = Time.time;
        }

        private void StartRun(int index)
        {
            runIndex = index;
            if (runIndex >= runs.Count)
            {
                Finish();
                return;
            }

            RunSpec run = runs[runIndex];
            fpIK.DebugMinNeckBelowEye = run.config == Config.Baseline ? 0f : (float?)null;
            player.SetMoveInput(Vector3.zero, false);
            player.Teleport(flatSpawn);
            player.DebugSetLook(0f, run.pitch);
            SetPhase(Phase.Settle);
        }

        /// <summary>모든 IK·목 접기가 끝난 뒤 이번 프레임 값을 기록 (안정 구간 뒤쪽 = 지상 값, 점프 구간 = 공중·착지)</summary>
        private void LateUpdate()
        {
            if (phase != Phase.Settle && phase != Phase.Jump) return;
            float elapsed = Time.time - phaseStart;
            if (phase == Phase.Settle && elapsed < settleTime * 0.5f) return;

            float eyeY = player.FirstPersonEyePose.position.y;
            float shoulderY = shoulders.Length > 0 ? shoulders.Max(t => t.position.y) : float.NegativeInfinity;
            Sample s = new Sample
            {
                run = runIndex,
                t = phase == Phase.Jump ? elapsed : elapsed - settleTime,
                air = phase == Phase.Jump && player.IsOnGround == false,
                neckPreCm = fpIK.NeckBelowEyeBeforeDrop * 100f,
                neckCm = neck != null ? (eyeY - neck.position.y) * 100f : 0f,
                shoulderCm = shoulders.Length > 0 ? (eyeY - shoulderY) * 100f : 0f,
                chestCm = chest != null ? (eyeY - chest.position.y) * 100f : 0f,
                hipRiseCm = hips != null ? (hips.position.y + fpIK.BodyDrop - player.transform.position.y) * 100f : 0f,
                dropCm = fpIK.BodyDrop * 100f,
                handCm = fpIK.Weight > 0f ? fpIK.HandPositionError : 0f,
                sightCm = fpIK.Weight > 0f && bodyIK != null && bodyIK.Sight != null ? Vector3.Distance(bodyIK.Sight.position, fpIK.SightTarget) * 100f : 0f,
            };
            samples.Add(s);
        }

        private void Finish()
        {
            phase = Phase.Done;
            try
            {
                File.WriteAllText(Path.Combine(outputFolder, "samples.csv"), Csv(), new UTF8Encoding(false));
                string summaryPath = Path.Combine(outputFolder, "summary.md");
                File.WriteAllText(summaryPath, Summary(), new UTF8Encoding(false));
                Debug.Log($"[FirstPersonJump] 측정 끝: {summaryPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirstPersonJump] 결과 저장 실패: {e.Message}");
            }

            Restore();
            Completed?.Invoke(outputFolder);
            Destroy(this);
        }

        private void Restore()
        {
            fpIK.DebugMinNeckBelowEye = null;
            if (player.CurrentViewMode != savedView) player.SwitchViewMode(savedView, 0f);
            player.SetMoveInput(Vector3.zero, false);
            player.Teleport(startPosition);
            player.DebugSetLook(startYaw, 0f);
            player.DebugBlockInput = false;
            if (flatGround != null) Destroy(flatGround);
        }

        private void OnDestroy()
        {
            if (player == null) return;
            player.Jumped -= OnJumped;
            player.Landed -= OnLanded;
            // 측정 중에 컴포넌트만 지운 경우 디버그 값과 입력을 되돌린다
            if (phase == Phase.Done) return;
            player.DebugBlockInput = false;
            player.SetMoveInput(Vector3.zero, false);
            if (fpIK != null) fpIK.DebugMinNeckBelowEye = null;
            if (flatGround != null) Destroy(flatGround);
        }

        private void OnGUI()
        {
            if (phase == Phase.Done || runIndex < 0 || runIndex >= runs.Count) return;
            RunSpec run = runs[runIndex];
            string text = $"First Person Jump Test {runIndex + 1}/{runs.Count}  {(run.walk ? "걷기" : "제자리")} · pitch {run.pitch:0} · {run.config}  [{phase}]\n" +
                          "측정 중에는 키보드·마우스를 건드리지 마세요";
            GUI.Box(new Rect(10, Screen.height - 60, 620, 48), text);
        }

        #region 결과
        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private string Csv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("run,walk,config,pitch,t,air,neckPreCm,neckCm,shoulderCm,chestCm,hipRiseCm,dropCm,handCm,sightCm");
            foreach (Sample s in samples)
            {
                RunSpec r = runs[s.run];
                sb.Append(s.run).Append(',').Append(r.walk ? 1 : 0).Append(',').Append(r.config).Append(',').Append(F(r.pitch)).Append(',')
                  .Append(F(s.t)).Append(',').Append(s.air ? 1 : 0).Append(',').Append(F(s.neckPreCm)).Append(',').Append(F(s.neckCm)).Append(',')
                  .Append(F(s.shoulderCm)).Append(',').Append(F(s.chestCm)).Append(',').Append(F(s.hipRiseCm)).Append(',')
                  .Append(F(s.dropCm)).Append(',').Append(F(s.handCm)).Append(',').Append(F(s.sightCm)).AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>실행 하나: 지상(안정 구간) 평균, 공중 최소 여유·최대 상승 등</summary>
        private struct RunStat
        {
            public int ground, air;
            public float groundNeck, groundNeckPre, groundShoulder, groundHip;
            public float airNeckPre, airNeck, airShoulder, airChest, airHipRise, airDrop, hand, sight;
        }

        private RunStat Stat(int run)
        {
            List<Sample> list = samples.Where(s => s.run == run).ToList();
            List<Sample> ground = list.Where(s => s.t < 0f).ToList();
            List<Sample> air = list.Where(s => s.air).ToList();
            var st = new RunStat { ground = ground.Count, air = air.Count };
            if (ground.Count > 0)
            {
                st.groundNeck = ground.Average(s => s.neckCm);
                st.groundNeckPre = ground.Min(s => s.neckPreCm);
                st.groundShoulder = ground.Average(s => s.shoulderCm);
                st.groundHip = ground.Average(s => s.hipRiseCm);
            }
            if (air.Count > 0)
            {
                st.airNeckPre = air.Min(s => s.neckPreCm);
                st.airNeck = air.Min(s => s.neckCm);
                st.airShoulder = air.Min(s => s.shoulderCm);
                st.airChest = air.Min(s => s.chestCm);
                st.airHipRise = st.groundNeckPre - st.airNeckPre;
                st.airDrop = air.Max(s => s.dropCm);
            }
            List<Sample> jump = list.Where(s => s.t >= 0f).ToList();
            st.hand = jump.Select(s => s.handCm).DefaultIfEmpty(0f).Max();
            st.sight = jump.Select(s => s.sightCm).DefaultIfEmpty(0f).Max();
            return st;
        }

        private string Summary()
        {
            Config[] configs = (Config[])Enum.GetValues(typeof(Config));
            var stats = Enumerable.Range(0, runs.Count).Select(i => (spec: runs[i], stat: Stat(i))).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("# 1인칭 점프 가림 측정");
            sb.AppendLine();
            sb.AppendLine($"- 측정 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, 실행 {runs.Count}회, 총 {(bodyIK != null && bodyIK.GripGun != null ? bodyIK.GripGun.name : "(없음)")}");
            sb.AppendLine("- 설정: Baseline = 몸 내리기 끔 / Current = 지금 인스펙터 값 (PlayerFirstPersonIK.minNeckBelowEye)");
            sb.AppendLine("- 값: 눈 높이 - 뼈 높이 (cm). 작을수록(음수면 눈보다 위) 그 뼈 주변 메시가 카메라를 가린다. " +
                          "목 상승 = 몸 내리기 전(애니메이션 + AimIK) 목이 지상보다 올라간 최대 거리. 몸 내리기는 '내리기 전 목' 값을 minNeckBelowEye 와 비교한다");
            sb.AppendLine($"- 판정: 공중 목 ≥ {neckPassCm}cm, 공중 쇄골·위팔 ≥ {shoulderPassCm}cm, 조준기 오차 ≤ {sightPassCm}cm");
            sb.AppendLine();

            sb.AppendLine("## 결론");
            sb.AppendLine();
            sb.AppendLine("| 설정 | 판정 | 지상 목 내리기 전 cm (최소) | 공중 목 cm (최소) | 공중 쇄골·위팔 cm (최소) | 목 상승 cm (최대) | 몸 내림 cm (최대) | 조준기 cm (최대) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            float groundNeckMin = 0f;
            foreach (Config config in configs)
            {
                var list = stats.Where(x => x.spec.config == config && x.stat.air > 0).Select(x => x.stat).ToList();
                if (list.Count == 0)
                {
                    sb.AppendLine($"| {config} | (공중 기록 없음) | | | | | | |");
                    continue;
                }
                float groundNeck = list.Min(s => s.groundNeckPre);
                float neckMin = list.Min(s => s.airNeck);
                float shoulderMin = list.Min(s => s.airShoulder);
                float sight = list.Max(s => s.sight);
                var reasons = new List<string>();
                if (neckMin < neckPassCm) reasons.Add($"목 {neckMin:0.0}cm");
                if (shoulderMin < shoulderPassCm) reasons.Add($"쇄골·위팔 {shoulderMin:0.0}cm");
                if (sight > sightPassCm) reasons.Add($"조준기 {sight:0.0}cm");
                sb.AppendLine($"| {config} | {(reasons.Count == 0 ? "✅" : "❌ " + string.Join(", ", reasons))} | {groundNeck:0.0} | {neckMin:0.0} | {shoulderMin:0.0} | " +
                              $"{list.Max(s => s.airHipRise):0.0} | {list.Max(s => s.airDrop):0.0} | {sight:0.0} |");
                if (config == Config.Baseline) groundNeckMin = groundNeck;
            }
            sb.AppendLine();
            // 지상에서는 몸을 내리지 않게: 지상 '내리기 전' 목 여유보다 조금 작게 (비교 대상이 내리기 전 값이므로)
            sb.AppendLine($"- 권장 minNeckBelowEye ≤ {Mathf.Max(0f, groundNeckMin - 2f) / 100f:0.00} m (Baseline 지상 목 여유(내리기 전) {groundNeckMin:0.0}cm - 2cm). 지금 값: {fpIK.MinNeckBelowEye:0.00} m");
            sb.AppendLine();

            sb.AppendLine("## 실행별");
            sb.AppendLine();
            sb.AppendLine("| 설정 | 이동 | pitch | 지상 목(내리기 전) | 지상 목 | 지상 쇄골·위팔 | 공중 목(내리기 전) | 공중 목 | 공중 쇄골·위팔 | 공중 가슴 | 목 상승 | 몸 내림 | 손 오차 | 조준기 |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var (r, st) in stats)
                sb.AppendLine($"| {r.config} | {(r.walk ? "걷기" : "제자리")} | {r.pitch:0} | {st.groundNeckPre:0.0} | {st.groundNeck:0.0} | {st.groundShoulder:0.0} | {st.airNeckPre:0.0} | {st.airNeck:0.0} | " +
                              $"{st.airShoulder:0.0} | {st.airChest:0.0} | {st.airHipRise:0.0} | {st.airDrop:0.0} | {st.hand:0.0} | {st.sight:0.0} |");
            return sb.ToString();
        }
        #endregion
    }
}
