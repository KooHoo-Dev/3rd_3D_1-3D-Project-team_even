using System;
using System.IO;
using HybridShooter.UI.Options.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HybridShooter.UI.MainMenu.Editor
{
    /// <summary>
    /// 메인 메뉴 미리보기 캡처. 상단 메뉴 Tools/MainMenu/Capture Preview 또는 batchmode
    /// (-executeMethod HybridShooter.UI.MainMenu.Editor.MainMenuPreviewCapture.CaptureBatch -captureDir 폴더).
    /// Test_MainMenu 를 열어 메뉴 카메라 시점으로 두 장을 찍는다 (씬은 저장하지 않는다):
    ///  - MainMenu_Title.png: PRESS ANY KEY 단계 (메뉴 숨김)
    ///  - MainMenu_Menu.png: 메뉴 단계 (GAME START 선택 모습은 아님 — 버튼은 평소 상태)
    /// 오버레이 캔버스는 카메라 렌더에 안 찍히므로 UI 전용 카메라(포스트 프로세싱 없음)로 투명 배경에 따로 찍어 장면 위에 합성한다
    /// (메인 카메라에 붙이면 노출·비네트가 UI 까지 어둡게 해 실제 화면과 달라지고, URP 는 Base 카메라의 '안 지우기'를 무시한다).
    /// UI 셰이더(UI/Default·TMP)는 미리 곱한 알파(Blend One OneMinusSrcAlpha)라 결과 = UI.rgb + 장면.rgb × (1 − UI.a). 파티클은 몇 초 미리 돌려 두고,
    /// 캐릭터는 Animator 를 한 번 돌려 기본 자세(T 포즈) 대신 대기 자세로 찍는다. 끝나면 씬을 저장하지 않고 다시 열어 바꾼 것을 버린다
    /// </summary>
    public static class MainMenuPreviewCapture
    {
        private const int Width = 1920;
        private const int Height = 1080;

        [MenuItem("Tools/MainMenu/Capture Preview")]
        public static void Capture()
        {
            if (Application.isBatchMode == false && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "Temp/MainMenuPreview");
            CaptureTo(dir);
            if (Application.isBatchMode == false) EditorUtility.RevealInFinder(dir);
        }

        /// <summary>batchmode 용. -captureDir 다음 인자가 저장 폴더 (없으면 Temp/MainMenuPreview)</summary>
        public static void CaptureBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-captureDir");
            string dir = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Directory.GetCurrentDirectory(), "Temp/MainMenuPreview");
            CaptureTo(dir);
        }

        private static void CaptureTo(string dir)
        {
            Directory.CreateDirectory(dir);
            EditorSceneManager.OpenScene(OptionsPopupBuilder.TestScenePath, OpenSceneMode.Single);

            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[MainMenuPreviewCapture] Main Camera 가 없습니다");
                return;
            }
            // 에디터에서는 CinemachineBrain 이 돌지 않으므로 메뉴 카메라 자리로 직접 옮긴다
            var menuCamera = UnityEngine.Object.FindAnyObjectByType<MenuCameraSway>();
            if (menuCamera != null) camera.transform.SetPositionAndRotation(menuCamera.transform.position, menuCamera.transform.rotation);

            foreach (var animator in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
            {
                if (animator.runtimeAnimatorController == null) continue;
                // 디스플레이 캐릭터는 CullUpdateTransforms 라 에디터 캡처에서는 자세가 안 바뀐다
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Update(0.5f);
            }

            foreach (var particles in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                if (particles.transform.parent == null || particles.transform.parent.GetComponent<ParticleSystem>() == null)
                    particles.Simulate(12f, true, true);

            var canvas = GameObject.Find("Canvas")?.GetComponent<Canvas>();
            Camera uiCamera = null;
            if (canvas != null)
            {
                const int uiLayer = 5;
                foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = uiLayer;
                camera.cullingMask &= ~(1 << uiLayer);

                uiCamera = new GameObject("PreviewUICamera").AddComponent<Camera>();
                uiCamera.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
                uiCamera.fieldOfView = camera.fieldOfView;
                uiCamera.cullingMask = 1 << uiLayer;
                uiCamera.clearFlags = CameraClearFlags.SolidColor;
                uiCamera.backgroundColor = Color.clear; // 투명 배경 → 알파로 합성
                uiCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = uiCamera;
                canvas.planeDistance = 1f; // 너무 가까우면 캔버스 크기가 작아져 TMP 글자가 사라진다
            }
            var pressAnyKey = canvas != null ? canvas.transform.Find("PressAnyKey") : null;
            var menu = canvas != null ? canvas.transform.Find("Menu") : null;
            var popup = canvas != null ? canvas.GetComponentInChildren<Options.OptionsPopup>(true) : null;
            if (popup != null) popup.gameObject.SetActive(false);

            SetActive(pressAnyKey, true);
            SetActive(menu, false);
            Shoot(camera, uiCamera, Path.Combine(dir, "MainMenu_Title.png"));

            SetActive(pressAnyKey, false);
            SetActive(menu, true);
            Shoot(camera, uiCamera, Path.Combine(dir, "MainMenu_Menu.png"));

            // 캔버스 모드·자세 등 캡처용으로 바꾼 것은 저장하지 않고 버린다
            EditorSceneManager.OpenScene(OptionsPopupBuilder.TestScenePath, OpenSceneMode.Single);
            Debug.Log($"[MainMenuPreviewCapture] {dir} 에 저장했습니다");
        }

        private static void SetActive(Transform t, bool active)
        {
            if (t != null) t.gameObject.SetActive(active);
        }

        private static void Shoot(Camera camera, Camera uiCamera, string path)
        {
            foreach (var text in UnityEngine.Object.FindObjectsByType<TMP_Text>(FindObjectsSortMode.None)) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();

            var scene = Render(camera);
            if (uiCamera != null)
            {
                var ui = Render(uiCamera);
                var a = scene.GetPixels32();
                var b = ui.GetPixels32();
                for (int i = 0; i < a.Length; i++)
                {
                    float keep = 1f - b[i].a / 255f;
                    a[i] = new Color32(
                        (byte)Mathf.Min(255, b[i].r + a[i].r * keep),
                        (byte)Mathf.Min(255, b[i].g + a[i].g * keep),
                        (byte)Mathf.Min(255, b[i].b + a[i].b * keep), 255);
                }
                scene.SetPixels32(a);
                scene.Apply();
                UnityEngine.Object.DestroyImmediate(ui);
            }

            File.WriteAllBytes(path, scene.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(scene);
        }

        /// <summary>카메라 하나를 화면 크기 텍스처로 (알파 포함)</summary>
        private static Texture2D Render(Camera camera)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            camera.targetTexture = previous;

            var active = RenderTexture.active;
            RenderTexture.active = rt;
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            texture.Apply();
            RenderTexture.active = active;

            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return texture;
        }
    }
}
