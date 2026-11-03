using Cysharp.Threading.Tasks;
using HybridShooter.UI.MainMenu;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.UI;

namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 엔딩 (최종 보스 최후 연출 → 이 씬 → 메인 메뉴). 컷 구성·카메라·우주선 경로·음악은 Timeline(PlayableDirector)이 맡고,
    /// 이 컴포넌트는 Timeline 시각을 읽어 Timeline 이 못 하는 것만 한다 (시각은 EndingTimeline, 씬은 Tools > Ending > Rebuild Ending Scene)
    ///   0  새벽 착륙장 — 플레이어가 구조선 경사로를 걸어 올라 해치 빛 속으로 (걷기 애니메이션 파라미터)
    ///  12  점화 → 18 이륙 → 24 우주(하늘·안개·환경광을 우주로) → 30 해돋이와 함께 궤도 돌파 → 36 도약 섬광
    ///  39  크레딧 (검은 띠가 걷힌다) → 51 행성에 금빛 균열 → 63 행성 폭발(EndingPlanetShatter) → 75 로고 → 84 암전 → 메인 메뉴
    ///      (PRESS ANY KEY 부터 — 새벽으로 바뀐 메뉴를 보여 준다. 메뉴의 새벽은 '떠나던 날 아침')
    /// 건너뛰기: 아무 키나 길게 누르면 ("정말 스킵할거야?") 건너뛰는 대신 EndingSkipPrank 의 질문창이 뜬다 — 엔딩이 느려지거나, 끝내 창이 폭발한다
    /// </summary>
    [RequireComponent(typeof(PlayableDirector))]
    public sealed class EndingDirector : MonoBehaviour
    {
        [SerializeField] private PlayableDirector timeline;

        [Header("환경 (EndingTimeline.Space 에서 바뀐다. 조명·볼륨은 각 장면 루트 아래 — Timeline 이 켜고 끈다)")]
        [SerializeField] private SceneEnvironment dawn;
        [SerializeField] private SceneEnvironment space;

        [Header("플레이어 (탑승)")]
        [SerializeField] private DisplayActor player;
        [Tooltip("Player_AC 의 앞뒤 이동 파라미터")]
        [SerializeField] private string moveParameter = "MoveY";
        [Tooltip("걷는 동안 파라미터 값 (경로를 걷는 속도와 맞춘 값)")]
        [SerializeField] private float walkValue = 1.1f;
        [SerializeField] private float walkStart = 0.8f;

        [Header("화면")]
        [SerializeField] private EndingCredits credits;
        [Tooltip("크레딧 중 행성 균열 → 폭발")]
        [SerializeField] private EndingPlanetShatter planetShatter;
        [SerializeField] private Graphic blackout;
        [SerializeField] private Graphic flash;
        [Tooltip("위아래 검은 띠 (최후 연출에서 이어진다). 크레딧에서 걷힌다")]
        [SerializeField] private RectTransform barTop;
        [SerializeField] private RectTransform barBottom;
        [SerializeField] private float barHeight = 130f;
        [Tooltip("검은 화면에서 밝아지는 시간 (최후 연출의 암전에서 이어진다)")]
        [SerializeField] private float openFade = 2f;
        [Tooltip("EndingTimeline.End 직전 암전 시간")]
        [SerializeField] private float endFade = 3f;

        [Header("건너뛰기")]
        [SerializeField] private CanvasGroup skipGroup;
        [SerializeField] private RectTransform skipBar;
        [SerializeField] private float skipHoldTime = 1f;
        [Tooltip("입력이 없으면 안내를 숨기기까지")]
        [SerializeField] private float skipPromptTime = 3f;
        [Tooltip("게이지가 다 차면 건너뛰는 대신 뜨는 질문창")]
        [SerializeField] private EndingSkipPrank prank;

        [Header("음악 (Timeline 오디오 트랙이 재생, 믹서 Bgm 그룹)")]
        [SerializeField] private AudioSource music;

        [Header("다음 씬")]
        [SerializeField] private string mainMenuScene = "Test_MainMenu";
        [Tooltip("BGM·암전 페이드를 기다리는 시간")]
        [SerializeField] private float loadMinWait = 1.6f;

        private bool inSpace;
        private bool environmentApplied;
        private float hold;
        private float promptUntil;
        /// <summary>질문창을 닫은 뒤 모든 키를 뗄 때까지 게이지를 받지 않는다 (선택에 쓴 키로 곧바로 다시 차지 않게)</summary>
        private bool waitRelease;
        /// <summary>메인 메뉴로 넘어갈 때의 암전 (0~1)</summary>
        private float leaveFade;
        private bool leaving;

        private void Start()
        {
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
            // 느려지기는 EndingSkipPrank 가 timeScale 과 Timeline 루트 속도를 함께 바꿔서 한다 — GameTime 이면 두 번 느려진다
            timeline.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
            timeline.time = 0d;
            timeline.Play();
            Evaluate(0f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float t = (float)timeline.time;

            if (leaving)
            {
                leaveFade = Mathf.MoveTowards(leaveFade, 1f, dt / loadMinWait);
                music.volume = 1f - leaveFade;
            }
            else
            {
                UpdateSkip(dt);
                if (t >= EndingTimeline.End) Leave();
            }
            Evaluate(t);
        }

        /// <summary>시각 t 의 화면 (Timeline 이 맡지 않는 것: 하늘·환경광, 걷기 파라미터, 크레딧, 행성 붕괴, 검은 띠, 암전·섬광)</summary>
        public void Evaluate(float t)
        {
            ApplyEnvironment(t >= EndingTimeline.Space);

            // 플레이어는 해치로 사라진 뒤 Timeline 이 꺼 둔다 (꺼진 Animator 는 파라미터를 받지 않는다)
            if (player.isActiveAndEnabled)
            {
                bool walking = t >= walkStart && t < EndingTimeline.BoardEnd;
                player.SetFloat(moveParameter, Mathf.MoveTowards(player.GetFloat(moveParameter), walking ? walkValue : 0f, Time.unscaledDeltaTime * 3f));
            }

            credits.Evaluate(t);
            planetShatter.Evaluate(t);

            // 검은 띠: 처음부터 내려와 있다가 크레딧에서 걷힌다
            float bars = 1f - Mathf.SmoothStep(0f, 1f, (t - EndingTimeline.Credits) / 1.2f);
            barTop.sizeDelta = new Vector2(0f, bars * barHeight);
            barBottom.sizeDelta = new Vector2(0f, bars * barHeight);

            float black = Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, t / openFade),
                                    Mathf.SmoothStep(0f, 1f, (t - (EndingTimeline.End - endFade)) / endFade));
            SetAlpha(blackout, Mathf.Max(black, leaveFade));
            SetAlpha(flash, FlashAmount(t));
        }

        /// <summary>도약 섬광: Jump 에 번쩍 → 0.9초에 걸쳐 사라진다. 크레딧 직전엔 다시 잠깐 어두워져 컷이 바뀐다</summary>
        private static float FlashAmount(float t)
        {
            if (t < EndingTimeline.Jump - 0.15f) return 0f;
            if (t < EndingTimeline.Jump) return Mathf.InverseLerp(EndingTimeline.Jump - 0.15f, EndingTimeline.Jump, t);
            return Mathf.Exp(-(t - EndingTimeline.Jump) / 0.35f);
        }

        // ───────────────────────── 환경 ─────────────────────────

        private void ApplyEnvironment(bool spaceNow)
        {
            if (environmentApplied && inSpace == spaceNow) return;
            environmentApplied = true;
            inSpace = spaceNow;
            (spaceNow ? space : dawn).Apply();
        }

        // ───────────────────────── 건너뛰기·종료 ─────────────────────────

        private void UpdateSkip(float dt)
        {
            // 창이 폭발한 뒤로는 안내도 게이지도 없다
            if (prank.Exploded && prank.IsOpen == false)
            {
                skipGroup.alpha = 0f;
                return;
            }

            if (prank.IsOpen) waitRelease = true;
            else if (waitRelease && AnyHeld() == false) waitRelease = false;
            bool blocked = waitRelease;

            if (blocked == false && AnyPressedThisFrame()) promptUntil = Time.unscaledTime + skipPromptTime;
            bool held = blocked == false && AnyHeld();
            hold = held ? hold + dt : (blocked ? 0f : Mathf.Max(0f, hold - dt * 2f));
            if (hold >= skipHoldTime)
            {
                hold = 0f;
                promptUntil = 0f;
                prank.Request();
            }

            bool show = held || (blocked == false && Time.unscaledTime < promptUntil);
            skipGroup.alpha = Mathf.MoveTowards(skipGroup.alpha, show ? 1f : 0f, dt * 4f);
            skipBar.localScale = new Vector3(Mathf.Clamp01(hold / skipHoldTime), 1f, 1f);
        }

        private void Leave()
        {
            if (leaving) return;
            leaving = true;
            skipGroup.alpha = 0f;
            prank.Dismiss();
            // 엔딩 뒤에는 PRESS ANY KEY 부터 — 새벽으로 바뀐 메뉴를 먼저 보여 준다 (MainMenuController.SkipPressAnyKey 를 켜지 않는다)
            SceneTransition.LoadAsync(mainMenuScene, loadMinWait, destroyCancellationToken).Forget();
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

        private static void SetAlpha(Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }
    }
}
