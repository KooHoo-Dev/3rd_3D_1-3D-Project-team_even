using System;
using System.Collections.Generic;
using HybridShooter.Weapon;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// WeaponHandPose 인스펙터. 총마다 손 자세를 맞추는 도구 세 가지:
    ///
    ///  ① 조준 자세 미리보기 (편집 모드) : 캐릭터를 이 총의 조준 애니메이션 자세로 세우고 손가락 값을 바로 보여 준다.
    ///                                    슬라이더를 움직이면 즉시 갱신. (팔꿈치 힌트는 IK 라서 플레이 중에만 보임)
    ///  ② 팔꿈치 힌트 만들기             : 총 아래에 LeftElbowHint / RightElbowHint 를 만든다. 씬에서 끌어 옮기면 팔꿈치가 그쪽으로 굽는다.
    ///  ③ 플레이 중 값 저장              : 플레이 중에 바꾼 손가락 값 + 총·LeftHandGrip·Muzzle·힌트 위치를 기억했다가
    ///                                    플레이를 끄면 Player 프리팹에 자동으로 넣는다. (원래 플레이 중 변경은 꺼지면 사라진다)
    /// </summary>
    [CustomEditor(typeof(WeaponHandPose))]
    public class WeaponHandPoseEditor : UnityEditor.Editor
    {
        private static readonly string[] SavedChildren = { "LeftHandGrip", "RightHandGrip", "Muzzle", "LeftElbowHint", "RightElbowHint" };

        // ── 미리보기 상태 ──
        private bool previewing;
        private AnimationClip previewClip;
        private HandFingerPoser previewRight, previewLeft;
        private Animator previewAnimator;

        public override void OnInspectorGUI()
        {
            var pose = (WeaponHandPose)target;

            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("손 자세 도구", EditorStyles.boldLabel);

            if (Application.isPlaying) DrawPlayModeTools(pose);
            else DrawEditModeTools(pose, changed);
        }

        // ───────────────────────── 편집 모드 ─────────────────────────

        private void DrawEditModeTools(WeaponHandPose pose, bool changed)
        {
            Animator animator = pose.GetComponentInParent<Animator>();
            if (animator == null)
            {
                EditorGUILayout.HelpBox("플레이어 손(WeaponSocket) 아래에 있는 총에서만 미리보기·힌트 만들기를 쓸 수 있습니다.", MessageType.Info);
                return;
            }

            if (previewing == false)
            {
                if (GUILayout.Button("① 조준 자세로 미리보기 (손가락 확인)")) StartPreview(pose, animator);
            }
            else
            {
                EditorGUILayout.HelpBox("미리보기 중입니다. 슬라이더를 움직이면 손가락이 바로 바뀝니다.\n끝나면 꼭 '미리보기 끝내기'를 누르세요 (다른 오브젝트를 선택해도 자동으로 끝납니다).", MessageType.Warning);
                if (changed) RefreshPreview(pose);
                if (GUILayout.Button("미리보기 끝내기")) StopPreview();
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("② 왼팔꿈치 힌트 만들기")) CreateElbowHint(pose, animator, false);
                if (GUILayout.Button("② 오른팔꿈치 힌트 만들기")) CreateElbowHint(pose, animator, true);
            }

            int pending = PlayModePoseStore.PendingCount;
            if (pending > 0) EditorGUILayout.HelpBox($"플레이 중 저장한 값 {pending}개가 반영 대기 중입니다.", MessageType.Info);
        }

        // 미리보기는 Unity 의 "애니메이션 모드"(Animation 창 미리보기와 같은 방식)에서 한다.
        // 이 모드에서 바뀐 뼈 값은 저장되지 않고 끝날 때 원래대로 돌아가므로, 프리팹 자동 저장에 자세가 섞여 들어가지 않는다.
        private void StartPreview(WeaponHandPose pose, Animator animator)
        {
            previewClip = AimClip(pose);
            if (previewClip == null) { Debug.LogWarning("[WeaponHandPose] 조준 자세 클립을 찾지 못했습니다."); return; }

            previewAnimator = animator;
            // 손가락 "편 자세" 기준은 프리팹의 기본 자세(지금) → 애니메이션 모드에 들어가기 전에 만든다
            Func<HumanBodyBones, Transform> find = bone => PlayerPrefabSetup.FindBone(animator, bone);
            previewRight = new HandFingerPoser(find, true);
            previewLeft = new HandFingerPoser(find, false);
            if (AnimationMode.InAnimationMode() == false) AnimationMode.StartAnimationMode();
            previewing = true;
            RefreshPreview(pose);
        }

        private void RefreshPreview(WeaponHandPose pose)
        {
            if (previewing == false || previewAnimator == null || AnimationMode.InAnimationMode() == false) return;
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(previewAnimator.gameObject, previewClip, previewClip.length * 0.25f);
            AnimationMode.EndSampling();
            previewRight.Apply(pose.right, pose, pose.right.fingerWeight);
            previewLeft.Apply(pose.left, pose, pose.left.fingerWeight);
            SceneView.RepaintAll();
        }

        private void StopPreview()
        {
            if (previewing == false) return;
            previewing = false;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            SceneView.RepaintAll();
        }

        private static AnimationClip AimClip(WeaponHandPose pose)
        {
            var item = pose.GetComponent<WeaponItem>();
            int type = item != null ? (int)item.Type : 1;
            return PlayerAnimatorBuilder.FindClip("IdleAim" + PlayerAnimParams.ClipSuffix(type == 0 ? 1 : type), "IdleAimAssaultRifle");
        }

        private void OnDisable() => StopPreview();

        /// <summary>조준 자세에서 팔꿈치 바깥쪽(어깨~손 직선에서 팔꿈치 쪽으로 더 나간 곳)에 힌트를 만든다</summary>
        private void CreateElbowHint(WeaponHandPose pose, Animator animator, bool right)
        {
            bool wasPreviewing = previewing;
            StopPreview();

            string name = right ? "RightElbowHint" : "LeftElbowHint";
            Transform hint = pose.transform.Find(name);
            if (hint == null)
            {
                hint = new GameObject(name).transform;
                Undo.RegisterCreatedObjectUndo(hint.gameObject, "Create Elbow Hint");
                hint.SetParent(pose.transform, false);
            }

            var snapshot = new WeaponAutoAlign.PoseSnapshot(animator.transform);
            try
            {
                WeaponAutoAlign.SampleAimPose(animator, (int)pose.GetComponent<WeaponItem>().Type);
                Transform upper = PlayerPrefabSetup.FindBone(animator, right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
                Transform lower = PlayerPrefabSetup.FindBone(animator, right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
                Transform hand = PlayerPrefabSetup.FindBone(animator, right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
                if (upper != null && lower != null && hand != null)
                {
                    Vector3 mid = (upper.position + hand.position) * 0.5f;
                    Vector3 outward = lower.position - mid;
                    if (outward.sqrMagnitude < 0.0001f) outward = -animator.transform.up;
                    hint.position = lower.position + outward.normalized * 0.3f;
                }
            }
            finally { snapshot.Restore(); }

            Undo.RecordObject(pose, "Assign Elbow Hint");
            pose.Get(right).elbowHint = hint;
            EditorUtility.SetDirty(pose);
            Selection.activeTransform = hint;
            if (wasPreviewing) StartPreview(pose, animator);
        }

        // ───────────────────────── 플레이 모드 ─────────────────────────

        private static void DrawPlayModeTools(WeaponHandPose pose)
        {
            var item = pose.GetComponent<WeaponItem>();
            var handIK = pose.GetComponentInParent<PlayerHandIK>();
            bool gunDriven = handIK != null && handIK.Mode == PlayerHandIK.GripMode.GunDriven;

            if (gunDriven)
            {
                EditorGUILayout.HelpBox("총 기준 모드: 아래 버튼으로 핸들을 고른 뒤 씬 뷰에서 옮기세요 (이동 W · 회전 E).\n" +
                                        "• 총 위치 = 어깨 기준으로 총이 놓일 곳 (조준선을 따라 같이 움직임)\n" +
                                        "• 오른손 자리 / 왼손 자리 = 총 위에서 손목이 올 곳", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("총 위치 (Hold)")) Select(handIK.GetHoldHandle(item), "Hold 핸들은 이 총을 한 번 든 뒤에 생깁니다.");
                    if (GUILayout.Button("오른손 자리")) Select(item != null ? item.RightHandGrip : null, "RightHandGrip 이 없습니다.");
                    if (GUILayout.Button("왼손 자리")) Select(item != null ? item.LeftHandGrip : null, "LeftHandGrip 이 없습니다.");
                }
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.HelpBox("플레이 중 바꾼 값은 플레이를 끄면 사라집니다.\n마음에 들면 아래 버튼을 누르세요. 플레이를 끈 뒤 Player 프리팹에 자동으로 들어갑니다.\n" +
                                    (gunDriven
                                        ? "(손가락 값 + 총 드는 위치 · 내림 각도 + 오른손·왼손 자리 · Muzzle · 팔꿈치 힌트)"
                                        : "(손가락 값 + 총 위치 + LeftHandGrip · Muzzle · 팔꿈치 힌트 위치)"), MessageType.Info);
            if (GUILayout.Button("③ 플레이 중 값 저장 (끄면 프리팹에 반영)", GUILayout.Height(28)))
                PlayModePoseStore.Capture(pose);

            if (PlayModePoseStore.IsPending(pose.gameObject.name))
                EditorGUILayout.LabelField($"✔ 저장됨 — 플레이를 끄면 '{pose.gameObject.name}' 에 반영됩니다.");
        }

        private static void Select(Transform t, string missingMessage)
        {
            if (t == null) { Debug.LogWarning("[WeaponHandPose] " + missingMessage); return; }
            Selection.activeTransform = t;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Focus();
        }

        /// <summary>플레이 중 저장한 값을 들고 있다가, 편집 모드로 돌아오면 Player 프리팹에 넣는다</summary>
        [InitializeOnLoad]
        internal static class PlayModePoseStore
        {
            [Serializable]
            private class LocalPose
            {
                public string name;
                public Vector3 position;
                public Quaternion rotation;
                public Vector3 scale;
            }

            [Serializable]
            private class Entry
            {
                public string weaponName;
                public string rightJson, leftJson;
                public float maxJointAngle, maxThumbAngle, maxSpreadAngle;
                public float loweredPitch, hipAim;
                /// <summary>손 기준 모드에서만: 총이 손 아래 있으므로 총 오브젝트 위치를 그대로 저장</summary>
                public bool hasWeaponPose;
                public LocalPose weapon;
                /// <summary>총 기준 모드에서만: Hold 핸들 위치 (어깨 기준 총 위치)</summary>
                public bool hasHold;
                public Vector3 holdPosition;
                public Quaternion holdRotation;
                public List<LocalPose> children = new List<LocalPose>();
            }

            [Serializable]
            private class EntryList { public List<Entry> items = new List<Entry>(); }

            // 플레이 종료 시 도메인 리로드가 있어도 살아남도록 SessionState 에 보관
            private const string Key = "HybridShooter.WeaponHandPose.Pending";

            static PlayModePoseStore()
            {
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) ApplyPending();
                };
            }

            public static int PendingCount => Load().items.Count;
            public static bool IsPending(string weaponName) => Load().items.Exists(e => e.weaponName == weaponName);

            public static void Capture(WeaponHandPose pose)
            {
                var list = Load();
                list.items.RemoveAll(e => e.weaponName == pose.gameObject.name);

                var entry = new Entry
                {
                    weaponName = pose.gameObject.name,
                    rightJson = JsonUtility.ToJson(pose.right),
                    leftJson = JsonUtility.ToJson(pose.left),
                    maxJointAngle = pose.maxJointAngle,
                    maxThumbAngle = pose.maxThumbAngle,
                    maxSpreadAngle = pose.maxSpreadAngle,
                    loweredPitch = pose.loweredPitch,
                    hipAim = pose.hipAim,
                };

                var handIK = pose.GetComponentInParent<PlayerHandIK>();
                Transform hold = handIK != null && handIK.Mode == PlayerHandIK.GripMode.GunDriven
                    ? handIK.GetHoldHandle(pose.GetComponent<WeaponItem>()) : null;
                if (hold != null)
                {
                    // 총 기준: 총은 실행 중 플레이어 루트 아래로 옮겨져 있어 총 오브젝트 위치는 저장하지 않는다
                    entry.hasHold = true;
                    entry.holdPosition = hold.localPosition;
                    entry.holdRotation = hold.localRotation;
                }
                else
                {
                    entry.hasWeaponPose = true;
                    entry.weapon = Snap(pose.transform);
                }
                foreach (var childName in SavedChildren)
                {
                    Transform child = pose.transform.Find(childName);
                    if (child != null) entry.children.Add(Snap(child));
                }
                list.items.Add(entry);
                SessionState.SetString(Key, JsonUtility.ToJson(list));
                Debug.Log($"[WeaponHandPose] '{pose.gameObject.name}' 값 저장. 플레이를 끄면 Player 프리팹에 반영됩니다.");
            }

            private static LocalPose Snap(Transform t)
                => new LocalPose { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale };

            private static EntryList Load()
            {
                string json = SessionState.GetString(Key, string.Empty);
                return string.IsNullOrEmpty(json) ? new EntryList() : JsonUtility.FromJson<EntryList>(json) ?? new EntryList();
            }

            private static void ApplyPending()
            {
                var list = Load();
                if (list.items.Count == 0) return;
                SessionState.EraseString(Key);

                var root = PrefabUtility.LoadPrefabContents(PlayerPrefabSetup.PlayerPrefabPath);
                try
                {
                    int applied = 0;
                    foreach (var entry in list.items)
                    {
                        WeaponItem item = FindWeapon(root, entry.weaponName);
                        if (item == null) { Debug.LogWarning($"[WeaponHandPose] 프리팹에서 '{entry.weaponName}' 을 찾지 못했습니다."); continue; }

                        var pose = item.GetComponent<WeaponHandPose>();
                        if (pose == null) pose = item.gameObject.AddComponent<WeaponHandPose>();

                        // 팔꿈치 힌트 연결은 프리팹 쪽 것을 유지하고 숫자만 덮어쓴다
                        Transform rightHint = pose.right.elbowHint, leftHint = pose.left.elbowHint;
                        JsonUtility.FromJsonOverwrite(entry.rightJson, pose.right);
                        JsonUtility.FromJsonOverwrite(entry.leftJson, pose.left);
                        pose.right.elbowHint = rightHint != null ? rightHint : item.transform.Find("RightElbowHint");
                        pose.left.elbowHint = leftHint != null ? leftHint : item.transform.Find("LeftElbowHint");
                        pose.maxJointAngle = entry.maxJointAngle;
                        pose.maxThumbAngle = entry.maxThumbAngle;
                        pose.maxSpreadAngle = entry.maxSpreadAngle;
                        pose.loweredPitch = entry.loweredPitch;
                        pose.hipAim = entry.hipAim;
                        if (entry.hasHold)
                        {
                            pose.hasHoldOffset = true;
                            pose.holdPosition = entry.holdPosition;
                            pose.holdRotation = entry.holdRotation;
                        }

                        if (entry.hasWeaponPose) Apply(item.transform, entry.weapon);
                        foreach (var child in entry.children)
                        {
                            Transform t = item.transform.Find(child.name);
                            if (t == null)
                            {
                                // 플레이 중에 새로 만든 힌트는 프리팹에도 만든다
                                t = new GameObject(child.name).transform;
                                t.SetParent(item.transform, false);
                                if (child.name == "RightElbowHint" && pose.right.elbowHint == null) pose.right.elbowHint = t;
                                if (child.name == "LeftElbowHint" && pose.left.elbowHint == null) pose.left.elbowHint = t;
                            }
                            Apply(t, child);
                            if (child.name == "RightHandGrip") item.RightHandGrip = t;
                        }
                        applied++;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabSetup.PlayerPrefabPath);
                    Debug.Log($"[WeaponHandPose] 플레이 중 저장한 손 자세 {applied}개를 Player 프리팹에 반영했습니다.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            private static void Apply(Transform t, LocalPose p)
            {
                t.localPosition = p.position;
                t.localRotation = p.rotation;
                t.localScale = p.scale;
            }

            private static WeaponItem FindWeapon(GameObject root, string name)
            {
                foreach (var item in root.GetComponentsInChildren<WeaponItem>(true))
                    if (item.gameObject.name == name) return item;
                return null;
            }
        }
    }
}
