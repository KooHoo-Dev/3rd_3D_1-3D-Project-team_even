using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 플레이어가 가진 총들(슬롯 1~4)을 관리한다: 바꾸기 · 쏘기 · 재장전.
    /// 입력은 직접 읽지 않는다. PlayerController 가 매 프레임 Tick(사격 버튼, 조준 버튼, 조준 광선) 을 불러 준다.
    ///
    ///  쏘기   : 카메라 정중앙 광선(조준점) 방향으로 판정 → 맞은 대상이 IDamageable 이면 피해
    ///           로켓은 총구에서 RocketProjectile 을 날린다
    ///  재장전 : R 또는 탄이 떨어진 채로 쏘면 자동. ReloadTime 동안 쏠 수 없다
    ///  바꾸기 : 1~4. 꺼내는 동안(drawTime) 쏠 수 없다. 재장전 중이면 취소
    ///
    /// 발행 이벤트: AmmoChangedEvent, ReloadStartedEvent, ReloadFinishedEvent, WeaponFiredEvent
    ///  (WeaponFiredEvent.Weapon 은 WeaponTable 을 쓰는 무기(라이플)만 값이 있고 나머지는 null)
    /// </summary>
    [RequireComponent(typeof(PlayerAnimator), typeof(PlayerMotor))]
    public class PlayerWeapons : MonoBehaviour
    {
        private const string AimSpeedKey = "aim";
        private const string CarrySpeedKey = "weapon";

        [Tooltip("슬롯 순서 = 숫자키 1, 2, 3, 4. Tools > Player > 3. Setup Player Prefab 이 자동으로 채운다")]
        [SerializeField] private WeaponItem[] slots = new WeaponItem[4];
        [SerializeField] private int startSlot;
        [Tooltip("무기를 꺼내는 데 걸리는 시간(초)")]
        [SerializeField] private float drawTime = 0.5f;
        [Tooltip("총알이 맞을 수 있는 레이어")]
        [SerializeField] private LayerMask hitMask = ~0;

        private PlayerAnimator anim;
        private PlayerMotor motor;
        private int currentSlot = -1;
        private float nextFireTime;
        private float reloadEndTime = float.NegativeInfinity;
        private float drawEndTime = float.NegativeInfinity;
        private bool reloading;
        private bool fireReleased = true;
        private float spin;
        private Vector3 aimPoint;

        public WeaponItem Current => currentSlot >= 0 ? slots[currentSlot] : null;
        public System.Collections.Generic.IReadOnlyList<WeaponItem> Slots => slots;
        public int CurrentSlot => currentSlot;
        public bool IsReloading => reloading;
        public bool IsDrawing => Time.time < drawEndTime;
        /// <summary>다른 행동(투척·회복)을 시작하면 안 되는 상태</summary>
        public bool IsBusy => reloading || IsDrawing;
        /// <summary>마지막 Tick 에서 계산한 조준점 (조준 IK 목표)</summary>
        public Vector3 AimPoint => aimPoint;
        /// <summary>마지막 Tick 에서 실제로 조준 중이었나 (카메라 줌용)</summary>
        public bool IsAiming { get; private set; }

        private void Awake()
        {
            anim = GetComponent<PlayerAnimator>();
            motor = GetComponent<PlayerMotor>();
        }

        private void Start()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                slots[i].Initialize();
                slots[i].SetHeld(false);
            }

            int first = FindUsableSlot(startSlot);
            if (first >= 0) Equip(first, playDraw: false);
            else anim.SetWeaponType((int)WeaponType.Unarmed);
        }

        // ───────────────────────── 매 프레임 (PlayerController 가 호출) ─────────────────────────

        /// <param name="canAct">구르기·벽넘기·투척·회복·사망 중이면 false</param>
        public void Tick(bool fireHeld, bool aimHeld, Ray aimRay, bool canAct)
        {
            var weapon = Current;
            aimPoint = ComputeAimPoint(aimRay, weapon);
            IsAiming = false;
            if (weapon == null) return;

            if (reloading && Time.time >= reloadEndTime) FinishReload();

            bool aiming = aimHeld && canAct;
            IsAiming = aiming;
            anim.SetAimHeld(aiming);
            motor.SetSpeedModifier(AimSpeedKey, aiming ? weapon.AimMoveSpeedMultiplier : 1f);

            bool wantFire = fireHeld && canAct && IsBusy == false;

            // 미니건 예열: 누르는 동안 돌고, 놓으면 천천히 멈춘다
            if (weapon.SpinUpTime > 0f)
                spin = Mathf.Clamp(spin + (wantFire ? Time.deltaTime : -Time.deltaTime), 0f, weapon.SpinUpTime);
            bool spunUp = weapon.SpinUpTime <= 0f || spin >= weapon.SpinUpTime;

            if (wantFire && spunUp)
            {
                bool triggerOk = weapon.FireMode == FireMode.Auto || fireReleased;
                if (triggerOk && Time.time >= nextFireTime)
                {
                    if (weapon.Magazine > 0) Shoot(weapon, aimRay, aiming);
                    else TryReload(); // 빈 탄창으로 쏘면 자동 재장전
                    fireReleased = false;
                }
            }
            if (fireHeld == false) fireReleased = true;

            // 라이플·미니건은 "쏘는 중" 동안 사격 모션을 반복 (샷건·로켓은 한 발마다 PlayFireShot 이 한 번 재생)
            bool loopFiring = wantFire && spunUp && weapon.Magazine > 0 && PlayerAnimParams.IsLoopFireWeapon((int)weapon.Type);
            anim.SetFiring(loopFiring);
        }

        // ───────────────────────── 쏘기 ─────────────────────────

        private void Shoot(WeaponItem weapon, Ray aimRay, bool aiming)
        {
            if (weapon.TryConsumeRound() == false) return;
            nextFireTime = Time.time + weapon.FireInterval;

            Transform muzzle = weapon.Muzzle;
            float spread = weapon.SpreadAngle * (aiming ? weapon.AimSpreadMultiplier : 1f);

            if (weapon.ShotKind == ShotKind.Projectile) LaunchProjectile(weapon, muzzle, spread);
            else
            {
                // 3인칭에서 카메라와 플레이어 사이의 물체를 맞히지 않도록, 광선 시작점을 총구 옆까지 당긴다
                Vector3 origin = aimRay.origin + aimRay.direction * Mathf.Max(0f, Vector3.Dot(muzzle.position - aimRay.origin, aimRay.direction));
                for (int i = 0; i < weapon.PelletsPerShot; i++)
                    FireHitscan(weapon, origin, Spread(aimRay.direction, spread));
            }

            CombatUtility.SpawnEffect(weapon.MuzzleFlashPrefab, muzzle.position, muzzle.rotation, weapon.EffectLifetime, muzzle);
            anim.PlayFireShot();
            EventBus<WeaponFiredEvent>.Publish(new WeaponFiredEvent(weapon.Def, muzzle.position));
            PublishAmmo();
        }

        private void FireHitscan(WeaponItem weapon, Vector3 origin, Vector3 direction)
        {
            if (CombatUtility.RaycastIgnoring(origin, direction, weapon.Range, transform, out var hit, hitMask) == false) return;

            if (hit.collider.TryGetDamageable(out var target) && target.CanBeHitBy(Faction.Player))
            {
                var info = new DamageInfo(weapon.Damage, DamageKind.Bullet, Faction.Player, gameObject, hit.point, direction,
                    weapon.Def != null ? weapon.Def.Id : 0);
                CombatUtility.ApplyDamage(target, hit.collider, info);
            }
            CombatUtility.SpawnEffect(weapon.ImpactPrefab, hit.point, Quaternion.LookRotation(hit.normal), weapon.EffectLifetime);
        }

        private void LaunchProjectile(WeaponItem weapon, Transform muzzle, float spread)
        {
            if (weapon.ProjectilePrefab == null)
            {
                Debug.LogWarning($"[PlayerWeapons] {weapon.DisplayName} 의 projectilePrefab 이 비어 있습니다.", weapon);
                return;
            }
            Vector3 dir = Spread((aimPoint - muzzle.position).normalized, spread);
            var go = Instantiate(weapon.ProjectilePrefab, muzzle.position, Quaternion.LookRotation(dir));
            var rocket = go.GetComponent<RocketProjectile>();
            if (rocket == null) rocket = go.AddComponent<RocketProjectile>();
            rocket.Launch(dir, weapon.ProjectileSpeed, weapon.Damage, weapon.ExplosionRadius, transform);
        }

        /// <summary>direction 을 중심으로 반각 angle 도 원뿔 안의 무작위 방향</summary>
        private static Vector3 Spread(Vector3 direction, float angle)
        {
            if (angle <= 0f) return direction;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(angle * Mathf.Deg2Rad);
            Quaternion basis = Quaternion.LookRotation(direction);
            return (basis * new Vector3(r.x, r.y, 1f)).normalized;
        }

        private Vector3 ComputeAimPoint(Ray aimRay, WeaponItem weapon)
        {
            float range = weapon != null ? weapon.Range : 100f;
            return CombatUtility.RaycastIgnoring(aimRay.origin, aimRay.direction, range, transform, out var hit, hitMask)
                ? hit.point
                : aimRay.origin + aimRay.direction * range;
        }

        // ───────────────────────── 재장전 ─────────────────────────

        public bool TryReload()
        {
            var weapon = Current;
            if (weapon == null || reloading || IsDrawing || weapon.IsMagazineFull) return false;

            float duration = weapon.ReloadTime;
            reloading = true;
            reloadEndTime = Time.time + duration;
            spin = 0f;
            anim.SetFiring(false);
            anim.PlayReload(duration);
            EventBus<ReloadStartedEvent>.Publish(new ReloadStartedEvent(duration));
            return true;
        }

        private void FinishReload()
        {
            reloading = false;
            Current?.Refill();
            EventBus<ReloadFinishedEvent>.Publish(new ReloadFinishedEvent());
            PublishAmmo();
        }

        public void CancelReload() => reloading = false;

        // ───────────────────────── 바꾸기 ─────────────────────────

        /// <summary>slot = 0 부터. 비어 있거나 이미 든 슬롯이면 false</summary>
        public bool TrySelect(int slot)
        {
            if (slot < 0 || slot >= slots.Length || slots[slot] == null || slot == currentSlot) return false;
            Equip(slot, playDraw: true);
            return true;
        }

        private void Equip(int slot, bool playDraw)
        {
            CancelReload();
            if (Current != null) Current.SetHeld(false);

            currentSlot = slot;
            var weapon = slots[slot];
            weapon.SetHeld(true);
            spin = 0f;
            nextFireTime = 0f;

            motor.SetSpeedModifier(CarrySpeedKey, weapon.CarryMoveSpeedMultiplier);
            anim.SetFiring(false);
            anim.SetWeaponType((int)weapon.Type);
            if (playDraw)
            {
                drawEndTime = Time.time + drawTime;
                anim.PlayDraw(drawTime);
            }
            PublishAmmo();
        }

        private int FindUsableSlot(int preferred)
        {
            if (preferred >= 0 && preferred < slots.Length && slots[preferred] != null) return preferred;
            for (int i = 0; i < slots.Length; i++) if (slots[i] != null) return i;
            return -1;
        }

        /// <summary>사망·부활 시 상태 초기화 (탄창 가득)</summary>
        public void ResetAll()
        {
            CancelReload();
            foreach (var w in slots) if (w != null) w.Refill();
            PublishAmmo();
        }

        private void PublishAmmo()
        {
            var weapon = Current;
            if (weapon != null) EventBus<AmmoChangedEvent>.Publish(new AmmoChangedEvent(weapon.Magazine, weapon.MagazineSize));
        }

        /// <summary>에디터 셋업 도구용: 슬롯 배열을 통째로 넣는다</summary>
        public void SetSlots(WeaponItem[] items) => slots = items;
    }
}
