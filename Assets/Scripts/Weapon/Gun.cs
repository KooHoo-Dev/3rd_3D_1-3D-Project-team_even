using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using Random = UnityEngine.Random;

namespace HybridShooter.Weapon
{
    /// <summary>총이 실제로 쓰는 능력치. 테이블(WeaponDef) 값 또는 강화가 적용된 값</summary>
    [Serializable]
    public struct GunStats
    {
        public int damage;
        [Tooltip("분당 발사 수 (RPM)")]
        public float fireRate;
        public int magazineSize;
        public float reloadTime;
        public float range;
        [Tooltip("기본 탄 퍼짐 원뿔 반각(도)")]
        public float spreadAngle;

        /// <summary>발사 간격(초) = 60 / RPM</summary>
        public float FireInterval => fireRate > 0f ? 60f / fireRate : 0f;

        public static GunStats From(WeaponDef def) => new GunStats
        {
            damage = def.Damage,
            fireRate = def.FireRate,
            magazineSize = def.MagazineSize,
            reloadTime = def.ReloadTime,
            range = def.Range,
            spreadAngle = def.SpreadAngle,
        };
    }

    /// <summary>
    /// 모든 총의 공통 뼈대. 무기별 차이는 하위 클래스가 virtual 함수를 덮어써서 만든다.
    ///
    /// 흐름
    ///  - GunSlot.Start → Initialize: WeaponTable(weaponId) + 강화(IUpgradeState) 로 능력치(Stats) 계산.
    ///    강화를 사면(UpgradePurchasedEvent) 다시 계산한다. 테이블에 행이 없으면 fallbackStats 를 쓴다.
    ///  - 입력: PlayerController → GunSlot → 지금 든 총의 TriggerDown / TriggerUp / AimDown / AimUp / Reload / ToggleFireMode
    ///  - 매 프레임: GunSlot.Update → Tick (장전·퍼짐 회복·반동 회복 → OnTick)
    ///  - 실제 발사: TryFire (탄·연사 간격·장전 검사) → Shoot (무기별 abstract)
    ///
    /// 무기별 기본 튜닝 값은 하위 클래스 생성자에서 정한다.
    /// (필드 초기값과 같은 시점이라, 씬/프리팹에 저장된 인스펙터 값이 있으면 그 값이 덮어쓴다)
    /// </summary>
    public abstract class Gun : MonoBehaviour
    {
        #region 설정(인스펙터)
        [Header("데이터")]
        [Tooltip("WeaponTable.Id. 게임 시작 시 이 행 + 강화 값으로 능력치를 만든다")]
        [SerializeField] protected int weaponId;
        [Tooltip("WeaponTable 에 행이 없을 때 쓰는 값 (테스트용)")]
        [SerializeField] protected GunStats fallbackStats = new GunStats
        {
            damage = 10, fireRate = 600f, magazineSize = 30, reloadTime = 2f, range = 100f, spreadAngle = 1f,
        };

        [Header("기준점")]
        [Tooltip("총구. 비우면 처음 꺼낼 때 모델 앞 끝에 자동으로 만든다")]
        [SerializeField] protected Transform muzzle;
        [Tooltip("꺼내는 데 걸리는 시간. 이 동안은 쏠 수 없다")]
        [SerializeField] protected float equipTime = 0.35f;

        [Header("명중")]
        [Tooltip("총알·폭발이 검사할 레이어")]
        [SerializeField] protected LayerMask hitMask = ~0;
        [Tooltip("맞은 자리에 생성할 이펙트 (선택)")]
        [SerializeField] protected GameObject impactEffectPrefab;
        [Tooltip("총구 화염 (선택)")]
        [SerializeField] protected GameObject muzzleFlashPrefab;

        [Header("탄 퍼짐 (도)")]
        [Tooltip("한 발 쏠 때마다 늘어나는 퍼짐")]
        [SerializeField] protected float bloomPerShot = 0.3f;
        [SerializeField] protected float maxBloom = 3f;
        [Tooltip("사격을 멈추면 초당 이만큼 줄어든다")]
        [SerializeField] protected float bloomRecovery = 6f;
        [Tooltip("최고 속도(달리기)로 움직일 때 더해지는 퍼짐")]
        [SerializeField] protected float moveSpreadPenalty = 1.5f;
        [Tooltip("공중에 떠 있을 때 더해지는 퍼짐")]
        [SerializeField] protected float airSpreadPenalty = 3f;
        [Tooltip("조준 중 퍼짐 배율")]
        [SerializeField, Range(0f, 1f)] protected float aimSpreadMultiplier = 0.4f;

