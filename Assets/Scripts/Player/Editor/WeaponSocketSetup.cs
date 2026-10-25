using System.IO;
using System.Linq;
using HybridShooter.Weapon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// WeaponSocket 을 오른손 뼈 아래로 옮긴다 (편집 모드, 한 번만). IK Pose Lab 의 편집 모드 버튼이 부른다.
    /// 몸에 조준 자세(IdleAim)를 입혀 놓고 그 자세의 월드 위치·회전을 유지한 채 옮기므로,
    /// 조준 자세의 총·왼손은 그대로이고 다른 자세(들고 다니기·재장전 등)에서는 총이 손을 따라간다.
    /// 손 위치 데이터(총 위치 = 소켓 기준, 왼손 = 총 기준)는 바꿀 필요가 없다
    /// </summary>
    internal static class WeaponSocketSetup
    {
        private const string ClipFolder = "Assets/ExternalAssets/PROTOFACTOR/Ultimate Animation Collection/Animations/Assault Rifle Animset";
        private const string AimClip = "Humanoid@IdleAimAssaultRifle";

        public static bool Find(out GunSlot gunSlot, out Transform socket, out Transform hand)
        {
            gunSlot = Object.FindFirstObjectByType<GunSlot>();
            socket = null;
            hand = null;
            if (gunSlot == null) return false;
            socket = new SerializedObject(gunSlot).FindProperty("weaponSocket").objectReferenceValue as Transform;
            Animator animator = gunSlot.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman) hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            return socket != null && hand != null;
        }

        /// <summary>소켓이 오른손 아래에 있으면 true</summary>
        public static bool IsOnHand(Transform socket, Transform hand) => socket != null && hand != null && socket.parent == hand;

        public static string MoveToRightHand()
        {
            if (Find(out GunSlot gunSlot, out Transform socket, out Transform hand) == false) return "GunSlot 의 weaponSocket 또는 오른손 뼈를 찾지 못했습니다";
            if (IsOnHand(socket, hand)) return "이미 오른손 아래에 있습니다";
            AnimationClip clip = LoadClip();
            if (clip == null) return $"클립 없음: {AimClip}";
            Animator animator = hand.GetComponentInParent<Animator>();

            // 조준 자세에서 소켓의 오른손 기준 자리를 잰다
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, clip, 0f);
            AnimationMode.EndSampling();
            Vector3 localPos = hand.InverseTransformPoint(socket.position);
            Quaternion localRot = Quaternion.Inverse(hand.rotation) * socket.rotation;
            Vector3 handScale = hand.lossyScale;
            Vector3 socketScale = socket.lossyScale;
            AnimationMode.StopAnimationMode();

            Undo.SetTransformParent(socket, hand, "WeaponSocket 을 오른손으로");
            Undo.RecordObject(socket, "WeaponSocket 을 오른손으로");
            socket.SetLocalPositionAndRotation(localPos, localRot);
            socket.localScale = new Vector3(socketScale.x / handScale.x, socketScale.y / handScale.y, socketScale.z / handScale.z);
            EditorSceneManager.MarkSceneDirty(socket.gameObject.scene);
            Selection.activeTransform = socket;
            return "WeaponSocket 을 오른손 아래로 옮겼습니다. 씬을 저장하세요 (Ctrl+S)";
        }

        private static AnimationClip LoadClip()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) != AimClip) continue;
                return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
            }
            return null;
        }
    }
}
