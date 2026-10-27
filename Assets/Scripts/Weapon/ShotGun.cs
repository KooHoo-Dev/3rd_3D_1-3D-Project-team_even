using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 산탄총 — 펌프 액션 근거리 무기. (참고: Apex 고정 산탄 패턴, CoD·둠 한 발씩 장전)
    ///  - 한 번에 pelletCount 발. 탄 배치는 고정 패턴(가운데 1 + 안쪽 원 + 바깥 원) + 약간의 흔들림 → 운보다 조준이 중요
    ///  - 같은 적에 맞은 펠릿은 합쳐서 한 번에 피해 (피격 반응이 펠릿마다 반복되지 않게)
    ///  - 거리 감쇠가 매우 크다. WeaponTable.Damage = 펠릿 1발 피해, Range = 감쇠 끝 거리
    ///  - 장전은 한 발씩 (WeaponTable.ReloadTime = 한 발 넣는 시간). 장전 중에도 탄이 있으면 바로 쏠 수 있다
    /// </summary>
    public class ShotGun : Gun
    {
        [Header("산탄총")]
        [SerializeField] private int pelletCount = 8;
        [Tooltip("패턴 위치에 더하는 무작위 흔들림 (퍼짐 대비 비율)")]
        [SerializeField, Range(0f, 1f)] private float pelletJitter = 0.2f;
        [Tooltip("이 거리까지는 100% 피해")]
        [SerializeField] private float falloffStart = 6f;
        [Tooltip("사거리 끝에서의 피해 비율")]
        [SerializeField, Range(0f, 1f)] private float minDamageRatio = 0.2f;
        [Tooltip("첫 탄을 넣기 전 자세 잡는 시간")]
        [SerializeField] private float reloadStartDelay = 0.3f;

        private struct PelletHit { public int Damage; public Vector3 Point; }
        private readonly Dictionary<IDamageable, PelletHit> pelletHits = new Dictionary<IDamageable, PelletHit>();
        private float nextShellTime;

        public ShotGun()
        {
            equipTime = 0.45f;
            bloomPerShot = 0f;
            moveSpreadPenalty = 0.5f;
            airSpreadPenalty = 1f;
            aimSpreadMultiplier = 0.75f;
            recoilPitch = 4f;
            recoilYaw = 1f;
            recoilRecoverRatio = 0.8f;
            recoilRecoverSpeed = 25f;
            aimFovMultiplier = 0.9f;
            aimMoveSpeedMultiplier = 0.85f;
        }

        protected override float MagazineUpgradeRate => 0.2f;
        protected override bool CanInterruptReload => true;

        protected override int DamageAtDistance(float distance) =>
            FalloffDamage(Stats.damage, distance, falloffStart, Stats.range, minDamageRatio);

        protected override void Shoot()
        {
            Ray aim = Owner.AimRay;
            float spread = CurrentSpread;
            int innerCount = (pelletCount - 1) / 2;
            pelletHits.Clear();

            for (int i = 0; i < pelletCount; i++)
            {
                Vector2 offset = PelletOffset(i, innerCount, spread);
                Vector3 direction = OffsetDirection(aim.direction, offset);
                if (Raycast(new Ray(aim.origin, direction), Stats.range, out var hit) == false)
                {
                    SpawnTracer(aim.origin + direction * Stats.range);
                    continue;
                }

                SpawnImpact(hit);
                SpawnTracer(hit.point);
                if (hit.collider.TryGetDamageable(out var target) == false || target.CanBeHitBy(Owner.Faction) == false) continue;

                int damage = DamageAtDistance(hit.distance);
                pelletHits[target] = pelletHits.TryGetValue(target, out var sum)
                    ? new PelletHit { Damage = sum.Damage + damage, Point = sum.Point }
                    : new PelletHit { Damage = damage, Point = hit.point };
            }

            foreach (var pair in pelletHits)
                pair.Key.TakeDamage(new DamageInfo(pair.Value.Damage, DamageKind.Bullet, Owner.Faction, Owner.gameObject, pair.Value.Point, aim.direction, weaponId));
        }

        /// <summary>0 = 가운데, 1~inner = 안쪽 원(퍼짐 50%), 나머지 = 바깥 원(퍼짐 100%)</summary>
        private Vector2 PelletOffset(int index, int innerCount, float spread)
        {
            Vector2 jitter = Random.insideUnitCircle * (spread * pelletJitter);
            if (index == 0) return jitter;

            bool inner = index <= innerCount;
            int ringCount = inner ? innerCount : pelletCount - 1 - innerCount;
            int ringIndex = inner ? index - 1 : index - 1 - innerCount;
            float angle = (ringIndex / (float)Mathf.Max(1, ringCount) + (inner ? 0f : 0.5f / Mathf.Max(1, ringCount))) * Mathf.PI * 2f;
            float radius = spread * (inner ? 0.5f : 1f);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius + jitter;
        }

        protected override void StartReload()
        {
            IsReloading = true;
            nextShellTime = Time.time + reloadStartDelay + Stats.reloadTime;
            int missing = Stats.magazineSize - Ammo;
            NotifyReloadStarted(reloadStartDelay + missing * Stats.reloadTime);
        }

        protected override void UpdateReload()
        {
            while (IsReloading && Time.time >= nextShellTime)
            {
                SetAmmo(Ammo + 1);
                PlayReloadInsertSfx(); // 한 발 넣을 때마다
                if (Ammo >= Stats.magazineSize) CompleteReload();
                else nextShellTime += Stats.reloadTime;
            }
        }
    }
}
