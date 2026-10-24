using System.IO;
using System.Linq;
using HybridShooter.Weapon;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 총 위치·왼손 손잡이 맞추기 (재구축 3-6). Tools/HybridShooter/Weapon Pose Preview
    ///
    ///  1. 씬의 플레이어 몸에 조준 자세(IdleAim) 또는 들고 다니기 자세(IdleHold)를 편집 모드에서 입힌다 (끝내면 원래 자세)
    ///  2. "소켓 방향 자동 맞춤": 조준 자세에서 몸 정면 = 총구 방향이 되도록 WeaponSocket 을 돌린다 (모든 총이 같은 소켓을 쓴다)
    ///  3. 위치는 미리보기 중에 씬 뷰에서 WeaponSocket(전체) 이나 총(개별)을 직접 옮겨 맞춘다 → 뼈가 아니라서 끝내도 남는다
    ///  4. "왼손 → 손잡이 저장": 지금 왼손 자리에 그 총의 LeftHandGrip 을 만든다 (PlayerBodyIK 가 왼손을 여기에 붙인다)
    /// </summary>
    public class WeaponPoseWindow : EditorWindow
    {
        private const string ClipFolder = "Assets/ExternalAssets/PROTOFACTOR/Ultimate Animation Collection/Animations/Assault Rifle Animset";

        private PlayerController player;
        private Animator animator;
        private Gun[] guns = new Gun[0];
        private int gunIndex;
        private string pose = "";

        [MenuItem("Tools/HybridShooter/Weapon Pose Preview")]
        private static void Open() => GetWindow<WeaponPoseWindow>("Weapon Pose");

        private void OnDisable() => StopPreview();

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "1) 조준 자세로 보기 → 2) 소켓 방향 자동 맞춤 → 3) 씬 뷰에서 WeaponSocket(전체)이나 총(개별) 위치를 손에 맞게 옮기기 → " +
                "4) 왼손이 총 앞손잡이 자리에 오도록 각 총마다 '왼손 → 손잡이 저장' 후 LeftHandGrip 을 옮겨 다듬기 → 5) 미리보기 끝내고 씬 저장",
                MessageType.Info);

            if (GUILayout.Button("씬에서 플레이어 찾기")) FindPlayer();
            if (player == null || animator == null) return;

            EditorGUILayout.LabelField("몸", animator.name);
            Transform socket = Socket();
            EditorGUILayout.LabelField("WeaponSocket", socket != null ? socket.name : "(총이 없음)");

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"자세 미리보기: {(AnimationMode.InAnimationMode() ? pose : "꺼짐")}", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("조준 자세")) Preview("IdleAimAssaultRifle");
                if (GUILayout.Button("들고 다니기 자세")) Preview("IdleHoldAssaultRifle");
                if (GUILayout.Button("미리보기 끝내기")) StopPreview();
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("총 소켓", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(socket == null || pose != "IdleAimAssaultRifle"))
            {
                if (GUILayout.Button("소켓 방향 자동 맞춤 (조준 자세에서: 몸 정면 = 총구)")) AlignSocket(socket);
            }
            if (socket != null && GUILayout.Button("WeaponSocket 선택")) Select(socket);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("총별 왼손 손잡이", EditorStyles.boldLabel);
            if (guns.Length == 0) return;
            gunIndex = Mathf.Clamp(gunIndex, 0, guns.Length - 1);
            gunIndex = EditorGUILayout.Popup("총", gunIndex, guns.Select(g => g != null ? g.name : "-").ToArray());
            Gun gun = guns[gunIndex];
            if (gun == null) return;
            Transform grip = gun.transform.Find(PlayerBodyIK.LeftHandGripName);
            EditorGUILayout.LabelField("LeftHandGrip", grip != null ? "있음" : "없음 (애니메이션의 왼손 자리를 그대로 씀)");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("총 선택")) Select(gun.transform);
                using (new EditorGUI.DisabledScope(AnimationMode.InAnimationMode() == false))
                {
                    if (GUILayout.Button("왼손 → 손잡이 저장")) SaveGrip(gun);
                }
                using (new EditorGUI.DisabledScope(grip == null))
                {
                    if (GUILayout.Button("손잡이 선택")) Select(grip);
                    if (GUILayout.Button("손잡이 지우기")) Undo.DestroyObjectImmediate(grip.gameObject);
                }
            }
        }

        private void FindPlayer()
        {
            player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null) { ShowNotification(new GUIContent("씬에 PlayerController 가 없습니다")); return; }
            var so = new SerializedObject(player);
            animator = so.FindProperty("bodyAnimator").objectReferenceValue as Animator;
            if (animator == null) animator = player.GetComponentInChildren<Animator>(true);
            guns = player.GetComponentsInChildren<Gun>(true);
        }

        private Transform Socket() => guns.Length > 0 && guns[0] != null ? guns[0].transform.parent : null;

        private void Preview(string clipKey)
        {
            AnimationClip clip = LoadClip(clipKey);
            if (clip == null) { ShowNotification(new GUIContent($"클립 없음: {clipKey}")); return; }
            if (AnimationMode.InAnimationMode() == false) AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, 0f);
            AnimationMode.EndSampling();
            pose = clipKey;
            SceneView.RepaintAll();
        }

        private void StopPreview()
        {
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            pose = "";
            SceneView.RepaintAll();
        }

        /// <summary>조준 자세에서 몸 정면이 총구(총 오브젝트의 앞쪽 = Gun.Muzzle 기준)가 되도록 소켓을 돌린다</summary>
        private void AlignSocket(Transform socket)
        {
            Undo.RecordObject(socket, "소켓 방향 자동 맞춤");
            socket.rotation = Quaternion.LookRotation(animator.transform.forward, animator.transform.up);
            if (PrefabUtility.IsPartOfPrefabInstance(socket)) PrefabUtility.RecordPrefabInstancePropertyModifications(socket);
            SceneView.RepaintAll();
        }

        /// <summary>지금(미리보기 자세) 왼손 자리에 총의 LeftHandGrip 을 만든다/옮긴다</summary>
        private void SaveGrip(Gun gun)
        {
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (leftHand == null) return;
            Transform grip = gun.transform.Find(PlayerBodyIK.LeftHandGripName);
            if (grip == null)
            {
                grip = new GameObject(PlayerBodyIK.LeftHandGripName).transform;
                Undo.RegisterCreatedObjectUndo(grip.gameObject, "왼손 손잡이 만들기");
                grip.SetParent(gun.transform, false);
            }
            Undo.RecordObject(grip, "왼손 손잡이 저장");
            grip.SetPositionAndRotation(leftHand.position, leftHand.rotation);
            Select(grip);
        }

        private static void Select(Transform t)
        {
            Selection.activeTransform = t;
            EditorGUIUtility.PingObject(t);
        }

        private static AnimationClip LoadClip(string key)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != "Humanoid@" + key) continue;
                return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
            }
            return null;
        }
    }
}
