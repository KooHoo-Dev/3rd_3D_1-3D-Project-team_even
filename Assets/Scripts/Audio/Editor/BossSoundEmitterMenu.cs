using UnityEditor;
using UnityEngine;

namespace HybridShooter.Audio.Editor
{
    /// <summary>
    /// Tools/Audio/Add Boss Sound Emitter.
    /// 보스 프리팹에 BossSoundEmitter 를 붙이고 패턴별 기본 키를 채운다 (BossSoundEmitter.FillDefaults).
    /// 이미 붙어 있으면 그대로 둔다 — 키를 다시 채우려면 Inspector 의 컴포넌트 메뉴에서 Reset
    /// </summary>
    internal static class BossSoundEmitterMenu
    {
        internal const string BossPrefabPath = "Assets/Resources/Boss/Clypeosaurus/Prefab/Clypeosaurus.prefab";

        [MenuItem("Tools/Audio/Add Boss Sound Emitter")]
        private static void AddFromMenu()
        {
            var prefab = EnsureEmitter();
            if (prefab != null) Selection.activeObject = prefab;
        }

        /// <summary>보스 프리팹에 이미터가 없으면 붙여 저장한다. 보스 프리팹을 돌려준다 (프리팹이 없으면 null)</summary>
        internal static GameObject EnsureEmitter()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[Audio] 보스 프리팹이 없습니다: {BossPrefabPath}");
                return null;
            }
            if (prefab.GetComponent<BossSoundEmitter>() != null) return prefab;

            var root = PrefabUtility.LoadPrefabContents(BossPrefabPath);
            try
            {
                root.AddComponent<BossSoundEmitter>().FillDefaults();
                PrefabUtility.SaveAsPrefabAsset(root, BossPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log($"[Audio] {BossPrefabPath} 에 BossSoundEmitter 추가 (패턴별 기본 키)");
            return AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        }
    }
}
