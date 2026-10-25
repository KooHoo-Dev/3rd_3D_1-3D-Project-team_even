using HybridShooter.UI;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 총별 조준 화면 (WeaponGripTable 조준 방식). Player 루트에 붙인다.
    ///  - 스코프 (저격총): 조준 동작(드라이버 AimWeight)이 올라오는 만큼 화면이 어두워지다가 scopeSwitchAim(중간)에서 가장 어두울 때
    ///    1·3인칭 모두 1인칭 눈 카메라(PlayerController.SetScopeView)로 바꾸고 다시 밝아진다. 그때
    ///    몸·총 레이어를 카메라에서 빼고 스코프 오버레이를 띄운다. 확대는 총의 aimFovMultiplier (총 시스템이 그대로 한다).
    ///    눈 카메라 기준이라 오버레이 중앙 = 사격 광선
    ///  - 어깨 견착 (로켓·그레네이드 런처): 1인칭 조준 중 화면 중앙에 견착 조준선 (총 자리는 PlayerFirstPersonIK 가 옆으로 비킨다)
    ///  - 조준기 정렬 (나머지): 아무것도 띄우지 않는다
    /// HUD 는 시작할 때 AimReticleHud 로 만든다 (스프라이트는 SCI-FI UI Pack, 컴포넌트를 붙일 때 자동 연결)
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10001)]
    public class PlayerAimView : MonoBehaviour
    {
        private const string ScopeRingGuid = "ac45c16fa8a0fe34d9fda5a5e77bdcd3";    // SciFi_Circle02Cyan_01
        private const string LauncherRingGuid = "9a0df22cc61528d438bcc3261972e06c"; // SciFi_Circle02Cyan_03

        [SerializeField] private PlayerController player;
        [SerializeField] private PlayerAnimationDriver driver;
        [SerializeField] private GunSlot gunSlot;
        [SerializeField] private PlayerBodyIK bodyIK;
        [SerializeField] private PlayerAimIK aimIK;

        [Header("HUD")]
        [SerializeField] private Sprite scopeRing;
        [SerializeField] private Sprite launcherRing;

        [Header("스코프")]
        [Tooltip("조준 동작 진행도(0~1)가 이 값일 때 스코프 화면으로 바뀐다 (가장 어두운 순간). 그 전까지는 진행도만큼 어두워진다")]
        [SerializeField, Range(0.05f, 1f)] private float scopeSwitchAim = 0.5f;
        [Tooltip("스코프로 바뀐 뒤 밝아지는 시간과 조준을 풀 때 암전 → 원래 화면 → 밝아짐에 걸리는 시간(초)")]
        [SerializeField] private float scopeFadeTime = 0.18f;
        [Tooltip("스코프 화면에서 카메라에 그리지 않을 레이어 (몸·총). 비워 두면 FP_Body, FP_Weapon, TP_Body")]
        [SerializeField] private LayerMask hideWhenScoped;

        private AimReticleHud hud;
        private float scopeBlend;     // 0 = 평소, 0.5 = 암전 최대(여기서 화면 교체), 1 = 스코프
        private bool scopeApplied;
        private Camera maskedCamera;
        private int savedMask;

        /// <summary>지금 든 총의 조준 방식 (Auto 는 풀어서)</summary>
        public WeaponGripTable.AimMode Mode { get; private set; } = WeaponGripTable.AimMode.Sight;
        /// <summary>스코프 화면이 떠 있는지</summary>
        public bool Scoped => scopeApplied;

        private void Reset() => AssignSprites();

        private void AssignSprites()
        {
#if UNITY_EDITOR
            if (scopeRing == null) scopeRing = LoadSprite(ScopeRingGuid);
            if (launcherRing == null) launcherRing = LoadSprite(LauncherRingGuid);
#endif
        }

#if UNITY_EDITOR
        private static Sprite LoadSprite(string guid)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
#endif

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (driver == null) driver = GetComponent<PlayerAnimationDriver>();
            if (gunSlot == null) gunSlot = GetComponent<GunSlot>();
            if (bodyIK == null) bodyIK = GetComponent<PlayerBodyIK>();
            if (aimIK == null) aimIK = GetComponent<PlayerAimIK>();
            if (hideWhenScoped.value == 0) hideWhenScoped = LayerMask.GetMask("FP_Body", "FP_Weapon", "TP_Body");
            AssignSprites();
            hud = AimReticleHud.Create(scopeRing, launcherRing);
        }

        private void OnDisable()
        {
            if (scopeApplied) ApplyScope(false);
            scopeBlend = 0f;
            if (hud != null) hud.Set(0f, 0f, 0f, -1f);
        }

        private void OnDestroy()
        {
            if (hud != null) Destroy(hud.gameObject);
        }

        private void LateUpdate()
        {
            if (player == null || driver == null || hud == null) return;
            Gun gun = gunSlot != null ? gunSlot.CurrentGun : null;
            WeaponGripTable table = bodyIK != null ? bodyIK.GripTable : null;
            Mode = gun == null ? WeaponGripTable.AimMode.Sight
                : table != null ? table.AimModeFor(gun) : WeaponGripTable.DefaultAimMode(WeaponGripTable.Key(gun));

            // 조준 중: 실제 조준(우클릭) 또는 IK Pose Lab 의 조준 고정. 재장전·꺼내기·사망·시점 전환 중에는 아님
            bool aiming = gun != null && (gun.IsAiming || driver.DebugAimOverride == true)
                          && driver.ActionBlend > 0.5f && player.IsAlive && player.CameraChanging == false;

            // scopeBlend: 0 평소, 0.5 가장 어두움(화면 교체), 1 스코프
            //  - 들어갈 때: 스코프로 바뀌기 전까지는 조준 동작 진행도에 묶는다 → 동작 중간에 바뀐다. 바뀐 뒤엔 시간으로 밝아짐
            //  - 나올 때: 시간으로 1 → 0 (0.5 에서 원래 화면)
            bool wantScope = Mode == WeaponGripTable.AimMode.Scope && aiming;
            float step = scopeFadeTime > 0f ? Time.deltaTime / scopeFadeTime : 1f;
            if (wantScope == false) scopeBlend = Mathf.MoveTowards(scopeBlend, 0f, step);
            else if (scopeApplied) scopeBlend = Mathf.MoveTowards(scopeBlend, 1f, step);
            else scopeBlend = 0.5f * Mathf.Clamp01(driver.AimWeight / scopeSwitchAim);
            // 시점 전환이 시작되면 컨트롤러가 스코프 화면을 먼저 끈다 → 바로 맞춘다
            if (scopeApplied && (player.ScopeView == false || player.CameraChanging)) scopeBlend = 0f;
            bool scoped = scopeBlend >= 0.5f;
            if (scoped != scopeApplied) ApplyScope(scoped);

            float flash = scopeBlend > 0f && scopeBlend < 1f ? 1f - Mathf.Abs(scopeBlend - 0.5f) * 2f : 0f;
            float launcher = Mode == WeaponGripTable.AimMode.Shoulder && aiming && player.IsFirstPerson
                ? Mathf.InverseLerp(0.5f, 1f, driver.AimWeight) : 0f;
            float range = aimIK != null && scopeApplied ? Vector3.Distance(player.CurrentCameraTransform.position, aimIK.AimPoint) : -1f;
            hud.Set(scopeApplied ? 1f : 0f, launcher, flash, range);
        }

        /// <summary>스코프 화면 켜기·끄기: 눈 카메라로, 몸·총 레이어 숨김</summary>
        private void ApplyScope(bool on)
        {
            scopeApplied = on;
            if (player == null) return;
            player.SetScopeView(on);
            if (on)
            {
                maskedCamera = player.FirstPersonCamera;
                if (maskedCamera == null) return;
                savedMask = maskedCamera.cullingMask;
                maskedCamera.cullingMask = savedMask & ~hideWhenScoped.value;
            }
            else if (maskedCamera != null)
            {
                maskedCamera.cullingMask = savedMask;
                maskedCamera = null;
            }
        }
    }
}
