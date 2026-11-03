using Cysharp.Threading.Tasks;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using HybridShooter.Round;
using HybridShooter.UI.MainMenu;
using HybridShooter.UI.Theme;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 게임 오버 화면. GameFlowUiBootstrap 이 RunDirector 가 있는 씬에 자동으로 붙인다 (씬에 직접 둬도 된다).
    ///  - GameState.GameOver 가 되면 openDelay(실제 시간) 뒤 열림 (게임 오버는 timeScale = 0)
    ///  - [재시작] → RunDirector.ReturnToReady → 무기 선택 화면(LoadoutPanel)이 다시 열리고 [출발]로 1스테이지부터.
    ///    무기 선택 화면(IRoundStartGate)이 없는 씬이면 RestartRun 으로 바로 다시 시작
    ///  - [메인 메뉴] → MainMenuController.SkipPressAnyKey 를 켜고 SceneTransition 으로 메인 메뉴 씬 (Build Settings 에 있어야 한다)
    /// 스타일은 UiTheme (메인 메뉴와 같은 모양): 왼쪽에서 사라지는 어두운 띠 위에 큰 GAME OVER + 붉은 선, 메인 메뉴식 글자 버튼(영문 + 작은 한글).
    /// 열리면 [재시작]이 선택된 상태라 키보드·패드로 바로 고를 수 있다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameOverPanel : MonoBehaviour
    {
        public const string DefaultMainMenuScene = "Test_MainMenu";

        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.55f);
        private const float MarginX = UiTheme.ScreenMargin;

        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private RunDirector runDirector;
        [Tooltip("메인 메뉴 씬 이름 (Build Settings 에 등록된 이름)")]
        [SerializeField] private string mainMenuScene = DefaultMainMenuScene;
        [Tooltip("게임 오버 후 화면이 뜰 때까지 (초, 실제 시간)")]
        [SerializeField, Min(0f)] private float openDelay = 1.2f;
        [Tooltip("메인 메뉴로 넘어갈 때 최소 대기 (초, BGM 페이드)")]
        [SerializeField, Min(0f)] private float menuLoadMinWait = 0.6f;
        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;

        private GameFlowUiSkin skin;
        private Canvas canvas;
        private TextMeshProUGUI roundText;
        private Button restartButton, menuButton;
        private PlayerController player;
        private float openAt = -1f;
        private int reachedRound;
        private bool leaving;

        public bool IsOpen { get; private set; }

        #region 유니티 메시지
        private void Awake()
        {
            if (runDirector == null) runDirector = FindAnyObjectByType<RunDirector>();
            player = FindAnyObjectByType<PlayerController>();
            skin = GameFlowUiSkin.Current;
            if (textFont == null) textFont = skin.textFont;
        }

        private void OnEnable()
        {
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<GameOverEvent>.Subscribe(OnGameOver);
        }

        private void OnDisable()
        {
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<GameOverEvent>.Unsubscribe(OnGameOver);
            openAt = -1f;
            if (IsOpen) Close();
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void Update()
        {
            if (openAt < 0f || Time.unscaledTime < openAt) return;
            openAt = -1f;
            Open();
        }
        #endregion

        #region 외부 API
        public void Open()
        {
            if (IsOpen) return;
            if (canvas == null) Build();
            LoadoutPanel.EnsureEventSystem();
            roundText.text = reachedRound > 0 ? $"스테이지 {reachedRound} 에서 쓰러졌습니다" : "";
            restartButton.interactable = menuButton.interactable = true;
            leaving = false;
            IsOpen = true;
            canvas.gameObject.SetActive(true);
            if (player != null) player.SetMenuInputBlocked(true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(restartButton.gameObject);
        }

        /// <summary>처음부터 다시. 무기 선택 화면이 있으면 그 화면을 거친다</summary>
        public void Restart()
        {
            if (IsOpen == false || leaving || runDirector == null) return;
            Close();
            if (ServiceLocator.TryGet<IRoundStartGate>(out _)) runDirector.ReturnToReady();
            else runDirector.RestartRun();
        }

        public void GoToMainMenu()
        {
            if (IsOpen == false || leaving) return;
            leaving = true;
            restartButton.interactable = menuButton.interactable = false;
            MainMenuController.SkipPressAnyKey = true;
            SceneTransition.LoadAsync(mainMenuScene, menuLoadMinWait, this.GetCancellationTokenOnDestroy()).Forget();
        }
        #endregion

        private void Close()
        {
            IsOpen = false;
            if (canvas != null) canvas.gameObject.SetActive(false);
            if (player != null) player.SetMenuInputBlocked(false);
        }

        private void OnGameOver(GameOverEvent e) => reachedRound = e.ReachedRound;

        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            if (e.Current == GameState.GameOver)
            {
                if (IsOpen == false) openAt = Time.unscaledTime + openDelay;
                return;
            }
            openAt = -1f;
            if (IsOpen) Close();
        }

        private void Build()
        {
            canvas = HudFactory.CreateCanvas("GameOverPanel", 105, true, out _);
            var root = canvas.transform;
            HudFactory.NewImage(HudFactory.Stretch(root, "Dim"), null, Dim).raycastTarget = true;
            // 왼쪽 글자 뒤 어두운 띠 (오른쪽으로 사라짐)
            var band = HudFactory.Rect(root, "Band", Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1100f, 0f));
            band.anchorMax = new Vector2(0f, 1f);
            UiThemeKit.Band(band, UiThemeKit.BandShape.FadeRight, 0.9f, 0.6f);

            var topLeft = new Vector2(0f, 1f);
            UiThemeKit.EnLabel(root, "Header", "SIGNAL LOST", UiTheme.LabelSize, UiTheme.RedText, TextAlignmentOptions.BottomLeft,
                topLeft, topLeft, new Vector2(MarginX, -280f), new Vector2(600f, 26f));
            UiThemeKit.EnLabel(root, "Title", "GAME OVER", 96f, UiTheme.MoonWhite, TextAlignmentOptions.TopLeft,
                topLeft, topLeft, new Vector2(MarginX, -314f), new Vector2(1000f, 110f), 22f);
            UiThemeKit.Line(root, "Line", UiTheme.Red, topLeft, topLeft, new Vector2(MarginX, -432f), new Vector2(140f, 3f));
            roundText = HudFactory.Label(root, "Round", textFont, 26f, TextAlignmentOptions.TopLeft,
                topLeft, topLeft, new Vector2(MarginX, -456f), new Vector2(900f, 40f));
            roundText.color = UiTheme.Steel;

            // 메인 메뉴식 글자 버튼 (위아래로 돈다)
            restartButton = CreateButton(root, "RestartButton", "RETRY", "재시작", 46f, -650f);
            restartButton.onClick.AddListener(Restart);
            menuButton = CreateButton(root, "MainMenuButton", "MAIN MENU", "메인 메뉴", 40f, -744f);
            menuButton.onClick.AddListener(GoToMainMenu);
            restartButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = menuButton, selectOnDown = menuButton };
            menuButton.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = restartButton, selectOnDown = restartButton };

            var hint = HudFactory.Label(root, "Hint", textFont, 22f, TextAlignmentOptions.BottomRight,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-80f, 40f), new Vector2(500f, 34f));
            hint.text = "<color=#E0574D>ENTER</color>  선택";
            hint.color = UiTheme.Steel;

            canvas.gameObject.SetActive(false);
        }

        /// <summary>영문 글자 버튼 + 오른쪽에 작은 한글 이름 (선택 효과는 영문 글자만)</summary>
        private Button CreateButton(Transform parent, string name, string text, string koreanText, float fontSize, float y)
        {
            var button = UiThemeKit.TextButton(parent, name, text, fontSize, new Vector2(0f, 1f), new Vector2(MarginX, y), new Vector2(640f, 84f));
            var label = button.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            float textWidth = label.GetPreferredValues(text).x;
            var korean = HudFactory.Label(button.transform, "Korean", textFont, 20f, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f + textWidth + 40f, -2f), new Vector2(200f, 30f));
            korean.text = koreanText;
            korean.color = UiTheme.SteelDim;
            return button;
        }
    }
}
