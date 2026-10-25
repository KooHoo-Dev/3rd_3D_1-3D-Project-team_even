using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 로켓 발사기 — 직선으로 날아가 닿는 순간 폭발. (참고: TF2·둠 로켓, CoD 무장 거리)
    ///  - 히트스캔이 아니라 느린 투사체 → 움직이는 적은 예측 사격. 무리·대형 적 처리용
    ///  - 직격하면 폭발 피해에 directHitBonus 를 더한다. 범위 피해는 중심에서 멀수록 감소
    ///  - armDistance 안에서 벽에 맞으면 불발 (코앞 자폭 방지). 같은 진영은 맞지 않는다
    ///  - 한 발 장전, 재장전이 길다. 탄창 강화는 받지 않고 재장전 강화만 받는다
    /// </summary>
    public class RocketLauncher : Gun
    {
        [Header("로켓")]
        [Tooltip("날아가는 로켓 외형 (비우면 구)")]
        [SerializeField] private GameObject rocketPrefab;
        [SerializeField] private float rocketSpeed = 30f;
        [SerializeField] private float rocketRadius = 0.15f;
        [SerializeField] private float armDistance = 2f;
        [Tooltip("직격 시 추가 피해 (Damage 대비 비율)")]
        [SerializeField, Range(0f, 2f)] private float directHitBonusRatio = 0.5f;
        [Tooltip("폭발 설정. damage 는 WeaponTable.Damage 로 덮어쓴다")]
        [SerializeField] private ExplosionSettings explosion = new ExplosionSettings
        {
            radius = 4f, minDamageRatio = 0.3f, force = 12f, sfxKey = "sfx_grenade_explode",
        };

        public RocketLauncher()
        {
            equipTime = 0.8f;
            bloomPerShot = 0f;
            moveSpreadPenalty = 2f;
            airSpreadPenalty = 3f;
            aimSpreadMultiplier = 0.3f;
            recoilPitch = 2.5f;
            recoilYaw = 0.6f;
            recoilRecoverRatio = 0.8f;
            recoilRecoverSpeed = 10f;
            aimFovMultiplier = 0.7f;
            aimMoveSpeedMultiplier = 0.6f;
        }

        protected override float MagazineUpgradeRate => 0f;

        protected override void Shoot()
        {
            Vector3 spawn = ProjectileSpawnPoint();
            Vector3 aimPoint = Owner.GetAimPoint(Stats.range, hitMask);
            Vector3 direction = RandomInCone((aimPoint - spawn).normalized, CurrentSpread);

            var settings = new GunProjectile.Settings
            {
                Gravity = 0f,
                Radius = rocketRadius,
                ExplodeOnSurface = true,
                ArmDistance = armDistance,
                DirectHitBonus = Mathf.RoundToInt(Stats.damage * directHitBonusRatio),
                MaxLifetime = Stats.range / Mathf.Max(1f, rocketSpeed) + 0.5f,
                Explosion = explosion,
            };
            settings.Explosion.damage = Stats.damage;
            GunProjectile.Spawn(rocketPrefab, spawn, direction * rocketSpeed, settings, CreateProjectileContext());
        }
    }
}
