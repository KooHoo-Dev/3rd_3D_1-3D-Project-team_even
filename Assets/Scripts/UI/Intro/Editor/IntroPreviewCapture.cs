using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HybridShooter.UI.Intro.Editor
{
    /// <summary>
    /// 인트로 미리보기 캡처. 상단 메뉴 Tools/Intro/Capture Preview 또는 batchmode
    /// (-executeMethod HybridShooter.UI.Intro.Editor.IntroPreviewCapture.CaptureBatch -captureDir 폴더).
    /// IntroCrawl 을 열어 Times 의 시각마다 IntroCrawlDirector.Evaluate 로 장면을 맞추고 찍는다 (Intro_06.4s.png …).
    /// 화면 UI(암전·도입 문구·섬광)도 보이도록 오버레이 캔버스를 카메라 캔버스로 바꿔 함께 찍는다 → 포스트 프로세싱이 UI 에도 걸려 실제보다 조금 어둡다.
    /// 끝나면 씬을 저장하지 않고 다시 열어 바꾼 것을 버린다
    /// </summary>
    public static class IntroPreviewCapture
    {
        private const int Width = 1920;
        private const int Height = 1080;
        private static readonly float[] Times = { 3f, 6.6f, 9f, 16f, 30f, 48f, 60f, 64.8f, 68.2f, 70.45f, 72.5f, 75.4f };

        [MenuItem("Tools/Intro/Capture Preview")]
        public static void Capture()
        {
            if (Application.isBatchMode == false && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp/IntroPreview");
            CaptureTo(dir);
            if (Application.isBatchMode == false) EditorUtility.RevealInFinder(dir);
        }

        /// <summary>batchmode 용. -captureDir 다음 인자가 저장 폴더 (없으면 Temp/IntroPreview)</summary>
        public static void CaptureBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-captureDir");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Directory.GetCurrentDirectory(), "Temp/IntroPreview");
            CaptureTo(dir);
        }

        private static void CaptureTo(string dir)
        {
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(IntroSceneBuilder.ScenePath, OpenSceneMode.Single);
            var director = UnityEngine.Object.FindAnyObjectByType<IntroCrawlDirector>();
            var camera = Camera.main;
            if (director == null || camera == null)
            {
                Debug.LogError("[IntroPreviewCapture] IntroCrawlDirector 또는 Main Camera 가 없습니다");
                return;
            }

            var overlay = GameObject.Find("Overlay")?.GetComponent<Canvas>();
            if (overlay != null)
            {
                overlay.renderMode = RenderMode.ScreenSpaceCamera;
                overlay.worldCamera = camera;
                overlay.planeDistance = 1f;
            }

            foreach (float t in Times)
            {
                foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) text.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                director.Evaluate(t);
                Shoot(camera, Path.Combine(dir, $"Intro_{t:00.0}s.png"));
            }

            EditorSceneManager.OpenScene(IntroSceneBuilder.ScenePath, OpenSceneMode.Single);
            Debug.Log($"[IntroPreviewCapture] {dir} 에 저장했습니다");
        }

        private static void Shoot(Camera camera, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previous;

            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = active;

            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
