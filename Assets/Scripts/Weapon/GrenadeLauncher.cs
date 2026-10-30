using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 유탄 발사기 — 포물선으로 날아가 튕기는 유탄. (참고: TF2 데모맨 유탄 + CoD 유탄 무장 거리)
    ///  - 중력을 받아 포물선. 조준선보다 살짝 위로 쏴서 중거리에서 조준점 근처에 떨어지게 한다
    ///  - 적에 직접 맞으면 즉시 폭발, 벽·바닥에는 튕기고 fuseTime 뒤 폭발 → 엄폐물 뒤·모퉁이 너머 공략
    ///  - 무장 거리 전에 터지면 피해가 줄어든다 (근접 남용 방지)
    ///  - 리볼버식 탄창, 재장전은 한 번에
    /// </summary>
    public class GrenadeLauncher : Gun
    {
        [Header("유탄")]
        [Tooltip("날아가는 유탄 외형 (비우면 구)")]
        [SerializeField] private GameObject grenadePrefab;
        [Tooltip("유탄 뒤 연기 꼬리 (선택)")]
        [SerializeField] private ProjectileTrail grenadeTrail;
        [SerializeField] private float launchSpeed = 28f;
        [Tooltip("조준선보다 위로 쏘는 각도(도)")]
        [SerializeField] private float launchAngle = 4f;
        [SerializeField] private float gravityScale = 1f;
        [SerializeField] private float grenadeRadius = 0.08f;
        [SerializeField, Range(0f, 1f)] private float bounciness = 0.45f;
        [Tooltip("발사 후 이 시간이 지나면 폭발")]
        [SerializeField] private float fuseTime = 2.5f;
        [Tooltip("이 거리 전에 터지면 피해가 줄어든다")]
        [SerializeField] private float armDistance = 4f;
        [Tooltip("코앞(0m)에서 터졌을 때의 피해 비율. armDistance 까지 100% 로 오른다")]
        [SerializeField, Range(0f, 1f)] private float unarmedDamageRatio = 0.5f;
        [Tooltip("폭발 설정. damage 는 WeaponTable.Damage 로 덮어쓴다")]
        [SerializeField] private ExplosionSettings explosion = new ExplosionSettings
        {
            radius = 3f, minDamageRatio = 0.25f, force = 8f,
        };

        public GrenadeLauncher()
        {
            equipTime = 0.6f;
            bloomPerShot = 0f;
            moveSpreadPenalty = 1.5f;
            airSpreadPenalty = 2f;
            aimSpreadMultiplier = 0.5f;
            recoilPitch = 3f;
            recoilYaw = 0.4f;
            recoilRecoverRatio = 0.8f;
            recoilRecoverSpeed = 12f;
            aimFovMultiplier = 0.8f;
            aimMoveSpeedMultiplier = 0.7f;
        }

        protected override float MagazineUpgradeRate => 0.2f;
        /// <summary>스플래시 무기: 보급 탄약 충전을 엄격하게 (WeaponTable 행이 없을 때)</summary>
        protected override float FallbackSupplyMagazineRatio => 0.25f;

        protected override void Shoot()
        {
            Vector3 spawn = ProjectileSpawnPoint(grenadeRadius);
            Vector3 aimPoint = Owner.GetAimPoint(Stats.range, hitMask);
            Vector3 direction = RandomInCone((aimPoint - spawn).normalized, CurrentSpread);
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude > 0.0001f) direction = Quaternion.AngleAxis(-launchAngle, right.normalized) * direction;

            var settings = new GunProjectile.Settings
            {
                Gravity = Physics.gravity.magnitude * gravityScale,
                Radius = grenadeRadius,
                ExplodeOnSurface = false,
                Bounciness = bounciness,
                FuseTime = fuseTime,
                ArmDistance = armDistance,
                UnarmedDamageRatio = unarmedDamageRatio,
                MaxLifetime = fuseTime + 1f,
                Explosion = explosion,
                Trail = grenadeTrail,
            };
            settings.Explosion.damage = Stats.damage;
            settings.Explosion.sfxKey = Def?.ExplodeSfx?.Key; // 기준은 WeaponTable.ExplodeSfxKey 하나 (빈칸 = 소리 없음)
            GunProjectile.Spawn(grenadePrefab, spawn, direction * launchSpeed, settings, CreateProjectileContext());
        }
    }
}
