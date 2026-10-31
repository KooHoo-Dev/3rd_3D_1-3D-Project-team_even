using Cysharp.Threading.Tasks;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using HybridShooter.UI.MainMenu;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace HybridShooter.UI.Intro
{
    /// <summary>
    /// 게임 시작 인트로 (메인 메뉴 GAME START → 이 씬 → 게임 씬). 스타워즈식 원근 크롤 + 우주선 격추 컷.
    /// 흐름 (초, BGM intro_bgm.py 75BPM 의 마디 경계와 맞춤 — 1마디 3.2초)
    ///   0.0  검은 화면 → 1.0 푸른 도입 문구 → 5.6 사라짐
    ///   6.4  BGM 첫 타격과 함께 별하늘로 컷 + 큰 로고가 멀어지며 작아짐 (마디 2)
    ///   7.0  크롤 시작. 바닥 쪽으로 눕힌 월드 캔버스 위에서 글이 위로 흘러가며 멀어진다. 먼 쪽은 글자마다 알파로 사라진다
    ///  57.6  크롤이 끝나며 카메라가 아래로 내려가 행성을 비춘다 (마디 18 빌드업 → 마디 20 행성)
    ///  64.0  우주선 컷 (IntroShipCut) → 70.4 격추 (마디 22) → 75.6 암전 → 76.8 종지 화음과 함께 게임 씬 로드
    /// 화면 상태는 시각 t 하나로 계산한다(Evaluate) → 건너뛰기·에디터 미리보기(IntroPreviewCapture)에서도 같은 장면.
    /// 크롤 속도는 글 길이에 맞춰 자동: 마지막 줄이 crawlEnd 에 사라지기 시작하도록.
    /// 건너뛰기: 아무 키(마우스·패드 버튼)나 길게 누른다 (실수로 넘기지 않게). 한 번 본 뒤에는 안내가 처음부터 보인다.
    /// 씬 구성은 Tools > Intro > Rebuild Intro Scene (IntroSceneBuilder)
    /// </summary>
    public sealed class IntroCrawlDirector : MonoBehaviour
    {
        private const string SeenKey = "Intro.Seen";
        /// <summary>월드 캔버스 1px = 0.01m</summary>
        private const float PixelToMeter = 0.01f;

        [SerializeField] private IntroCrawlText content;

        [Header("화면")]
        [SerializeField] private Transform viewCamera;
        [SerializeField] private CanvasGroup openingGroup;
        [SerializeField] private TMP_Text openingText;
        [SerializeField] private TMP_Text logoText;
        [SerializeField] private RectTransform crawlRect;
        [SerializeField] private TMP_Text crawlText;
        [SerializeField] private Graphic blackout;
        [SerializeField] private Graphic flash;
        [SerializeField] private IntroShipCut shipCut;

        [Header("타이밍(초)")]
        [SerializeField] private float openingIn = 1f;
        [SerializeField] private float openingOut = 4.8f;
        [SerializeField] private float openingFade = 0.8f;
        [Tooltip("BGM 첫 타격 — 검은 화면이 별하늘로 바뀌고 로고가 나온다")]
        [SerializeField] private float logoTime = 6.4f;
        [SerializeField] private float logoDuration = 8f;
        [SerializeField] private float crawlStart = 7f;
        [Tooltip("이 시각에 마지막 줄이 사라지기 시작한다")]
        [SerializeField] private float crawlEnd = 57.6f;
        [SerializeField] private float tiltStart = 57.6f;
        [SerializeField] private float tiltEnd = 64f;
        [SerializeField] private float endFadeStart = 75.6f;
        [SerializeField] private float endTime = 76.8f;

        [Header("로고")]
        [SerializeField] private float logoStartDistance = 3.2f;
        [Tooltip("거리 = 시작 거리 × e^(rate × 경과 초) → 일정한 비율로 작아진다")]
        [SerializeField] private float logoRecedeRate = 0.42f;

        [Header("크롤 (캔버스 면을 따라 잰 거리, m)")]
        [Tooltip("글 첫 줄이 화면 아래 밖에서 출발하는 거리")]
        [SerializeField] private float startBelow = 1.5f;
        [SerializeField] private float fadeStart = 17f;
        [SerializeField] private float fadeEnd = 25f;

        [Header("카메라")]
        [Tooltip("크롤이 끝난 뒤 아래로 숙이는 각도")]
        [SerializeField] private float tiltAngle = 45f;

        [Header("건너뛰기")]
        [SerializeField] private CanvasGroup skipGroup;
        [SerializeField] private RectTransform skipBar;
        [SerializeField] private float skipHoldTime = 1f;
        [Tooltip("입력이 없으면 안내를 숨기기까지 (처음 보는 경우)")]
        [SerializeField] private float skipPromptTime = 3f;
        [SerializeField] private float skipFadeTime = 0.4f;

        [Header("다음 씬")]
        [SerializeField] private string nextSceneName = "Monster Player Integration";
        [Tooltip("BGM 페이드아웃(SoundService 1.5초)을 기다리는 시간")]
        [SerializeField] private float loadMinWait = 1.6f;
        [SerializeField] private string bgmKey = "bgm_intro";

        private Quaternion baseRotation;
        private bool prepared;
        private float crawlSpeed;
        private float time;
        private float hold;
        private float promptUntil;
        private float skipFade;
        private bool skipping;
        private bool loading;
        private bool seen;

        private void Awake() => Prepare();

        private void Start()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            seen = PlayerPrefs.GetInt(SeenKey, 0) == 1;
            if (ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlayBgm(bgmKey);
            Evaluate(0f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (loading == false) UpdateSkip(dt);

            if (skipping)
            {
                skipFade = Mathf.MoveTowards(skipFade, 1f, dt / skipFadeTime);
                if (skipFade >= 1f) Load();
            }
            else
            {
                time += dt;
                if (time >= endTime) Load();
            }
            Evaluate(time);
        }

        /// <summary>텍스트·카메라 기준값 준비. 에디터 미리보기에서도 부른다</summary>
        public void Prepare()
        {
            if (prepared) return;
            prepared = true;
            baseRotation = viewCamera.localRotation;
            openingText.text = content.openingLine;
            logoText.text = content.logo;
            crawlText.text = content.BuildCrawl();
            crawlText.ForceMeshUpdate();

            // 마지막 줄(글 아래 끝)이 crawlEnd 에 fadeStart 에 닿는 속도
            float height = crawlText.preferredHeight * PixelToMeter;
            crawlSpeed = (startBelow + height + fadeStart) / Mathf.Max(1f, crawlEnd - crawlStart);
        }

        /// <summary>시각 t 의 화면. 에디터 미리보기용으로 공개</summary>
        public void Evaluate(float t)
        {
            Prepare();

            openingGroup.alpha = Mathf.Min(Ramp(t, openingIn, openingFade), 1f - Ramp(t, openingOut, openingFade));
            EvaluateLogo(t);
            EvaluateCrawl(t);

            float tilt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(tiltStart, tiltEnd, t)) * tiltAngle;
            viewCamera.localRotation = baseRotation * Quaternion.Euler(tilt, 0f, 0f) * shipCut.Shake(t);
            shipCut.Evaluate(t, viewCamera);

            float black = t < logoTime ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(endFadeStart, endTime, t));
            SetAlpha(blackout, Mathf.Max(black, skipFade));
            SetAlpha(flash, shipCut.FlashAmount(t));
        }

        // ───────────────────────── 장면 ─────────────────────────

        private void EvaluateLogo(float t)
        {
            float elapsed = t - logoTime;
            bool visible = elapsed >= 0f && elapsed <= logoDuration;
            logoText.gameObject.SetActive(visible);
            if (visible == false) return;

            var position = logoText.transform.localPosition;
            position.z = logoStartDistance * Mathf.Exp(logoRecedeRate * elapsed);
            logoText.transform.localPosition = position;
            logoText.alpha = 1f - Ramp(t, logoTime + logoDuration - 2f, 2f);
        }

        private void EvaluateCrawl(float t)
        {
            // 캔버스 원점 = 화면 아래 끝. 글 위 끝이 startBelow 만큼 아래에서 출발
            float top = (-startBelow + crawlSpeed * Mathf.Max(0f, t - crawlStart)) / PixelToMeter;
            crawlRect.anchoredPosition = new Vector2(0f, top);
            crawlText.enabled = t >= crawlStart && t < tiltEnd;
            if (crawlText.enabled) FadeCrawl(top);
        }

        /// <summary>글자마다 캔버스 면을 따라 잰 거리로 알파 (먼 쪽에서 사라진다)</summary>
        private void FadeCrawl(float top)
        {
            var info = crawlText.textInfo;
            for (int i = 0; i < info.characterCount; i++)
            {
                var c = info.characterInfo[i];
                if (c.isVisible == false) continue;
                float distance = (top + (c.ascender + c.descender) * 0.5f) * PixelToMeter;
                byte alpha = (byte)(255f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, fadeEnd, distance))));
                var colors = info.meshInfo[c.materialReferenceIndex].colors32;
                for (int v = 0; v < 4; v++) colors[c.vertexIndex + v].a = alpha;
            }
            crawlText.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }

        // ───────────────────────── 건너뛰기·종료 ─────────────────────────

        private void UpdateSkip(float dt)
        {
            if (AnyPressedThisFrame()) promptUntil = Time.unscaledTime + skipPromptTime;
            bool held = AnyHeld();
            hold = held ? hold + dt : Mathf.Max(0f, hold - dt * 2f);
            if (hold >= skipHoldTime) skipping = true;

            bool show = seen || held || Time.unscaledTime < promptUntil;
            skipGroup.alpha = Mathf.MoveTowards(skipGroup.alpha, show && time > 0.5f ? 1f : 0f, dt * 4f);
            skipBar.localScale = new Vector3(Mathf.Clamp01(hold / skipHoldTime), 1f, 1f);
        }

        private static bool AnyHeld()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            return (keyboard != null && keyboard.anyKey.isPressed)
                || (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed))
                || (pad != null && (pad.buttonSouth.isPressed || pad.buttonEast.isPressed || pad.startButton.isPressed));
        }

        private static bool AnyPressedThisFrame()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            var pad = Gamepad.current;
            return (keyboard != null && keyboard.anyKey.wasPressedThisFrame)
                || (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame))
                || (pad != null && (pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame));
        }

        private void Load()
        {
            if (loading) return;
            loading = true;
            skipGroup.alpha = 0f;
            PlayerPrefs.SetInt(SeenKey, 1);
            PlayerPrefs.Save();
            SceneTransition.LoadAsync(nextSceneName, loadMinWait, destroyCancellationToken).Forget();
        }

        // ───────────────────────── 공통 ─────────────────────────

        /// <summary>start 부터 length 동안 0 → 1</summary>
        private static float Ramp(float t, float start, float length) => Mathf.Clamp01((t - start) / length);

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
