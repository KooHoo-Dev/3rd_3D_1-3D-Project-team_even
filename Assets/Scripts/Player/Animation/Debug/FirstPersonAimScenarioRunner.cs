using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HybridShooter.Weapon;
using RootMotion.FinalIK;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 1인칭 조준 자동 측정 (디버그 전용). 메뉴 Tools/HybridShooter/First Person Aim Test 가 Play 중인 Player 에 붙인다.
    /// 1인칭에서 조준기가 화면 중앙(조준)·정해진 자리(허리)에 오는지, 목이 카메라 앞으로 나오지 않는지 같은 조건으로 잰다 (회귀 확인용)
    ///  - 실행 하나 = (총 꺼내기 대기) → 시선·조준 고정 후 안정(settle) → 기록(record). 매 실행 평평한 판 위 같은 자리에서 시작
    ///  - 설정: Baseline = 고치기 전 (도달 당김 끔, 위쪽 척추 가중치 0.7, 목 보호가 모든 IK 뒤 목을 봄) / Current = 지금 인스펙터 값
    ///  - 조합: 총(Quick = 지금 총만) × 설정 × 허리/조준 × (서 있기: pitch 7개, 걷기: pitch 3개) + 3인칭 Baseline 조준 (3인칭이 바뀌지 않았는지)
    ///  - 측정값(프레임마다, 모든 IK·목 접기가 끝난 뒤): 조준기 ↔ 목표 자리 거리, 손 오차(FBBIK 직후), 도달 비율(FBBIK 전·후 어깨),
    ///    FBBIK 가 어깨를 옮긴 거리, 도달 당김, 눈이 목보다 앞선 거리(FBBIK 전·최종), 목 보호 밀기, 벽 당김
    ///  - 판정(총마다): 조준기 오차(서 있기 -55° 까지, 걷기), 정면·아래 목 여유, 목 보호 밀기(순환 감지), 손 떨림
    /// 결과: OutputFolder 에 samples.csv + summary.md (맨 위 최종 결론). 끝나면 Completed(폴더) 를 알리고 모든 설정을 되돌린 뒤 스스로 지운다
    /// 실행 순서 10003: Update 는 컨트롤러·드라이버 뒤 (다음 프레임 반영), LateUpdate 는 모든 IK·목 접기(10002) 뒤
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(10003)]
    public class FirstPersonAimScenarioRunner : MonoBehaviour
    {
        public enum Config { Baseline, Current }

        private struct RunSpec
        {
            public Gun gun;
            public ViewMode view;
            public bool aim;
            public bool walk;
            public float pitch;
            public Config config;
        }

        private struct Sample
        {
            public int run;
            public float pitch, aimWeight, fpWeight, spineWeight;
            public float sightDeg, sightCm;
            public float handCm, handDeg, gunEndDeg;
            public float reachRatio, preReachRatio, shoulderShiftCm, reachPullCm, neckClearCm, neckPreClearCm, neckPushCm, wallPullCm, muzzleDeg;
        }

        private enum Phase { Boot, Equip, Settle, Record, Done }

        /// <summary>측정이 끝나면 결과 폴더 경로 (에디터 메뉴가 Play 를 끝낸다)</summary>
        public static event Action<string> Completed;

        public static readonly float[] Pitches = { 55f, 30f, 0f, -20f, -40f, -55f, -70f };
        public static readonly float[] WalkPitches = { 30f, 0f, -40f };
        private const float BaselineSpineUpWeight = 0.7f; // 고치기 전 위를 볼 때 척추 가중치 (= firstPersonSpineWeight)

        [Tooltip("true = 지금 든 총만, false = 플레이어가 가진 모든 총")]
        public bool quick = true;
        [Tooltip("결과 폴더. 비우면 <프로젝트>/Logs/FirstPersonAim/<시각>")]
        public string outputFolder;
        [SerializeField] private float bootTime = 1f;
        [SerializeField] private float equipTimeout = 4f;
        [SerializeField] private float settleTime = 0.6f;
        [SerializeField] private float settleTimeout = 3f;
        [SerializeField] private float recordTime = 0.5f;
        [SerializeField] private float walkRecordTime = 1.5f;

        [Header("판정")]
        [Tooltip("조준기 오차(cm): 서 있기 -55° 까지의 최대")]
        [SerializeField] private float sightPassCm = 0.5f;
        [Tooltip("조준기 오차(cm): 걷기 최대")]
        [SerializeField] private float walkSightPassCm = 1f;
        [Tooltip("정면·아래를 볼 때 눈이 목보다 앞서야 하는 최소 거리(cm, 화면에 그려지는 자세)")]
        [SerializeField] private float neckPassCm = 10f;
        [Tooltip("목 보호가 눈을 미는 최대(cm). 넘으면 손 IK 와 서로 미는 순환으로 본다")]
        [SerializeField] private float neckPushPassCm = 25f;
        [Tooltip("손 떨림(cm, 실행 안 표준편차) 최대")]
        [SerializeField] private float jitterPassCm = 0.3f;

        private PlayerController player;
        private PlayerAnimationDriver driver;
        private PlayerBodyIK bodyIK;
        private PlayerAimIK aimIK;
        private PlayerFirstPersonIK fpIK;
        private PlayerFirstPersonNeck fpNeck;
        private GunSlot gunSlot;
        private Transform neckBone;
        private Gun startGun;
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
        private float settledSince = -1f;
        private float lastNeckPush;
        private Vector3 shoulderBefore;

        private void Start()
        {
            player = GetComponent<PlayerController>();
            driver = GetComponent<PlayerAnimationDriver>();
            bodyIK = GetComponent<PlayerBodyIK>();
            aimIK = GetComponent<PlayerAimIK>();
            fpIK = GetComponent<PlayerFirstPersonIK>();
            fpNeck = GetComponent<PlayerFirstPersonNeck>();
            gunSlot = GetComponent<GunSlot>();
            if (player == null || driver == null || bodyIK == null || fpIK == null || gunSlot == null || gunSlot.CurrentGun == null)
            {
                Debug.LogError($"{name}: FirstPersonAimScenarioRunner 는 총을 든 Player (PlayerController·PlayerAnimationDriver·PlayerBodyIK·PlayerFirstPersonIK·GunSlot) 에 붙여야 합니다", this);
                Destroy(this);
                return;
            }

            Animator animator = driver.Animator != null ? driver.Animator : GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
                if (neckBone == null) neckBone = animator.GetBoneTransform(HumanBodyBones.Head);
            }

            if (string.IsNullOrEmpty(outputFolder))
                outputFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "FirstPersonAim", DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            Directory.CreateDirectory(outputFolder);

            startGun = gunSlot.CurrentGun;
            savedView = player.CurrentViewMode;
            startPosition = player.transform.position;
            startYaw = player.LookYaw;
            CreateFlatGround();
            BuildRuns();

            if (fpIK.FullBodyIK != null) fpIK.FullBodyIK.solver.OnPreUpdate += RecordShoulderBeforeFullBodyIK;
            player.DebugBlockInput = true;
            player.Teleport(flatSpawn);
            phase = Phase.Boot;
            phaseStart = Time.time;
            Debug.Log($"[FirstPersonAim] 측정 시작: {runs.Count}회 → {outputFolder}");
        }

        private void BuildRuns()
        {
            runs.Clear();
            List<Gun> guns = new List<Gun> { startGun };
            if (quick == false)
                foreach (Gun g in GetComponentsInChildren<Gun>(true))
                    if (guns.Contains(g) == false) guns.Add(g);

            foreach (Gun gun in guns)
                foreach (Config config in Enum.GetValues(typeof(Config)))
                    foreach (bool aim in new[] { false, true })
                    {
                        foreach (float pitch in Pitches)
                            runs.Add(new RunSpec { gun = gun, view = ViewMode.FirstPerson, aim = aim, pitch = pitch, config = config });
                        foreach (float pitch in WalkPitches)
                            runs.Add(new RunSpec { gun = gun, view = ViewMode.FirstPerson, aim = aim, walk = true, pitch = pitch, config = config });
                    }
            // 3인칭 비교용 (설정과 무관해야 한다): 처음 총, Baseline, 조준
            foreach (float pitch in Pitches)
                runs.Add(new RunSpec { gun = startGun, view = ViewMode.ThirdPerson, aim = true, pitch = pitch, config = Config.Baseline });
        }

        /// <summary>시작 자리 위 높은 곳에 평평한 판 (씬 지형·벽과 섞이지 않게, 벽 당김이 끼지 않게)</summary>
        private void CreateFlatGround()
        {
            flatGround = GameObject.CreatePrimitive(PrimitiveType.Cube);
            flatGround.name = "FirstPersonAim_FlatGround";
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
            // 걷기는 꺼내기가 끝난 뒤부터 (시선 yaw 0 = 월드 정면)
            player.SetMoveInput(run.walk && phase != Phase.Equip ? Vector3.forward : Vector3.zero, false);
            switch (phase)
            {
                case Phase.Equip:
                    if ((gunSlot.CurrentGun == run.gun && driver.ActionBlend >= 0.999f) || elapsed >= equipTimeout)
                        SetPhase(Phase.Settle);
                    break;
                case Phase.Settle:
                    // 목 보호 밀기도 멈출 때까지 (손 IK 와 서로 밀어 천천히 수렴할 수 있다). 걷기는 흔들리므로 보지 않는다
                    bool settled = Mathf.Abs(driver.AimWeight - (run.aim ? 1f : 0f)) < 0.01f && driver.ActionBlend >= 0.999f
                                   && player.CameraChanging == false && (run.walk || Mathf.Abs(player.NeckPush - lastNeckPush) < 0.0005f);
                    if (settled == false) settledSince = -1f;
                    else if (settledSince < 0f) settledSince = Time.time;
                    if ((settledSince >= 0f && Time.time - settledSince >= settleTime) || elapsed >= settleTimeout)
                        SetPhase(Phase.Record);
                    break;
                case Phase.Record:
                    if (elapsed >= (run.walk ? walkRecordTime : recordTime))
                    {
                        StartRun(runIndex + 1);
                        return;
                    }
                    break;
            }
            player.DebugSetLook(0f, run.pitch);
            lastNeckPush = player.NeckPush;
        }

        private void SetPhase(Phase next)
        {
            phase = next;
            phaseStart = Time.time;
            settledSince = -1f;
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
            ApplyConfig(run);
            if (player.CurrentViewMode != run.view) player.SwitchViewMode(run.view, 0f);
            driver.DebugAimOverride = run.aim;
            player.SetMoveInput(Vector3.zero, false);
            player.Teleport(flatSpawn);
            player.DebugSetLook(0f, run.pitch);
            if (gunSlot.CurrentGun != run.gun)
            {
                gunSlot.DebugEquip(run.gun);
                SetPhase(Phase.Equip);
            }
            else SetPhase(Phase.Settle);
        }

        /// <summary>Baseline = 고치기 전 동작을 디버그 값으로 되살린다, Current = 덮어쓰지 않는다</summary>
        private void ApplyConfig(RunSpec run)
        {
            bool baseline = run.config == Config.Baseline;
            fpIK.DebugMaxReachRatio = baseline ? 0f : (float?)null;
            if (aimIK != null) aimIK.DebugSpineWeightUpOverride = baseline ? BaselineSpineUpWeight : (float?)null;
            if (fpNeck != null) fpNeck.DebugBeforeFullBodyIKWeight = baseline ? 0f : (float?)null;
        }

        /// <summary>FBBIK 가 풀리기 직전 오른 어깨(위팔) 위치 (FBBIK 가 어깨를 얼마나 옮기는지 보려고)</summary>
        private void RecordShoulderBeforeFullBodyIK()
        {
            FullBodyBipedIK fbbik = fpIK.FullBodyIK;
            if (fbbik != null && fbbik.references.rightUpperArm != null) shoulderBefore = fbbik.references.rightUpperArm.position;
        }

        private void ClearOverrides()
        {
            if (driver != null) driver.DebugAimOverride = null;
            if (aimIK != null) aimIK.DebugSpineWeightUpOverride = null;
            if (fpIK != null) fpIK.DebugMaxReachRatio = null;
            if (fpNeck != null) fpNeck.DebugBeforeFullBodyIKWeight = null;
        }

        /// <summary>모든 IK·목 접기가 끝난 뒤 이번 프레임 값을 기록</summary>
        private void LateUpdate()
        {
            if (phase != Phase.Record) return;
            Gun gun = bodyIK.GripGun;
            if (gun == null || gun != runs[runIndex].gun || bodyIK.Sight == null) return; // 바꿔 들지 못한 총은 기록하지 않는다

            Pose eye = player.FirstPersonEyePose;
            Vector3 sight = bodyIK.Sight.position;
            Vector3 target = fpIK.SightTarget;
            bool firstPerson = runs[runIndex].view == ViewMode.FirstPerson && fpIK.Weight > 0f;

            Sample s = new Sample
            {
                run = runIndex,
                pitch = player.LookPitch,
                aimWeight = driver.AimWeight,
                fpWeight = fpIK.Weight,
                spineWeight = aimIK != null ? aimIK.SpineWeight : 0f,
                sightDeg = firstPerson ? Vector3.Angle(sight - eye.position, target - eye.position) : 0f,
                sightCm = firstPerson ? Vector3.Distance(sight, target) * 100f : 0f,
                handCm = fpIK.HandPositionError,
                handDeg = fpIK.HandRotationError,
                gunEndDeg = Vector3.Angle(gun.transform.forward, eye.rotation * Vector3.forward),
                reachRatio = firstPerson ? ReachRatio(null) : 0f,
                preReachRatio = firstPerson ? ReachRatio(shoulderBefore) : 0f,
                shoulderShiftCm = firstPerson ? ShoulderShift() : 0f,
                reachPullCm = fpIK.ReachPull * 100f,
                neckPushCm = player.NeckPush * 100f,
                wallPullCm = fpIK.WallPull * 100f,
                muzzleDeg = aimIK != null ? aimIK.MuzzleError : 0f,
            };
            if (neckBone != null)
            {
                Vector3 flatForward = Quaternion.Euler(0f, player.LookYaw, 0f) * Vector3.forward;
                s.neckClearCm = Vector3.Dot(eye.position - neckBone.position, flatForward) * 100f;
                Vector3 neckBefore = fpNeck != null ? fpNeck.NeckBeforeFullBodyIK : neckBone.position;
                s.neckPreClearCm = Vector3.Dot(eye.position - neckBefore, flatForward) * 100f;
            }
            samples.Add(s);
        }

        /// <summary>오른 어깨(위팔) → 오른손 효과기 목표 거리 ÷ 팔 길이. 1 이 넘으면 팔을 다 펴도 못 닿는다. shoulder = null 이면 FBBIK 뒤 어깨</summary>
        private float ReachRatio(Vector3? shoulder)
        {
            FullBodyBipedIK fbbik = fpIK.FullBodyIK;
            if (fbbik == null) return 0f;
            RootMotion.BipedReferences refs = fbbik.references;
            if (refs.rightUpperArm == null || refs.rightForearm == null || refs.rightHand == null) return 0f;
            float length = Vector3.Distance(refs.rightUpperArm.position, refs.rightForearm.position)
                         + Vector3.Distance(refs.rightForearm.position, refs.rightHand.position);
            if (length <= 0f) return 0f;
            return Vector3.Distance(shoulder ?? refs.rightUpperArm.position, fbbik.solver.rightHandEffector.position) / length;
        }

        /// <summary>FBBIK 가 오른 어깨를 시선 수평 정면으로 옮긴 거리(cm)</summary>
        private float ShoulderShift()
        {
            FullBodyBipedIK fbbik = fpIK.FullBodyIK;
            if (fbbik == null || fbbik.references.rightUpperArm == null) return 0f;
            Vector3 flatForward = Quaternion.Euler(0f, player.LookYaw, 0f) * Vector3.forward;
            return Vector3.Dot(fbbik.references.rightUpperArm.position - shoulderBefore, flatForward) * 100f;
        }

        private void Finish()
        {
            phase = Phase.Done;
            try
            {
                File.WriteAllText(Path.Combine(outputFolder, "samples.csv"), Csv(), new UTF8Encoding(false));
                string summaryPath = Path.Combine(outputFolder, "summary.md");
                File.WriteAllText(summaryPath, Summary(), new UTF8Encoding(false));
                Debug.Log($"[FirstPersonAim] 측정 끝: {summaryPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FirstPersonAim] 결과 저장 실패: {e.Message}");
            }

            Restore();
            Completed?.Invoke(outputFolder);
            Destroy(this);
        }

        private void Restore()
        {
            ClearOverrides();
            if (startGun != null && gunSlot.CurrentGun != startGun) gunSlot.DebugEquip(startGun);
            if (player.CurrentViewMode != savedView) player.SwitchViewMode(savedView, 0f);
            player.SetMoveInput(Vector3.zero, false);
            player.Teleport(startPosition);
            player.DebugSetLook(startYaw, 0f);
            player.DebugBlockInput = false;
            if (flatGround != null) Destroy(flatGround);
        }

        private void OnDestroy()
        {
            if (fpIK != null && fpIK.FullBodyIK != null) fpIK.FullBodyIK.solver.OnPreUpdate -= RecordShoulderBeforeFullBodyIK;
            // 측정 중에 컴포넌트만 지운 경우 디버그 값과 입력을 되돌린다
            if (phase == Phase.Done || player == null) return;
            player.DebugBlockInput = false;
            player.SetMoveInput(Vector3.zero, false);
            ClearOverrides();
            if (flatGround != null) Destroy(flatGround);
        }

        private void OnGUI()
        {
            if (phase == Phase.Done || runIndex < 0 || runIndex >= runs.Count) return;
            RunSpec run = runs[runIndex];
            string text = $"First Person Aim Test {runIndex + 1}/{runs.Count}  {run.gun.name} · {ViewName(run.view)} · {(run.aim ? "조준" : "허리")} · {(run.walk ? "걷기" : "서 있기")} · pitch {run.pitch:0} · {run.config}  [{phase}]\n" +
                          "측정 중에는 키보드·마우스를 건드리지 마세요";
            GUI.Box(new Rect(10, Screen.height - 60, 820, 48), text);
        }

        #region 결과
        private static string ViewName(ViewMode view) => view == ViewMode.FirstPerson ? "1P" : "3P";

        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private string Csv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("run,gun,view,aim,walk,config,targetPitch,pitch,aimWeight,fpWeight,spineWeight,sightDeg,sightCm,handCm,handDeg,gunEndDeg,reachRatio,preReachRatio,shoulderShiftCm,reachPullCm,neckClearCm,neckPreClearCm,neckPushCm,wallPullCm,muzzleDeg");
            foreach (Sample s in samples)
            {
                RunSpec r = runs[s.run];
                sb.Append(s.run).Append(',').Append(r.gun.name).Append(',').Append(ViewName(r.view)).Append(',').Append(r.aim ? 1 : 0).Append(',')
                  .Append(r.walk ? 1 : 0).Append(',').Append(r.config).Append(',').Append(F(r.pitch)).Append(',').Append(F(s.pitch)).Append(',')
                  .Append(F(s.aimWeight)).Append(',').Append(F(s.fpWeight)).Append(',').Append(F(s.spineWeight)).Append(',')
                  .Append(F(s.sightDeg)).Append(',').Append(F(s.sightCm)).Append(',').Append(F(s.handCm)).Append(',').Append(F(s.handDeg)).Append(',')
                  .Append(F(s.gunEndDeg)).Append(',').Append(F(s.reachRatio)).Append(',').Append(F(s.preReachRatio)).Append(',')
                  .Append(F(s.shoulderShiftCm)).Append(',').Append(F(s.reachPullCm)).Append(',').Append(F(s.neckClearCm)).Append(',')
                  .Append(F(s.neckPreClearCm)).Append(',').Append(F(s.neckPushCm)).Append(',').Append(F(s.wallPullCm)).Append(',')
                  .Append(F(s.muzzleDeg)).AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>실행 하나의 평균·최댓값·표준편차</summary>
        private struct RunStat
        {
            public int count;
            public float sight, sightMax, handStd, gunEnd, preReach, reach, shoulderShift, reachPull, neckPre, neckMin, neckPushMax, wallPull, muzzle, spine;
        }

        private RunStat Stat(int run)
        {
            List<Sample> list = samples.Where(s => s.run == run).ToList();
            var st = new RunStat { count = list.Count };
            if (list.Count == 0) return st;
            st.sight = list.Average(s => s.sightCm);
            st.sightMax = list.Max(s => s.sightCm);
            float hm = list.Average(s => s.handCm);
            st.handStd = Mathf.Sqrt(list.Average(s => (s.handCm - hm) * (s.handCm - hm)));
            st.gunEnd = list.Average(s => s.gunEndDeg);
            st.preReach = list.Average(s => s.preReachRatio);
            st.reach = list.Average(s => s.reachRatio);
            st.shoulderShift = list.Average(s => s.shoulderShiftCm);
            st.reachPull = list.Average(s => s.reachPullCm);
            st.neckPre = list.Average(s => s.neckPreClearCm);
            st.neckMin = list.Min(s => s.neckClearCm);
            st.neckPushMax = list.Max(s => s.neckPushCm);
            st.wallPull = list.Max(s => s.wallPullCm);
            st.muzzle = list.Average(s => s.muzzleDeg);
            st.spine = list.Average(s => s.spineWeight);
            return st;
        }

        private IEnumerable<(RunSpec spec, RunStat stat)> Runs(Gun gun, Config config, bool? walk = null)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                RunSpec r = runs[i];
                if (r.gun != gun || r.view != ViewMode.FirstPerson || r.config != config) continue;
                if (walk.HasValue && r.walk != walk.Value) continue;
                RunStat st = Stat(i);
                if (st.count > 0) yield return (r, st);
            }
        }

        /// <summary>총 하나 × 설정 하나의 판정</summary>
        private struct Verdict
        {
            public bool measured, pass;
            public float sightToMinus55, sightMinus70, walkSight, neckMin, neckPushMax, jitter;
            public string reasons;
        }

        private Verdict Judge(Gun gun, Config config)
        {
            var still = Runs(gun, config, false).ToList();
            var walk = Runs(gun, config, true).ToList();
            var v = new Verdict { measured = still.Count > 0 };
            if (v.measured == false) return v;
            v.sightToMinus55 = still.Where(x => x.spec.pitch >= -55f).Select(x => x.stat.sightMax).DefaultIfEmpty(0f).Max();
            v.sightMinus70 = still.Where(x => x.spec.pitch < -55f).Select(x => x.stat.sightMax).DefaultIfEmpty(0f).Max();
            v.walkSight = walk.Select(x => x.stat.sightMax).DefaultIfEmpty(0f).Max();
            v.neckMin = still.Where(x => x.spec.pitch >= 0f).Select(x => x.stat.neckMin).DefaultIfEmpty(0f).Min();
            // 순환 감지는 서 있기만 (걸을 때는 허리가 굽어 목 보호가 원래 목적대로 크게 밀 수 있다)
            v.neckPushMax = still.Select(x => x.stat.neckPushMax).DefaultIfEmpty(0f).Max();
            v.jitter = still.Select(x => x.stat.handStd).DefaultIfEmpty(0f).Max();

            var reasons = new List<string>();
            if (v.sightToMinus55 > sightPassCm) reasons.Add($"조준기 {v.sightToMinus55:0.0}cm");
            if (v.walkSight > walkSightPassCm) reasons.Add($"걷기 조준기 {v.walkSight:0.0}cm");
            if (v.neckMin < neckPassCm) reasons.Add($"목 여유 {v.neckMin:0.0}cm");
            if (v.neckPushMax > neckPushPassCm) reasons.Add($"목 밀기 {v.neckPushMax:0.0}cm(순환)");
            if (v.jitter > jitterPassCm) reasons.Add($"손 떨림 {v.jitter:0.00}cm");
            v.pass = reasons.Count == 0;
            v.reasons = v.pass ? "통과" : string.Join(", ", reasons);
            return v;
        }

        private string Summary()
        {
            Gun[] guns = runs.Where(r => r.view == ViewMode.FirstPerson).Select(r => r.gun).Distinct().ToArray();
            Config[] configs = (Config[])Enum.GetValues(typeof(Config));
            var sb = new StringBuilder();
            sb.AppendLine("# 1인칭 조준 측정");
            sb.AppendLine();
            sb.AppendLine($"- 측정 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}, 실행 {runs.Count}회, 총 {guns.Length}개");
            sb.AppendLine($"- 설정: Baseline = 고치기 전 (도달 당김 끔, 위쪽 척추 가중치 {BaselineSpineUpWeight}, 목 보호가 모든 IK 뒤 목을 봄) / Current = 지금 인스펙터 값");
            sb.AppendLine($"- 판정: 조준기 오차(서 있기 -55° 까지) ≤ {sightPassCm}cm, 걷기 ≤ {walkSightPassCm}cm, 정면·아래 목 여유 ≥ {neckPassCm}cm, " +
                          $"목 밀기(서 있기) ≤ {neckPushPassCm}cm, 손 떨림 ≤ {jitterPassCm}cm. -70° 는 참고 (당길 수 있는 한계)");
            sb.AppendLine("- pitch: 양수 = 아래, 음수 = 위. 조준기 오차 = 실제 조준기 ↔ 목표 자리 거리 (조준 중 목표 = 화면 중앙 축)");
            sb.AppendLine();

            // 결론
            sb.AppendLine("## 결론");
            sb.AppendLine();
            sb.Append("| 설정 |");
            foreach (Gun gun in guns) sb.Append($" {gun.name} |");
            sb.AppendLine(" 통과 | 최악 조준기 cm (-55°까지 / -70° / 걷기) | 최소 목 여유 cm | 최대 목 밀기 cm |");
            sb.Append("|---|");
            foreach (Gun _ in guns) sb.Append("---|");
            sb.AppendLine("---|---|---|---|");
            int currentPass = 0;
            foreach (Config config in configs)
            {
                var verdicts = guns.Select(g => Judge(g, config)).Where(v => v.measured).ToList();
                int passCount = verdicts.Count(v => v.pass);
                float worst = verdicts.Select(v => v.sightToMinus55).DefaultIfEmpty(0f).Max();
                float worst70 = verdicts.Select(v => v.sightMinus70).DefaultIfEmpty(0f).Max();
                float worstWalk = verdicts.Select(v => v.walkSight).DefaultIfEmpty(0f).Max();
                float neck = verdicts.Select(v => v.neckMin).DefaultIfEmpty(0f).Min();
                float push = verdicts.Select(v => v.neckPushMax).DefaultIfEmpty(0f).Max();
                sb.Append($"| {config} |");
                foreach (Gun gun in guns)
                {
                    Verdict v = Judge(gun, config);
                    sb.Append(v.measured ? (v.pass ? " ✅ |" : $" ❌ {v.reasons} |") : " (기록 없음) |");
                }
                sb.AppendLine($" {passCount}/{verdicts.Count} | {worst:0.0} / {worst70:0.0} / {worstWalk:0.0} | {neck:0.0} | {push:0.0} |");

                if (config == Config.Current) currentPass = passCount;
            }
            sb.AppendLine();
            sb.AppendLine(currentPass == guns.Length
                ? "**Current: 모든 총에서 판정 통과**"
                : $"**Current: 통과 {currentPass}/{guns.Length}** (실패 사유는 위 표)");
            sb.AppendLine();

            foreach (Gun gun in guns)
            {
                sb.AppendLine($"## {gun.name}");
                sb.AppendLine();
                foreach (bool aim in new[] { true, false })
                {
                    sb.AppendLine($"### 1인칭 {(aim ? "조준" : "허리")} · 서 있기");
                    sb.AppendLine();
                    sb.AppendLine("| 설정 | pitch | 척추 w | 조준기 cm | 최대 cm | 손 떨림 cm | 도달 FBBIK 전 | 도달 | 어깨 이동 cm | 도달 당김 cm | 목 앞 FBBIK 전 cm | 목 앞 최종 cm(최소) | 목 밀기 cm | 벽 cm |");
                    sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
                    foreach (Config config in configs)
                        foreach (var (r, st) in Runs(gun, config, false).Where(x => x.spec.aim == aim))
                            sb.AppendLine($"| {config} | {r.pitch:0} | {st.spine:0.00} | {st.sight:0.0} | {st.sightMax:0.0} | {st.handStd:0.00} | {st.preReach:0.00} | {st.reach:0.00} | {st.shoulderShift:0.0} | {st.reachPull:0.0} | {st.neckPre:0.0} | {st.neckMin:0.0} | {st.neckPushMax:0.0} | {st.wallPull:0.0} |");
                    sb.AppendLine();

                    sb.AppendLine($"### 1인칭 {(aim ? "조준" : "허리")} · 걷기");
                    sb.AppendLine();
                    sb.AppendLine("| 설정 | pitch | 조준기 cm | 최대 cm | 손 떨림 cm | 도달 당김 cm | 목 앞 최종 cm(최소) | 목 밀기 cm(최대) |");
                    sb.AppendLine("|---|---|---|---|---|---|---|---|");
                    foreach (Config config in configs)
                        foreach (var (r, st) in Runs(gun, config, true).Where(x => x.spec.aim == aim))
                            sb.AppendLine($"| {config} | {r.pitch:0} | {st.sight:0.0} | {st.sightMax:0.0} | {st.handStd:0.00} | {st.reachPull:0.0} | {st.neckMin:0.0} | {st.neckPushMax:0.0} |");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("## 3인칭 조준 (비교용, Baseline)");
            sb.AppendLine();
            sb.AppendLine("| pitch | 척추 w | 총구 오차° |");
            sb.AppendLine("|---|---|---|");
            for (int i = 0; i < runs.Count; i++)
            {
                if (runs[i].view != ViewMode.ThirdPerson) continue;
                RunStat st = Stat(i);
                sb.AppendLine($"| {runs[i].pitch:0} | {st.spine:0.00} | {st.muzzle:0.00} |");
            }
            return sb.ToString();
        }
        #endregion
    }
}