        [Header("반동 (도)")]
        [Tooltip("한 발당 위로 튀는 각도")]
        [SerializeField] protected float recoilPitch = 0.5f;
        [Tooltip("한 발당 좌우 랜덤 폭(±)")]
        [SerializeField] protected float recoilYaw = 0.2f;
        [Tooltip("조준 중 반동 배율")]
        [SerializeField, Range(0f, 1f)] protected float aimRecoilMultiplier = 0.7f;
        [Tooltip("사격을 멈추면 위로 튄 반동 중 이 비율만큼 저절로 내려온다")]
        [SerializeField, Range(0f, 1f)] protected float recoilRecoverRatio = 0.6f;
        [Tooltip("반동이 내려오는 속도(도/초)")]
        [SerializeField] protected float recoilRecoverSpeed = 15f;

        [Header("조준 (우클릭)")]
        [Tooltip("조준 중 FOV 배율. 작을수록 많이 확대")]
        [SerializeField, Range(0.1f, 1f)] protected float aimFovMultiplier = 0.8f;
        [Tooltip("조준 중 마우스 감도 배율")]
        [SerializeField, Range(0.1f, 1f)] protected float aimSensitivityMultiplier = 0.8f;
        [Tooltip("조준 중 이동 속도 배율")]
        [SerializeField, Range(0.1f, 1f)] protected float aimMoveSpeedMultiplier = 0.75f;

        [Header("장전 소리")]
        [Tooltip("장전 시간 중 탄창을 넣는 소리(ReloadInsertSfx)를 낼 시점 비율. 한 발씩 넣는 산탄총은 쓰지 않음")]
        [SerializeField, Range(0.1f, 0.9f)] protected float reloadInsertAt = 0.55f;
        #endregion

        #region 상태
        private float nextFireTime;
        private float readyTime;
        private float reloadEndTime;
        private float reloadInsertTime;
        private bool reloadInserted;
        private float bloom;
        private float recoilToRecover;
        private float lastShotTime = float.NegativeInfinity;
        private bool aimZoomApplied;
        private bool subscribed;

        protected static readonly RaycastHit[] SharedHits = new RaycastHit[32];
        #endregion

        #region 외부 API
        public int WeaponId => weaponId;
        /// <summary>테이블 행 (없으면 null)</summary>
        public WeaponDef Def { get; private set; }
        /// <summary>강화 전 기본 능력치</summary>
        public GunStats BaseStats { get; private set; }
        /// <summary>강화가 적용된 능력치</summary>
        public GunStats Stats { get; private set; }
        public string DisplayName => Def != null ? Def.Name : GetType().Name;
        public GunSlot Owner { get; private set; }

        public int Ammo { get; private set; }
        public int MagazineSize => Stats.magazineSize;
        public bool IsReloading { get; protected set; }
        public bool IsTriggerHeld { get; private set; }
        public bool IsAiming { get; private set; }
        public bool IsEquipping => Time.time < readyTime;
        public Transform Muzzle { get { EnsureMuzzle(); return muzzle; } }

        /// <summary>지금 쏘면 적용될 퍼짐(도). 이동·공중·연사·조준 반영</summary>
        public virtual float CurrentSpread => HipSpread * (IsAiming ? aimSpreadMultiplier : 1f);

        /// <summary>GunSlot 이 게임 시작 시(또는 새로 슬롯에 넣을 때) 호출. 테이블·강화 값을 읽어 탄창을 채운다</summary>
        public void Initialize(GunSlot owner)
        {
            Owner = owner;
            Def = DataManager.Weapons.TryGet(weaponId, out var def) ? def : null;
            if (Def == null)
                Debug.LogWarning($"{name}: WeaponTable 에 Id {weaponId} 가 없어 fallbackStats 를 사용합니다", this);

            PrepareModel();
            RefreshStats();
            Ammo = Stats.magazineSize;

            if (subscribed == false)
            {
                EventBus<UpgradePurchasedEvent>.Subscribe(OnUpgradePurchased);
                subscribed = true;
            }
            OnInitialized();
        }

