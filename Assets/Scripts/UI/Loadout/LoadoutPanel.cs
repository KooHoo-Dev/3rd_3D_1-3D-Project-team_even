using System.Collections;
using System.Collections.Generic;
using HybridShooter.Core.Data;
using HybridShooter.Player;
using HybridShooter.Round;
using HybridShooter.Weapon;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 출발 전 무기 선택 화면 (게임 씬 안의 Ready 단계). 씬에 하나 둔다.
    ///  - 씬 시작 시 열림 → 플레이어 입력 차단(PlayerController.SetMenuInputBlocked) + 커서 표시
    ///  - 4칸: 1번 Sidearm(잠금) · 2·3번 선택 가능 · 4번 "보급 전용"(선택 불가)
    ///  - 칸 클릭 → 후보 목록: WeaponTable.Category = Loadout · 해금(IWeaponUnlocks, 없으면 전부) · 다른 칸에 없는 총
    ///  - [출발] → GunSlot.ApplyLoadout → 입력 해제 → RunDirector.StartRun()
    /// 마지막 선택은 PlayerPrefs(Loadout.Slot1, Loadout.Slot2)에 저장해 다음에 기본값으로 쓴다.
    /// 이 화면을 쓰는 씬은 RunDirector 의 startOnPlay 를 꺼야 한다 (켜 두면 선택 전에 라운드가 시작된다).
    /// 재시작(RestartRun)에서는 다시 열지 않고 기존 선택을 유지한다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadoutPanel : MonoBehaviour
    {
        private const string PrefsKeyPrefix = "Loadout.Slot";

        private static readonly Color Dim = new Color(0f, 0.02f, 0.04f, 0.82f);
        private static readonly Color CardColor = new Color(0.05f, 0.12f, 0.16f, 0.92f);
        private static readonly Color LockedColor = new Color(0.04f, 0.07f, 0.09f, 0.85f);
        private static readonly Color MutedText = new Color(0.6f, 0.7f, 0.75f, 1f);
        private static readonly Color ButtonColor = new Color(0.15f, 0.55f, 0.75f, 1f);
        /// <summary>후보 버튼 (3열 × 360 + 간격 2 × 16 = 슬롯 카드 줄 폭 1112)</summary>
        private static readonly Vector2 CandidateSize = new Vector2(360f, 92f);

        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private GunSlot gunSlot;
        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private RunDirector runDirector;
        [Tooltip("씬 시작 시 자동으로 연다")]
        [SerializeField] private bool openOnStart = true;
        [SerializeField] private TMP_FontAsset numberFont;
        [Tooltip("한글 글자용 (NotoSansKR SDF)")]
        [SerializeField] private TMP_FontAsset textFont;

        private sealed class SlotView
        {
            public int Index;
            public Image Background, Frame, Icon;
            public TextMeshProUGUI Key, Name, Note;
            public Button Button;
        }

        private Canvas canvas;
        private SlotView[] slotViews;
        private RectTransform candidateList;
        private TextMeshProUGUI candidateTitle;
        private readonly List<GameObject> candidateItems = new List<GameObject>();
        private PlayerController player;

        /// <summary>LoadoutSlots 순서의 선택 무기 Id</summary>
        private int[] selection;
        private int activeSlot;

        public bool IsOpen { get; private set; }

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
            if (gunSlot == null) gunSlot = FindAnyObjectByType<GunSlot>();
            if (runDirector == null) runDirector = FindAnyObjectByType<RunDirector>();
            if (gunSlot != null) player = gunSlot.GetComponent<PlayerController>();
            AssignAssets();
        }

        private IEnumerator Start()
        {
            // GunSlot.Start(시작 무기 배치)가 끝난 뒤 연다
            yield return null;
            if (openOnStart) Open();
        }

        private void OnDisable()
        {
            if (IsOpen) Close();
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }
        #endregion

        #region 외부 API
        public void Open()
        {
            if (IsOpen) return;
            if (gunSlot == null)
            {
                Debug.LogWarning($"{name}: GunSlot 을 찾지 못해 무기 선택 화면을 열 수 없습니다", this);
                return;
            }
            if (runDirector != null && runDirector.IsRunning)
                Debug.LogWarning($"{name}: 라운드가 이미 진행 중입니다. 이 씬의 RunDirector startOnPlay 를 꺼야 선택 후 출발합니다", this);

            if (canvas == null) Build();
            EnsureEventSystem();
            selection = LoadSelection();
            activeSlot = 0;
            IsOpen = true;
            canvas.gameObject.SetActive(true);
            if (player != null) player.SetMenuInputBlocked(true);
            Refresh();
        }

        /// <summary>선택을 적용하고 닫은 뒤 라운드를 시작한다</summary>
        public void Confirm()
        {
            if (IsOpen == false) return;
            if (gunSlot.ApplyLoadout(selection) == false) return;
            SaveSelection();
            Close();
            if (runDirector != null) runDirector.StartRun();
        }
        #endregion

        #region 선택
        private void Close()
        {
            IsOpen = false;
            if (canvas != null) canvas.gameObject.SetActive(false);
            if (player != null) player.SetMenuInputBlocked(false);
        }

        /// <summary>후보 = Loadout 분류 · 해금 · 다른 칸에 없는 총 (지금 칸의 총은 포함). WeaponId 순</summary>
        private List<Gun> Candidates(int slot)
        {
            var list = new List<Gun>();
            foreach (var gun in gunSlot.AllGuns)
            {
                if (gun == null || GunSlot.CategoryOf(gun) != WeaponCategory.Loadout || GunSlot.IsUnlocked(gun.WeaponId) == false) continue;
                bool usedElsewhere = false;
                for (int i = 0; i < selection.Length; i++)
                    if (i != slot && selection[i] == gun.WeaponId) usedElsewhere = true;
                if (usedElsewhere == false) list.Add(gun);
            }
            list.Sort((a, b) => a.WeaponId.CompareTo(b.WeaponId));
            return list;
        }

        private void Pick(int weaponId)
        {
            selection[activeSlot] = weaponId;
            // 다음 빈 칸이 아니라 다음 칸으로 넘겨 두 칸을 빠르게 고를 수 있게
            if (activeSlot < selection.Length - 1) activeSlot++;
            Refresh();
        }

        /// <summary>저장된 선택 → 유효하지 않으면 지금 GunSlot 2·3번 칸 → 그래도 비면 후보 앞에서부터</summary>
        private int[] LoadSelection()
        {
            int count = GunSlot.LoadoutSlots.Count;
            var ids = new int[count];
            for (int i = 0; i < count; i++)
            {
                int saved = PlayerPrefs.GetInt(PrefsKeyPrefix + GunSlot.LoadoutSlots[i], 0);
                Gun current = gunSlot.GetGun(GunSlot.LoadoutSlots[i]);
                ids[i] = IsSelectable(saved, ids, i) ? saved : current != null && IsSelectable(current.WeaponId, ids, i) ? current.WeaponId : 0;
            }

            selection = ids;
            for (int i = 0; i < count; i++)
            {
                if (ids[i] != 0) continue;
                var candidates = Candidates(i);
                if (candidates.Count > 0) ids[i] = candidates[0].WeaponId;
            }
            return ids;
        }

        private bool IsSelectable(int weaponId, int[] picked, int slot)
        {
            if (weaponId == 0) return false;
            for (int i = 0; i < slot; i++)
                if (picked[i] == weaponId) return false;
            foreach (var gun in gunSlot.AllGuns)
                if (gun != null && gun.WeaponId == weaponId)
                    return GunSlot.CategoryOf(gun) == WeaponCategory.Loadout && GunSlot.IsUnlocked(weaponId);
            return false;
        }

        private void SaveSelection()
        {
            for (int i = 0; i < selection.Length; i++)
                PlayerPrefs.SetInt(PrefsKeyPrefix + GunSlot.LoadoutSlots[i], selection[i]);
            PlayerPrefs.Save();
        }

        private Gun FindGun(int weaponId)
        {
            foreach (var gun in gunSlot.AllGuns)
                if (gun != null && gun.WeaponId == weaponId) return gun;
            return null;
        }

        private static Sprite IconOf(Gun gun) => gun != null && gun.Def != null ? HudFactory.WeaponIcon(gun.Def.IconKey) : null;
        #endregion

        #region 갱신
        private void Refresh()
        {
            foreach (var view in slotViews)
            {
                int loadoutIndex = IndexOfLoadout(view.Index);
                bool selectable = loadoutIndex >= 0;
                bool active = selectable && loadoutIndex == activeSlot;
                view.Frame.enabled = active;
                view.Button.interactable = selectable;
                view.Background.color = selectable ? CardColor : LockedColor;

                Gun gun = null;
                if (view.Index == GunSlot.SupplySlot)
                {
                    view.Name.text = "보급 무기";
                    view.Name.color = MutedText;
                    view.Note.text = "보급 상자 전용";
                }
                else if (selectable)
                {
                    gun = FindGun(selection[loadoutIndex]);
                    view.Name.color = active ? AimReticleHud.Cyan : HudFactory.TextColor;
                    view.Note.text = active ? "아래에서 선택" : "클릭해서 변경";
                }
                else
                {
                    gun = gunSlot.GetGun(view.Index);
                    view.Name.color = HudFactory.TextColor;
                    view.Note.text = "고정";
                }
                if (view.Index != GunSlot.SupplySlot) view.Name.text = gun != null ? gun.DisplayName : "-";

                // 아이콘이 있으면 아이콘 아래에 이름, 없으면 이름만 가운데
                Sprite icon = IconOf(gun);
                view.Icon.sprite = icon;
                view.Icon.enabled = icon != null;
                view.Icon.color = view.Name.color;
                view.Name.fontSize = icon != null ? 22f : 28f;
                view.Name.rectTransform.anchoredPosition = new Vector2(0f, icon != null ? -22f : 4f);
            }

            RefreshCandidates();
        }

        private void RefreshCandidates()
        {
            foreach (var item in candidateItems) Destroy(item);
            candidateItems.Clear();

            candidateTitle.text = $"{GunSlot.LoadoutSlots[activeSlot] + 1}번 칸 무기";
            int current = selection[activeSlot];
            foreach (var gun in Candidates(activeSlot))
            {
                int id = gun.WeaponId;
                bool chosen = id == current;
                string stats = gun.Def != null
                    ? $"피해 {gun.Def.Damage} · 연사 {gun.Def.FireRate:0} · 탄창 {gun.Def.MagazineSize} · {(gun.Def.FireMode == FireMode.Auto ? "자동" : "단발")}"
                    : "";
                Color textColor = chosen ? Color.black : HudFactory.TextColor;
                var button = CreateButton(candidateList, "Weapon" + id, CandidateSize, chosen ? AimReticleHud.Cyan : CardColor,
                    out var label);

                // 왼쪽 아이콘, 오른쪽 이름·능력치 (아이콘이 없으면 글자를 왼쪽부터)
                Sprite icon = IconOf(gun);
                float textX = icon != null ? 124f : 16f;
                if (icon != null)
                {
                    var image = HudFactory.NewImage(HudFactory.Rect(button.transform, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(12f, 0f), new Vector2(104f, 40f)), icon, textColor);
                    image.preserveAspect = true;
                }
                label.text = gun.DisplayName;
                label.color = textColor;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 0.5f);
                label.rectTransform.pivot = new Vector2(0f, 0.5f);
                label.rectTransform.anchoredPosition = new Vector2(textX, 14f);
                label.rectTransform.sizeDelta = new Vector2(CandidateSize.x - textX - 8f, 32f);
                var info = HudFactory.Label(button.transform, "Stats", textFont, 15f, TextAlignmentOptions.MidlineLeft,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(textX, -18f), new Vector2(CandidateSize.x - textX - 8f, 24f));
                info.text = stats;
                info.color = chosen ? new Color(0f, 0f, 0f, 0.75f) : MutedText;
                button.onClick.AddListener(() => Pick(id));
                candidateItems.Add(button.gameObject);
            }
        }

        private static int IndexOfLoadout(int slotIndex)
        {
            for (int i = 0; i < GunSlot.LoadoutSlots.Count; i++)
                if (GunSlot.LoadoutSlots[i] == slotIndex) return i;
            return -1;
        }
        #endregion

        #region 화면 만들기
        private void Build()
        {
            canvas = HudFactory.CreateCanvas("LoadoutPanel", 100, true, out _);
            var root = canvas.transform;
            HudFactory.NewImage(HudFactory.Stretch(root, "Dim"), null, Dim).raycastTarget = true;

            var title = HudFactory.Label(root, "Title", textFont, 44f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(900f, 60f));
            title.text = "출발 전 무기 선택";
            var hint = HudFactory.Label(root, "Hint", textFont, 20f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(900f, 30f));
            hint.text = "2·3번 칸을 눌러 무기를 고르세요. 1번은 권총 고정, 4번은 보급 무기 전용입니다";
            hint.color = MutedText;

            // 4칸
            var keys = new PlayerController.InputPlayerSettings().slotKeys;
            const float cardWidth = 260f, cardHeight = 150f, gap = 24f;
            float rowWidth = cardWidth * GunSlot.SlotCount + gap * (GunSlot.SlotCount - 1);
            var row = HudFactory.Rect(root, "Slots", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(rowWidth, cardHeight));
            slotViews = new SlotView[GunSlot.SlotCount];
            for (int i = 0; i < slotViews.Length; i++)
            {
                var rt = HudFactory.Rect(row, "Slot" + i, Vector2.zero, Vector2.zero, new Vector2(i * (cardWidth + gap), 0f), new Vector2(cardWidth, cardHeight));
                var view = new SlotView { Index = i };
                view.Background = rt.gameObject.AddComponent<Image>();
                view.Background.color = CardColor;
                view.Button = rt.gameObject.AddComponent<Button>();
                view.Button.targetGraphic = view.Background;
                int loadoutIndex = IndexOfLoadout(i);
                if (loadoutIndex >= 0)
                    view.Button.onClick.AddListener(() => { activeSlot = loadoutIndex; Refresh(); });

                view.Frame = HudFactory.NewImage(HudFactory.Rect(rt, "Active", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(cardWidth, 5f)), null, AimReticleHud.Cyan);
                view.Key = HudFactory.Label(rt, "Key", numberFont, 26f, TextAlignmentOptions.TopLeft,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(60f, 34f));
                view.Key.text = i < keys.Length ? HudFactory.KeyName(keys[i]) : (i + 1).ToString();
                view.Icon = HudFactory.NewImage(HudFactory.Rect(rt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(20f, 12f), new Vector2(160f, 48f)), null, HudFactory.TextColor);
                view.Icon.preserveAspect = true;
                view.Name = HudFactory.Label(rt, "Name", textFont, 28f, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(cardWidth - 20f, 30f));
                view.Note = HudFactory.Label(rt, "Note", textFont, 18f, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(cardWidth - 20f, 24f));
                view.Note.color = MutedText;
                slotViews[i] = view;
            }

            // 후보 목록
            candidateTitle = HudFactory.Label(root, "CandidateTitle", textFont, 26f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -400f), new Vector2(900f, 36f));
            candidateList = HudFactory.Rect(root, "Candidates", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -450f), new Vector2(rowWidth, 220f));
            var grid = candidateList.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = CandidateSize;
            grid.spacing = new Vector2(20f, 16f);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            // 출발
            var start = CreateButton(root, "StartButton", new Vector2(320f, 72f), ButtonColor, out var startLabel);
            var startRt = (RectTransform)start.transform;
            startRt.anchorMin = startRt.anchorMax = new Vector2(0.5f, 0f);
            startRt.pivot = new Vector2(0.5f, 0f);
            startRt.anchoredPosition = new Vector2(0f, 110f);
            startLabel.text = "출발";
            startLabel.fontSize = 32f;
            start.onClick.AddListener(Confirm);

            canvas.gameObject.SetActive(false);
        }

        private Button CreateButton(Transform parent, string name, Vector2 size, Color color, out TextMeshProUGUI label)
        {
            var rt = HudFactory.Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            label = HudFactory.Label(rt, "Label", textFont, 24f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            return button;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
        #endregion
    }
}
