using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridShooter.Core.GameFlow;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HybridShooter.UI.Ending.Editor
{
    /// <summary>
    /// 메인 메뉴 씬(Build Settings 의 Test_MainMenu)에 '클리어 뒤 새벽' 버전을 만든다 (Tools > Ending > Rebuild Menu Cleared Variant).
    /// 디오라마(MainMenuDioramaBuilder)와 별도 최상위 루트 ClearedVariant 에 만들어, 디오라마를 다시 만들어도 지워지지 않는다
    /// (대신 끄는 대상 참조가 끊기므로 이 메뉴를 다시 실행 — 실행 시 MenuClearedVariant 가 빈 참조를 바로 알린다).
    ///  - 끄는 것: 몬스터 무리·원경 보스·추락한 우주선·달빛·불씨·원경 섬광
    ///  - 켜는 것: 새벽 해(달이 있던 자리에 떠오른다)·새벽 볼륨·착륙한 구조선(추락선 자리, 같은 크기)과 해치 빛
    /// 확인: Tools > Ending > 클리어 기록 설정 → 플레이
    /// </summary>
    public static class MenuClearedVariantBuilder
    {
        private const string MenuSceneName = "Test_MainMenu";
        private const string RootName = "ClearedVariant";

        /// <summary>클리어 뒤 끄는 디오라마 오브젝트 (씬 최상위부터의 경로)</summary>
        private static readonly string[] HiddenPaths =
        {
            "Diorama/Crowd", "Diorama/Clypeosaurus_Display", "Diorama/Spaceship_Broken",
            "Environment/Moonlight", "Environment/Atmosphere/Embers", "Environment/Atmosphere/DistantFlash",
        };
        private const string BrokenShipPath = "Diorama/Spaceship_Broken";

        /// <summary>새벽 해: 달빛(12, 194)과 같은 방위, 더 낮게 → 화면에서 달이 있던 쪽 지평선 위로 떠오른다</summary>
        private static readonly Quaternion SunRotation = Quaternion.Euler(7f, 194f, 0f);
        /// <summary>구조선 방향 (추락선은 비스듬히 카메라를 등졌다 → 착륙선은 기수를 카메라 쪽으로 살짝)</summary>
        private const float ShipYaw = 205f;

        [MenuItem("Tools/Ending/Rebuild Menu Cleared Variant")]
        public static void Rebuild()
        {
            if (Application.isBatchMode == false && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            var settings = EditorBuildSettings.scenes.FirstOrDefault(s => Path.GetFileNameWithoutExtension(s.path) == MenuSceneName);
            if (settings == null) throw new FileNotFoundException("[MenuClearedVariantBuilder] Build Settings 에 메인 메뉴 씬이 없습니다", MenuSceneName);
            var scene = EditorSceneManager.OpenScene(settings.path, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            foreach (var old in roots.Where(r => r.name == RootName)) Object.DestroyImmediate(old);

            var hidden = new List<GameObject>();
            foreach (string path in HiddenPaths) hidden.Add(Find(roots, path));
            var broken = Find(roots, BrokenShipPath);

            var root = new GameObject(RootName, typeof(MenuClearedVariant));
            var dawn = new GameObject("Dawn").transform;
            dawn.SetParent(root.transform, false);
            var sun = EndingDawnLook.CreateSun(dawn, SunRotation);
            EndingDawnLook.CreateVolume(dawn, EndingDawnLook.MenuVolumeProfilePath);
            CreateRescueShip(dawn, broken);

            var so = new SerializedObject(root.GetComponent<MenuClearedVariant>());
            var array = so.FindProperty("hideWhenCleared");
            array.arraySize = hidden.Count;
            for (int i = 0; i < hidden.Count; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = hidden[i];
            so.FindProperty("dawnRoot").objectReferenceValue = dawn.gameObject;
            var env = EndingDawnLook.Environment(sun);
            so.FindProperty("dawn.Skybox").objectReferenceValue = env.Skybox;
            so.FindProperty("dawn.Sun").objectReferenceValue = env.Sun;
            so.FindProperty("dawn.Fog").boolValue = env.Fog;
            so.FindProperty("dawn.FogColor").colorValue = env.FogColor;
            so.FindProperty("dawn.FogDensity").floatValue = env.FogDensity;
            so.FindProperty("dawn.AmbientSky").colorValue = env.AmbientSky;
            so.FindProperty("dawn.AmbientEquator").colorValue = env.AmbientEquator;
            so.FindProperty("dawn.AmbientGround").colorValue = env.AmbientGround;
            so.ApplyModifiedPropertiesWithoutUndo();

            dawn.gameObject.SetActive(false); // 평소(클리어 전)는 밤 그대로 — MenuClearedVariant 가 실행 시 켠다
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[MenuClearedVariantBuilder] {settings.path} 에 {RootName} 를 만들었습니다. 'Tools > Ending > 클리어 기록 설정' 뒤 플레이하면 새벽 메뉴를 볼 수 있습니다");
        }

        /// <summary>추락선 자리에 같은 크기로, 땅에 내려앉은 구조선 + 해치에서 새어 나오는 따뜻한 빛</summary>
        private static void CreateRescueShip(Transform parent, GameObject broken)
        {
            var brokenBounds = EndingDawnLook.ModelBounds(broken);
            var ship = new GameObject("Rescue Ship").transform;
            ship.SetParent(parent, false);
            var size = EndingDawnLook.AddShipModel(ship, Mathf.Max(brokenBounds.size.x, brokenBounds.size.z));
            ship.position = new Vector3(brokenBounds.center.x, size.y * 0.5f + 0.3f, brokenBounds.center.z);
            ship.rotation = Quaternion.Euler(0f, ShipYaw, 0f);

            var hatch = new GameObject("Hatch Light", typeof(Light)).GetComponent<Light>();
            hatch.transform.SetParent(ship, false);
            hatch.transform.localPosition = new Vector3(0f, -size.y * 0.35f, -size.z * 0.18f);
            hatch.type = LightType.Point;
            hatch.color = new Color(1f, 0.82f, 0.6f);
            hatch.range = size.z * 0.4f;
            hatch.intensity = 6f;
        }

        private static GameObject Find(GameObject[] roots, string path)
        {
            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash);
            var root = roots.FirstOrDefault(r => r.name == rootName);
            var found = root == null ? null : slash < 0 ? root.transform : root.transform.Find(path.Substring(slash + 1));
            if (found == null) throw new FileNotFoundException("[MenuClearedVariantBuilder] 메인 메뉴 씬에 오브젝트가 없습니다 (디오라마 구성이 바뀌었으면 HiddenPaths 를 고친다)", path);
            return found.gameObject;
        }

        // ───────────────────────── 클리어 기록 (확인용) ─────────────────────────

        [MenuItem("Tools/Ending/클리어 기록 설정 (새벽 메뉴 보기)")]
        private static void MarkCleared()
        {
            GameClearRecord.MarkCleared();
            Debug.Log("[Ending] 클리어 기록을 켰습니다 — 메인 메뉴가 새벽 버전으로 시작합니다");
        }

        [MenuItem("Tools/Ending/클리어 기록 초기화")]
        private static void ResetRecord()
        {
            GameClearRecord.Reset();
            Debug.Log("[Ending] 클리어 기록을 지웠습니다 — 메인 메뉴가 밤 버전으로 시작합니다");
        }
    }
}
