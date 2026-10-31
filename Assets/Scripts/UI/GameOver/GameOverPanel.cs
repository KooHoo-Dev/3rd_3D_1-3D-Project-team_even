using Cysharp.Threading.Tasks;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using HybridShooter.Round;
using HybridShooter.UI.MainMenu;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 게임 오버 화면. GameFlowUiBootstrap 이 RunDirector 가 있는 씬에 자동으로 붙인다 (씬에 직접 둬도 된다).
    ///  - GameState.GameOver 가 되면 openDelay(실제 시간) 뒤 열림 (게임 오버는 timeScale = 0)
    ///  - [재시작] → RunDirector.ReturnToReady → 무기 선택 화면(LoadoutPanel)이 다시 열리고 [출발]로 1스테이지부터.
    ///    무기 선택 화면(IRoundStartGate)이 없는 씬이면 RestartRun 으로 바로 다시 시작
    ///  - [메인 메뉴] → MainMenuController.SkipPressAnyKey 를 켜고 SceneTransition 으로 메인 메뉴 씬 (Build Settings 에 있어야 한다)
    /// 겉모양은 GameFlowUiSkin(GameOver_bg · Title_GAMEOVER · Btn_Rectangle01Cyan · Btn_MainMenuCyan)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameOverPanel : MonoBehaviour
    {
        public const string DefaultMainMenuScene = "Test_MainMenu";

        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.7f);
        private static readonly Color ButtonColor = new Color(0.15f, 0.55f, 0.75f, 1f);
        private static readonly Color MutedText = new Color(0.6f, 0.7f, 0.75f, 1f);

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

            // GAME OVER 띠 + 글자 (팩의 Popup_GameOver 구성)
            var band = HudFactory.Rect(root, "Band", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1126f, 228f));
            if (skin.gameOverBackground != null) GameFlowUiSkin.Paint(HudFactory.NewImage(band, null, Color.clear), skin.gameOverBackground, Color.clear);
            if (skin.gameOverTitle != null)
            {
                var titleImage = HudFactory.NewImage(HudFactory.Rect(band, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 120f)), skin.gameOverTitle, Color.white);
                titleImage.preserveAspect = true;
            }
            else
            {
                var title = HudFactory.Label(band, "Title", textFont, 72f, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 110f));
                title.text = "GAME OVER";
                title.color = new Color(1f, 0.35f, 0.3f, 1f);
            }

            roundText = HudFactory.Label(root, "Round", textFont, 24f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(900f, 40f));
            roundText.color = MutedText;

            restartButton = CreateButton(root, "RestartButton", "재시작", new Vector2(-220f, -140f), new Vector2(360f, 120f),
                skin.primaryButton, skin.primaryButtonPressed, skin.disabledButton);
            restartButton.onClick.AddListener(Restart);
            menuButton = CreateButton(root, "MainMenuButton", "메인 메뉴", new Vector2(220f, -140f), new Vector2(380f, 120f),
                skin.menuButton, skin.menuButtonPressed, null);
            menuButton.onClick.AddListener(GoToMainMenu);

            canvas.gameObject.SetActive(false);
        }

        private Button CreateButton(Transform parent, string name, string text, Vector2 position, Vector2 size, Sprite normal, Sprite pressed, Sprite disabled)
        {
            var rt = HudFactory.Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size);
            var image = rt.gameObject.AddComponent<Image>();
            GameFlowUiSkin.Paint(image, normal, ButtonColor);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            GameFlowUiSkin.SetStates(button, pressed, disabled);
            var label = HudFactory.Label(rt, "Label", textFont, 32f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            label.text = text;
            return button;
        }
    }
}
