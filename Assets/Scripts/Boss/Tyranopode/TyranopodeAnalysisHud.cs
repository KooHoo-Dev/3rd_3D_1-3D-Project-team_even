using System;
using System.Collections.Generic;
using HybridShooter.UI.Theme;
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
    ///  - 회피 안내 (촉수 폭풍): 화면 아래쪽에 [ EVADE ] + 점프 키. 촉수가 닿기 직전 JUMP! 가 크게 깜빡이고, 익히면 학습 완료
    /// TyranopodeController 가 만들고 보스와 함께 사라진다.
    /// 스타일은 UiTheme: 배너·회피 안내는 양끝이 흐려지는 띠 + 짧은 붉은 선, 머리글 청회색 · 본문 달빛 흰색, 약점은 호박색 괄호
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
        private const string StormHeader = "[ ANALYSIS ]  TENTACLE EXTENSION  //  SCAN {0:0.0}%";
        private const string StormLine1 = "촉수 연장 감지 — 저공 쓸기";
        private const string StormLine2 = "점프 [{0}] 로 촉수를 뛰어넘어 회피";
        private const string EvadeHeader = "[ EVADE ]  LOW SWEEP INCOMING";
        private const string EvadeLine = "점프 [{0}] — 촉수를 뛰어넘으세요";
        private const string EvadeCue = "JUMP!";
        private const string EvadeLearned = "회피 성공 — 패턴 학습 완료";
        private const float EvadeY = -170f;       // 화면 가운데 아래
        private const float CueTime = 0.5f;
        private const float LearnedTime = 2.2f;
        private const string PopText = "[ IMMUNE ]  0 DMG";
        private const string ReticleLabel = "WEAK POINT  {0:0}m";
        private const string ReticleSub = "포자 핵";

        /// <summary>머리글·부가 줄·IMMUNE 표시 (예전 시안 자리)</summary>
        private static readonly Color Cyan = UiTheme.Steel;
        /// <summary>약점 조준선 (예전 주황 자리)</summary>
        private static readonly Color Amber = UiTheme.Ember;
        private static readonly Color Body = UiTheme.MoonWhite;

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

        private CanvasGroup evade;
        private TextMeshProUGUI evadeLine;
        private TextMeshProUGUI evadeCue;
        private bool evadeVisible;
        private float cueStart = -100f;
        private float learnedStart = -100f;

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

        /// <summary>처음 촉수 폭풍: 요령 배너 (jumpKey = 화면에 보일 키 이름)</summary>
        public void ShowStormHint(string jumpKey)
            => ShowBanner(StormHeader, StormLine1, string.Format(StormLine2, jumpKey), ImmuneBannerY);

        /// <summary>회피 안내를 켠다 (촉수 폭풍 쓸기마다, 점프로 넘길 때까지)</summary>
        public void ShowEvadePrompt(string jumpKey)
        {
            evadeLine.text = string.Format(EvadeLine, jumpKey);
            evadeVisible = true;
            evade.gameObject.SetActive(true);
        }

        public void HideEvadePrompt() => evadeVisible = false;

        /// <summary>촉수가 곧 닿는다: JUMP! 를 크게 깜빡인다</summary>
        public void CueJump() => cueStart = Time.time;

        /// <summary>점프로 넘겼다: 학습 완료를 잠깐 보이고 안내를 끈다</summary>
        public void ShowEvadeLearned()
        {
            learnedStart = Time.time;
            evadeVisible = false;
            evade.gameObject.SetActive(true);
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
            UpdateEvade();
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

        private void UpdateEvade()
        {
            if (evade.gameObject.activeSelf == false) return;
            bool learned = Time.time - learnedStart < LearnedTime;
            float target = evadeVisible || learned ? 1f : 0f;
            evade.alpha = Mathf.MoveTowards(evade.alpha, target, Time.deltaTime * 4f);
            if (target <= 0f && evade.alpha <= 0.01f)
            {
                evade.gameObject.SetActive(false);
                return;
            }

            if (learned)
            {
                evadeLine.text = EvadeLearned;
                evadeLine.color = Cyan;
                evadeCue.gameObject.SetActive(false);
                return;
            }
            evadeLine.color = Body;

            // 촉수가 닿기 직전: 크게 튀어나왔다가 줄며 깜빡인다
            float cue = (Time.time - cueStart) / CueTime;
            bool cueing = cue >= 0f && cue < 1f;
            evadeCue.gameObject.SetActive(cueing);
            if (cueing)
            {
                evadeCue.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.6f, 1f, cue);
                Color c = Body;
                c.a = Mathf.Repeat(Time.time * 12f, 1f) < 0.5f ? 1f : 0.45f;
                evadeCue.color = c;
            }
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
            BuildEvade();
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
            UiThemeKit.Band(rect, UiThemeKit.BandShape.FadeBoth, 0.85f, 0.18f);

            header = Label(rect, tech, 18f, Cyan, new Vector2(0f, -22f), 1f);
            header.characterSpacing = 6f;
            line1 = Label(rect, korean, 30f, Body, new Vector2(0f, -66f), 1f);
            line1.text = BannerLine1;
            line2 = Label(rect, korean, 22f, Cyan, new Vector2(0f, -108f), 1f);
            line2.text = BannerLine2;
            Line(rect, new Vector2(60f, 2f), new Vector2(0f, -140f), UiTheme.Red, new Vector2(0.5f, 1f));

            scanLine = NewRect("ScanLine", rect, new Vector2(640f, 1f));
            scanLine.anchorMin = scanLine.anchorMax = new Vector2(0.5f, 1f);
            scanLine.gameObject.AddComponent<Image>().color = new Color(Cyan.r, Cyan.g, Cyan.b, 0.25f);
            rect.gameObject.SetActive(false);
        }

        private void BuildEvade()
        {
            var rect = NewRect("EvadePrompt", canvasRect, new Vector2(640f, 96f));
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, EvadeY);
            evade = rect.gameObject.AddComponent<CanvasGroup>();
            evade.blocksRaycasts = false;
            UiThemeKit.Band(rect, UiThemeKit.BandShape.FadeBoth, 0.85f, 0.2f);

            // 위험 안내라 머리글만 붉게
            var header = Label(rect, tech, 16f, UiTheme.RedText, new Vector2(0f, -18f), 1f);
            header.text = EvadeHeader;
            header.characterSpacing = 8f;
            evadeLine = Label(rect, korean, 28f, Body, new Vector2(0f, -60f), 1f);
            evadeCue = Label(rect, tech, 64f, Body, new Vector2(0f, 70f), 1f);
            evadeCue.characterSpacing = 16f;
            evadeCue.text = EvadeCue;
            evadeCue.gameObject.SetActive(false);
            evade.alpha = 0f;
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

            reticleLabel = Label(reticle, tech, 16f, Amber, new Vector2(0f, -62f), 0.5f);
            reticleLabel.characterSpacing = 6f;
            var sub = Label(reticle, korean, 18f, Body, new Vector2(0f, -84f), 0.5f);
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

        /// <summary>단색 선. anchor 를 주면 그 기준점(부모 비율)에 둔다 (기본 = 가운데)</summary>
        private static void Line(RectTransform parent, Vector2 size, Vector2 offset, Color color, Vector2? anchor = null)
        {
            var line = NewRect("Line", parent, size);
            if (anchor.HasValue) line.anchorMin = line.anchorMax = anchor.Value;
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
