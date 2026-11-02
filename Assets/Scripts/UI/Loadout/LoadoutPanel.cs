using System.Collections;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
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
    /// 스테이지 시작 전 무기 선택 화면. GameFlowUiBootstrap 이 RunDirector 가 있는 씬에 자동으로 붙인다 (씬에 직접 둬도 된다).
    /// 열리는 때 — 모두 [출발]을 누를 때까지 라운드 시작을 붙잡는다 (IRoundStartGate):
    ///  - 씬 시작 (openOnStart) → [출발] = RunDirector.StartRun
    ///  - 스테이지 클리어 후 강화 선택을 마침 (RewardSelectionFinishedEvent, Reward 상태 = 시간 정지 그대로) → [출발] = 다음 라운드 진행
    ///  - 게임 오버 → 재시작(RunDirector.ReturnToReady, GameOver → Ready) → [출발] = StartRun
    /// [출발] 뒤 라운드 정리(RoundCleanupEvent)에서 모든 총이 시작 탄약으로 돌아가고 보급 무기는 칸에서 사라진다 (Gun·GunSlot).
    ///  - 4칸: 1번 Sidearm(고정) · 2·3번 선택 · 4번 "보급 전용"(선택 불가)
    ///  - 칸 클릭 → 후보: Category = Loadout · 해금(IWeaponUnlocks) · 다른 칸에 없는 총 + [비우기] + 잠긴 총(자물쇠, UnlockOrder 번째 해금)
    ///  - 2·3번 중 한 칸은 채워야 출발. 마지막 선택은 PlayerPrefs(Loadout.Slot1, Loadout.Slot2, 0 = 빈 칸)
    /// 겉모양은 GameFlowUiSkin(SCI-FI UI Pack) 을 쓰고, 스킨이 비었으면 단색으로 그린다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadoutPanel : MonoBehaviour, IRoundStartGate
    {
        public const string PrefsKeyPrefix = "Loadout.Slot";

        private enum Mode { Departure, NextStage }

        private static readonly Color Dim = new Color(0f, 0.02f, 0.04f, 0.82f);
        private static readonly Color CardColor = new Color(0.05f, 0.12f, 0.16f, 0.92f);
        private static readonly Color LockedColor = new Color(0.04f, 0.07f, 0.09f, 0.85f);
        private static readonly Color MutedText = new Color(0.6f, 0.7f, 0.75f, 1f);
        private static readonly Color WarningColor = new Color(1f, 0.72f, 0.3f, 1f);
        private static readonly Color ButtonColor = new Color(0.15f, 0.55f, 0.75f, 1f);
        /// <summary>후보 버튼 (3열 × 360 + 간격 2 × 20 = 1120)</summary>
        private static readonly Vector2 CandidateSize = new Vector2(360f, 92f);

        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private GunSlot gunSlot;
        [Tooltip("비워 두면 씬에서 찾는다")]
        [SerializeField] private RunDirector runDirector;
        [Tooltip("씬 시작 시 자동으로 열고 [출발]까지 라운드 시작을 붙잡는다. 끄면 이 씬은 바로 시작한다")]
        [SerializeField] private bool openOnStart = true;
        [Tooltip("스테이지 클리어 후 강화 선택을 마치면 열고 다음 라운드를 붙잡는다")]
        [SerializeField] private bool openBetweenStages = true;
        [Tooltip("비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset numberFont;
        [Tooltip("한글 글자용 (NotoSansKR SDF). 비워 두면 GameFlowUiSkin 의 글꼴")]
        [SerializeField] private TMP_FontAsset textFont;

        private sealed class SlotView
        {
            public int Index;
            public Image Background, Frame, Icon;
            public TextMeshProUGUI Key, Name, Note;
            public Button Button;
        }

        private GameFlowUiSkin skin;
        private Canvas canvas;
        private SlotView[] slotViews;
        private RectTransform candidateList;
        private TextMeshProUGUI titleText, hintText, candidateTitle, startLabel, emptyWarningText;
        private readonly List<GameObject> candidateItems = new List<GameObject>();
        private PlayerController player;

        /// <summary>LoadoutSlots 순서의 선택 무기 Id</summary>
        private int[] selection;
        private int activeSlot;
        private Mode mode;
        private Button startButton;
        /// <summary>[출발]을 누를 때까지 라운드 시작을 붙잡는 중</summary>
        private bool holding;
        /// <summary>GameOver → Ready 를 받음. 같은 프레임에 바로 다시 시작(RestartRun)했는지 보고 연다</summary>
        private bool reopenPending;

        public bool IsOpen { get; private set; }
        public bool HoldsStart => isActiveAndEnabled && holding;

        #region 유니티 메시지
        private void Awake()
        {
            if (gunSlot == null) gunSlot = FindAnyObjectByType<GunSlot>();
            if (runDirector == null) runDirector = FindAnyObjectByType<RunDirector>();
            if (gunSlot != null) player = gunSlot.GetComponent<PlayerController>();
            skin = GameFlowUiSkin.Current;
            if (textFont == null) textFont = skin.textFont;
            if (numberFont == null) numberFont = skin.numberFont;
        }

        private void OnEnable()
        {
            ServiceLocator.Register<IRoundStartGate>(this);
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<RewardSelectionFinishedEvent>.Subscribe(OnRewardSelectionFinished);
            // RunDirector.Start(한 프레임 뒤 자동 시작)보다 먼저 붙잡는다
            if (openOnStart && (runDirector == null || runDirector.IsRunning == false)) holding = true;
        }

        private IEnumerator Start()
        {
            // GunSlot.Start(시작 무기 배치)가 끝난 뒤 연다
            yield return null;
            if (holding && IsOpen == false) Open();
        }

        private void OnDisable()
        {
            ServiceLocator.Unregister<IRoundStartGate>(this);
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<RewardSelectionFinishedEvent>.Unsubscribe(OnRewardSelectionFinished);
            reopenPending = false;
            holding = false;
            if (IsOpen) Close();
        }

        private void Update()
        {
            if (reopenPending == false) return;
            reopenPending = false;
            if (runDirector != null && runDirector.CurrentState == GameState.Ready && runDirector.IsRunning == false) Open();
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }
        #endregion

        #region 외부 API
        /// <summary>연다. 라운드 진행 중(보상 단계)이면 "다음 스테이지 준비", 아니면 "출발 전". [출발]까지 시작을 붙잡는다</summary>
        public void Open()
        {
            if (IsOpen) return;
            if (gunSlot == null)
            {
                Debug.LogWarning($"{name}: GunSlot 을 찾지 못해 무기 선택 화면을 열 수 없습니다", this);
                holding = false;
                return;
            }

            if (canvas == null) Build();
            EnsureEventSystem();
            mode = runDirector != null && runDirector.IsRunning ? Mode.NextStage : Mode.Departure;
            selection = LoadSelection();
            activeSlot = 0;
            holding = true;
            IsOpen = true;
            canvas.gameObject.SetActive(true);
            if (player != null) player.SetMenuInputBlocked(true);
            Refresh();
        }

        /// <summary>선택을 적용하고 닫는다. 출발 전이면 라운드를 시작하고, 보상 단계면 RunDirector 가 다음 라운드로 넘어간다</summary>
        public void Confirm()
        {
            if (IsOpen == false || HasAnySelection() == false) return;
            if (gunSlot.ApplyLoadout(selection) == false) return;
            SaveSelection();
            Close();
            holding = false;
            if (runDirector != null && runDirector.IsRunning == false && runDirector.CurrentState == GameState.Ready) runDirector.StartRun();
        }
        #endregion

        #region 선택
        private void Close()
        {
            IsOpen = false;
            if (canvas != null) canvas.gameObject.SetActive(false);
            if (player != null) player.SetMenuInputBlocked(false);
        }

        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            if (e.Previous == GameState.GameOver && e.Current == GameState.Ready) reopenPending = true;
            // 게임 오버로 보상 단계가 끊기면 붙잡지 않는다 (게임 오버 화면이 재시작을 맡는다)
            if (e.Current == GameState.GameOver)
            {
                holding = false;
                if (IsOpen) Close();
            }
        }

        /// <summary>강화 선택을 마침 → 다음 스테이지 무기 선택 (RunDirector 는 [출발]까지 Reward 상태로 기다린다)</summary>
        private void OnRewardSelectionFinished(RewardSelectionFinishedEvent e)
        {
            if (openBetweenStages == false || runDirector == null || runDirector.IsRunning == false) return;
            if (runDirector.CurrentState != GameState.Reward) return;
            Open();
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

        /// <summary>아직 잠긴 Loadout 총. 해금 순서(UnlockOrder) 순</summary>
        private List<Gun> LockedGuns()
        {
            var list = new List<Gun>();
            foreach (var gun in gunSlot.AllGuns)
                if (gun != null && GunSlot.CategoryOf(gun) == WeaponCategory.Loadout && GunSlot.IsUnlocked(gun.WeaponId) == false) list.Add(gun);
            list.Sort((a, b) => UnlockOrderOf(a) != UnlockOrderOf(b) ? UnlockOrderOf(a).CompareTo(UnlockOrderOf(b)) : a.WeaponId.CompareTo(b.WeaponId));
            return list;
        }

        private static int UnlockOrderOf(Gun gun) => gun.Def != null ? gun.Def.UnlockOrder : 0;

        /// <summary>지금 칸을 비울 수 있는지 (다른 칸이 하나라도 차 있어야 한다)</summary>
        private bool CanClear(int slot)
        {
            for (int i = 0; i < selection.Length; i++)
                if (i != slot && selection[i] != 0) return true;
            return false;
        }

        private bool HasAnySelection()
        {
            foreach (int id in selection)
                if (id != 0) return true;
            return false;
        }

        private void Pick(int weaponId)
        {
            selection[activeSlot] = weaponId;
            // 다음 빈 칸이 아니라 다음 칸으로 넘겨 두 칸을 빠르게 고를 수 있게
            if (activeSlot < selection.Length - 1) activeSlot++;
            Refresh();
        }

        /// <summary>저장된 선택(0 = 일부러 비운 칸) → 유효하지 않으면 지금 GunSlot 2·3번 칸 → 그래도 비면 후보 앞에서부터</summary>
        private int[] LoadSelection()
        {
            int count = GunSlot.LoadoutSlots.Count;
            var ids = new int[count];
            var keepEmpty = new bool[count];
            for (int i = 0; i < count; i++)
            {
                string key = PrefsKeyPrefix + GunSlot.LoadoutSlots[i];
                int saved = PlayerPrefs.GetInt(key, 0);
                keepEmpty[i] = saved == 0 && PlayerPrefs.HasKey(key);
                Gun current = gunSlot.GetGun(GunSlot.LoadoutSlots[i]);
                ids[i] = keepEmpty[i] ? 0
                    : IsSelectable(saved, ids, i) ? saved
                    : current != null && IsSelectable(current.WeaponId, ids, i) ? current.WeaponId : 0;
            }

            selection = ids;
            for (int i = 0; i < count; i++)
            {
                if (ids[i] != 0 || (keepEmpty[i] && HasAnySelection())) continue;
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
            int nextRound = runDirector != null ? runDirector.CurrentRound + 1 : 1;
            titleText.text = mode == Mode.NextStage ? $"스테이지 {nextRound} 준비" : "출발 전 무기 선택";
            hintText.text = mode == Mode.NextStage
                ? "스테이지 클리어! 다음 스테이지에 들고 갈 무기를 고르세요. 모든 총은 새 탄약으로, 보급 무기는 사라집니다"
                : "2·3번 칸을 눌러 무기를 고르세요. 1번은 권총 고정, 4번은 보급 무기 전용입니다";
            startLabel.text = mode == Mode.NextStage ? "다음 스테이지" : "출발";

            foreach (var view in slotViews)
            {
                int loadoutIndex = IndexOfLoadout(view.Index);
                bool selectable = loadoutIndex >= 0;
                bool active = selectable && loadoutIndex == activeSlot;
                view.Button.interactable = selectable;
                GameFlowUiSkin.Paint(view.Background, active ? skin.slotSelected : skin.slot, selectable ? CardColor : LockedColor);
                if (selectable == false && skin.slot != null) view.Background.color = new Color(1f, 1f, 1f, 0.55f);
                // 스킨이 있으면 선택 칸 스프라이트가 강조를 맡는다
                view.Frame.enabled = active && skin.slotSelected == null;

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
                if (view.Index != GunSlot.SupplySlot) view.Name.text = gun != null ? gun.DisplayName : selectable ? "비어 있음" : "-";
                if (selectable && gun == null && active == false) view.Name.color = MutedText;

                // 아이콘이 있으면 아이콘 아래에 이름, 없으면 이름만 가운데
                Sprite icon = IconOf(gun);
                view.Icon.sprite = icon;
                view.Icon.enabled = icon != null;
                view.Icon.color = view.Name.color;
                // 글자 칸 높이는 글꼴 줄 높이(NotoSansKR ≈ 1.45 × 크기) 이상이어야 한다 (모자라면 TMP 가 줄임표로 글자를 통째로 숨김)
                view.Name.fontSize = icon != null ? 22f : 28f;
                view.Name.rectTransform.anchoredPosition = new Vector2(0f, icon != null ? -22f : 4f);
                view.Name.rectTransform.sizeDelta = new Vector2(view.Name.rectTransform.sizeDelta.x, icon != null ? 36f : 44f);
                view.Icon.rectTransform.anchoredPosition = new Vector2(20f, icon != null ? 24f : 12f);
            }

            if (startButton != null) startButton.interactable = HasAnySelection();
            if (emptyWarningText != null) emptyWarningText.gameObject.SetActive(HasFillableEmptySlot());
            RefreshCandidates();
        }

        /// <summary>비어 있는 2·3번 칸 중 채울 수 있는 해금 무기가 남은 칸이 있는지 (해금 전이라 채울 무기가 없으면 경고하지 않는다)</summary>
        private bool HasFillableEmptySlot()
        {
            for (int i = 0; i < selection.Length; i++)
                if (selection[i] == 0 && Candidates(i).Count > 0) return true;
            return false;
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
                var button = AddCandidate("Weapon" + id, gun, TitleOf(gun), StatsOf(gun), id == current, false);
                button.onClick.AddListener(() => Pick(id));
            }

            if (CanClear(activeSlot))
                AddCandidate("Clear", null, "비우기", "이 칸을 빈 칸으로", current == 0, false).onClick.AddListener(() => Pick(0));

            foreach (var gun in LockedGuns())
                AddCandidate("Locked" + gun.WeaponId, gun, TitleOf(gun),
                    $"잠김 · 스테이지 클리어 {UnlockOrderOf(gun)}회째에 해금", false, true);
        }

        /// <summary>이름 + 작은 글씨 사격 방식 (능력치 줄이 넘치지 않게 이름 줄로 올림)</summary>
        private static string TitleOf(Gun gun) =>
            gun.Def == null ? gun.DisplayName
                : $"{gun.DisplayName}  <size=65%><color=#99B3BF>{(gun.Def.FireMode == FireMode.Auto ? "자동" : "단발")}</color></size>";

        private static string StatsOf(Gun gun) =>
            gun.Def != null ? $"피해 {gun.Def.Damage} · 연사 {gun.Def.FireRate:0} · 탄창 {gun.Def.MagazineSize}" : "";

        /// <summary>후보 카드 하나. 왼쪽 아이콘, 오른쪽 이름·설명 (아이콘이 없으면 글자를 왼쪽부터). locked 면 누를 수 없고 자물쇠 표시</summary>
        private Button AddCandidate(string objectName, Gun gun, string title, string detail, bool chosen, bool locked)
        {
            bool skinned = skin.slot != null;
            Color textColor = chosen ? (skinned ? AimReticleHud.Cyan : Color.black) : locked ? MutedText : HudFactory.TextColor;
            var button = CreateButton(candidateList, objectName, CandidateSize, out var label);
            // 잠긴 카드도 보통 카드와 같은 틀(같은 크기·테두리)을 어둡게 쓰고, 자물쇠로 구분한다
            var background = (Image)button.targetGraphic;
            GameFlowUiSkin.Paint(background, chosen ? skin.slotSelected : skin.slot, chosen ? AimReticleHud.Cyan : locked ? LockedColor : CardColor);
            if (locked && skin.slot != null) background.color = new Color(0.55f, 0.55f, 0.55f, 0.85f);
            GameFlowUiSkin.SetStates(button, skin.slotPressed, null);
            button.interactable = locked == false;

            Sprite icon = IconOf(gun);
            float textX = icon != null ? 124f : 16f;
            if (icon != null)
            {
                var image = HudFactory.NewImage(HudFactory.Rect(button.transform, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(12f, 0f), new Vector2(104f, 40f)), icon, locked ? new Color(1f, 1f, 1f, 0.3f) : textColor);
                image.preserveAspect = true;
            }
            if (locked && skin.lockIcon != null)
            {
                var lockImage = HudFactory.NewImage(HudFactory.Rect(button.transform, "Lock", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(-14f, 0f), new Vector2(40f, 40f)), skin.lockIcon, Color.white);
                lockImage.preserveAspect = true;
            }
            label.text = title;
            label.color = textColor;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            // 글자 칸 높이 ≥ 글꼴 줄 높이(≈ 1.45 × 크기). 잠긴 카드는 오른쪽 자물쇠 자리를 비운다
            float rightGap = locked ? 60f : 14f;
            label.rectTransform.anchoredPosition = new Vector2(textX, 14f);
            label.rectTransform.sizeDelta = new Vector2(CandidateSize.x - textX - rightGap, 40f);
            var info = HudFactory.Label(button.transform, "Stats", textFont, 15f, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(textX, -20f), new Vector2(CandidateSize.x - textX - rightGap, 26f));
            info.text = detail;
            info.color = chosen && skinned == false ? new Color(0f, 0f, 0f, 0.75f) : MutedText;
            candidateItems.Add(button.gameObject);
            return button;
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
            if (skin.window != null)
                GameFlowUiSkin.Paint(HudFactory.NewImage(HudFactory.Rect(root, "Window", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(1300f, 940f)), null, Color.clear), skin.window, Color.clear);

            titleText = HudFactory.Label(root, "Title", textFont, 44f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(900f, 72f));
            hintText = HudFactory.Label(root, "Hint", textFont, 20f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1200f, 34f));
            hintText.color = MutedText;

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
                GameFlowUiSkin.SetStates(view.Button, skin.slotPressed, null);
                int loadoutIndex = IndexOfLoadout(i);
                if (loadoutIndex >= 0)
                    view.Button.onClick.AddListener(() => { activeSlot = loadoutIndex; Refresh(); });

                view.Frame = HudFactory.NewImage(HudFactory.Rect(rt, "Active", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(cardWidth, 5f)), null, AimReticleHud.Cyan);
                view.Key = HudFactory.Label(rt, "Key", numberFont, 26f, TextAlignmentOptions.TopLeft,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(60f, 34f));
                view.Key.text = i < keys.Length ? HudFactory.KeyName(keys[i]) : (i + 1).ToString();
                view.Icon = HudFactory.NewImage(HudFactory.Rect(rt, "Icon", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(20f, 12f), new Vector2(160f, 48f)), null, HudFactory.TextColor);
                view.Icon.preserveAspect = true;
                view.Name = HudFactory.Label(rt, "Name", textFont, 28f, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), new Vector2(cardWidth - 20f, 30f));
                view.Note = HudFactory.Label(rt, "Note", textFont, 18f, TextAlignmentOptions.Center,
                    new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(cardWidth - 20f, 30f));
                view.Note.color = MutedText;
                slotViews[i] = view;
            }

            // 후보 목록
            candidateTitle = HudFactory.Label(root, "CandidateTitle", textFont, 26f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -396f), new Vector2(900f, 44f));
            candidateList = HudFactory.Rect(root, "Candidates", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -450f), new Vector2(rowWidth, 220f));
            var grid = candidateList.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = CandidateSize;
            grid.spacing = new Vector2(20f, 16f);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            // 출발
            var start = CreateButton(root, "StartButton", new Vector2(340f, 110f), out startLabel);
            GameFlowUiSkin.Paint((Image)start.targetGraphic, skin.primaryButton, ButtonColor);
            GameFlowUiSkin.SetStates(start, skin.primaryButtonPressed, skin.disabledButton);
            var startRt = (RectTransform)start.transform;
            startRt.anchorMin = startRt.anchorMax = new Vector2(0.5f, 0f);
            startRt.pivot = new Vector2(0.5f, 0f);
            startRt.anchoredPosition = new Vector2(0f, 100f);
            startLabel.fontSize = 32f;
            start.onClick.AddListener(Confirm);
            startButton = start;

            // 빈 칸 경고 (출발 버튼 위). 출발은 막지 않는다
            emptyWarningText = HudFactory.Label(root, "EmptySlotWarning", textFont, 22f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 226f), new Vector2(1100f, 36f));
            emptyWarningText.text = "[주의] 비어 있는 무기 칸이 있습니다. 그대로 출발하면 그 칸은 사용할 수 없습니다";
            emptyWarningText.color = WarningColor;
            emptyWarningText.gameObject.SetActive(false);

            canvas.gameObject.SetActive(false);
        }

        private Button CreateButton(Transform parent, string name, Vector2 size, out TextMeshProUGUI label)
        {
            var rt = HudFactory.Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var image = rt.gameObject.AddComponent<Image>();
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            label = HudFactory.Label(rt, "Label", textFont, 24f, TextAlignmentOptions.Center,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            return button;
        }

        internal static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }
        #endregion
    }
}
