using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 로켓 발사기 — 직선으로 날아가 닿는 순간 폭발. (참고: TF2·둠 로켓, CoD 무장 거리)
    ///  - 히트스캔이 아니라 느린 투사체 → 움직이는 적은 예측 사격. 무리·대형 적 처리용
    ///  - 직격하면 폭발 피해에 directHitBonus 를 더한다. 범위 피해는 중심에서 멀수록 감소
    ///  - armDistance 안에서 터지면 피해가 줄어든다 (근접 남용 방지). 쏜 사람·같은 진영은 맞지 않는다
    ///  - 한 발 장전, 재장전이 길다. 탄창 강화는 받지 않고 재장전 강화만 받는다
    /// </summary>
    public class RocketLauncher : Gun
    {
        [Header("로켓")]
        [Tooltip("날아가는 로켓 외형 (비우면 구)")]
        [SerializeField] private GameObject rocketPrefab;
        [Tooltip("로켓 뒤 불꽃·연기 꼬리 (선택)")]
        [SerializeField] private ProjectileTrail rocketTrail;
        [SerializeField] private float rocketSpeed = 30f;
        [SerializeField] private float rocketRadius = 0.15f;
        [Tooltip("이 거리 전에 터지면 피해가 줄어든다")]
        [SerializeField] private float armDistance = 2f;
        [Tooltip("코앞(0m)에서 터졌을 때의 피해 비율. armDistance 까지 100% 로 오른다")]
        [SerializeField, Range(0f, 1f)] private float unarmedDamageRatio = 0.5f;
        [Tooltip("직격 시 추가 피해 (Damage 대비 비율)")]
        [SerializeField, Range(0f, 2f)] private float directHitBonusRatio = 0.5f;
        [Tooltip("폭발 설정. damage 는 WeaponTable.Damage, sfxKey 는 WeaponTable.ExplodeSfxKey 로 덮어쓴다")]
        [SerializeField] private ExplosionSettings explosion = new ExplosionSettings
        {
            radius = 12f, minDamageRatio = 0.3f, force = 12f,
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
        /// <summary>스플래시 무기: 보급 탄약 충전을 엄격하게 (WeaponTable 행이 없을 때)</summary>
        protected override float FallbackSupplyMagazineRatio => 0.25f;

        protected override void Shoot()
        {
            Vector3 spawn = ProjectileSpawnPoint(rocketRadius);
            Vector3 aimPoint = Owner.GetAimPoint(Stats.range, hitMask);
            Vector3 direction = RandomInCone((aimPoint - spawn).normalized, CurrentSpread);

            var settings = new GunProjectile.Settings
            {
                Gravity = 0f,
                Radius = rocketRadius,
                ExplodeOnSurface = true,
                ArmDistance = armDistance,
                UnarmedDamageRatio = unarmedDamageRatio,
                DirectHitBonus = Mathf.RoundToInt(Stats.damage * directHitBonusRatio),
                MaxLifetime = Stats.range / Mathf.Max(1f, rocketSpeed) + 0.5f,
                Explosion = explosion,
                Trail = rocketTrail,
            };
            settings.Explosion.damage = Stats.damage;
            settings.Explosion.sfxKey = Def?.ExplodeSfx?.Key; // 기준은 WeaponTable.ExplodeSfxKey 하나 (빈칸 = 소리 없음)
            GunProjectile.Spawn(rocketPrefab, spawn, direction * rocketSpeed, settings, CreateProjectileContext());
        }
    }
}
