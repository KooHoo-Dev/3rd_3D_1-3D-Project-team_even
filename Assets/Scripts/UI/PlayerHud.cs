using DG.Tweening;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Player;
using HybridShooter.Weapon;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 플레이어 기본 HUD. Player 루트에 붙인다. 시작할 때 화면 맨 위 Canvas 를 코드로 만든다.
    ///  - 왼쪽 아래 맨 위: 남은 목숨 ×n / 최대 (PlayerLivesChangedEvent). 줄면 깜빡이고 마지막이면 빨강. 사망·부활 중에도 보인다
    ///  - 왼쪽 아래: 체력 바·숫자 (PlayerHpChangedEvent). 저체력이면 빨갛게 깜빡임
    ///  - 체력 아래: 수류탄·회복 팩 개수/최대 (GrenadeCountChangedEvent, HealPackCountChangedEvent). 1개 이상일 때만
    ///  - 보급으로 수류탄·회복 팩·예비 탄창을 받으면 해당 아이콘이 통 튀고 "+N" 이 떠오른다 (SupplyCollectedEvent, RoundSupplyGrantedEvent — 실제 지급량, DOTween)
    ///  - 가운데 아래: 스태미나 (PlayerStaminaChangedEvent). 움직이는 동안, 또는 가득 찰 때까지만. 가득 차면 서서히 사라짐
    ///  - 오른쪽 아래: 무기 이름·사격 모드, 탄창 안 탄약/탄창 크기 (AmmoChangedEvent), 예비 탄창 칸·개수/최대 (별도 줄), 탄창 눈금 바.
    ///    재장전 중엔 바가 차오르고, 취소되면 바로 원래대로 (ReloadStarted/FinishedEvent). 예비 탄창 없이 R → 예비 탄창 줄이 빨갛게 깜빡임 (ReloadDeniedEvent)
    /// 스코프 화면에서는 탄약만 남기고, 사망 때는 목숨만 남기고, 강화 선택·게임 오버·일시정지 때는 모두 숨긴다.
    /// 그림은 SCI-FI UI Pack (컴포넌트를 붙일 때 자동 연결), 수류탄 아이콘은 실행 중에 그린다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHud : MonoBehaviour
    {
        private const string HpFillGuid = "55bc530dc21c6254f8c43ccfb7116406";       // Linearprogress005White
        private const string HpBackGuid = "915a1f519ce975e47abc655acd57a737";       // Linearprogress005White_bg
        private const string AmmoFillGuid = "e2d1811d1caaa0840ab1bccf77ac6500";     // Linearprogress004White (눈금)
        private const string AmmoBackGuid = "f98c40a69a4785d42838b5c78a3684d9";     // Linearprogress004White_bg
        private const string StaminaFillGuid = "18af17b6ac2e04f4cada01278d39e843";  // Linearprogress001White
        private const string StaminaBackGuid = "393bc0395f3730d4c98603dbbe66953c";  // Linearprogress001White_bg
        private const string HealIconGuid = "8144acae632f40449aa9f42c8ccc8074";     // ICON_Health
        private const string NumberFontGuid = "4d0bb7337f597634b94b9005947e8e28";   // Aldrich-Regular SDF
        private const string LabelFontGuid = "7fd30bfeb37f5884dabd391567e6530d";    // Play-Bold SDF

        private static readonly Color Amber = new Color(1f, 0.72f, 0.2f, 1f);
        private static readonly Color Red = new Color(1f, 0.27f, 0.22f, 1f);
        private static readonly Color TextColor = new Color(0.85f, 0.97f, 1f, 1f);
        private static readonly Color BackColor = new Color(1f, 1f, 1f, 0.3f);

        [SerializeField] private PlayerController player;
        [SerializeField] private GunSlot gunSlot;
        [SerializeField] private PlayerAimView aimView;

        [Header("에셋 (SCI-FI UI Pack)")]
        [SerializeField] private Sprite hpFill;
        [SerializeField] private Sprite hpBack;
        [SerializeField] private Sprite ammoFill;
        [SerializeField] private Sprite ammoBack;
        [SerializeField] private Sprite staminaFill;
        [SerializeField] private Sprite staminaBack;
        [SerializeField] private Sprite healIcon;
        [SerializeField] private TMP_FontAsset numberFont;
        [SerializeField] private TMP_FontAsset labelFont;

        [Header("경고")]
        [Tooltip("체력 비율이 이 값 이하면 빨갛게 깜빡인다")]
        [SerializeField, Range(0f, 1f)] private float lowHealthRatio = 0.3f;
        [Tooltip("탄창 비율이 이 값 이하면 주황")]
        [SerializeField, Range(0f, 1f)] private float lowAmmoRatio = 0.25f;
        [Tooltip("깜빡임 속도 (회/초)")]
        [SerializeField] private float blinkRate = 2.5f;
        [Tooltip("목숨이 줄어든 뒤 깜빡이는 시간(초)")]
        [SerializeField] private float livesFlashTime = 1.2f;
        [Tooltip("예비 탄창 없이 재장전을 누른 뒤 예비 탄창 줄이 깜빡이는 시간(초)")]
        [SerializeField] private float reloadDeniedFlashTime = 0.6f;

        [Header("스태미나")]
        [Tooltip("멈춰 있고 가득 찼을 때 사라지는 시간(초)")]
        [SerializeField] private float staminaFadeTime = 0.5f;

        [Header("보급 획득 피드백")]
        [Tooltip("아이콘이 튀는 크기 (1 + 이 값 배)")]
        [SerializeField, Range(0f, 1f)] private float gainPunch = 0.35f;
        [Tooltip("튀는 시간 / \"+N\" 이 떠올라 사라지는 시간(초, 실제 시간)")]
        [SerializeField, Min(0.1f)] private float gainDuration = 0.5f;
        [Tooltip("\"+N\" 이 떠오르는 높이(px)")]
        [SerializeField] private float gainRise = 36f;

        private Canvas canvas;
        private CanvasGroup rootGroup, mainGroup, vitalsGroup, staminaGroup, livesGroup;
        private Image hpBar, ammoBar, staminaBar;
        private TextMeshProUGUI hpText, ammoText, infoText, statusText, grenadeText, healText, livesText;
        private Image livesIcon;
        private GameObject grenadeSlot, healSlot;
        private RectTransform grenadeIconRect, healIconRect;
        private TextMeshProUGUI spareText;
        private Image[] sparePips;

        private int hp = 1, maxHp = 1;
        private int ammo, magazine;
        private int spare, maxSpare;
        private bool infiniteSpare;
        private int shownSpare = -1, shownMaxSpare = -1;
        private bool shownInfiniteSpare;
        private float reloadDeniedUntil;
        private float stamina = 1f, maxStamina = 1f;
        private bool reloading;
        private float reloadStart, reloadDuration;
        private bool stateHidden;
        private Gun shownGun;
        private string shownInfo;
        private int shownHp = -1, shownAmmo = -1, shownMagazine = -1;
        private int lives = 1, maxLives = 1, shownLives = -1, shownMaxLives = -1;
        private float livesFlashUntil;

        private void Reset() => AssignAssets();

        private void AssignAssets()
        {
#if UNITY_EDITOR
            if (hpFill == null) hpFill = Load<Sprite>(HpFillGuid);
            if (hpBack == null) hpBack = Load<Sprite>(HpBackGuid);
            if (ammoFill == null) ammoFill = Load<Sprite>(AmmoFillGuid);
            if (ammoBack == null) ammoBack = Load<Sprite>(AmmoBackGuid);
            if (staminaFill == null) staminaFill = Load<Sprite>(StaminaFillGuid);
            if (staminaBack == null) staminaBack = Load<Sprite>(StaminaBackGuid);
            if (healIcon == null) healIcon = Load<Sprite>(HealIconGuid);
            if (numberFont == null) numberFont = Load<TMP_FontAsset>(NumberFontGuid);
            if (labelFont == null) labelFont = Load<TMP_FontAsset>(LabelFontGuid);
#endif
        }

#if UNITY_EDITOR
        private static T Load<T>(string guid) where T : Object
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
        }
