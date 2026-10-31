using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 보스 연출(등장·최후)이 함께 쓰는 시네마틱 카메라와 화면 틀.
    ///  - 카메라: 플레이어 카메라 위에 그린다 (depth 100). Place 로 놓으면 흔들림을 더한다
    ///  - 캔버스: 위아래 검은 띠 (SetBars 0~1). 연출 문구는 Canvas 아래에 붙인다
    /// parent 아래에 만들어 parent 가 사라질 때 함께 사라진다
    /// </summary>
    public sealed class TyranopodeCinematicRig
    {
        private const int CanvasOrder = 1000;
        private const float BarHeight = 130f;

        private readonly RectTransform barTop;
        private readonly RectTransform barBottom;
        private float shakeUntil;

        public Camera Camera { get; }
        public RectTransform Canvas { get; }
        /// <summary>흔들림 세기 (연출이 시간에 따라 바꾼다)</summary>
        public float ShakeStrength { get; set; }
        public bool IsShaking => Time.time < shakeUntil;

        public TyranopodeCinematicRig(Transform parent, string name)
        {
            var go = new GameObject(name + "Camera");
            go.transform.SetParent(parent, false);
            Camera = go.AddComponent<Camera>();
            Camera.depth = 100f; // 플레이어 카메라 위에 그린다
            Camera.fieldOfView = 50f;
            Camera.nearClipPlane = 0.3f;
            Camera.farClipPlane = 2000f;
            Camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;

            var canvasGo = new GameObject(name + "Canvas", typeof(RectTransform));
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasOrder;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            canvasGo.AddComponent<GraphicRaycaster>();
            Canvas = (RectTransform)canvasGo.transform;

            barTop = Bar(Canvas, "BarTop", 1f);
            barBottom = Bar(Canvas, "BarBottom", 0f);
        }

        /// <summary>검은 띠 0 = 없음, 1 = 다 내려옴</summary>
        public void SetBars(float amount)
        {
            float h = Mathf.SmoothStep(0f, 1f, amount) * BarHeight;
            barTop.sizeDelta = new Vector2(0f, h);
            barBottom.sizeDelta = new Vector2(0f, h);
        }

        public void Shake(float strength, float duration)
        {
            ShakeStrength = strength;
            shakeUntil = Time.time + duration;
        }

        /// <summary>카메라를 놓는다. 흔들리는 중이면 화면 기준으로 흔든다</summary>
        public void Place(Vector3 position, Quaternion rotation)
        {
            Camera.transform.SetPositionAndRotation(position, rotation);
            if (IsShaking == false) return;
            float seed = Time.time * 18f;
            Vector3 offset = new Vector3(Mathf.PerlinNoise(seed, 0f) - 0.5f, Mathf.PerlinNoise(0f, seed) - 0.5f, 0f) * 2f * ShakeStrength;
            Camera.transform.position += rotation * offset;
        }

        private static RectTransform Bar(RectTransform parent, string barName, float anchorY)
        {
            var go = new GameObject(barName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = Color.black;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, anchorY);
            rect.anchorMax = new Vector2(1f, anchorY);
            rect.pivot = new Vector2(0.5f, anchorY);
            rect.sizeDelta = Vector2.zero;
            return rect;
        }
    }
}
