using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using HybridShooter.UI.Theme;
using HybridShooter.Weapon;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 무기 슬롯 요약 HUD (4칸). Player 루트에 붙인다. 오른쪽 아래 무기 패널(PlayerHud) 바로 위에 Canvas 를 코드로 만든다.
    ///  - 칸마다 키 번호 · 아이콘(WeaponTable.IconKey, 없으면 이름) · 지금 든 칸 강조
    ///  - 4번 보급 칸: 비어 있으면 "보급" 회색, 보급 무기 탄이 다 떨어지면 흐리게
    ///  - GunSlot.GunChanged(바꿔 들기) + LoadoutChanged(구성 변경) 로 갱신
    ///  - 번호 키로 빈 칸을 고르면(GunSlot.EmptySlotSelected) 슬롯 줄 위에 잠깐 경고
    /// 스코프 화면·사망 중·보스 연출 중·강화 선택·게임 오버·일시정지 때는 숨긴다 (PlayerHud 와 같은 규칙)
    /// 스타일은 UiTheme: 칸 상자 없이 키 번호 + 아이콘, 든 칸만 붉은 밑줄 · 밝은 색, 나머지는 청회색으로 흐리게 (뒤 띠는 PlayerHud 무기 패널이 깐다)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponSlotHud : MonoBehaviour
    {
        /// <summary>들지 않은 칸 투명도</summary>
        private const float IdleAlpha = 0.7f;
        /// <summary>빈 칸 투명도</summary>
        private const float EmptyAlpha = 0.45f;

        [SerializeField] private GunSlot gunSlot;
        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerAimView aimView;
        [SerializeField] private TMP_FontAsset numberFont;
        [Tooltip("한글 글자용 (NotoSansKR SDF)")]
        [SerializeField] private TMP_FontAsset textFont;

        [Header("배치 (1920×1080 기준, 오른쪽 아래 모서리에서)")]
        [Tooltip("PlayerHud 무기 패널(-416, 40, 360×188) 위")]
        [SerializeField] private Vector2 position = new Vector2(-416f, 238f);
        [SerializeField] private Vector2 cellSize = new Vector2(84f, 58f);
        [SerializeField] private float spacing = 8f;
        [Tooltip("보급 무기 탄이 다 떨어졌을 때 칸 투명도")]
        [SerializeField, Range(0f, 1f)] private float emptyAmmoAlpha = 0.35f;
        [Tooltip("빈 칸 번호 키를 눌렀을 때 경고를 띄워 두는 시간(초)")]
        [SerializeField, Min(0.5f)] private float emptyWarningTime = 1.6f;


        private Canvas canvas;
        private CanvasGroup rootGroup;
        private Cell[] cells;
        private TextMeshProUGUI emptyWarning;
        private float emptyWarningUntil = -1f;
        private bool stateHidden;
        private bool shownSupplyEmpty;

        private sealed class Cell
        {
            public CanvasGroup Group;
            public Image Frame, Icon;
            public TextMeshProUGUI Key, Name;
        }

        private void Reset() => AssignAssets();

        private void AssignAssets()
        {
#if UNITY_EDITOR
            if (numberFont == null) numberFont = HudFactory.LoadAsset<TMP_FontAsset>(HudFactory.NumberFontGuid);
            if (textFont == null) textFont = HudFactory.LoadAsset<TMP_FontAsset>(HudFactory.TextFontGuid);
#endif
        }

        #region 유니티 메시지
        private void Awake()
        {
            if (gunSlot == null) gunSlot = GetComponent<GunSlot>();
            if (player == null) player = GetComponent<PlayerController>();
            if (aimView == null) aimView = GetComponent<PlayerAimView>();
            AssignAssets();
            Build();
        }

        private void OnEnable()
        {
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            if (gunSlot != null)
            {
                gunSlot.GunChanged += OnGunChanged;
                gunSlot.LoadoutChanged += Refresh;
                gunSlot.EmptySlotSelected += OnEmptySlotSelected;
            }
            if (canvas != null) canvas.gameObject.SetActive(true);
            Refresh();
        }

        private void OnDisable()
        {
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            if (gunSlot != null)
            {
                gunSlot.GunChanged -= OnGunChanged;
                gunSlot.LoadoutChanged -= Refresh;
                gunSlot.EmptySlotSelected -= OnEmptySlotSelected;
            }
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void Update()
        {
            // 연출(보스 등장·최후 시네마틱) 중에는 숨긴다
            bool cinematic = player != null && player.IsInCinematic;
            bool alive = player == null || player.IsAlive;
            bool scoped = aimView != null && aimView.Scoped;
            rootGroup.alpha = stateHidden || cinematic || alive == false || scoped || LoadoutHolding ? 0f : 1f;

            // 보급 무기 탄 소진은 이벤트가 없으므로 (든 총만 AmmoChangedEvent) 여기서 본다
            if (SupplyEmpty != shownSupplyEmpty) Refresh();

            if (emptyWarningUntil >= 0f && Time.unscaledTime >= emptyWarningUntil)
            {
                emptyWarningUntil = -1f;
                emptyWarning.gameObject.SetActive(false);
            }
        }
        #endregion

        #region 갱신
        private bool SupplyEmpty
        {
            get
            {
                Gun supply = gunSlot != null ? gunSlot.GetGun(GunSlot.SupplySlot) : null;
                return supply != null && supply.IsOutOfAmmo;
            }
        }

        private void OnGunChanged(int index, Gun gun) => Refresh();

        /// <summary>빈 칸 번호 키: 슬롯 줄 위에 잠깐 경고</summary>
        private void OnEmptySlotSelected(int index)
        {
            emptyWarning.text = index == GunSlot.SupplySlot
                ? "보급 무기 없음 — 보급 상자에서 획득"
                : $"{index + 1}번 칸이 비어 있습니다";
            emptyWarning.gameObject.SetActive(true);
            emptyWarningUntil = Time.unscaledTime + emptyWarningTime;
        }

        /// <summary>무기 선택 화면(LoadoutPanel, IRoundStartGate)이 라운드 시작을 붙잡고 있는 동안 — 그 화면 뒤로 비치지 않게 숨긴다</summary>
        private static bool LoadoutHolding => ServiceLocator.TryGet<IRoundStartGate>(out var gate) && gate.HoldsStart;

        private void OnGameStateChanged(GameStateChangedEvent e)
            => stateHidden = e.Current == GameState.Reward || e.Current == GameState.GameOver || e.Current == GameState.Paused;

        private void Refresh()
        {
            if (cells == null) return;
            shownSupplyEmpty = SupplyEmpty;
            int current = gunSlot != null ? gunSlot.CurrentIndex : -1;

            for (int i = 0; i < cells.Length; i++)
            {
                Cell cell = cells[i];
                Gun gun = gunSlot != null ? gunSlot.GetGun(i) : null;
                bool selected = i == current && gun != null;

                cell.Frame.enabled = selected;
                cell.Key.color = selected ? UiTheme.RedText : UiTheme.Steel;

                if (gun == null)
                {
                    cell.Icon.enabled = false;
                    cell.Name.text = i == GunSlot.SupplySlot ? "보급" : "-";
                    cell.Name.color = UiTheme.Steel;
                    cell.Group.alpha = EmptyAlpha;
                    continue;
                }

                Sprite icon = HudFactory.WeaponIcon(gun.Def != null ? gun.Def.IconKey : "");
                Color color = selected ? UiTheme.MoonWhite : UiTheme.Steel;
                cell.Icon.sprite = icon;
                cell.Icon.enabled = icon != null;
                cell.Icon.color = color;
                cell.Name.text = icon != null ? "" : gun.DisplayName;
                cell.Name.color = color;
                cell.Group.alpha = i == GunSlot.SupplySlot && shownSupplyEmpty ? emptyAmmoAlpha : selected ? 1f : IdleAlpha;
            }
        }
        #endregion

        #region 화면 만들기
        private void Build()
        {
            canvas = HudFactory.CreateCanvas("WeaponSlotHud", 60, false, out rootGroup);
            float width = cellSize.x * GunSlot.SlotCount + spacing * (GunSlot.SlotCount - 1);
            var root = HudFactory.Rect(canvas.transform, "Slots", new Vector2(1f, 0f), Vector2.zero, position, new Vector2(width, cellSize.y));

            var keys = new PlayerController.InputPlayerSettings().slotKeys;
            cells = new Cell[GunSlot.SlotCount];
            for (int i = 0; i < cells.Length; i++)
            {
                var rt = HudFactory.Rect(root, "Slot" + i, Vector2.zero, Vector2.zero, new Vector2(i * (cellSize.x + spacing), 0f), cellSize);
                var cell = new Cell { Group = rt.gameObject.AddComponent<CanvasGroup>() };
                cell.Group.blocksRaycasts = false;
                cell.Group.interactable = false;

                cell.Frame = HudFactory.NewImage(HudFactory.Rect(rt, "Selected", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(cellSize.x, 3f)), null, UiTheme.Red);
                // 아이콘은 가로로 긴 실루엣(256×96)이라 칸 폭을 채우고, 키 번호 아래에 둔다
                cell.Icon = HudFactory.NewImage(HudFactory.Rect(rt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), cellSize - new Vector2(4f, 22f)), null, UiTheme.Steel);
                cell.Icon.preserveAspect = true;
                cell.Key = HudFactory.Label(rt, "Key", numberFont, 16f, TextAlignmentOptions.TopLeft,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(2f, -2f), new Vector2(24f, 22f));
                cell.Key.text = i < keys.Length ? HudFactory.KeyName(keys[i]) : (i + 1).ToString();
                cell.Name = HudFactory.Label(rt, "Name", textFont, 16f, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(4f, -6f), new Vector2(cellSize.x - 10f, 26f));
                cells[i] = cell;
            }

            emptyWarning = HudFactory.Label(root, "EmptyWarning", textFont, 18f, TextAlignmentOptions.BottomRight,
                new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(width + 120f, 26f));
            emptyWarning.color = UiTheme.Ember;
            emptyWarning.gameObject.SetActive(false);
        }
        #endregion
    }
}
