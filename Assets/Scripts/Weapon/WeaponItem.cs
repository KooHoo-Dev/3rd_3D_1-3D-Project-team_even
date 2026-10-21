using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 총 한 자루에 붙는 컴포넌트. "이 총은 무엇이고, 얼마나 세고, 손은 어디를 잡는가" 를 담는다.
    /// 쏘는 행동 자체는 PlayerWeapons 가 하고, 이 컴포넌트는 값과 위치만 들고 있다.
    ///
    /// 손 위치
    ///  - 총은 오른손 뼈 아래(WeaponSocket)에 붙어 있어 오른손은 자동으로 총을 쥔다.
    ///  - 왼손은 IK 가 leftHandGrip 위치로 끌어당긴다 → 총마다 leftHandGrip 만 맞추면 어떤 총이든 두 손으로 잡는다.
    ///  - muzzle(총구)의 파란 축(Z)이 총알이 나가는 방향. 조준 IK 가 이 축을 조준점으로 돌린다.
    ///  씬 뷰에서 이 오브젝트를 선택하면 노란 공(왼손), 빨간 선(총구 방향)이 보인다.
    /// </summary>
    public class WeaponItem : MonoBehaviour
    {
        [Header("종류")]
        [SerializeField] private WeaponType type = WeaponType.Rifle;
        [SerializeField] private ShotKind shotKind = ShotKind.Hitscan;
        [SerializeField] private string displayName = "Rifle";

        [Header("데이터 테이블 (선택)")]
        [Tooltip("WeaponTable 의 Id. 0 이 아니면 아래 능력치 대신 테이블 값을 쓴다 (라이플 = 2001)")]
        [SerializeField] private int tableId;
        [Tooltip("강화(탄창 증가, 재장전 단축)를 이 무기에 적용할지")]
        [SerializeField] private bool affectedByUpgrades;

        [Header("능력치 (테이블이 없을 때)")]
        [SerializeField] private FireMode fireMode = FireMode.Auto;
        [SerializeField] private int damage = 10;
        [Tooltip("분당 발사 수 (RPM)")]
        [SerializeField] private float fireRate = 600f;
        [SerializeField] private int magazineSize = 30;
        [SerializeField] private float reloadTime = 2f;
        [SerializeField] private float range = 120f;
        [Tooltip("탄 퍼짐 원뿔 반각(도)")]
        [SerializeField] private float spreadAngle = 1.2f;

        [Header("추가 능력치")]
        [Tooltip("한 번 쏠 때 나가는 탄 수 (샷건 산탄)")]
        [SerializeField, Min(1)] private int pelletsPerShot = 1;
        [Tooltip("조준 중 퍼짐 배율")]
        [SerializeField, Range(0f, 1f)] private float aimSpreadMultiplier = 0.5f;
        [Tooltip("누르고 이 시간이 지나야 발사 시작 (미니건 회전 예열). 0 = 즉시")]
        [SerializeField] private float spinUpTime;
        [Tooltip("조준 중 이동 속도 배율")]
        [SerializeField, Range(0.1f, 1f)] private float aimMoveSpeedMultiplier = 0.45f;
        [Tooltip("조준 시 카메라 시야각 배율 (작을수록 크게 확대). 0 = 카메라 리그 기본값")]
        [SerializeField, Range(0f, 1f)] private float aimFovMultiplier;
        [Tooltip("들고 있는 동안 이동 속도 배율 (무거운 무기)")]
        [SerializeField, Range(0.1f, 1f)] private float carryMoveSpeedMultiplier = 1f;

        [Header("투사체 (ShotKind = Projectile)")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private float projectileSpeed = 35f;
        [SerializeField] private float explosionRadius = 5f;

        [Header("손 · 총구 위치")]
        [Tooltip("총알이 나가는 위치. 파란 축(Z)이 발사 방향")]
        [SerializeField] private Transform muzzle;
        [Tooltip("왼손이 잡을 위치와 방향 (IK 목표)")]
        [SerializeField] private Transform leftHandGrip;
        [Tooltip("오른손목이 올 위치와 방향 (IK 목표). 비워 두면 자식 'RightHandGrip' 을 찾고, 그것도 없으면 실행 시 지금 손 위치로 만든다")]
        [SerializeField] private Transform rightHandGrip;

        [Header("연출 (선택)")]
        [SerializeField] private GameObject muzzleFlashPrefab;
        [SerializeField] private GameObject impactPrefab;
        [SerializeField] private float effectLifetime = 1.5f;

        private bool initialized;
        private int magazine;

        public WeaponType Type => type;
        public ShotKind ShotKind => shotKind;
        public string DisplayName => displayName;
        /// <summary>WeaponTable 에서 왔으면 그 정의, 아니면 null</summary>
        public WeaponDef Def { get; private set; }

        public FireMode FireMode { get; private set; }
        public int Damage { get; private set; }
        public float FireInterval { get; private set; }
        public float Range { get; private set; }
        public float SpreadAngle { get; private set; }
        public int PelletsPerShot => pelletsPerShot;
        public float AimSpreadMultiplier => aimSpreadMultiplier;
        public float SpinUpTime => spinUpTime;
        public float AimMoveSpeedMultiplier => aimMoveSpeedMultiplier;
        public float CarryMoveSpeedMultiplier => carryMoveSpeedMultiplier;
        public float AimFovMultiplier => aimFovMultiplier;
        public GameObject ProjectilePrefab => projectilePrefab;
        public float ProjectileSpeed => projectileSpeed;
        public float ExplosionRadius => explosionRadius;
        public GameObject MuzzleFlashPrefab => muzzleFlashPrefab;
        public GameObject ImpactPrefab => impactPrefab;
        public float EffectLifetime => effectLifetime;

        public Transform Muzzle => muzzle != null ? muzzle : transform;
        public Transform LeftHandGrip => leftHandGrip;
        public Transform RightHandGrip
        {
            get
            {
                if (rightHandGrip == null) rightHandGrip = transform.Find("RightHandGrip");
                return rightHandGrip;
            }
            set => rightHandGrip = value;
        }

        public int Magazine => magazine;
        public bool IsMagazineFull => magazine >= MagazineSize;

        /// <summary>강화 반영한 탄창 크기</summary>
        public int MagazineSize
        {
            get
            {
                int size = Def != null ? Def.MagazineSize : magazineSize;
                if (affectedByUpgrades && ServiceLocator.TryGet<IUpgradeState>(out var upgrades))
                    size += Mathf.RoundToInt(upgrades.GetTotalValue(UpgradeType.MagazineSize));
                return Mathf.Max(1, size);
            }
        }

        /// <summary>강화 반영한 재장전 시간</summary>
        public float ReloadTime
        {
            get
            {
                float time = Def != null ? Def.ReloadTime : reloadTime;
                if (affectedByUpgrades && ServiceLocator.TryGet<IUpgradeState>(out var upgrades))
                    time *= Mathf.Clamp(1f - upgrades.GetTotalValue(UpgradeType.ReloadSpeed), 0.2f, 1f);
                return Mathf.Max(0.1f, time);
            }
        }

        /// <summary>처음 한 번 값 확정 (테이블 읽기) + 탄창 가득</summary>
        public void Initialize()
        {
            if (initialized) return;
            initialized = true;

#if UNITY_EDITOR
            // Static 으로 표시된 물체는 플레이 시작 때 메시가 합쳐져 손을 따라오지 않는다
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.gameObject.isStatic)
                {
                    Debug.LogWarning($"[WeaponItem] {name} 에 Static 표시된 오브젝트({t.name})가 있어 총이 손을 따라오지 않습니다. " +
                                     "Tools > Player > 3. Setup Player Prefab 을 다시 실행하거나 인스펙터 오른쪽 위 Static 을 끄세요.", this);
                    break;
                }
#endif

            if (tableId != 0 && DataManager.Weapons.TryGet(tableId, out var def)) Def = def;
            else if (tableId != 0) Debug.LogWarning($"[WeaponItem] WeaponTable 에 Id {tableId} 가 없어 인스펙터 값을 씁니다.", this);

            FireMode = Def != null ? Def.FireMode : fireMode;
            Damage = Def != null ? Def.Damage : damage;
            FireInterval = Def != null ? Def.FireInterval : (fireRate > 0f ? 60f / fireRate : 0.1f);
            Range = Def != null ? Def.Range : range;
            SpreadAngle = Def != null ? Def.SpreadAngle : spreadAngle;
            magazine = MagazineSize;
        }

        /// <summary>한 발 소모. 탄이 없으면 false</summary>
        public bool TryConsumeRound()
        {
            if (magazine <= 0) return false;
            magazine--;
            return true;
        }

        public void Refill() => magazine = MagazineSize;

        /// <summary>
        /// 손에 들었을 때: 총에 달린 물리(콜라이더·리지드바디)를 꺼서 플레이어와 부딪히지 않게 한다.
        /// 보이기/숨기기도 함께.
        /// </summary>
        public void SetHeld(bool visible)
        {
            foreach (var col in GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (var body in GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            gameObject.SetActive(visible);
        }

        private void OnDrawGizmosSelected()
        {
            if (leftHandGrip != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(leftHandGrip.position, 0.03f);
                Gizmos.DrawLine(leftHandGrip.position, leftHandGrip.position + leftHandGrip.forward * 0.08f);
            }
            Transform m = Muzzle;
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(m.position, 0.02f);
            Gizmos.DrawLine(m.position, m.position + m.forward * 0.5f);
        }
    }
}
