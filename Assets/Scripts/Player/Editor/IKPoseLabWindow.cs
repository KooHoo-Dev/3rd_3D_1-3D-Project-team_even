using System.Linq;
using HybridShooter.Weapon;
using RootMotion.FinalIK;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// IK 작업 창 (Tools/HybridShooter/IK Pose Lab). **Play 모드에서** IK 가 실제로 도는 모습을 보며 손 위치를 맞춘다.
    ///  - 자세 고정(들고 다니기·조준), 시점 전환, 시간 정지·느리게, 총 바꿔 들기, 반동 한 발
    ///  - 사격 반동(PlayerGunRecoil): 한 발·연사 시험, 총별 세기 슬라이더, 씬 뷰 총구 궤적
    ///  - 조준 시야 검사(WeaponSightCheck): 1인칭 조준 때 화면 가운데가 총에 가리는지, 조준선 높이 자동 맞춤
    ///  - 조준 방식(조준기 정렬 / 스코프 화면 / 어깨 견착)과 견착 자리 (1인칭 무기 자리 칸)
    ///  - 자동 맞춤: 애니메이션 손 위치 + 총 메쉬 모양으로 총 위치·왼손·조준기 처음 값을 계산 (WeaponGripAutoFit)
    ///  - 편집: 씬 뷰에서 총, RightHandGrip(손잡이 지점), LeftHandGrip(왼손), Sight(조준기)를 옮기면 손이 바로 따라오고,
    ///    값은 WeaponGripTable 에셋에 자동 저장된다 (Play 를 끝내도 남는다)
    ///  - 오른손: 손잡이 지점을 총의 손잡이로 옮기고 "총을 오른손에 붙이기" → 그 지점이 오른손 바닥에 오도록 총이 옮겨진다
    ///  - 편집 모드: WeaponSocket 이 오른손 아래가 아니면 옮기는 버튼 (WeaponSocketSetup)
    /// </summary>
    public class IKPoseLabWindow : EditorWindow
    {
        private const string TablePath = "Assets/Scenes/MS/Animations/WeaponGripTable.asset";
        private static readonly float[] Speeds = { 0f, 0.1f, 0.25f, 1f };
        private static readonly string[] SpeedLabels = { "정지", "0.1x", "0.25x", "1x" };

        private PlayerController player;
        private PlayerAnimationDriver driver;
        private PlayerBodyIK bodyIK;
        private GunSlot gunSlot;
        private PlayerGunRecoil gunRecoil;
        private PlayerAimView aimView;
        private PlayerAimIK aimIK;
        private PlayerFirstPersonIK fpIK;
        private PlayerItemIK itemIK;
        private PlayerItems items;
        private Transform healMarker;   // 회복 자세 편집 중 씬 뷰에서 옮기는 표시
        private bool editGrenade;       // 수류탄 모형 자리 편집 중
        private Gun[] guns = new Gun[0];
        private float burstUntil = -1f;  // 연사 시험: 이 게임 시각까지
        private float nextShot;
        private bool showSight;          // 씬 뷰에 조준 시선·가림 표시
        private string[] sightReport;    // 모든 총 조준 시야 검사 결과

        private float speed = 1f;
        private float resumeUntil = -1f; // 정지 중 잠깐 움직일 때 (총 교체·반동), 이 실제 시각에 다시 정지
        private bool autoSave = true;
        private bool showMarkers = true;
        private string message = "";
        private Vector2 scroll;

        [MenuItem("Tools/HybridShooter/IK Pose Lab")]
        private static void Open() => GetWindow<IKPoseLabWindow>("IK Pose Lab");

        #region 창
        private void OnEnable()
        {
            EditorApplication.update += EditorTick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorTick;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            SceneView.duringSceneGui -= OnSceneGUI;
            RestoreRuntime();
            SaveTable();
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode) { RestoreRuntime(); SaveTable(); }
            if (change == PlayModeStateChange.EnteredEditMode || change == PlayModeStateChange.EnteredPlayMode) player = null;
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawTableField();
            DrawSocketSection();
            DrawComponentSetup();

            if (EditorApplication.isPlaying == false)
            {
                EditorGUILayout.HelpBox("Play 를 누르면 작업할 수 있습니다. (총 위치·손 위치는 Play 중에만 IK 가 돌아서 보입니다)", MessageType.Info);
                if (string.IsNullOrEmpty(message) == false) EditorGUILayout.HelpBox(message, MessageType.None);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (Bind() == false)
            {
                EditorGUILayout.HelpBox("씬에서 PlayerController / PlayerAnimationDriver / PlayerBodyIK / GunSlot 을 찾지 못했습니다", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawPoseSection();
            DrawGunSection();
            DrawEditSection();
            DrawFirstPersonSection();
            DrawSightSection();
            DrawRecoilSection();
            DrawItemSection();
            DrawFootSection();
            DrawStatus();
            if (string.IsNullOrEmpty(message) == false) EditorGUILayout.HelpBox(message, MessageType.None);
            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region 화면
        private void DrawTableField()
        {
            PlayerAnimationProfile profile = FindProfile();
            if (profile == null) { EditorGUILayout.HelpBox("PlayerAnimationProfile 을 찾지 못했습니다", MessageType.Warning); return; }
            if (profile.gripTable == null) CreateTable(profile);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("손 위치 데이터", profile.gripTable, typeof(WeaponGripTable), false);
        }

        /// <summary>WeaponSocket 이 오른손 아래에 있는지. 아니면 (편집 모드에서) 옮기는 버튼</summary>
        private void DrawSocketSection()
        {
            if (WeaponSocketSetup.Find(out _, out Transform socket, out Transform hand) == false) return;
            if (WeaponSocketSetup.IsOnHand(socket, hand)) return;
            EditorGUILayout.HelpBox($"WeaponSocket 이 오른손이 아니라 '{(socket.parent != null ? socket.parent.name : "-")}' 아래에 있습니다. " +
                                    "총이 손을 따라가지 않습니다 (들고 다니기 자세에서 총이 떠 있음)", MessageType.Warning);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button(EditorApplication.isPlaying ? "Play 를 끝내고 옮기세요" : "WeaponSocket 을 오른손으로 옮기기 (조준 자세 그대로)"))
                    message = WeaponSocketSetup.MoveToRightHand();
            }
        }

        /// <summary>빠진 IK 컴포넌트 (편집 모드에서 추가)</summary>
        private void DrawComponentSetup()
        {
            var owner = Object.FindFirstObjectByType<PlayerController>();
            if (owner == null) return;

            // EditorIK 로 미리보기했던 솔버는 executedInEditor 가 켜진 채 저장되어 Play 에서 초기화되지 않는다
            IK[] stale = owner.GetComponentsInChildren<IK>(true).Where(ik => ik.GetIKSolver() != null && ik.GetIKSolver().executedInEditor).ToArray();
            if (stale.Length > 0 && EditorApplication.isPlaying == false)
            {
                EditorGUILayout.HelpBox($"{string.Join(", ", stale.Select(ik => ik.GetType().Name))} 에 편집 모드 실행 표시가 남아 있습니다 (Play 에서 IK 가 풀리지 않음. 지금은 PlayerBodyIK 가 시작할 때 꺼 줌)", MessageType.Warning);
                if (GUILayout.Button("씬에서 편집 모드 실행 표시 끄기"))
                {
                    foreach (IK ik in stale)
                    {
                        Undo.RecordObject(ik, "executedInEditor 끄기");
                        ik.GetIKSolver().executedInEditor = false;
                        EditorUtility.SetDirty(ik);
                    }
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
                    message = "껐습니다. 씬을 저장하세요 (Ctrl+S)";
                }
            }

            DrawAddComponent<PlayerFirstPersonIK>(owner, "1인칭 무기 자리");
            DrawAddComponent<PlayerItemIK>(owner, "아이템 사용 모습");
            DrawAddComponent<PlayerGunRecoil>(owner, "사격 반동");
            DrawAddComponent<PlayerAimView>(owner, "저격 스코프·견착 조준 화면");

            // 예전 Final IK Recoil (손 뼈 축 기준이라 총구가 내려감). PlayerGunRecoil 이 시작할 때 꺼 주지만 씬에서도 지운다
            Recoil[] old = owner.GetComponentsInChildren<Recoil>(true);
            if (old.Length > 0 && EditorApplication.isPlaying == false)
            {
                EditorGUILayout.HelpBox("예전 Final IK Recoil 이 남아 있습니다 (반동은 이제 PlayerGunRecoil)", MessageType.Info);
                if (GUILayout.Button("Final IK Recoil 지우기"))
                {
                    foreach (Recoil r in old) Undo.DestroyObjectImmediate(r);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
                    message = "Final IK Recoil 을 지웠습니다. 씬을 저장하세요 (Ctrl+S)";
                }
            }
        }

        /// <summary>
        /// 사격 반동 (총 기준): 한 발·연사 시험, 지금 든 총의 세기 (바꾸면 그 총 값으로 표에 저장), 씬 뷰 총구 궤적
        /// </summary>
        private void DrawRecoilSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("사격 반동", EditorStyles.boldLabel);
            if (gunRecoil == null) { EditorGUILayout.HelpBox("PlayerGunRecoil 이 없습니다 (Play 를 끝내고 위 버튼으로 추가)", MessageType.Warning); return; }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("한 발")) FireRecoil();
                if (GUILayout.Button("연사 1초 (초당 10발)")) StartBurst();
                if (GUILayout.Button("0.1x 로 보기")) SetSpeed(0.1f);
            }

            PlayerAnimationProfile profile = driver.Profile;
            if (profile != null)
            {
                EditorGUI.BeginChangeCheck();
                float magnitude = EditorGUILayout.Slider("전체 배수 (모든 총)", profile.recoilMagnitude, 0f, 3f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(profile, "반동 전체 배수");
                    profile.recoilMagnitude = magnitude;
                    EditorUtility.SetDirty(profile);
                }
            }

            WeaponGripTable table = bodyIK.GripTable;
            Gun gun = bodyIK.GripGun;
            WeaponGripTable.Entry entry = table != null ? table.Find(gun) : null;
            if (gun == null) return;
            WeaponGripTable.RecoilSettings s = table != null ? table.RecoilFor(gun) : WeaponGripTable.DefaultRecoil(WeaponGripTable.Key(gun));
            bool custom = entry != null && entry.customRecoil;
            EditorGUILayout.LabelField($"{gun.name} 값", custom ? "이 총 값" : "총 종류 기본값 (바꾸면 이 총 값으로 저장)", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(entry == null))
            {
                EditorGUI.BeginChangeCheck();
                float pitch = EditorGUILayout.Slider("총구 튀는 각도 (°)", s.pitch, 0f, 15f);
                float back = EditorGUILayout.Slider("뒤로 밀림 (cm)", s.back * 100f, 0f, 15f) / 100f;
                float yaw = EditorGUILayout.Slider("좌우 흔들림 (°)", s.yaw, 0f, 5f);
                float roll = EditorGUILayout.Slider("기울기 흔들림 (°)", s.roll, 0f, 8f);
                float kick = EditorGUILayout.Slider("튀는 시간 (초, 복귀는 약 5배)", s.kickTime, 0.02f, 0.2f);
                float stack = EditorGUILayout.Slider("연사 누적 한계 (배)", s.maxStack, 1f, 5f);
                if (EditorGUI.EndChangeCheck() && entry != null)
                {
                    Undo.RecordObject(table, "반동 세기");
                    entry.customRecoil = true;
                    entry.recoil = new WeaponGripTable.RecoilSettings(pitch, back, yaw, roll, kick, stack);
                    EditorUtility.SetDirty(table);
                }
                if (custom && GUILayout.Button("총 종류 기본값으로"))
                {
                    Undo.RecordObject(table, "반동 세기");
                    entry.customRecoil = false;
                    EditorUtility.SetDirty(table);
                }
            }
            if (entry == null) EditorGUILayout.LabelField("이 총은 데이터가 없습니다 (자동 맞춤 먼저). 기본값으로 동작합니다", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("총구 튐 · 뒤로 (지금 / 이번 최대)",
                $"{gunRecoil.CurrentPitch:0.0}° / {gunRecoil.PeakPitch:0.0}°  ·  {gunRecoil.CurrentBack * 100f:0.0} / {gunRecoil.PeakBack * 100f:0.0} cm");
            EditorGUILayout.LabelField("씬 뷰 주황 선 = 이번 반동 동안 총구 궤적 (위로 튀었다가 제자리로 와야 정상, 멈춰 서서 보기)", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// 조준 시야 검사: 조준할 때 눈(카메라)에서 조준기까지 사이를 총이 가리는지. 지금 든 총은 매번, 모든 총은 버튼으로
        /// </summary>
        private void DrawSightSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("조준 시야 검사 (1인칭 조준 때 화면 가운데)", EditorStyles.boldLabel);
            Gun gun = bodyIK.GripGun;
            WeaponGripTable table = bodyIK.GripTable;
            WeaponGripTable.Entry entry = table != null ? table.Find(gun) : null;
            if (fpIK == null || gun == null || bodyIK.Sight == null || entry == null)
            {
                EditorGUILayout.LabelField("총 데이터(조준기)와 PlayerFirstPersonIK 가 있어야 합니다", EditorStyles.miniLabel);
                return;
            }
            if (table.AimModeFor(gun) != WeaponGripTable.AimMode.Sight)
            {
                EditorGUILayout.LabelField($"{gun.name}: {AimModeName(table.AimModeFor(gun))} 방식이라 검사하지 않습니다 (조준기를 눈앞에 두지 않음)", EditorStyles.miniLabel);
                return;
            }
            float near = SightNear();
            EditorGUILayout.LabelField("1인칭 카메라 잘림 거리", $"{near * 100f:0.0} cm (이보다 가까운 면은 잘려서 그 안쪽이 보인다)");

            Transform g = gun.transform;
            Vector3 sight = WeaponGripAutoFit.ToMetric(g, bodyIK.Sight.position);
            float ads = SightAds(entry);
            WeaponSightCheck.Result result = WeaponSightCheck.Check(gun, sight - Vector3.forward * ads, sight, near);
            EditorGUILayout.LabelField($"{gun.name} (조준 거리 {ads * 100f:0}cm)", Verdict(result));
            Camera cam = player.FirstPersonCamera;
            if (player.IsFirstPerson && driver.AimWeight > 0.99f && cam != null)
            {
                WeaponSightCheck.Result live = WeaponSightCheck.Check(gun, WeaponGripAutoFit.ToMetric(g, cam.transform.position), sight, near);
                EditorGUILayout.LabelField("지금 카메라 기준", Verdict(live));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("조준선 높이 자동 맞춤 (이 총)")) RaiseSight(gun, entry, true);
                if (GUILayout.Button("▲ 2mm", GUILayout.Width(60))) MoveSight(gun, 0.002f);
                if (GUILayout.Button("▼ 2mm", GUILayout.Width(60))) MoveSight(gun, -0.002f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("모든 총 검사")) CheckAllSights();
                if (GUILayout.Button("모든 총 자동 맞춤")) { foreach (Gun other in guns) if (other != null && table.Find(other) != null && table.AimModeFor(other) == WeaponGripTable.AimMode.Sight) RaiseSight(other, table.Find(other), false); CheckAllSights(); }
            }
            showSight = EditorGUILayout.ToggleLeft("씬 뷰에 눈·시선·가림 표시 (흰 점 = 눈, 초록/빨강 = 시선, 빨간 점 = 가린 자리)", showSight);
            EditorGUILayout.LabelField("올리면 총이 화면 아래로 내려가고 시선이 총 윗면 위로 지나갑니다 (볼을 개머리판에 붙이고 조준선 위로 보는 자세)", EditorStyles.wordWrappedMiniLabel);
            if (sightReport != null)
                foreach (string line in sightReport) EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
        }

        private float SightNear()
        {
            Camera cam = player != null ? player.FirstPersonCamera : null;
            return cam != null ? cam.nearClipPlane : 0.03f;
        }

        private float SightAds(WeaponGripTable.Entry entry) => entry != null && entry.adsDistance > 0f ? entry.adsDistance : fpIK.DefaultAdsDistance;

        /// <summary>총 기준 조준기 자리: 지금 든 총은 Sight 자식(편집 중인 값), 아니면 표의 값</summary>
        private Vector3 SightMetric(Gun gun, WeaponGripTable.Entry entry)
        {
            Transform g = gun.transform;
            Vector3 world = gun == bodyIK.GripGun && bodyIK.Sight != null ? bodyIK.Sight.position : g.TransformPoint(entry.sightPosition);
            return WeaponGripAutoFit.ToMetric(g, world);
        }

        private static string Verdict(WeaponSightCheck.Result r)
        {
            if (r.valid == false) return "총 메쉬가 없습니다";
            if (r.eyeInside) return "✗ 눈이 총 안 (잘림 면이 총을 잘라 안쪽이 보임)";
            if (r.blockDistance >= 0f) return $"✗ 눈 앞 {r.blockDistance * 100f:0.0}cm 에서 가림{(r.blockBackFace ? " (총 안쪽 면)" : "")}";
            return "✓ 가림 없음";
        }

        private void CheckAllSights()
        {
            WeaponGripTable table = bodyIK.GripTable;
            float near = SightNear();
            sightReport = guns.Where(g => g != null).Select(g =>
            {
                WeaponGripTable.Entry e = table != null ? table.Find(g) : null;
                if (e == null) return $"{g.name}: 데이터 없음";
                if (table.AimModeFor(g) != WeaponGripTable.AimMode.Sight) return $"{g.name}: {AimModeName(table.AimModeFor(g))} (검사 안 함)";
                Vector3 sight = SightMetric(g, e);
                float ads = SightAds(e);
                return $"{g.name}: {Verdict(WeaponSightCheck.Check(g, sight - Vector3.forward * ads, sight, near))}";
            }).ToArray();
        }

        /// <summary>가림이 없어질 때까지 조준기를 올린다 (값은 표에 저장)</summary>
        private void RaiseSight(Gun gun, WeaponGripTable.Entry entry, bool report)
        {
            Vector3 sight = SightMetric(gun, entry);
            float raise = WeaponSightCheck.FindRaise(gun, sight, SightAds(entry), SightNear());
            if (raise < 0f)
            {
                message = $"{gun.name}: 8cm 올려도 가립니다 → 조준기 지점이 총 안쪽인지, 조준 거리를 늘려야 하는지 확인하세요";
                return;
            }
            if (raise > 0f) MoveSight(gun, raise);
            if (report) message = raise > 0f ? $"{gun.name}: 조준선을 {raise * 100f:0.0}cm 올렸습니다" : $"{gun.name}: 이미 가림이 없습니다";
        }

        /// <summary>조준기를 총 위쪽으로 amount(m) 옮긴다 (지금 든 총은 Sight 자식 → 자동 저장, 다른 총은 표 값)</summary>
        private void MoveSight(Gun gun, float amount)
        {
            WeaponGripTable table = bodyIK.GripTable;
            WeaponGripTable.Entry entry = table != null ? table.Find(gun) : null;
            if (entry == null) return;
            Transform g = gun.transform;
            Undo.RecordObject(table, "조준선 높이");
            if (gun == bodyIK.GripGun && bodyIK.Sight != null)
            {
                bodyIK.Sight.position += g.up * amount;
                entry.sightPosition = bodyIK.Sight.localPosition;
            }
            else entry.sightPosition = g.InverseTransformPoint(g.TransformPoint(entry.sightPosition) + g.up * amount);
            EditorUtility.SetDirty(table);
            SaveTable();
        }

        private void DrawAddComponent<T>(PlayerController owner, string role) where T : Component
        {
            if (owner.GetComponent<T>() != null) return;
            EditorGUILayout.HelpBox($"Player 에 {typeof(T).Name}({role})가 없습니다", MessageType.Warning);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button(EditorApplication.isPlaying ? "Play 를 끝내고 추가하세요" : $"{typeof(T).Name} 추가"))
                {
                    Undo.AddComponent<T>(owner.gameObject);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
                    message = $"{typeof(T).Name} 를 추가했습니다. 씬을 저장하세요 (Ctrl+S)";
                }
            }
        }

        /// <summary>
        /// 아이템: 수류탄 던지기·회복 동작 시험, 회복 때 왼손 자리와 손에 든 수류탄 모형 자리를 씬 뷰에서 옮겨 고친다 (프로필에 바로 저장)
        /// </summary>
        private void DrawItemSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("아이템", EditorStyles.boldLabel);
            PlayerAnimationProfile profile = driver.Profile;
            PlayerAnimationProfile.WeaponSet set = driver.CurrentSet;
            if (set == null || set.throwClipLength <= 0f)
                EditorGUILayout.HelpBox("던지기 동작이 없습니다. Play 를 끝내고 Tools/HybridShooter/Add Throw Action 을 실행하세요", MessageType.Warning);
            else
                EditorGUILayout.LabelField("던지기 (클립 분석)", $"{(set.throwWithLeftHand ? "왼손" : "오른손")} · 놓는 순간 {set.throwRelease:0.00}");
            if (itemIK == null || items == null || profile == null) { EditorGUILayout.HelpBox("PlayerItemIK / PlayerItems 가 없습니다", MessageType.Warning); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("수류탄 던지기")) ThrowForTest();
                if (GUILayout.Button("회복 동작")) { itemIK.Play(profile.healPose); ResumeBriefly(profile.healPose.holdTime + profile.healPose.moveTime * 2f + 0.2f); }
            }

            bool editHeal = healMarker != null;
            if (GUILayout.Toggle(editHeal, "회복 자세 편집 (왼손을 붙잡아 두고 표시를 옮기기)", "Button") != editHeal)
            {
                if (editHeal) StopHealEdit();
                else StartHealEdit();
            }
            if (healMarker != null && GUILayout.Button("왼손 표시 선택")) Select(healMarker);
            EditorGUI.BeginChangeCheck();
            float hold = EditorGUILayout.Slider("회복: 머무는 시간", profile.healPose.holdTime, 0f, 2f);
            float move = EditorGUILayout.Slider("회복: 오가는 시간", profile.healPose.moveTime, 0.05f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "회복 동작 시간");
                profile.healPose.holdTime = hold;
                profile.healPose.moveTime = move;
                EditorUtility.SetDirty(profile);
            }

            if (GUILayout.Toggle(editGrenade, "손에 든 수류탄 모형 편집 (모형을 옮기기)", "Button") != editGrenade)
            {
                editGrenade = !editGrenade;
                itemIK.HoldGrenade = editGrenade;
                itemIK.EditingModel = false;
                if (editGrenade == false) SaveProfile();
            }
            if (editGrenade && items.ThrowHand == null) EditorGUILayout.HelpBox("던지는 손이 정해지지 않았습니다 (던지기 동작 필요)", MessageType.Warning);
            EditorGUILayout.LabelField("왼손 아이템 자세", $"{itemIK.PoseWeight:0.00}");
        }

        /// <summary>
        /// 발 IK (GrounderFBBIK): 시험 지형, 발·골반 상태, 설정 (프로필 footIK 에 저장하고 바로 입힌다)
        /// </summary>
        private void DrawFootSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("발 IK", EditorStyles.boldLabel);
            GrounderFBBIK grounder = bodyIK.Grounder;
            PlayerAnimationProfile profile = driver.Profile;
            if (grounder == null || profile == null) { EditorGUILayout.HelpBox("GrounderFBBIK 가 없습니다 (Build Player Animator)", MessageType.Warning); return; }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(IKTestTerrain.Exists ? "시험 지형 다시 만들기 (플레이어 앞)" : "시험 지형 만들기 (플레이어 앞)")) IKTestTerrain.Create(player);
                using (new EditorGUI.DisabledScope(IKTestTerrain.Exists == false))
                {
                    if (GUILayout.Button("지우기", GUILayout.Width(60))) IKTestTerrain.Remove();
                }
            }
            EditorGUILayout.LabelField("왼쪽부터 경사 10·20·30·40° / 계단 10·20·30cm / 40cm 턱", EditorStyles.miniLabel);

            EditorGUILayout.LabelField("발 IK 가중치", $"{grounder.weight:0.00}  (땅 위·구르기 아님·살아 있음일 때 1)");
            var legs = grounder.solver.legs;
            if (legs != null)
            {
                for (int i = 0; i < legs.Length; i++)
                {
                    var leg = legs[i];
                    if (leg == null || leg.initiated == false) continue;
                    EditorGUILayout.LabelField(i == 0 ? "왼발" : i == 1 ? "오른발" : $"다리 {i}",
                        $"{(leg.isGrounded ? "땅 닿음" : "공중")}  ·  IK {leg.IKOffset * 100f:+0.0;-0.0;0.0} cm  ·  땅까지 {leg.heightFromGround * 100f:0.0} cm");
                }
            }
            if (grounder.solver.pelvis != null)
                EditorGUILayout.LabelField("골반", $"{grounder.solver.pelvis.heightOffset * 100f:+0.0;-0.0;0.0} cm");

            PlayerAnimationProfile.FootIKSettings s = profile.footIK;
            EditorGUI.BeginChangeCheck();
            float maxStep = EditorGUILayout.Slider("최대 턱 높이", s.maxStep, 0.05f, 0.8f);
            float footRadius = EditorGUILayout.Slider("발 크기", s.footRadius, 0.02f, 0.3f);
            float footSpeed = EditorGUILayout.Slider("발 속도", s.footSpeed, 0.5f, 10f);
            float pelvisSpeed = EditorGUILayout.Slider("골반 속도", s.pelvisSpeed, 0.5f, 15f);
            float lowerPelvis = EditorGUILayout.Slider("골반 내리기", s.lowerPelvisWeight, 0f, 1f);
            float liftPelvis = EditorGUILayout.Slider("골반 올리기", s.liftPelvisWeight, 0f, 1f);
            float footRotation = EditorGUILayout.Slider("발 기울이기", s.footRotationWeight, 0f, 1f);
            float maxAngle = EditorGUILayout.Slider("발 최대 각도", s.maxFootRotationAngle, 0f, 80f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "발 IK 설정");
                s.maxStep = maxStep;
                s.footRadius = footRadius;
                s.footSpeed = footSpeed;
                s.pelvisSpeed = pelvisSpeed;
                s.lowerPelvisWeight = lowerPelvis;
                s.liftPelvisWeight = liftPelvis;
                s.footRotationWeight = footRotation;
                s.maxFootRotationAngle = maxAngle;
                EditorUtility.SetDirty(profile);
                bodyIK.ApplyFootSettings();
            }
        }

        private void ThrowForTest()
        {
            if (items.Grenades <= 0) items.AddGrenades(1);
            if (items.TryThrowGrenade() == false) { message = "지금은 던질 수 없습니다 (공중·구르기 중, 이미 던지는 중 등)"; return; }
            ResumeBriefly(items.ThrowDuration + 0.5f);
        }

        /// <summary>회복 자세를 붙잡아 두고, 그 자리에 표시를 만들어 선택한다 (표시는 몸을 따라 움직인다)</summary>
        private void StartHealEdit()
        {
            PlayerAnimationProfile profile = driver.Profile;
            itemIK.HoldPose = true;
            Pose pose = itemIK.WorldPose(profile.healPose);
            healMarker = new GameObject("HealPose (편집)").transform;
            healMarker.SetParent(driver.Animator.transform, true);
            Quaternion rotation = profile.healPose.useRotation || bodyIK.LeftHandBone == null ? pose.rotation : bodyIK.LeftHandBone.rotation;
            healMarker.SetPositionAndRotation(pose.position, rotation);
            Select(healMarker);
            ResumeBriefly(0.3f);
        }

        private void StopHealEdit()
        {
            if (itemIK != null) itemIK.HoldPose = false;
            if (healMarker != null) Object.Destroy(healMarker.gameObject);
            healMarker = null;
            SaveProfile();
        }

        /// <summary>편집 중인 아이템 자리를 프로필에 기록 (에셋이라 Play 를 끝내도 남는다)</summary>
        private void CaptureItems()
        {
            if (itemIK == null || driver == null || driver.Profile == null) return;
            PlayerAnimationProfile profile = driver.Profile;
            if (healMarker != null)
            {
                itemIK.StorePose(profile.healPose, healMarker.position, healMarker.rotation);
                EditorUtility.SetDirty(profile);
            }
            if (editGrenade && itemIK.GrenadeModel != null && items.ThrowHand != null)
            {
                if (itemIK.EditingModel == false)
                {
                    // 모형이 처음 손에 붙은 뒤부터 직접 옮긴다
                    itemIK.EditingModel = true;
                    Select(itemIK.GrenadeModel);
                    return;
                }
                Transform hand = items.ThrowHand;
                profile.grenadeInHandOffset = Quaternion.Inverse(hand.rotation) * (itemIK.GrenadeModel.position - itemIK.PalmCenter(hand));
                EditorUtility.SetDirty(profile);
            }
        }

        private void SaveProfile()
        {
            if (driver != null && driver.Profile != null) AssetDatabase.SaveAssetIfDirty(driver.Profile);
        }

        /// <summary>1인칭 무기 자리 (총별): 평소 조준기 자리, 조준 거리. 1인칭에서 보며 고친다</summary>
        private void DrawFirstPersonSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("1인칭 무기 자리", EditorStyles.boldLabel);
            if (fpIK == null) { EditorGUILayout.HelpBox("PlayerFirstPersonIK 가 없습니다", MessageType.Warning); return; }
            if (player.IsFirstPerson == false) { EditorGUILayout.LabelField("위에서 1인칭으로 바꾸면 조정할 수 있습니다", EditorStyles.miniLabel); return; }
            WeaponGripTable table = bodyIK.GripTable;
            WeaponGripTable.Entry entry = table != null ? table.Find(bodyIK.GripGun) : null;
            if (entry == null) { EditorGUILayout.LabelField("이 총은 데이터가 없습니다 (자동 맞춤 먼저)", EditorStyles.miniLabel); return; }

            // 조준 방식 (Auto = 총 종류별 기본)
            WeaponGripTable.AimMode mode = table.AimModeFor(bodyIK.GripGun);
            EditorGUI.BeginChangeCheck();
            var picked = (WeaponGripTable.AimMode)EditorGUILayout.EnumPopup($"조준 방식 (지금: {AimModeName(mode)})", entry.aimMode);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(table, "조준 방식");
                entry.aimMode = picked;
                EditorUtility.SetDirty(table);
            }
            if (mode == WeaponGripTable.AimMode.Scope)
                EditorGUILayout.LabelField("조준하면 1·3인칭 모두 스코프 화면 (Game 뷰). 총·몸은 숨겨지고 확대는 총의 aimFovMultiplier", EditorStyles.wordWrappedMiniLabel);
            if (aimView == null && mode != WeaponGripTable.AimMode.Sight)
                EditorGUILayout.HelpBox("PlayerAimView 가 없어 스코프·견착 조준선이 뜨지 않습니다 (Play 를 끝내고 위 버튼으로 추가)", MessageType.Warning);

            Vector3 hip = entry.fpHipSight != Vector3.zero ? entry.fpHipSight : fpIK.DefaultHipSight;
            float ads = entry.adsDistance > 0f ? entry.adsDistance : fpIK.DefaultAdsDistance;
            Vector3 shoulder = entry.fpShoulderSight != Vector3.zero ? entry.fpShoulderSight : fpIK.DefaultShoulderSight;
            EditorGUILayout.LabelField("평소 조준기 자리 (눈 기준, \"들고 다니기 고정\"으로 보기)", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            hip.x = EditorGUILayout.Slider("오른쪽", hip.x, -0.3f, 0.3f);
            hip.y = EditorGUILayout.Slider("위", hip.y, -0.4f, 0.1f);
            hip.z = EditorGUILayout.Slider("앞", hip.z, 0.1f, 0.6f);
            if (mode == WeaponGripTable.AimMode.Shoulder)
            {
                EditorGUILayout.LabelField("어깨 견착 조준: 조준기 자리 (\"조준 고정\"으로 보기, 화면 중앙은 조준선 UI)", EditorStyles.miniLabel);
                shoulder.x = EditorGUILayout.Slider("견착: 오른쪽", shoulder.x, -0.1f, 0.4f);
                shoulder.y = EditorGUILayout.Slider("견착: 위", shoulder.y, -0.4f, 0.1f);
                shoulder.z = EditorGUILayout.Slider("견착: 앞", shoulder.z, 0.05f, 0.6f);
            }
            else
            {
                EditorGUILayout.LabelField("조준 (\"조준 고정\"으로 보기)", EditorStyles.miniLabel);
                ads = EditorGUILayout.Slider("눈 ↔ 조준기 거리", ads, 0.05f, 0.4f);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(table, "1인칭 무기 자리");
                entry.fpHipSight = hip;
                entry.adsDistance = ads;
                if (mode == WeaponGripTable.AimMode.Shoulder) entry.fpShoulderSight = shoulder;
                EditorUtility.SetDirty(table);
            }
            if (GUILayout.Button("이 총을 기본값으로"))
            {
                Undo.RecordObject(table, "1인칭 무기 자리");
                entry.fpHipSight = Vector3.zero;
                entry.adsDistance = 0f;
                entry.fpShoulderSight = Vector3.zero;
                EditorUtility.SetDirty(table);
            }

            Camera cam = player.FirstPersonCamera;
            if (cam != null && bodyIK.Sight != null && driver.AimWeight > 0.99f && mode == WeaponGripTable.AimMode.Sight)
            {
                Vector3 screen = cam.WorldToScreenPoint(bodyIK.Sight.position);
                float off = new Vector2(screen.x - cam.pixelWidth * 0.5f, screen.y - cam.pixelHeight * 0.5f).magnitude;
                EditorGUILayout.LabelField("조준기 화면 중앙 오차", $"{off:0.0} px  ({(off < 3f ? "중앙" : "어긋남 → 조준기 지점 확인")})");
            }
            EditorGUILayout.LabelField("1인칭 IK · 벽 당김", $"{fpIK.Weight:0.00}  ·  {fpIK.WallPull * 100f:0} cm");
            EditorGUILayout.LabelField("FBBIK 상태", FullBodyIKStatus(fpIK.FullBodyIK), EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("FBBIK 푼 횟수", fpIK.FbbikSolveCount.ToString());
            // 진단: FBBIK 직후 vs 프레임 끝. 직후가 크면 FBBIK 가 목표에 못 닿음, 직후는 작은데 끝이 크면 그 뒤에 누가 손·총을 덮어씀
            EditorGUILayout.LabelField("오른손 목표 오차 (FBBIK 직후)", $"{fpIK.HandPositionError:0.0} cm  ·  {fpIK.HandRotationError:0.0}°");
            Vector3 eyeForward = player.FirstPersonEyePose.rotation * Vector3.forward;
            float endAngle = bodyIK.GripGun != null ? Vector3.Angle(bodyIK.GripGun.transform.forward, eyeForward) : 0f;
            EditorGUILayout.LabelField("총 ↔ 시선 각도 (직후 · 프레임 끝)", $"{fpIK.GunAngleError:0.0}°  ·  {endAngle:0.0}°");
        }

        private static string AimModeName(WeaponGripTable.AimMode mode) => mode switch
        {
            WeaponGripTable.AimMode.Scope => "스코프 화면",
            WeaponGripTable.AimMode.Shoulder => "어깨 견착",
            _ => "조준기 정렬",
        };

        /// <summary>FBBIK 가 실제로 도는지: 켜짐 · 초기화 · Final IK 의 유효성 메시지</summary>
        private static string FullBodyIKStatus(FullBodyBipedIK fbbik)
        {
            if (fbbik == null) return "없음";
            if (fbbik.isActiveAndEnabled == false) return "꺼져 있음";
            string message = "";
            bool valid = fbbik.solver.IsValid(ref message);
            if (fbbik.solver.initiated) return valid ? "초기화됨" : $"초기화됨 (유효하지 않음: {message})";
            return $"초기화 안 됨 → IK 가 풀리지 않음. 이유: {(valid ? "(유효성 통과)" : message)}";
        }

        private void DrawPoseSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("자세 · 시점 · 시간", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                bool? current = driver.DebugAimOverride;
                if (Toggle(current == null, "평소대로")) SetPose(null);
                if (Toggle(current == false, "들고 다니기 고정")) SetPose(false);
                if (Toggle(current == true, "조준 고정")) SetPose(true);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (Toggle(player.IsFirstPerson, "1인칭")) SetView(ViewMode.FirstPerson);
                if (Toggle(player.IsFirstPerson == false, "3인칭")) SetView(ViewMode.ThirdPerson);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < Speeds.Length; i++)
                    if (Toggle(Mathf.Approximately(speed, Speeds[i]), SpeedLabels[i])) SetSpeed(Speeds[i]);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("반동 한 발")) FireRecoil();
                if (GUILayout.Button("씬 뷰: 옆에서")) FrameHands(Side.Right);
                if (GUILayout.Button("위에서")) FrameHands(Side.Top);
                if (GUILayout.Button("카메라 시점")) FrameHands(Side.Camera);
            }
        }

        private void DrawGunSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("총", EditorStyles.boldLabel);
            if (guns.Length == 0) { EditorGUILayout.HelpBox("WeaponSocket 아래에 총이 없습니다", MessageType.Warning); return; }
            Gun current = gunSlot.CurrentGun;
            int index = System.Array.IndexOf(guns, current);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀", GUILayout.Width(30))) Equip(guns[(index - 1 + guns.Length) % guns.Length]);
                int picked = EditorGUILayout.Popup(index, guns.Select(g => g != null ? g.name : "-").ToArray());
                if (picked != index && picked >= 0) Equip(guns[picked]);
                if (GUILayout.Button("▶", GUILayout.Width(30))) Equip(guns[(index + 1) % guns.Length]);
            }
        }

        private void DrawEditSection()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("손 위치 편집", EditorStyles.boldLabel);
            WeaponGripTable table = bodyIK.GripTable;
            Gun gun = bodyIK.GripGun;
            using (new EditorGUI.DisabledScope(table == null || gun == null))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("이 총 자동 맞춤")) AutoFit(false);
                    if (GUILayout.Button("모든 총 자동 맞춤")) AutoFit(true);
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("총") && gun != null) Select(gun.transform);
                    if (GUILayout.Button("손잡이 지점") && bodyIK.RightHandGrip != null) Select(bodyIK.RightHandGrip);
                    if (GUILayout.Button("왼손") && bodyIK.LeftHandGrip != null) Select(bodyIK.LeftHandGrip);
                    if (GUILayout.Button("조준기") && bodyIK.Sight != null) Select(bodyIK.Sight);
                }
                EditorGUILayout.LabelField("오른손: 손잡이 지점을 총의 손잡이로 옮긴 뒤 → 총을 오른손에 붙이기", EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("총을 오른손에 붙이기")) AttachToHand();
                    if (GUILayout.Button("손잡이 자동 찾기")) ResetGripPoint();
                }
                autoSave = EditorGUILayout.ToggleLeft("씬 뷰에서 옮긴 값을 자동 저장 (데이터가 있는 총만)", autoSave);
                showMarkers = EditorGUILayout.ToggleLeft("씬 뷰에 손·조준기 표시", showMarkers);
                if (GUILayout.Button("저장된 값으로 되돌리기")) { bodyIK.ApplyGrip(); message = "저장된 값으로 되돌렸습니다"; }
            }
            if (table == null) EditorGUILayout.HelpBox("손 위치 데이터가 없습니다 (프로필의 gripTable 확인)", MessageType.Warning);
        }

        private void DrawStatus()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("상태", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("조준 정도 · 왼손 IK", $"{driver.AimWeight:0.00}  ·  {bodyIK.LeftHandWeight:0.00}");
            if (aimIK != null && driver.AimWeight > 0.99f)
            {
                float error = aimIK.MuzzleError;
                EditorGUILayout.LabelField("총구 조준 오차", $"{error:0.00}°  ({(error < 1f ? "총구가 조준점을 향함" : "어긋남")})");
            }
            if (bodyIK.RightHandGrip != null && bodyIK.GripGun != null)
            {
                float error = Vector3.Distance(bodyIK.RightHandGrip.position, WeaponGripAutoFit.RightGripTarget(driver.Animator, bodyIK.GripGun.transform)) * 100f;
                EditorGUILayout.LabelField("오른손 오차", $"{error:0.0} cm  ({(error < 0.5f ? "붙어 있음" : "총을 오른손에 붙이기 필요")})");
            }
            if (bodyIK.LeftHandBone != null && bodyIK.LeftHandWeight > 0.99f)
            {
                float error = Vector3.Distance(bodyIK.LeftHandBone.position, bodyIK.LeftHandTarget) * 100f;
                string verdict = error < 1f ? "닿음" : error < 4f ? "조금 모자람" : "팔이 안 닿음 → 왼손 지점을 몸 쪽으로";
                EditorGUILayout.LabelField("왼손 오차", $"{error:0.0} cm  ({verdict})");
            }

            WeaponGripTable table = bodyIK.GripTable;
            if (table == null) return;
            foreach (Gun gun in guns)
                if (gun != null) EditorGUILayout.LabelField(gun.name, table.Find(gun) != null ? "데이터 있음" : "없음 (자동 맞춤 필요)");
        }
        #endregion

        #region 동작
        private bool Bind()
        {
            if (player != null && driver != null && bodyIK != null && gunSlot != null) return true;
            player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null) return false;
            driver = player.GetComponent<PlayerAnimationDriver>();
            bodyIK = player.GetComponent<PlayerBodyIK>();
            gunSlot = player.GetComponent<GunSlot>();
            gunRecoil = player.GetComponent<PlayerGunRecoil>();
            aimView = player.GetComponent<PlayerAimView>();
            WeaponSightCheck.ClearCache();
            aimIK = player.GetComponent<PlayerAimIK>();
            fpIK = player.GetComponent<PlayerFirstPersonIK>();
            itemIK = player.GetComponent<PlayerItemIK>();
            items = player.GetComponent<PlayerItems>();
            guns = player.GetComponentsInChildren<Gun>(true);
            return driver != null && bodyIK != null && gunSlot != null;
        }

        private void SetSpeed(float value)
        {
            speed = value;
            resumeUntil = -1f;
            Time.timeScale = value;
        }

        /// <summary>정지 중이면 잠깐 움직였다가 다시 정지 (꺼내기 동작·반동이 끝나도록)</summary>
        private void ResumeBriefly(float seconds)
        {
            if (speed > 0f) return;
            Time.timeScale = 1f;
            resumeUntil = (float)EditorApplication.timeSinceStartup + seconds;
        }

        private void SetPose(bool? aim)
        {
            driver.DebugAimOverride = aim;
            ResumeBriefly(0.5f);
        }

        private void SetView(ViewMode mode)
        {
            player.SwitchViewMode(mode);
            ResumeBriefly(1f);
        }

        private void Equip(Gun gun)
        {
            if (gun == null) return;
            gunSlot.DebugEquip(gun);
            ResumeBriefly(1f);
            message = $"{gun.name} 을(를) 들었습니다";
        }

        private void FireRecoil()
        {
            if (gunRecoil == null) { message = "PlayerGunRecoil 이 없습니다"; return; }
            gunRecoil.Fire();
            ResumeBriefly(0.6f);
        }

        private void StartBurst()
        {
            if (gunRecoil == null) { message = "PlayerGunRecoil 이 없습니다"; return; }
            burstUntil = Time.time + 1f;
            nextShot = Time.time;
            ResumeBriefly(1.8f);
        }

        /// <summary>연사 시험: 게임 시간 0.1초마다 한 발 (느리게 보기에서도 같은 간격)</summary>
        private void TickBurst()
        {
            if (burstUntil < 0f || gunRecoil == null) return;
            if (Time.time > burstUntil) { burstUntil = -1f; return; }
            while (Time.time >= nextShot)
            {
                gunRecoil.Fire();
                nextShot += 0.1f;
            }
        }

        private void AutoFit(bool allGuns)
        {
            WeaponGripTable table = bodyIK.GripTable;
            Animator animator = driver.Animator;
            if (table == null || animator == null) return;
            if (driver.AimWeight < 0.99f)
            {
                SetPose(true);
                message = "조준 자세로 바꿨습니다. 자세가 잡힌 뒤 다시 누르세요";
                return;
            }
            if (WeaponGripAutoFit.Measure(animator, bodyIK, out var hands, out string error) == false) { message = error; return; }

            Undo.RecordObject(table, "손 위치 자동 맞춤");
            int count = 0;
            foreach (Gun gun in allGuns ? guns : new[] { bodyIK.GripGun })
            {
                if (gun == null) continue;
                WeaponGripAutoFit.Fit(gun, hands, table.GetOrAdd(gun));
                count++;
            }
            EditorUtility.SetDirty(table);
            SaveTable();
            bodyIK.ApplyGrip();
            message = $"{count}자루 자동 맞춤 완료. 어긋난 총은 씬 뷰에서 다듬으세요";
        }

        /// <summary>손잡이 지점이 오른손 바닥에 오도록 총만 옮긴다 (왼손·조준기는 총 기준이라 같이 따라간다)</summary>
        private void AttachToHand()
        {
            Gun gun = bodyIK.GripGun;
            if (gun == null) return;
            if (bodyIK.RightHandGrip == null) ResetGripPoint();
            if (bodyIK.RightHandGrip == null) return;
            Vector3 target = WeaponGripAutoFit.RightGripTarget(driver.Animator, gun.transform);
            gun.transform.position += target - bodyIK.RightHandGrip.position;
            Capture();
            message = $"{gun.name}: 손잡이를 오른손에 붙였습니다";
        }

        /// <summary>손잡이 지점을 총 메쉬에서 다시 찾는다 (총은 움직이지 않음)</summary>
        private void ResetGripPoint()
        {
            WeaponGripTable table = bodyIK.GripTable;
            Gun gun = bodyIK.GripGun;
            WeaponGripTable.Entry entry = table != null ? table.Find(gun) : null;
            if (entry == null) { message = "이 총은 데이터가 없습니다 (자동 맞춤 먼저)"; return; }
            Capture(); // 지금까지 옮긴 값을 먼저 저장 (ApplyGrip 이 데이터로 되돌리므로)
            entry.gripPoint = WeaponGripAutoFit.FindGrip(gun);
            EditorUtility.SetDirty(table);
            bodyIK.ApplyGrip();
            message = $"{gun.name}: 손잡이 지점을 메쉬에서 찾았습니다. 맞지 않으면 씬 뷰에서 옮기세요";
        }

        /// <summary>Play 중 씬 뷰에서 옮긴 총·왼손·조준기 위치를 데이터에 기록</summary>
        private void EditorTick()
        {
            if (EditorApplication.isPlaying == false || player == null) return;
            if (resumeUntil > 0f && EditorApplication.timeSinceStartup > resumeUntil)
            {
                resumeUntil = -1f;
                Time.timeScale = speed;
            }
            if (autoSave) Capture();
            CaptureItems();
            TickBurst();

            // 손잡이 지점이 없는 총(예전 데이터)은 메쉬에서 찾아 표시해 둔다
            WeaponGripTable table = bodyIK != null ? bodyIK.GripTable : null;
            WeaponGripTable.Entry entry = table != null ? table.Find(bodyIK.GripGun) : null;
            if (entry != null && entry.gripPoint == Vector3.zero) ResetGripPoint();
        }

        private void Capture()
        {
            WeaponGripTable table = bodyIK != null ? bodyIK.GripTable : null;
            Gun gun = bodyIK != null ? bodyIK.GripGun : null;
            WeaponGripTable.Entry entry = table != null ? table.Find(gun) : null;
            if (entry == null) return;

            Transform t = gun.transform;
            bool changed = Differs(entry.gunPosition, t.localPosition) || Differs(entry.gunRotation, t.localRotation);
            entry.gunPosition = t.localPosition;
            entry.gunRotation = t.localRotation;
            if (bodyIK.LeftHandGrip != null)
            {
                changed |= Differs(entry.leftHandPosition, bodyIK.LeftHandGrip.localPosition) || Differs(entry.leftHandRotation, bodyIK.LeftHandGrip.localRotation);
                entry.leftHandPosition = bodyIK.LeftHandGrip.localPosition;
                entry.leftHandRotation = bodyIK.LeftHandGrip.localRotation;
            }
            if (bodyIK.Sight != null)
            {
                changed |= Differs(entry.sightPosition, bodyIK.Sight.localPosition);
                entry.sightPosition = bodyIK.Sight.localPosition;
            }
            if (bodyIK.RightHandGrip != null)
            {
                changed |= Differs(entry.gripPoint, bodyIK.RightHandGrip.localPosition);
                entry.gripPoint = bodyIK.RightHandGrip.localPosition;
            }
            if (changed) EditorUtility.SetDirty(table);
        }

        private static bool Differs(Vector3 a, Vector3 b) => (a - b).sqrMagnitude > 1e-10f;
        private static bool Differs(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) > 0.01f;

        /// <summary>Play 중 바꾼 시간·자세 고정을 원래대로</summary>
        private void RestoreRuntime()
        {
            if (EditorApplication.isPlaying) Time.timeScale = 1f;
            if (driver != null) driver.DebugAimOverride = null;
            if (healMarker != null) StopHealEdit();
            if (editGrenade && itemIK != null) { itemIK.HoldGrenade = false; itemIK.EditingModel = false; }
            editGrenade = false;
            SaveProfile();
            speed = 1f;
            resumeUntil = -1f;
            burstUntil = -1f;
            sightReport = null;
        }

        private static void SaveTable()
        {
            PlayerAnimationProfile profile = FindProfile();
            if (profile != null && profile.gripTable != null) AssetDatabase.SaveAssetIfDirty(profile.gripTable);
        }

        private static PlayerAnimationProfile FindProfile()
        {
            var driver = Object.FindFirstObjectByType<PlayerAnimationDriver>();
            if (driver != null && driver.Profile != null) return driver.Profile;
            string guid = AssetDatabase.FindAssets("t:PlayerAnimationProfile").FirstOrDefault();
            return guid != null ? AssetDatabase.LoadAssetAtPath<PlayerAnimationProfile>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }

        private static void CreateTable(PlayerAnimationProfile profile)
        {
            var table = AssetDatabase.LoadAssetAtPath<WeaponGripTable>(TablePath);
            if (table == null)
            {
                table = CreateInstance<WeaponGripTable>();
                AssetDatabase.CreateAsset(table, TablePath);
            }
            Undo.RecordObject(profile, "손 위치 데이터 연결");
            profile.gripTable = table;
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        private static void Select(Transform t)
        {
            Selection.activeTransform = t;
            Tools.current = Tool.Move;
            EditorGUIUtility.PingObject(t);
        }
        #endregion

        #region 씬 뷰
        private enum Side { Right, Top, Camera }

        private void FrameHands(Side side)
        {
            SceneView view = SceneView.lastActiveSceneView;
            Transform right = bodyIK.RightHandBone, left = bodyIK.LeftHandBone;
            if (view == null || right == null || left == null) return;
            Vector3 center = (right.position + left.position) * 0.5f;
            Transform body = driver.Animator.transform;
            Transform cam = player.CurrentCameraTransform;
            Quaternion rotation = side switch
            {
                Side.Right => Quaternion.LookRotation(-body.right, Vector3.up),
                Side.Top => Quaternion.LookRotation(Vector3.down, body.forward),
                _ => cam != null ? cam.rotation : body.rotation,
            };
            if (side == Side.Camera && cam != null) center = cam.position + cam.forward * 0.6f;
            view.LookAt(center, rotation, 0.6f);
        }

        private void OnSceneGUI(SceneView view)
        {
            if (EditorApplication.isPlaying == false || bodyIK == null || bodyIK.GripGun == null) return;
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            DrawRecoilTrail();
            if (showSight) DrawSightLine();
            if (showMarkers == false) return;
            float size = 0.015f;

            Transform gun = bodyIK.GripGun.transform;
            Marker(gun.position, gun.rotation, new Color(1f, 0.5f, 0.2f), size, "총");
            Vector3 palm = WeaponGripAutoFit.RightGripTarget(driver.Animator, gun);
            Handles.color = Color.yellow;
            Handles.SphereHandleCap(0, palm, Quaternion.identity, size * 1.5f, EventType.Repaint);
            Handles.Label(palm + Vector3.down * 0.02f, "오른손 바닥", EditorStyles.whiteMiniLabel);
            if (bodyIK.RightHandGrip != null)
            {
                Marker(bodyIK.RightHandGrip.position, gun.rotation, Color.magenta, size, "손잡이");
                Handles.color = Color.magenta;
                Handles.DrawDottedLine(bodyIK.RightHandGrip.position, palm, 2f);
            }
            if (bodyIK.LeftHandGrip != null)
                Marker(bodyIK.LeftHandGrip.position, bodyIK.LeftHandGrip.rotation, new Color(0.3f, 0.7f, 1f), size, "왼손");
            if (bodyIK.Sight != null)
            {
                Handles.color = Color.green;
                Handles.DrawLine(bodyIK.Sight.position, bodyIK.Sight.position + gun.forward * 0.5f);
                Marker(bodyIK.Sight.position, bodyIK.Sight.rotation, Color.green, size, "조준기");
            }
            // 빨강 = 총구 정면, 하늘색 = 사격 광선(조준 중). 두 선이 조준점에서 만나야 한다
            Transform muzzle = bodyIK.GripGun.Muzzle;
            bool aiming = aimIK != null && driver.AimWeight > 0.99f;
            float length = aiming ? Vector3.Distance(muzzle.position, aimIK.AimPoint) : 3f;
            Handles.color = Color.red;
            Handles.DrawLine(muzzle.position, muzzle.position + gun.forward * length);
            if (aiming)
            {
                Handles.color = Color.cyan;
                Handles.DrawLine(gunSlot.AimRay.origin, aimIK.AimPoint);
                Handles.SphereHandleCap(0, aimIK.AimPoint, Quaternion.identity, 0.05f, EventType.Repaint);
            }
        }

        /// <summary>이번 반동 동안 총구가 지나간 자리 (주황)</summary>
        private void DrawRecoilTrail()
        {
            if (gunRecoil == null || gunRecoil.TrailCount < 2) return;
            Handles.color = new Color(1f, 0.6f, 0.1f);
            for (int i = 1; i < gunRecoil.TrailCount; i++) Handles.DrawLine(gunRecoil.TrailPoint(i - 1), gunRecoil.TrailPoint(i), 2f);
        }

        /// <summary>조준 때 눈 · 잘림 면 · 시선 (가림이 있으면 빨강, 가린 자리 표시). 1인칭 조준 중이면 실제 카메라 기준</summary>
        private void DrawSightLine()
        {
            Gun gun = bodyIK.GripGun;
            WeaponGripTable table = bodyIK.GripTable;
            WeaponGripTable.Entry entry = table != null ? table.Find(gun) : null;
            if (fpIK == null || bodyIK.Sight == null || entry == null || table.AimModeFor(gun) != WeaponGripTable.AimMode.Sight) return;
            Transform g = gun.transform;
            Camera cam = player.FirstPersonCamera;
            float near = SightNear();
            Vector3 sight = WeaponGripAutoFit.ToMetric(g, bodyIK.Sight.position);
            bool live = player.IsFirstPerson && driver.AimWeight > 0.99f && cam != null;
            Vector3 eye = live ? WeaponGripAutoFit.ToMetric(g, cam.transform.position) : sight - Vector3.forward * SightAds(entry);
            WeaponSightCheck.Result r = WeaponSightCheck.Check(gun, eye, sight, near);

            Vector3 eyeWorld = WeaponGripAutoFit.ToWorld(g, eye);
            Vector3 sightWorld = bodyIK.Sight.position;
            Vector3 forward = (sightWorld - eyeWorld).normalized;
            Handles.color = Color.white;
            Handles.SphereHandleCap(0, eyeWorld, Quaternion.identity, 0.01f, EventType.Repaint);
            Handles.Label(eyeWorld + g.up * 0.02f, "눈", EditorStyles.whiteMiniLabel);

            // 잘림 면 (카메라 화면 비율·시야각)
            float fov = cam != null ? cam.fieldOfView : 60f;
            float aspect = cam != null ? cam.aspect : 16f / 9f;
            float h = near * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad), w = h * aspect;
            Quaternion look = Quaternion.LookRotation(forward, g.up);
            Vector3 c = eyeWorld + forward * near;
            Vector3 right = look * Vector3.right * w, up = look * Vector3.up * h;
            Handles.color = r.eyeInside ? Color.red : new Color(1f, 1f, 1f, 0.6f);
            Handles.DrawPolyLine(c - right - up, c + right - up, c + right + up, c - right + up, c - right - up);

            bool blocked = r.blockDistance >= 0f;
            Handles.color = blocked || r.eyeInside ? Color.red : Color.green;
            Handles.DrawLine(eyeWorld, sightWorld, 2f);
            if (blocked)
            {
                Vector3 hit = WeaponGripAutoFit.ToWorld(g, r.blockPoint);
                Handles.SphereHandleCap(0, hit, Quaternion.identity, 0.012f, EventType.Repaint);
                Handles.Label(hit + g.up * 0.02f, r.blockBackFace ? "가림 (안쪽 면)" : "가림", EditorStyles.whiteMiniLabel);
            }
        }

        private static void Marker(Vector3 position, Quaternion rotation, Color color, float size, string label)
        {
            Handles.color = color;
            Handles.SphereHandleCap(0, position, rotation, size * 2f, EventType.Repaint);
            Handles.DrawLine(position, position + rotation * Vector3.forward * 0.06f);
            Handles.Label(position + Vector3.up * 0.02f, label, EditorStyles.whiteMiniLabel);
        }

        private static bool Toggle(bool on, string label) => GUILayout.Toggle(on, label, "Button") && on == false;
        #endregion
    }
}
