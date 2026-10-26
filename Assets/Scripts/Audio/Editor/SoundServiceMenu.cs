using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace HybridShooter.Audio.Editor
{
    /// <summary>상단 메뉴 Tools/Audio. 씬에 사운드 오브젝트 만들기</summary>
    internal static class SoundServiceMenu
    {
        private const string MixerPath = "Assets/Resources/Audio/GameMixer.mixer";

        /// <summary>SoundService 를 만들고 GameMixer 를 연결한다. 이미 있으면 믹서가 비었을 때만 연결</summary>
        [MenuItem("Tools/Audio/Create Sound Service")]
        private static void Create()
        {
            var existing = Object.FindAnyObjectByType<SoundService>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                if (AssignMixer(existing)) Debug.Log("[Audio] 기존 SoundService 에 GameMixer 를 연결했습니다", existing);
                else Debug.Log("[Audio] 씬에 이미 SoundService 가 있습니다", existing);
                return;
            }

            var go = new GameObject("SoundService", typeof(SoundService), typeof(GameSoundRouter));
            Undo.RegisterCreatedObjectUndo(go, "Create Sound Service");
            AssignMixer(go.GetComponent<SoundService>());
            Selection.activeGameObject = go;
        }

        /// <summary>mixer 칸이 비어 있으면 GameMixer 를 넣는다. 그룹은 실행 시 SoundService 가 이름으로 찾는다</summary>
        private static bool AssignMixer(SoundService service)
        {
            var so = new SerializedObject(service);
            var property = so.FindProperty("mixer");
            if (property.objectReferenceValue != null) return false;

            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                Debug.LogWarning($"[Audio] {MixerPath} 가 없습니다. 믹서 없이 동작합니다", service);
                return false;
            }
            property.objectReferenceValue = mixer;
            so.ApplyModifiedProperties(); // Undo 기록 + 씬 dirty
            return true;
        }
    }
}
