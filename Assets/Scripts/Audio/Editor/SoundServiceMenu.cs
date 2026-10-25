using UnityEditor;
using UnityEngine;

namespace HybridShooter.Audio.Editor
{
    /// <summary>상단 메뉴 Tools/Audio. 씬에 사운드 오브젝트 만들기</summary>
    internal static class SoundServiceMenu
    {
        [MenuItem("Tools/Audio/Create Sound Service")]
        private static void Create()
        {
            var existing = Object.FindAnyObjectByType<SoundService>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[Audio] 씬에 이미 SoundService 가 있습니다", existing);
                return;
            }

            var go = new GameObject("SoundService", typeof(SoundService), typeof(GameSoundRouter));
            Undo.RegisterCreatedObjectUndo(go, "Create Sound Service");
            Selection.activeGameObject = go;
        }
    }
}