        /// <summary>테이블 값 + 강화 값으로 Stats 를 다시 만든다. 탄창이 줄어들면 남은 탄도 맞춘다</summary>
        public void RefreshStats()
        {
            GunStats stats = Def != null ? GunStats.From(Def) : fallbackStats;
            BaseStats = stats;

            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades))
            {
                float reloadCut = upgrades.GetTotalValue(UpgradeType.ReloadSpeed);
                float extraMagazine = upgrades.GetTotalValue(UpgradeType.MagazineSize);
                stats.reloadTime *= Mathf.Clamp(1f - reloadCut, 0.1f, 1f);
                stats.magazineSize += Mathf.RoundToInt(extraMagazine * MagazineUpgradeRate);
            }
            Stats = stats;

            if (Ammo > Stats.magazineSize) Ammo = Stats.magazineSize;
            PublishAmmo();
        }

        /// <summary>슬롯에서 꺼낼 때</summary>
        public void Equip()
        {
            gameObject.SetActive(true);
            readyTime = Time.time + equipTime;
            EnsureMuzzle();
            OnEquipped();
            PublishAmmo();
        }

        /// <summary>다른 총으로 바꿀 때. 장전·조준·방아쇠를 모두 푼다</summary>
        public void Unequip()
        {
            TriggerUp();
            AimUp();
            CancelReload();
            OnUnequipped();
            gameObject.SetActive(false);
        }

        public void TriggerDown()
        {
            if (IsTriggerHeld) return;
            IsTriggerHeld = true;
            OnTriggerDown();
        }

        public void TriggerUp()
        {
            if (IsTriggerHeld == false) return;
            IsTriggerHeld = false;
            OnTriggerUp();
        }

        public void AimDown()
        {
            if (IsAiming) return;
            IsAiming = true;
            AimStartTime = Time.time;
            OnAimChanged(true);
            RefreshAimZoom();
        }

        public void AimUp()
        {
            if (IsAiming == false) return;
            IsAiming = false;
            OnAimChanged(false);
            RefreshAimZoom();
        }

        /// <summary>재장전 시작. 이미 장전 중이거나 탄창이 가득 차면 false</summary>
        public bool Reload()
        {
            if (IsReloading || Ammo >= Stats.magazineSize || Owner == null) return false;
            StartReload();
            return true;
        }

        public void CancelReload()
        {
            if (IsReloading == false) return;
            FinishReload();
        }

        /// <summary>사격 모드 전환 (B). 지원하지 않는 총은 false</summary>
        public virtual bool ToggleFireMode() => false;

        /// <summary>GunSlot 이 장착 중인 총에 대해서만 매 프레임 호출</summary>
        public void Tick(float dt)
        {
            if (IsReloading) UpdateReload();

            bool resting = Time.time - lastShotTime > Stats.FireInterval + 0.05f;
            if (resting) bloom = Mathf.MoveTowards(bloom, 0f, bloomRecovery * dt);
            if (resting && recoilToRecover > 0f)
            {
                float step = Mathf.Min(recoilToRecover, recoilRecoverSpeed * dt);
                recoilToRecover -= step;
                Owner.AddRecoil(-step, 0f);
            }

            OnTick(dt);
            if (aimZoomApplied != AimZoomActive) RefreshAimZoom();
        }
        #endregion

        #region 하위 클래스가 덮어쓰는 함수
        /// <summary>실제 발사 (총알 판정·투사체 생성). TryFire 가 탄·연사 간격을 확인한 뒤 호출</summary>
        protected abstract void Shoot();

        /// <summary>기본: 누를 때 한 발 (반자동)</summary>
        protected virtual void OnTriggerDown() => TryFire();
        protected virtual void OnTriggerUp() { }
        protected virtual void OnTick(float dt) { }
        protected virtual void OnInitialized() { }
        protected virtual void OnEquipped() { }
        protected virtual void OnUnequipped() { }
        protected virtual void OnAimChanged(bool aiming) { }

        /// <summary>탄창 강화(+N발) 중 이 총이 받는 비율. 탄창이 작은 총은 낮게</summary>
        protected virtual float MagazineUpgradeRate => 1f;
        /// <summary>장전 중에 쏘면 장전을 끊고 쏠 수 있는지 (산탄총 한 발씩 장전)</summary>
        protected virtual bool CanInterruptReload => false;
        /// <summary>지금 화면 확대를 걸지. 저격총은 노리쇠 당기는 동안 끈다</summary>
        protected virtual bool AimZoomActive => IsAiming;

        /// <summary>한 발당 반동 (x = 위, y = 오른쪽)</summary>
        protected virtual Vector2 GetRecoilKick() => new Vector2(recoilPitch, Random.Range(-recoilYaw, recoilYaw));

        /// <summary>거리에 따른 피해. 기본은 감쇠 없음</summary>
        protected virtual int DamageAtDistance(float distance) => Stats.damage;

        /// <summary>기본: 탄창 전체를 ReloadTime 뒤에 한 번에 채운다 (소리: 시작 → reloadInsertAt 시점 탄창 넣기 → 완료)</summary>
        protected virtual void StartReload()
        {
            IsReloading = true;
            reloadEndTime = Time.time + Stats.reloadTime;
            reloadInsertTime = Time.time + Stats.reloadTime * reloadInsertAt;
            reloadInserted = false;
            NotifyReloadStarted(Stats.reloadTime);
        }

        protected virtual void UpdateReload()
        {
            if (reloadInserted == false && Time.time >= reloadInsertTime)
            {
                reloadInserted = true;
                PlayReloadInsertSfx();
            }
            if (Time.time < reloadEndTime) return;
            SetAmmo(Stats.magazineSize);
            CompleteReload();
        }
        #endregion

        #region 하위 클래스용 도구
        protected float AimStartTime { get; set; }
        /// <summary>이번 연사에서 몇 번째 발인지 (0 = 첫 발). Shoot 안에서 읽으면 지금 발의 번호</summary>
        protected int ShotsInSpray { get; private set; }
        protected float NextFireTime => nextFireTime;

        /// <summary>조준을 뺀 퍼짐 = 기본 + 연사 누적 + 이동/공중</summary>
        protected float HipSpread => Stats.spreadAngle + bloom + MovementSpread;

        protected float MovementSpread
        {
            get
            {
                if (Owner == null) return 0f;
                float spread = moveSpreadPenalty * Owner.MoveRatio;
                if (Owner.IsGrounded == false) spread += airSpreadPenalty;
                return spread;
            }
        }

        /// <summary>
        /// 쏠 수 있으면 탄 1발을 쓰고 Shoot 을 부른다. 반동·퍼짐·소리·이벤트까지 처리.
        /// 탄이 없으면 자동 재장전을 시작하고 false
        /// </summary>
        protected bool TryFire()
        {
            if (Owner == null || Owner.CanUseWeapon == false) return false;
            if (IsEquipping || Time.time < nextFireTime) return false;
            if (IsReloading && (CanInterruptReload == false || Ammo <= 0)) return false;
            if (Ammo <= 0)
            {
                Reload();
                return false;
            }
            if (IsReloading) CancelReload();

            float interval = Stats.FireInterval;
            bool continuous = Time.time - nextFireTime < Time.deltaTime + 0.01f && Time.time - lastShotTime < interval * 1.5f + 0.05f;
            ShotsInSpray = continuous ? ShotsInSpray + 1 : 0;
            // 연사 중이면 이전 예정 시각에서 이어 붙여 프레임이 낮아도 RPM 을 지킨다
            nextFireTime = (continuous ? nextFireTime : Time.time) + interval;
            lastShotTime = Time.time;

            SetAmmo(Ammo - 1);
            Shoot();
            ApplyRecoil();
            bloom = Mathf.Min(bloom + bloomPerShot, maxBloom);

            Vector3 muzzlePos = Muzzle.position;
            if (muzzleFlashPrefab != null) Destroy(Instantiate(muzzleFlashPrefab, muzzlePos, Muzzle.rotation, Muzzle), 1f);
            PlaySfx(Def?.FireSfx, muzzlePos);
            if (IsCurrent) EventBus<WeaponFiredEvent>.Publish(new WeaponFiredEvent(Def, muzzlePos));
            return true;
        }

        /// <summary>다음 발사를 extra 초 더 늦춘다 (점사 사이 간격 등)</summary>
        protected void DelayNextShot(float extra) => nextFireTime = Mathf.Max(nextFireTime, Time.time) + extra;

        protected void SetAmmo(int value)
        {
            Ammo = Mathf.Clamp(value, 0, Stats.magazineSize);
            PublishAmmo();
        }

        protected void NotifyReloadStarted(float duration)
        {
            PlaySfx(Def?.ReloadSfx, transform.position);
            if (IsCurrent) EventBus<ReloadStartedEvent>.Publish(new ReloadStartedEvent(duration));
        }

        /// <summary>탄창·탄 넣는 소리 (기본 총은 장전 중간, 산탄총은 한 발마다)</summary>
        protected void PlayReloadInsertSfx() => PlaySfx(Def?.ReloadInsertSfx, transform.position);

        /// <summary>탄을 끝까지 채운 장전 완료. 완료음(노리쇠·펌프) 후 FinishReload. 취소는 CancelReload → FinishReload 로 완료음 없음</summary>
        protected void CompleteReload()
        {
            PlaySfx(Def?.ReloadEndSfx, transform.position);
            FinishReload();
        }

        /// <summary>장전 종료 (완료·취소 공통). UI 진행 표시를 닫도록 ReloadFinishedEvent 를 보낸다</summary>
        protected void FinishReload()
        {
            IsReloading = false;
            if (IsCurrent) EventBus<ReloadFinishedEvent>.Publish(new ReloadFinishedEvent());
        }

        /// <summary>조준 방향에 지금 퍼짐을 적용한 방향</summary>
        protected Vector3 SpreadDirection() => RandomInCone(Owner.AimRay.direction, CurrentSpread);

        /// <summary>조준선(화면 중앙)에서 히트스캔 한 발. 맞으면 피해 + 이펙트</summary>
        protected bool FireBullet(Vector3 direction)
        {
            Ray ray = new Ray(Owner.AimRay.origin, direction);
            if (Raycast(ray, Stats.range, out var hit) == false)
            {
                Debug.DrawRay(ray.origin, direction * Stats.range, Color.yellow, 0.1f);
                return false;
            }
            Debug.DrawLine(ray.origin, hit.point, Color.red, 0.1f);
            DealDamage(hit.collider, DamageAtDistance(hit.distance), hit.point, direction);
            SpawnImpact(hit);
            return true;
        }

        /// <summary>자기 몸을 뺀 가장 가까운 명중</summary>
        protected bool Raycast(Ray ray, float range, out RaycastHit closest)
        {
            int count = Physics.RaycastNonAlloc(ray, SharedHits, range, hitMask, QueryTriggerInteraction.Ignore);
            closest = default;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (IsOwnCollider(SharedHits[i].collider) || SharedHits[i].distance >= best) continue;
                best = SharedHits[i].distance;
                closest = SharedHits[i];
            }
            return best < float.MaxValue;
        }

        /// <summary>자기 몸을 뺀 모든 명중을 가까운 순으로 SharedHits 앞쪽에 채우고 개수를 돌려준다 (관통용)</summary>
        protected int RaycastAll(Ray ray, float range)
        {
            int count = Physics.RaycastNonAlloc(ray, SharedHits, range, hitMask, QueryTriggerInteraction.Ignore);
            int kept = 0;
            for (int i = 0; i < count; i++)
                if (IsOwnCollider(SharedHits[i].collider) == false) SharedHits[kept++] = SharedHits[i];
            Array.Sort(SharedHits, 0, kept, HitDistanceComparer.Instance);
            return kept;
        }

        protected bool IsOwnCollider(Collider col) => Owner != null && col.transform.IsChildOf(Owner.OwnerRoot);

        /// <summary>맞은 콜라이더가 피해를 받을 수 있는 적이면 피해를 준다</summary>
        protected bool DealDamage(Collider col, int amount, Vector3 point, Vector3 direction, DamageKind kind = DamageKind.Bullet)
        {
            if (amount <= 0 || col.TryGetDamageable(out var target) == false || target.CanBeHitBy(Owner.Faction) == false) return false;
            target.TakeDamage(new DamageInfo(amount, kind, Owner.Faction, Owner.gameObject, point, direction, weaponId));
            return true;
        }

        protected void SpawnImpact(in RaycastHit hit)
        {
            if (impactEffectPrefab == null) return;
            Destroy(Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal)), 2f);
        }

        /// <summary>투사체 생성 위치. 총구가 벽 너머에 있으면 벽 앞에서 생성 (벽 뚫기 방지)</summary>
        protected Vector3 ProjectileSpawnPoint()
        {
            Vector3 from = Owner.AimRay.origin;
            Vector3 to = Muzzle.position;
            Vector3 delta = to - from;
            if (Raycast(new Ray(from, delta), delta.magnitude, out var hit))
                return hit.point - delta.normalized * 0.1f;
            return to;
        }

        /// <summary>투사체가 같이 들고 다닐 "누가 쐈나" 정보</summary>
        protected ProjectileContext CreateProjectileContext() =>
            new ProjectileContext(Owner.Faction, Owner.gameObject, Owner.OwnerRoot, weaponId, hitMask);

        protected static void PlaySfx(SoundDef sound, Vector3 position)
        {
            if (sound == null) return;
            if (ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(sound, position);
        }

        /// <summary>forward 를 중심으로 반각 halfAngle(도) 원뿔 안의 무작위 방향 (원판 균등 분포)</summary>
        protected static Vector3 RandomInCone(Vector3 forward, float halfAngle)
        {
            if (halfAngle <= 0f) return forward.normalized;
            return OffsetDirection(forward, Random.insideUnitCircle * halfAngle);
        }

        /// <summary>forward 에서 (오른쪽, 위) 로 각도(도)만큼 벌린 방향</summary>
        protected static Vector3 OffsetDirection(Vector3 forward, Vector2 offsetDegrees)
        {
            Quaternion basis = Quaternion.LookRotation(forward);
            Vector3 local = new Vector3(Mathf.Tan(offsetDegrees.x * Mathf.Deg2Rad), Mathf.Tan(offsetDegrees.y * Mathf.Deg2Rad), 1f);
            return (basis * local).normalized;
        }

        /// <summary>falloffStart 까지는 100%, falloffEnd 에서 minRatio 까지 선형 감소</summary>
        protected static int FalloffDamage(int damage, float distance, float falloffStart, float falloffEnd, float minRatio)
        {
            float t = Mathf.InverseLerp(falloffStart, Mathf.Max(falloffStart + 0.01f, falloffEnd), distance);
            return Mathf.Max(1, Mathf.RoundToInt(damage * Mathf.Lerp(1f, minRatio, t)));
        }
        #endregion

        #region 내부
        private bool IsCurrent => Owner != null && Owner.CurrentGun == this;

        private void OnDestroy()
        {
            if (subscribed) EventBus<UpgradePurchasedEvent>.Unsubscribe(OnUpgradePurchased);
        }

        private void OnUpgradePurchased(UpgradePurchasedEvent evt) => RefreshStats();

        private void PublishAmmo()
        {
            if (IsCurrent) EventBus<AmmoChangedEvent>.Publish(new AmmoChangedEvent(Ammo, Stats.magazineSize));
        }

        private void ApplyRecoil()
        {
            Vector2 kick = GetRecoilKick();
            if (IsAiming) kick *= aimRecoilMultiplier;
            Owner.AddRecoil(kick.x, kick.y);
            recoilToRecover += kick.x * recoilRecoverRatio;
        }

        private void RefreshAimZoom()
        {
            if (Owner == null) return;
            aimZoomApplied = AimZoomActive;
            if (aimZoomApplied) Owner.SetAimZoom(aimFovMultiplier, aimSensitivityMultiplier, aimMoveSpeedMultiplier);
            else Owner.ClearAimZoom();
        }

        /// <summary>에셋 모델의 충돌용 메시(UCX_)를 꺼서 플레이어 몸·카메라와 부딪히지 않게 하고, 레이어를 총과 맞춘다</summary>
        private void PrepareModel()
        {
            foreach (var col in GetComponentsInChildren<Collider>(true)) col.enabled = false;
            int layer = gameObject.layer;
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>총구가 비어 있으면 모델 경계상자의 앞 끝에 만든다</summary>
        private void EnsureMuzzle()
        {
            if (muzzle != null) return;
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(transform, false);

            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            Vector3 f = transform.forward;
            float extent = Mathf.Abs(f.x) * bounds.extents.x + Mathf.Abs(f.y) * bounds.extents.y + Mathf.Abs(f.z) * bounds.extents.z;
            muzzle.position = bounds.center + f * extent;
        }

        private sealed class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
        #endregion
    }
}