#endif

        #region 유니티 메시지
        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (gunSlot == null) gunSlot = GetComponent<GunSlot>();
            if (aimView == null) aimView = GetComponent<PlayerAimView>();
            AssignAssets();
            Build();
        }

        private void OnEnable()
        {
            EventBus<PlayerHpChangedEvent>.Subscribe(OnHpChanged);
            EventBus<PlayerStaminaChangedEvent>.Subscribe(OnStaminaChanged);
            EventBus<AmmoChangedEvent>.Subscribe(OnAmmoChanged);
            EventBus<ReloadStartedEvent>.Subscribe(OnReloadStarted);
            EventBus<ReloadFinishedEvent>.Subscribe(OnReloadFinished);
            EventBus<ReloadDeniedEvent>.Subscribe(OnReloadDenied);
            EventBus<GrenadeCountChangedEvent>.Subscribe(OnGrenadeCountChanged);
            EventBus<HealPackCountChangedEvent>.Subscribe(OnHealPackCountChanged);
            EventBus<PlayerLivesChangedEvent>.Subscribe(OnLivesChanged);
            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<SupplyCollectedEvent>.Subscribe(OnSupplyCollected);
            EventBus<RoundSupplyGrantedEvent>.Subscribe(OnRoundSupplyGranted);
            if (gunSlot != null) gunSlot.GunChanged += OnGunChanged;
            if (canvas != null) canvas.gameObject.SetActive(true);
        }

        private void OnDisable()
        {
            EventBus<PlayerHpChangedEvent>.Unsubscribe(OnHpChanged);
            EventBus<PlayerStaminaChangedEvent>.Unsubscribe(OnStaminaChanged);
            EventBus<AmmoChangedEvent>.Unsubscribe(OnAmmoChanged);
            EventBus<ReloadStartedEvent>.Unsubscribe(OnReloadStarted);
            EventBus<ReloadFinishedEvent>.Unsubscribe(OnReloadFinished);
            EventBus<ReloadDeniedEvent>.Unsubscribe(OnReloadDenied);
            EventBus<GrenadeCountChangedEvent>.Unsubscribe(OnGrenadeCountChanged);
            EventBus<HealPackCountChangedEvent>.Unsubscribe(OnHealPackCountChanged);
            EventBus<PlayerLivesChangedEvent>.Unsubscribe(OnLivesChanged);
            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<SupplyCollectedEvent>.Unsubscribe(OnSupplyCollected);
            EventBus<RoundSupplyGrantedEvent>.Unsubscribe(OnRoundSupplyGranted);
            if (gunSlot != null) gunSlot.GunChanged -= OnGunChanged;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (canvas != null) Destroy(canvas.gameObject);
        }

        /// <summary>체력·스태미나 시작값은 이벤트 없이 바로 읽는다 (체력은 바뀔 때만 발행된다)</summary>
        private void Start()
        {
            if (player != null)
            {
                hp = player.CurrentHealth;
                maxHp = player.MaxHealth;
                stamina = player.CurrentStamina;
                maxStamina = player.MaxStamina;
                lives = player.Lives;
                maxLives = player.MaxLives;
            }
            if (gunSlot != null) OnGunChanged(gunSlot.CurrentIndex, gunSlot.CurrentGun);
        }

        private void Update()
        {
            // 연출(보스 등장·최후 시네마틱) 중에는 숨긴다
            bool hidden = stateHidden || (player != null && player.IsInCinematic);
            rootGroup.alpha = hidden ? 0f : 1f;
            if (hidden) return;
            UpdateLives(); // 사망·부활 중에도 보인다 (곧 부활할지 알려야 하므로)

            bool alive = player == null || player.IsAlive;
            mainGroup.alpha = alive ? 1f : 0f;
            if (alive == false) return;

            bool scoped = aimView != null && aimView.Scoped;
            float blink = 0.55f + 0.45f * Mathf.Cos(Time.time * blinkRate * Mathf.PI * 2f);
            vitalsGroup.alpha = scoped ? 0f : 1f;

            UpdateHealth(blink);
            UpdateStamina(scoped);
            UpdateAmmo(blink);
            UpdateSpare();
            UpdateInfo();
        }
        #endregion

        #region 갱신
        private void UpdateHealth(float blink)
        {
            float ratio = maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f;
            hpBar.fillAmount = ratio;
            bool low = ratio <= lowHealthRatio;
            Color color = low ? WithAlpha(Red, blink) : AimReticleHud.Cyan;
            hpBar.color = color;
            hpText.color = low ? color : TextColor;
            if (shownHp != hp)
            {
                shownHp = hp;
                hpText.SetText("{0}", hp);
            }
        }

        /// <summary>남은 목숨 ×n / 최대. 줄어든 직후 깜빡이고, 마지막 목숨(또는 0)이면 빨강</summary>
        private void UpdateLives()
        {
            livesGroup.alpha = aimView != null && aimView.Scoped ? 0f : 1f; // 스코프 화면에서는 숨김
            if (shownLives != lives || shownMaxLives != maxLives)
            {
                shownLives = lives;
                shownMaxLives = maxLives;
                livesText.SetText("×{0}<size=60%><color=#9FE8F5> / {1}</color></size>", lives, maxLives);
            }

            bool last = lives <= 1;
            Color color = last ? Red : AimReticleHud.Cyan;
            if (Time.time < livesFlashUntil)
                color = Mathf.Repeat(Time.time * 6f, 1f) < 0.5f ? Color.white : Red;
            livesIcon.color = color;
            livesText.color = last || Time.time < livesFlashUntil ? color : TextColor;
        }

        /// <summary>움직이는 동안, 또는 가득 찰 때까지 보인다. 멈춰서 가득 차면 staminaFadeTime 동안 사라진다</summary>
        private void UpdateStamina(bool scoped)
        {
            float ratio = maxStamina > 0f ? Mathf.Clamp01(stamina / maxStamina) : 1f;
            bool moving = player != null && player.HorizontalSpeed > 0.1f;
            bool show = scoped == false && (moving || ratio < 0.999f);
            float target = show ? 1f : 0f;
            float speed = show ? 8f : 1f / Mathf.Max(0.01f, staminaFadeTime);
            staminaGroup.alpha = Mathf.MoveTowards(staminaGroup.alpha, target, speed * Time.deltaTime);

            staminaBar.fillAmount = ratio;
            staminaBar.color = player != null && player.IsStaminaExhausted ? Amber : AimReticleHud.Cyan;
        }

        private void UpdateAmmo(float blink)
        {
            if (shownAmmo != ammo || shownMagazine != magazine)
            {
                shownAmmo = ammo;
                shownMagazine = magazine;
                ammoText.SetText("{0}<size=50%><color=#9FE8F5> / {1}</color></size>", ammo, magazine);
            }

            if (reloading)
            {
                float progress = reloadDuration > 0f ? Mathf.Clamp01((Time.time - reloadStart) / reloadDuration) : 1f;
                ammoBar.fillAmount = progress;
                ammoBar.color = WithAlpha(TextColor, 0.8f);
                ammoText.color = TextColor;
                SetStatus("RELOADING", WithAlpha(TextColor, blink));
                return;
            }

            float ratio = magazine > 0 ? (float)ammo / magazine : 0f;
            bool hasSpare = infiniteSpare || spare > 0;
            ammoBar.fillAmount = ratio;
            if (ammo <= 0 && magazine > 0)
            {
                Color color = WithAlpha(Red, blink);
                ammoBar.color = color;
                ammoText.color = color;
                SetStatus(hasSpare ? "RELOAD" : "NO AMMO", color);
            }
            else if (hasSpare == false)
            {
                // 지금 탄창이 마지막 (예비 탄창 없음)
                ammoBar.color = Amber;
                ammoText.color = ratio <= lowAmmoRatio ? Amber : TextColor;
                SetStatus("LAST MAG", Amber);
            }
            else if (ratio <= lowAmmoRatio)
            {
                ammoBar.color = Amber;
                ammoText.color = Amber;
                SetStatus("LOW AMMO", Amber);
            }
            else
            {
                ammoBar.color = AimReticleHud.Cyan;
                ammoText.color = TextColor;
                SetStatus(null, TextColor);
            }
        }

        /// <summary>
        /// 예비 탄창 줄 (탄창 안 탄약과 별개): 최대 개수만큼 칸 + "개수/최대". 칸이 너무 많으면 숫자만, 무한이면 INF.
        /// 0 개면 빨강, 1 개면 주황. 예비 탄창 없이 재장전을 누르면 잠깐 빨갛게 깜빡인다
        /// </summary>
        private void UpdateSpare()
        {
            if (shownSpare != spare || shownMaxSpare != maxSpare || shownInfiniteSpare != infiniteSpare)
            {
                shownSpare = spare;
                shownMaxSpare = maxSpare;
                shownInfiniteSpare = infiniteSpare;
                if (infiniteSpare) spareText.SetText("MAG <size=80%>INF</size>");
                else spareText.SetText("MAG {0}<size=70%><color=#9FE8F5>/{1}</color></size>", spare, maxSpare);

                bool pips = infiniteSpare == false && maxSpare <= sparePips.Length;
                for (int i = 0; i < sparePips.Length; i++)
                    sparePips[i].gameObject.SetActive(pips && i < maxSpare);
            }

            Color full = AimReticleHud.Cyan;
            Color text = TextColor;
            if (infiniteSpare == false && spare <= 0) full = text = Red;
            else if (infiniteSpare == false && spare == 1) full = text = Amber;
            if (Time.time < reloadDeniedUntil)
                full = text = Mathf.Repeat(Time.time * 6f, 1f) < 0.5f ? Color.white : Red;

            spareText.color = text;
            Color empty = WithAlpha(TextColor, 0.18f);
            for (int i = 0; i < sparePips.Length; i++)
                if (sparePips[i].gameObject.activeSelf) sparePips[i].color = i < spare ? full : empty;
        }

        /// <summary>무기 이름 · 사격 모드. 라이플은 B 로 모드가 바뀌므로 매 프레임 비교한다</summary>
        private void UpdateInfo()
        {
            string info = shownGun == null ? string.Empty : $"{WeaponLabel(shownGun)}   {FireModeLabel(shownGun)}";
            if (info == shownInfo) return;
            shownInfo = info;
            infoText.text = info;
        }

        private void SetStatus(string text, Color color)
        {
            bool on = string.IsNullOrEmpty(text) == false;
            if (statusText.gameObject.activeSelf != on) statusText.gameObject.SetActive(on);
            if (on == false) return;
            if (statusText.text != text) statusText.text = text;
            statusText.color = color;
        }

        private static Color WithAlpha(Color color, float alpha) { color.a = alpha; return color; }

        private static string WeaponLabel(Gun gun)
        {
            switch (gun)
            {
                case AssaultRifle _: return "ASSAULT RIFLE";
                case SniperRifle _: return "SNIPER RIFLE";
                case Rifle _: return "RIFLE";
                case Pistol _: return "PISTOL";
                case SMG _: return "SMG";
                case ShotGun _: return "SHOTGUN";
                case RocketLauncher _: return "ROCKET LAUNCHER";
                case GrenadeLauncher _: return "GRENADE LAUNCHER";
                default: return gun.GetType().Name.ToUpperInvariant();
            }
        }

        private static string FireModeLabel(Gun gun)
        {
            if (gun is Rifle rifle)
                return rifle.FireMode == RifleFireMode.Auto ? "AUTO" : rifle.FireMode == RifleFireMode.Burst ? "BURST" : "SEMI";
            if (gun.Def == null) return string.Empty;
            return gun.Def.FireMode == Core.Data.FireMode.Auto ? "AUTO" : "SEMI";
        }
        #endregion

        #region 이벤트
        private void OnHpChanged(PlayerHpChangedEvent e) { hp = e.Current; maxHp = e.Max; }

        private void OnStaminaChanged(PlayerStaminaChangedEvent e) { stamina = e.Current; maxStamina = e.Max; }

        private void OnAmmoChanged(AmmoChangedEvent e)
        {
            ammo = e.Magazine;
            magazine = e.MagazineSize;
            spare = e.SpareMagazines;
            maxSpare = e.MaxMagazines;
            infiniteSpare = e.InfiniteMagazines;
        }

        private void OnReloadDenied(ReloadDeniedEvent e) => reloadDeniedUntil = Time.time + reloadDeniedFlashTime;

        private void OnReloadStarted(ReloadStartedEvent e)
        {
            reloading = true;
            reloadStart = Time.time;
            reloadDuration = e.Duration;
        }

        private void OnReloadFinished(ReloadFinishedEvent e) => reloading = false;

        private void OnGrenadeCountChanged(GrenadeCountChangedEvent e) => SetItem(grenadeSlot, grenadeText, e.Current, e.Max);

        private void OnHealPackCountChanged(HealPackCountChangedEvent e) => SetItem(healSlot, healText, e.Current, e.Max);

        // ── 보급 획득 피드백 (실제 지급량만. 개수 변경 이벤트는 재시작 초기화·무기 교체에도 바뀌어 쓰지 않는다) ──

        private void OnSupplyCollected(SupplyCollectedEvent e)
        {
            if (e.Def != null && e.ItemAmount > 0)
            {
                if (e.Def.SupplyType == SupplyType.Grenade) PlayGain(grenadeIconRect, e.ItemAmount);
                else if (e.Def.SupplyType == SupplyType.Heal) PlayGain(healIconRect, e.ItemAmount);
            }
            if (e.MagazinesAdded > 0) PlayGain(spareText != null ? spareText.rectTransform : null, e.MagazinesAdded);
        }

        private void OnRoundSupplyGranted(RoundSupplyGrantedEvent e)
        {
            if (e.Grenades > 0) PlayGain(grenadeIconRect, e.Grenades);
            if (e.HealPacks > 0) PlayGain(healIconRect, e.HealPacks);
            if (e.MagazinesAdded > 0) PlayGain(spareText != null ? spareText.rectTransform : null, e.MagazinesAdded);
        }

        /// <summary>아이콘이 한 번 통 튀고, 그 옆에서 "+N" 이 떠오르며 사라진다 (실제 시간 — 일시정지와 무관)</summary>
        private void PlayGain(RectTransform icon, int amount)
        {
            if (icon == null || icon.gameObject.activeInHierarchy == false) return;

            icon.DOKill(true);
            icon.localScale = Vector3.one;
            icon.DOPunchScale(Vector3.one * gainPunch, gainDuration, 6, 0.6f).SetUpdate(true);

            var label = Label(icon.parent, "Gain", numberFont, 24f, TextAlignmentOptions.Center, Vector2.zero, new Vector2(80f, 30f));
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = icon.anchorMin;
            rt.pivot = new Vector2(0.5f, 0f);
            // 아이콘 중심 바로 위에서 시작 (피벗과 무관하게)
            Vector2 center = icon.anchoredPosition + Vector2.Scale(new Vector2(0.5f, 0.5f) - icon.pivot, icon.rect.size);
            Vector2 start = center + new Vector2(0f, icon.rect.height * 0.5f);
            rt.anchoredPosition = start;
            label.text = $"+{amount}";
            label.color = AimReticleHud.Cyan;

            DOTween.Sequence()
                .Join(DOTween.To(() => rt.anchoredPosition, p => rt.anchoredPosition = p, start + Vector2.up * gainRise, gainDuration).SetEase(Ease.OutCubic))
                .Join(DOTween.To(() => label.alpha, a => label.alpha = a, 0f, gainDuration).SetEase(Ease.InQuad))
                .SetUpdate(true)
                .SetTarget(label)
                .OnComplete(() => { if (label != null) Destroy(label.gameObject); });
        }

        private void OnLivesChanged(PlayerLivesChangedEvent e)
        {
            if (e.Current < lives) livesFlashUntil = Time.time + livesFlashTime;
            lives = e.Current;
            maxLives = e.Max;
        }

        /// <summary>1개 이상일 때만 보인다 (가로 정렬이 빈칸을 당겨 준다)</summary>
        private static void SetItem(GameObject slot, TextMeshProUGUI text, int current, int max)
        {
            slot.SetActive(current > 0);
            text.SetText("{0}<size=70%><color=#9FE8F5>/{1}</color></size>", current, max);
        }

        private void OnGunChanged(int index, Gun gun)
        {
            shownGun = gun;
            reloading = gun != null && gun.IsReloading;
            reloadDeniedUntil = 0f;
            if (gun == null) return;
            ammo = gun.Ammo;
            magazine = gun.MagazineSize;
            spare = gun.SpareMagazines;
            maxSpare = gun.MaxMagazines;
            infiniteSpare = gun.InfiniteMagazines;
        }

        private void OnGameStateChanged(GameStateChangedEvent e)
            => stateHidden = e.Current == GameState.Reward || e.Current == GameState.GameOver || e.Current == GameState.Paused;
        #endregion

        #region 화면 만들기
        private void Build()
        {
            var go = new GameObject("PlayerHud", typeof(RectTransform));
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60; // 스코프(AimReticleHud 50) 위에 탄약이 보이게
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            rootGroup = go.AddComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false;
            rootGroup.interactable = false;
            // 사망 중 숨기는 묶음 (체력·소모품·스태미나·무기). 목숨은 이 밖에 둔다
            var main = Rect(go.transform, "Main", Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            main.anchorMax = Vector2.one;
            mainGroup = main.gameObject.AddComponent<CanvasGroup>();
            mainGroup.blocksRaycasts = false;
            mainGroup.interactable = false;
            Transform root = main;

            // 왼쪽 아래, 체력 위: 목숨
            livesGroup = Group(go.transform, "Lives", new Vector2(0f, 0f), new Vector2(56f, 158f), new Vector2(300f, 44f));
            livesIcon = NewImage(Rect(livesGroup.transform, "Icon", Vector2.zero, new Vector2(0f, 0.5f), new Vector2(0f, 22f), new Vector2(34f, 34f)), HeartSprite());
            livesIcon.preserveAspect = true;
            livesIcon.color = AimReticleHud.Cyan;
            livesText = Label(livesGroup.transform, "Count", numberFont, 32f, TextAlignmentOptions.MidlineLeft,
                new Vector2(44f, 2f), new Vector2(240f, 40f));

            // 왼쪽 아래: 체력 + 소모품
            vitalsGroup = Group(root, "Vitals", new Vector2(0f, 0f), new Vector2(56f, 40f), new Vector2(560f, 110f));
            Label(vitalsGroup.transform, "HpLabel", labelFont, 24f, TextAlignmentOptions.MidlineLeft,
                new Vector2(0f, 52f), new Vector2(50f, 40f)).text = "HP";
            hpBar = Bar(vitalsGroup.transform, "Hp", hpBack, hpFill, new Vector2(52f, 55f), new Vector2(400f, 34f));
            hpText = Label(vitalsGroup.transform, "HpValue", numberFont, 34f, TextAlignmentOptions.MidlineLeft,
                new Vector2(466f, 52f), new Vector2(100f, 40f));

            var items = Rect(vitalsGroup.transform, "Items", Vector2.zero, Vector2.zero, new Vector2(0f, 0f), new Vector2(400f, 40f));
            var row = items.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 30f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = false;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            grenadeSlot = ItemSlot(items, "Grenade", GrenadeSprite(), out grenadeText);
            grenadeIconRect = (RectTransform)grenadeSlot.transform.Find("Icon");
            healSlot = ItemSlot(items, "HealPack", healIcon, out healText);
            healIconRect = (RectTransform)healSlot.transform.Find("Icon");
            grenadeSlot.SetActive(false);
            healSlot.SetActive(false);

            // 가운데 아래: 스태미나
            staminaGroup = Group(root, "Stamina", new Vector2(0.5f, 0f), new Vector2(-140f, 70f), new Vector2(280f, 10f));
            staminaBar = Bar(staminaGroup.transform, "Stamina", staminaBack, staminaFill, Vector2.zero, new Vector2(280f, 10f));
            staminaGroup.alpha = 0f;

            // 오른쪽 아래: 무기 (위에서부터 이름·모드 / 탄창 안 탄약 / 예비 탄창 / 탄창 바)
            var weapon = Group(root, "Weapon", new Vector2(1f, 0f), new Vector2(-416f, 40f), new Vector2(360f, 188f)).transform;
            infoText = Label(weapon, "Info", labelFont, 22f, TextAlignmentOptions.BottomRight, new Vector2(0f, 150f), new Vector2(360f, 30f));
            infoText.color = WithAlpha(TextColor, 0.75f);
            infoText.characterSpacing = 4f;
            ammoText = Label(weapon, "Ammo", numberFont, 64f, TextAlignmentOptions.BottomRight, new Vector2(0f, 66f), new Vector2(360f, 80f));
            statusText = Label(weapon, "Status", labelFont, 22f, TextAlignmentOptions.BottomLeft, new Vector2(0f, 74f), new Vector2(180f, 30f));
            statusText.characterSpacing = 4f;
            statusText.gameObject.SetActive(false);

            // 예비 탄창: 칸(오른쪽 정렬) + 개수/최대
            var pips = Rect(weapon, "SparePips", Vector2.zero, Vector2.zero, new Vector2(0f, 28f), new Vector2(228f, 32f));
            var pipRow = pips.gameObject.AddComponent<HorizontalLayoutGroup>();
            pipRow.spacing = 5f;
            pipRow.childAlignment = TextAnchor.MiddleRight;
            pipRow.childControlWidth = pipRow.childControlHeight = false;
            pipRow.childForceExpandWidth = pipRow.childForceExpandHeight = false;
            Sprite pipSprite = MagazineSprite();
            sparePips = new Image[MaxSparePips];
            for (int i = 0; i < MaxSparePips; i++)
            {
                sparePips[i] = NewImage(Rect(pips, "Pip" + i, Vector2.zero, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 24f)), pipSprite);
                sparePips[i].gameObject.SetActive(false);
            }
            spareText = Label(weapon, "Spare", numberFont, 26f, TextAlignmentOptions.MidlineRight, new Vector2(236f, 28f), new Vector2(124f, 32f));
            // 획득 피드백이 가운데 기준으로 튀도록 피벗을 가운데로 (위치는 그대로)
            spareText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            spareText.rectTransform.anchoredPosition += spareText.rectTransform.sizeDelta * 0.5f;

            ammoBar = Bar(weapon, "Magazine", ammoBack, ammoFill, new Vector2(20f, 0f), new Vector2(340f, 20f));
        }

        /// <summary>예비 탄창 칸 최대 수. 최대 예비 탄창이 이보다 많으면 칸 없이 숫자만</summary>
        private const int MaxSparePips = 10;

        /// <summary>anchor 기준 왼쪽 아래 모서리를 position 에 두는 묶음</summary>
        private static CanvasGroup Group(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rt = Rect(parent, name, anchor, Vector2.zero, position, size);
            var group = rt.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            return group;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary>배경 + 왼쪽부터 차오르는 채움. 채움 Image 를 돌려준다</summary>
        private static Image Bar(Transform parent, string name, Sprite back, Sprite fill, Vector2 position, Vector2 size)
        {
            var bg = NewImage(Rect(parent, name + "Back", Vector2.zero, Vector2.zero, position, size), back);
            bg.color = BackColor;
            var bar = NewImage(Rect(parent, name + "Fill", Vector2.zero, Vector2.zero, position, size), fill);
            bar.type = Image.Type.Filled;
            bar.fillMethod = Image.FillMethod.Horizontal;
            bar.fillOrigin = (int)Image.OriginHorizontal.Left;
            bar.color = AimReticleHud.Cyan;
            return bar;
        }

        private GameObject ItemSlot(Transform parent, string name, Sprite icon, out TextMeshProUGUI count)
        {
            var slot = Rect(parent, name, Vector2.zero, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(110f, 40f));
            var image = NewImage(Rect(slot, "Icon", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(30f, 30f)), icon);
            image.color = AimReticleHud.Cyan;
            image.preserveAspect = true;
            count = Label(slot, "Count", numberFont, 28f, TextAlignmentOptions.MidlineLeft, new Vector2(40f, 0f), new Vector2(70f, 40f));
            count.rectTransform.anchorMin = count.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            count.rectTransform.pivot = new Vector2(0f, 0.5f);
            return slot.gameObject;
        }

        private static Image NewImage(RectTransform rt, Sprite sprite)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, TMP_FontAsset font, float size,
                                             TextAlignmentOptions alignment, Vector2 position, Vector2 box)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.zero, position, box);
            var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            text.color = TextColor;
            return text;
        }

        /// <summary>
        /// 수류탄 실루엣 (64px, 흰색): 몸통 타원 + 십자 홈 + 목 + 안전 손잡이 + 고리.
        /// 팩의 폭탄 아이콘은 만화풍이라 직접 그린다. 색은 Image.color 로 입힌다
        /// </summary>
        private static Sprite GrenadeSprite()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HudGrenade", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float body = (new Vector2((p.x - 30f) / 15f, (p.y - 25f) / 19f).magnitude - 1f) * 15f;
                float grooves = Mathf.Min(Mathf.Abs(p.y - 25f), Mathf.Abs(p.x - 30f)) - 1.1f;
                body = Mathf.Max(body, -grooves);
                float neck = Box(p, new Vector2(30f, 47f), new Vector2(6f, 4f));
                float lever = Mathf.Min(Box(p, new Vector2(40f, 50f), new Vector2(8f, 2f)), Box(p, new Vector2(46.5f, 40f), new Vector2(2f, 11f)));
                float ring = Mathf.Abs((p - new Vector2(19f, 52f)).magnitude - 5.5f) - 1.4f;
                float d = Mathf.Min(Mathf.Min(body, neck), Mathf.Min(lever, ring));
                byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>목숨 하트 (64px, 흰색). 하트 곡선 (x²+y²-1)³ - x²y³ ≤ 0 을 4×4 표본으로 가장자리를 부드럽게. 색은 Image.color 로</summary>
        private static Sprite HeartSprite()
        {
            const int size = 64;
            const int samples = 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "HudHeart", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int inside = 0;
                for (int sy = 0; sy < samples; sy++)
                for (int sx = 0; sx < samples; sx++)
                {
                    float hx = ((x + (sx + 0.5f) / samples) / size - 0.5f) * 2.6f;
                    float hy = ((y + (sy + 0.5f) / samples) / size - 0.45f) * 2.6f;
                    float a = hx * hx + hy * hy - 1f;
                    if (a * a * a - hx * hx * hy * hy * hy <= 0f) inside++;
                }
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * inside / (samples * samples)));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>예비 탄창 칸 (16×32px, 흰색): 살짝 앞으로 휜 탄창 몸통 + 위쪽 급탄구. 색은 Image.color 로</summary>
        private static Sprite MagazineSprite()
        {
            const int width = 16, height = 32;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "HudMagazine", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float bend = (1f - p.y / height) * 2.5f; // 아래로 갈수록 앞(오른쪽)으로
                float body = Box(p, new Vector2(7.5f + bend, 13f), new Vector2(4.5f, 12f)) - 1f;
                float lips = Box(p, new Vector2(8f, 28.5f), new Vector2(3.5f, 2.5f));
                float d = Mathf.Min(body, lips);
                byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                pixels[y * width + x] = new Color32(255, 255, 255, a);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f);
        }

        /// <summary>중심 center, 반너비 half 인 사각형까지의 거리 (안쪽 음수)</summary>
        private static float Box(Vector2 p, Vector2 center, Vector2 half)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - center.x) - half.x, Mathf.Abs(p.y - center.y) - half.y);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        }
        #endregion
    }
}
