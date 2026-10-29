using Cysharp.Threading.Tasks;
using DG.Tweening;
using HybridShooter.UI.Options;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>
    /// 메인 메뉴 흐름: PressAnyKey → Menu → (Options) → StartingGame.
    ///  - PressAnyKey: 로고 + 깜빡이는 "PRESS ANY KEY". 아무 키·마우스 버튼·패드 버튼 → 메뉴 슬라이드인 + 카메라가 플레이어 쪽으로 조금 다가감
    ///  - Menu: 좌측 세로 버튼(첫 선택 GAME START). 선택이 비면(빈 곳 클릭 등) 마지막 버튼을 다시 선택해 키보드·패드로 계속 움직일 수 있게.
    ///    선택된 버튼은 MenuBackdropDirector 로 넘겨 배경이 반응한다 (옵션 팝업이 열려 있는 동안은 OPTIONS 반응 유지)
    ///  - 게임에서 메뉴로 돌아올 때는 SkipPressAnyKey 를 켜 두면 바로 메뉴
    /// 레이아웃은 Tools > MainMenu > Rebuild Menu UI 가 만들고, 이 컴포넌트는 흐름만 맡는다.
    /// TODO(6단계): 1인칭 진입 연출·종료 확인 팝업
    /// </summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        private enum State { PressAnyKey, Menu, StartingGame }

        /// <summary>게임 → 메인 메뉴로 돌아올 때 켠다. 한 번 쓰면 꺼진다</summary>
        public static bool SkipPressAnyKey;

        [Header("PRESS ANY KEY")]
        [SerializeField] private CanvasGroup pressAnyKeyGroup;
        [Tooltip("깜빡임 한 번(밝음→어두움→밝음) 시간(초)")]
        [SerializeField] private float blinkPeriod = 1.2f;
        [SerializeField] private float blinkMinAlpha = 0.3f;

        [Header("메뉴")]
        [Tooltip("그라데이션 배경·버전·조작 힌트를 함께 페이드")]
        [SerializeField] private CanvasGroup menuGroup;
        [SerializeField] private Button gameStartButton;
        [SerializeField] private Button optionsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private OptionsPopup optionsPopup;
        [Tooltip("빌드 버전(Application.version) 표시")]
        [SerializeField] private TMP_Text versionText;

        [Header("슬라이드인")]
        [SerializeField] private float slideTime = 0.4f;
        [Tooltip("버튼마다 늦게 들어오는 시간(초)")]
        [SerializeField] private float slideStagger = 0.06f;
        [Tooltip("왼쪽 화면 밖에서 들어오는 거리(px, 1920 기준)")]
        [SerializeField] private float slideDistance = 700f;

        [Header("배경 연출")]
        [Tooltip("호버 연동 연출. 비우면 배경이 반응하지 않는다")]
        [SerializeField] private MenuBackdropDirector backdrop;

        [Header("카메라")]
        [Tooltip("비우면 카메라를 움직이지 않는다")]
        [SerializeField] private MenuCameraSway menuCamera;
        [Tooltip("메뉴가 열릴 때 카메라 이동량 (카메라 기준 로컬, m)")]
        [SerializeField] private Vector3 menuCameraOffset = new Vector3(-0.1f, 0f, 0.6f);
        [SerializeField] private float cameraMoveTime = 1.2f;

        [Header("게임 시작")]
        [Tooltip("Build Settings 에 있는 게임 씬 이름")]
        [SerializeField] private string gameSceneName = "Monster Player Integration";
        [Tooltip("BGM 페이드아웃을 기다리는 최소 시간(초)")]
        [SerializeField] private float loadMinWait = 1f;

        private State state;
        private Button[] buttons;
        private Vector2[] buttonPositions;
        private GameObject lastSelected;

        private void Awake()
        {
            buttons = new[] { gameStartButton, optionsButton, quitButton };
            buttonPositions = new Vector2[buttons.Length];
            for (int i = 0; i < buttons.Length; i++) buttonPositions[i] = ((RectTransform)buttons[i].transform).anchoredPosition;

            gameStartButton.onClick.AddListener(StartGame);
            optionsButton.onClick.AddListener(OpenOptions);
            quitButton.onClick.AddListener(Quit);
            if (optionsPopup != null) optionsPopup.Closed += OnOptionsClosed;
            if (versionText != null) versionText.text = "v" + Application.version;
        }

        private void Start()
        {
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            if (SkipPressAnyKey)
            {
                SkipPressAnyKey = false;
                ShowMenu(true);
                return;
            }

            state = State.PressAnyKey;
            SetGroup(menuGroup, 0f, false);
            for (int i = 0; i < buttons.Length; i++) ButtonRect(i).anchoredPosition = buttonPositions[i] - new Vector2(slideDistance, 0f);
            pressAnyKeyGroup.alpha = 1f;
            pressAnyKeyGroup.gameObject.SetActive(true);
            pressAnyKeyGroup.DOFade(blinkMinAlpha, blinkPeriod * 0.5f)
                .SetLoops(-1, LoopType.Yoyo).SetEase(Ease.InOutSine).SetUpdate(true).SetTarget(this);
            // 키보드·마우스·패드 어느 버튼이든 한 번
            InputSystem.onAnyButtonPress.CallOnce(_ => { if (this != null && state == State.PressAnyKey) ShowMenu(false); });
        }

        private void OnDestroy()
        {
            DOTween.Kill(this);
            if (optionsPopup != null) optionsPopup.Closed -= OnOptionsClosed;
        }

        private void Update()
        {
            if (state != State.Menu || EventSystem.current == null) return;
            if (optionsPopup != null && optionsPopup.IsOpen) return;

            var selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.activeInHierarchy) lastSelected = selected;
            else if (lastSelected != null) EventSystem.current.SetSelectedGameObject(lastSelected);
            if (backdrop != null) backdrop.SetHover(ToHover(lastSelected));

            // 옵션을 닫은 같은 Esc 는 무시
            if (OptionsPopup.CancelPressed() && (optionsPopup == null || optionsPopup.LastClosedFrame != Time.frameCount))
            {
                // TODO(6단계): 종료 확인 팝업
            }
        }

        // ───────────────────────── 상태 ─────────────────────────

        /// <summary>메뉴 표시. immediate = 슬라이드·카메라 이동 없이 (게임에서 돌아올 때)</summary>
        private void ShowMenu(bool immediate)
        {
            state = State.Menu;
            DOTween.Kill(this);
            pressAnyKeyGroup.gameObject.SetActive(false);
            MoveCamera(immediate);

            if (immediate)
            {
                for (int i = 0; i < buttons.Length; i++) ButtonRect(i).anchoredPosition = buttonPositions[i];
                SetGroup(menuGroup, 1f, true);
                Select(gameStartButton);
                return;
            }

            // 다 들어온 뒤에 누를 수 있게 (PRESS ANY KEY 를 누른 클릭·Enter 가 버튼까지 눌리지 않게)
            SetGroup(menuGroup, 0f, false);
            menuGroup.DOFade(1f, slideTime).SetUpdate(true).SetTarget(this);
            float last = 0f;
            for (int i = 0; i < buttons.Length; i++)
            {
                float delay = i * slideStagger;
                last = delay + slideTime;
                ButtonRect(i).DOAnchorPos(buttonPositions[i], slideTime).SetDelay(delay)
                    .SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(this);
            }
            DOVirtual.DelayedCall(last, () =>
            {
                SetGroup(menuGroup, 1f, true);
                Select(gameStartButton);
            }, true).SetTarget(this);
        }

        private void MoveCamera(bool immediate)
        {
            if (menuCamera == null) return;
            var from = menuCamera.BasePosition;
            var to = from + menuCamera.BaseRotation * menuCameraOffset;
            if (immediate)
            {
                menuCamera.SetBase(to, menuCamera.BaseRotation);
                return;
            }
            DOVirtual.Float(0f, 1f, cameraMoveTime, t => menuCamera.SetBase(Vector3.Lerp(from, to, t), menuCamera.BaseRotation))
                .SetEase(Ease.InOutSine).SetUpdate(true).SetTarget(this);
        }

        private void StartGame()
        {
            if (state != State.Menu) return;
            state = State.StartingGame;
            SetGroup(menuGroup, menuGroup.alpha, false);
            // TODO(6단계): 1인칭 진입 연출 후 로드
            SceneTransition.LoadAsync(gameSceneName, loadMinWait, destroyCancellationToken).Forget();
        }

        private void OpenOptions()
        {
            if (state != State.Menu || optionsPopup == null) return;
            optionsPopup.Open();
        }

        private void OnOptionsClosed() => Select(optionsButton);

        private void Quit()
        {
            if (state != State.Menu) return;
            // TODO(6단계): 종료 확인 팝업을 거친다
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ───────────────────────── 공통 ─────────────────────────

        private MenuHover ToHover(GameObject selected)
        {
            if (selected == null) return MenuHover.None;
            if (selected == gameStartButton.gameObject) return MenuHover.GameStart;
            if (selected == optionsButton.gameObject) return MenuHover.Options;
            if (selected == quitButton.gameObject) return MenuHover.Quit;
            return MenuHover.None;
        }

        private RectTransform ButtonRect(int index) => (RectTransform)buttons[index].transform;

        private void Select(Button button)
        {
            lastSelected = button.gameObject;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
        }

        private static void SetGroup(CanvasGroup group, float alpha, bool interactable)
        {
            group.alpha = alpha;
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }
    }
}
