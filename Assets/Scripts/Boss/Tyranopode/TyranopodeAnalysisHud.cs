using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 사이보그 전투 분석 화면 (포효 소환 중 보스 무적 안내). 플레이어의 시야 장치가 대상을 분석해 띄워 주는 느낌:
    ///  - 분석 배너: 모서리 괄호 틀 + 훑고 지나가는 주사선, 글자가 한 자씩 찍히고 켜질 때 지직거린다
    ///  - [ IMMUNE ] 표시: 무적인 보스를 맞힌 자리에 잠깐 떠서 흔들리다 위로 사라진다
    ///  - 약점 조준선: 공중 포자에 돌아가는 괄호 + 거리. 화면 밖이면 가장자리에 붙어 방향을 가리킨다
    ///  - 등장 이름 카드: 같은 배너 틀로 새 목표·흡수량을 알린다 (등장 연출의 검은 띠 아래쪽에 뜬다)
    /// TyranopodeController 가 만들고 보스와 함께 사라진다
    /// </summary>
    public sealed class TyranopodeAnalysisHud : MonoBehaviour
    {
        private const int CanvasOrder = 850;
        private const int PopCount = 8;
        private const float BannerHold = 4.5f;
        private const float TypeSpeed = 40f;      // 초당 글자
        private const float EdgeMargin = 90f;

        private const float ImmuneBannerY = -120f;
        private const float IntroCardY = -230f;   // 등장 연출의 검은 띠(130) 아래

        private const string BannerHeader = "[ ANALYSIS ]  TARGET : TYRANOPODE  //  SCAN {0:0.0}%";
        private const string BannerLine1 = "외피 경화 감지 — 피해 무효";
        private const string BannerLine2 = "약점 : 공중 포자 핵  >>  파괴 시 경화 해제";
        private const string IntroHeader = "[ ANALYSIS ]  NEW TARGET : TYRANOPODE  //  SCAN {0:0.0}%";
        private const string IntroLine1 = "생체 에너지 흡수 감지 — 위협 등급 상승";
        private const string IntroLine2Shield = "흡수 개체 {0}  >>  외피 생체막 형성 : 막을 먼저 파괴";
        private const string IntroLine2None = "흡수 개체 없음  >>  생체막 미형성";
        private const string PopText = "[ IMMUNE ]  0 DMG";
        private const string ReticleLabel = "WEAK POINT  {0:0}m";
        private const string ReticleSub = "포자 핵";

        private static readonly Color Cyan = new Color(0.35f, 0.95f, 1f, 1f);
        private static readonly Color Amber = new Color(1f, 0.75f, 0.25f, 1f);
        private static readonly Color Panel = new Color(0.02f, 0.08f, 0.1f, 0.6f);

        private sealed class Pop
        {
            public RectTransform Rect;
            public TextMeshProUGUI Text;
            public Vector3 World;
            public float Start;
        }

        private TMP_FontAsset korean;
        private TMP_FontAsset tech;
        private Func<Camera> viewCamera;
        private RectTransform canvasRect;

        private CanvasGroup banner;
        private RectTransform bannerRect;
        private string headerFormat = BannerHeader;
        private TextMeshProUGUI header;
        private TextMeshProUGUI line1;
        private TextMeshProUGUI line2;
        private RectTransform scanLine;
        private float bannerStart = -100f;
        private bool bannerVisible;

        private readonly List<Pop> pops = new List<Pop>();
        private int nextPop;

        private RectTransform reticle;
        private RectTransform reticleFrame;
        private RectTransform reticleArrow;
        private TextMeshProUGUI reticleLabel;
        private Transform weakPoint;

        public static TyranopodeAnalysisHud Create(TMP_FontAsset koreanFont, TMP_FontAsset techFont, Func<Camera> camera)
        {
            var hud = new GameObject("TyranopodeAnalysisHud").AddComponent<TyranopodeAnalysisHud>();
            hud.korean = koreanFont;
            hud.tech = techFont;
            hud.viewCamera = camera;
            hud.Build();
            return hud;
        }

        // ───────────────────────── 바깥에서 부르는 것 ─────────────────────────

        /// <summary>분석 배너를 (다시) 띄운다: 글자가 처음부터 찍힌다</summary>
        public void ShowImmuneBanner() => ShowBanner(BannerHeader, BannerLine1, BannerLine2, ImmuneBannerY);

        /// <summary>등장 이름 카드: 흡수한 몬스터 수와 생체막(보호막)이 생겼는지</summary>
        public void ShowIntroCard(int absorbed, bool shielded)
        {
            string second = shielded ? string.Format(IntroLine2Shield, absorbed) : IntroLine2None;
            ShowBanner(IntroHeader, IntroLine1, second, IntroCardY);
        }

        private void ShowBanner(string headerText, string first, string second, float y)
        {
            headerFormat = headerText;
            line1.text = first;
            line2.text = second;
            bannerRect.anchoredPosition = new Vector2(0f, y);
            bannerStart = Time.time;
            bannerVisible = true;
            banner.gameObject.SetActive(true);
            line1.maxVisibleCharacters = 0;
            line2.maxVisibleCharacters = 0;
        }

        public void HideBanner() => bannerVisible = false;

        /// <summary>무적인 보스를 맞힌 자리에 [ IMMUNE ]</summary>
        public void PopImmune(Vector3 world)
        {
            var pop = pops[nextPop];
            nextPop = (nextPop + 1) % pops.Count;
            pop.World = world;
            pop.Start = Time.time;
            pop.Rect.gameObject.SetActive(true);
        }

        public void TrackWeakPoint(Transform target)
        {
            weakPoint = target;
            reticle.gameObject.SetActive(true);
        }

        public void StopTracking()
        {
            weakPoint = null;
            reticle.gameObject.SetActive(false);
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        private void LateUpdate()
        {
            var cam = viewCamera();
            UpdateBanner();
            UpdatePops(cam);
            UpdateReticle(cam);
        }

        private void UpdateBanner()
        {
            if (banner.gameObject.activeSelf == false) return;
            float t = Time.time - bannerStart;
            bool hold = bannerVisible && t < BannerHold;

            // 켜질 때·꺼질 때 지직거림
            float alpha = hold ? 1f : Mathf.MoveTowards(banner.alpha, 0f, Time.deltaTime * 2.5f);
            if (t < 0.18f || (hold == false && alpha > 0.05f)) alpha *= Random.value < 0.5f ? 0.25f : 1f;
            banner.alpha = alpha;
            if (hold == false && banner.alpha <= 0.01f)
            {
                banner.gameObject.SetActive(false);
                return;
            }

            header.text = string.Format(headerFormat, Mathf.Min(99.9f, 60f + t * 30f + Random.Range(0f, 0.9f)));
            int typed = Mathf.FloorToInt(Mathf.Max(0f, t - 0.15f) * TypeSpeed);
            line1.maxVisibleCharacters = typed;
            line2.maxVisibleCharacters = Mathf.Max(0, typed - line1.text.Length - 6);

            // 주사선: 틀 위에서 아래로 계속 훑는다
            float sweep = Mathf.Repeat(Time.time * 0.9f, 1f);
            scanLine.anchoredPosition = new Vector2(0f, Mathf.Lerp(-6f, -144f, sweep));
        }

        private void UpdatePops(Camera cam)
        {
            foreach (var pop in pops)
            {
                if (pop.Rect.gameObject.activeSelf == false) continue;
                float t = Time.time - pop.Start;
                if (t > 0.8f || TryToCanvas(cam, pop.World, out Vector2 local, out _) == false)
                {
                    pop.Rect.gameObject.SetActive(false);
                    continue;
                }
                // 처음 잠깐 흔들리며 깜빡이고, 이후 위로 올라가며 사라진다
                Vector2 glitch = t < 0.12f ? new Vector2(Random.Range(-7f, 7f), Random.Range(-3f, 3f)) : Vector2.zero;
                pop.Rect.anchoredPosition = local + new Vector2(0f, 30f + t * 70f) + glitch;
                Color color = Cyan;
                color.a = (t < 0.12f && Random.value < 0.4f) ? 0.3f : 1f - Mathf.Clamp01((t - 0.35f) / 0.45f);
                pop.Text.color = color;
            }
        }

        private void UpdateReticle(Camera cam)
        {
            if (weakPoint == null)
            {
                if (reticle.gameObject.activeSelf) StopTracking();
                return;
            }

            bool onScreen = TryToCanvas(cam, weakPoint.position, out Vector2 local, out bool behind);
            Vector2 half = canvasRect.rect.size * 0.5f - new Vector2(EdgeMargin, EdgeMargin);
            if (behind) local = -local;
            bool clamped = onScreen == false || behind || Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y;
            if (clamped)
            {
                // 가장자리에 붙여 방향을 가리킨다
                Vector2 dir = local.sqrMagnitude > 1f ? local.normalized : Vector2.up;
                float scale = Mathf.Min(half.x / Mathf.Max(0.001f, Mathf.Abs(dir.x)), half.y / Mathf.Max(0.001f, Mathf.Abs(dir.y)));
                local = dir * scale;
                reticleArrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
            }
            reticle.anchoredPosition = local;
            reticleArrow.gameObject.SetActive(clamped);

            float pulse = 1f + 0.08f * Mathf.Sin(Time.time * 6f);
            reticleFrame.localRotation = Quaternion.Euler(0f, 0f, Time.time * 40f);
            reticleFrame.localScale = Vector3.one * pulse;
            reticleLabel.text = string.Format(ReticleLabel, Vector3.Distance(cam.transform.position, weakPoint.position));
        }

        /// <summary>월드 위치 → 캔버스 좌표. behind = 카메라 뒤</summary>
        private bool TryToCanvas(Camera cam, Vector3 world, out Vector2 local, out bool behind)
        {
            Vector3 screen = cam.WorldToScreenPoint(world);
            behind = screen.z < 0f;
            bool ok = RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out local);
            return ok && behind == false;
        }

        // ───────────────────────── 화면 만들기 ─────────────────────────

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasOrder;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = (RectTransform)transform;

            BuildBanner();
            for (int i = 0; i < PopCount; i++) pops.Add(BuildPop());
            BuildReticle();
        }

        private void BuildBanner()
        {
            var rect = NewRect("AnalysisBanner", canvasRect, new Vector2(820f, 150f));
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, ImmuneBannerY);
            bannerRect = rect;
            banner = rect.gameObject.AddComponent<CanvasGroup>();
            banner.blocksRaycasts = false;
            rect.gameObject.AddComponent<Image>().color = Panel;
            Corners(rect, 28f, 3f, Cyan);

            header = Label(rect, tech, 20f, Cyan, new Vector2(0f, -22f), 1f);
            line1 = Label(rect, korean, 32f, Amber, new Vector2(0f, -68f), 1f);
            line1.text = BannerLine1;
            line2 = Label(rect, korean, 24f, Cyan, new Vector2(0f, -112f), 1f);
            line2.text = BannerLine2;

            scanLine = NewRect("ScanLine", rect, new Vector2(800f, 2f));
            scanLine.anchorMin = scanLine.anchorMax = new Vector2(0.5f, 1f);
            scanLine.gameObject.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);
            rect.gameObject.SetActive(false);
        }

        private Pop BuildPop()
        {
            var text = Label(canvasRect, tech, 24f, Cyan, Vector2.zero, 0.5f);
            text.text = PopText;
            text.gameObject.SetActive(false);
            return new Pop { Rect = text.rectTransform, Text = text };
        }

        private void BuildReticle()
        {
            reticle = NewRect("WeakPointReticle", canvasRect, new Vector2(110f, 110f));
            reticle.anchorMin = reticle.anchorMax = new Vector2(0.5f, 0.5f);

            reticleFrame = NewRect("Frame", reticle, new Vector2(80f, 80f));
            Corners(reticleFrame, 22f, 3f, Amber);
            var dot = NewRect("Core", reticle, new Vector2(10f, 10f));
            dot.localRotation = Quaternion.Euler(0f, 0f, 45f);
            dot.gameObject.AddComponent<Image>().color = Amber;

            reticleArrow = NewRect("Arrow", reticle, new Vector2(0f, 0f));
            var arrowHead = NewRect("Head", reticleArrow, new Vector2(18f, 18f));
            arrowHead.anchoredPosition = new Vector2(0f, 62f);
            arrowHead.localRotation = Quaternion.Euler(0f, 0f, 45f);
            arrowHead.gameObject.AddComponent<Image>().color = Amber;

            reticleLabel = Label(reticle, tech, 18f, Amber, new Vector2(0f, -62f), 0.5f);
            var sub = Label(reticle, korean, 18f, Cyan, new Vector2(0f, -84f), 0.5f);
            sub.text = ReticleSub;
            reticle.gameObject.SetActive(false);
        }

        /// <summary>틀의 네 모서리에 ㄱ자 괄호</summary>
        private static void Corners(RectTransform parent, float length, float thickness, Color color)
        {
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i % 2, i / 2);
                var anchor = NewRect("Corner", parent, Vector2.zero);
                anchor.anchorMin = anchor.anchorMax = corner;
                Vector2 inward = new Vector2(corner.x == 0f ? 1f : -1f, corner.y == 0f ? 1f : -1f);
                Line(anchor, new Vector2(length, thickness), new Vector2(inward.x * length * 0.5f, 0f), color);
                Line(anchor, new Vector2(thickness, length), new Vector2(0f, inward.y * length * 0.5f), color);
            }
        }

        private static void Line(RectTransform parent, Vector2 size, Vector2 offset, Color color)
        {
            var line = NewRect("Line", parent, size);
            line.anchoredPosition = offset;
            line.gameObject.AddComponent<Image>().color = color;
        }

        /// <summary>글자 하나. anchorY = 부모 기준 세로 기준점 (1 = 위, 0.5 = 가운데)</summary>
        private static TextMeshProUGUI Label(RectTransform parent, TMP_FontAsset font, float size, Color color, Vector2 position, float anchorY)
        {
            var rect = NewRect("Text", parent, new Vector2(800f, size * 1.5f));
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, anchorY);
            rect.anchoredPosition = position;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.characterSpacing = 4f;
            return text;
        }

        private static RectTransform NewRect(string name, Transform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
