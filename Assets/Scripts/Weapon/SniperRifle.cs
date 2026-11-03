using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 저격소총 — 볼트 액션 장거리 무기. (참고: CoD·배틀필드 볼트액션 저격총)
    ///  - 탄 퍼짐이 없다: 조준 여부·이동·공중·땅울림과 상관없이 조준점으로 정확히 나간다. 난이도는 반동과 노리쇠 간격으로 조절
    ///  - 한 발 쏘면 노리쇠를 당긴다(= 발사 간격). 기본은 그동안에도 확대 유지 (unscopeWhileBolting 을 켜면 풀렸다가 다시 확대)
    ///  - 반동은 노리쇠를 당기는 동안 전부 내려와 다음 발을 같은 목표에 바로 쏠 수 있다
    ///  - 관통: 적을 뚫고 뒤의 적까지 맞힌다 (뚫을 때마다 피해 감소). 벽은 못 뚫는다
    /// </summary>
    public class SniperRifle : Gun
    {
        [Header("저격소총")]
        [Tooltip("한 발로 맞힐 수 있는 최대 적 수")]
        [SerializeField] private int maxPenetration = 3;
        [Tooltip("적을 하나 뚫을 때마다 남는 피해 비율")]
        [SerializeField, Range(0f, 1f)] private float penetrationDamageRatio = 0.7f;
        [Tooltip("노리쇠를 당기는 동안 확대를 푼다 (끄면 스코프 화면 그대로 유지)")]
        [SerializeField] private bool unscopeWhileBolting = false;

        private readonly HashSet<IDamageable> pierced = new HashSet<IDamageable>();

        public SniperRifle()
        {
            equipTime = 0.6f;
            recoilPitch = 3f;
            recoilYaw = 0.5f;
            recoilRecoverRatio = 1f;
            recoilRecoverSpeed = 10f;
            aimFovMultiplier = 0.3f;
            aimSensitivityMultiplier = 0.35f;
            aimMoveSpeedMultiplier = 0.5f;
        }

        private bool IsBolting => Time.time < NextFireTime;
        protected override float MagazineUpgradeRate => 0.2f;
        protected override bool AimZoomActive => IsAiming && (unscopeWhileBolting == false || IsBolting == false);

        public override float CurrentSpread => 0f;

        protected override void Shoot()
        {
            Vector3 direction = Owner.AimRay.direction;
            Ray ray = new Ray(Owner.AimRay.origin, direction);
            int count = RaycastAll(ray, Stats.range);
            float damage = Stats.damage;
            int hitCount = 0;
            Vector3 end = ray.GetPoint(Stats.range);
            pierced.Clear();

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = SharedHits[i];
                if (hit.collider.TryGetDamageable(out var target) == false)
                {
                    SpawnImpact(hit); // 벽·지형에서 멈춤
                    end = hit.point;
                    break;
                }
                if (target.CanBeHitBy(Owner.Faction) == false || pierced.Add(target) == false) continue;

                target.TakeDamage(new DamageInfo(Mathf.RoundToInt(damage), DamageKind.Bullet, Owner.Faction, Owner.gameObject, hit.point, direction, weaponId));
                damage *= penetrationDamageRatio;
                if (++hitCount >= maxPenetration) { end = hit.point; break; }
            }
            Debug.DrawLine(ray.origin, end, Color.cyan, 0.5f);
            SpawnTracer(end);
        }
    }
}
