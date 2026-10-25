using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 플레이어의 소모품 (수류탄 · 회복 팩). 개수 관리 + 수치 계산 + 사용.
    ///
    /// 흐름
    ///  - 보급 상자 → IItemUser.AddGrenades / AddHealPacks 로 개수 보충
    ///  - PlayerController 입력 → TryThrowGrenade (G) / TryUseHeal (H)
    ///  - 수치 = 원본(GrenadeTable / 인스펙터) + 강화(IUpgradeState). 강화를 사면(UpgradePurchasedEvent) 다시 계산
    ///
    /// 애니메이션 · IK 연결
    ///  - ThrowStarted → PlayerAnimationDriver 가 Throw 동작을 throwDuration 에 맞춰 재생 (그동안 조준·왼손 IK 는 꺼진다)
    ///  - 손을 떠나는 순간: 던지기 클립이 있으면 드라이버가 ReleaseByAnimation 을 켜고, 클립 분석으로 찾은 순간에 ReleaseGrenade() 를 부른다
    ///    (안 오면 throwDuration 끝에 자동으로 던진다). 클립이 없으면 releaseTime 타이머
    ///  - ThrowHand = 드라이버가 넣는 던지는 손 뼈 (비우면 카메라 앞). 손에 든 수류탄 모형은 PlayerItemIK (모형이 보이면 그 자리에서 날아간다)
    ///  - HealStarted → PlayerItemIK 가 왼손을 잠깐 회복 자리로 / HealEnded → 이펙트
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerItems : MonoBehaviour, IItemUser
    {
        #region 설정(인스펙터)
        [Header("수류탄")]
        [Tooltip("던질 수류탄 프리팹 (Assets/Prefabs/Weapons/Grenade)")]
        [SerializeField] private Grenade grenadePrefab;
        [Tooltip("PlayerTable 이 없을 때 쓰는 시작 / 최대 개수 (테스트용)")]
        [SerializeField] private int fallbackStartGrenades = 3;
        [SerializeField] private int fallbackMaxGrenades = 5;
        [Tooltip("GrenadeTable 에 행이 없을 때 쓰는 값 (테스트용)")]
        [SerializeField] private GrenadeStats fallbackGrenadeStats = new GrenadeStats
        {
            damage = 80, radius = 6f, fuseTime = 2f, throwSpeed = 18f,
        };
        [Tooltip("폭발이 검사할 레이어")]
        [SerializeField] private LayerMask grenadeHitMask = ~0;

        [Header("투척 동작")]
        [Tooltip("던지는 동작 전체 시간. 이 동안은 다시 던지거나 쏠 수 없다")]
        [SerializeField] private float throwDuration = 0.6f;
        [Tooltip("G 를 누른 뒤 수류탄이 손을 떠나는 시간")]
        [SerializeField] private float releaseTime = 0.25f;
        [Tooltip("켜면 타이머 대신 애니메이션 이벤트가 ReleaseGrenade() 를 부른다")]
        [SerializeField] private bool releaseByAnimationEvent;
        [Tooltip("조준선보다 위로 던지는 각도(도)")]
        [SerializeField] private float upwardAngle = 10f;
        [Tooltip("수류탄이 생성될 손 (비우면 카메라 앞 spawnForward 지점)")]
        [SerializeField] private Transform throwHand;
        [SerializeField] private float spawnForward = 0.6f;
        [Tooltip("플레이어 이동 속도를 수류탄에 얼마나 더할지")]
        [SerializeField, Range(0f, 1f)] private float inheritVelocity = 0.5f;

        [Header("회복 팩")]
        [SerializeField] private int startHealPacks = 1;
        [SerializeField] private int maxHealPacks = 3;
        [Tooltip("회복 팩 기본 수치. totalCap 0 = 최대 체력")]
        [SerializeField] private HealStats baseHealStats = new HealStats
        {
            healPerSecond = 50f, duration = 3f, totalCap = 0,
        };
        #endregion

        #region 상태
        private PlayerController player;
        private GunSlot gunSlot;
        private GrenadeDef grenadeDef;
        private Collider[] ownColliders;

        private int grenades;
        private int healPacks;

        private float throwStartTime;
        private bool throwing;
        private bool released;

        private bool healing;
        private float healEndTime;
        private int healBudget;
        private float healCarry;
        #endregion

        #region 외부 API
        public event Action ThrowStarted;
        public event Action GrenadeReleased;
        public event Action HealStarted;
        public event Action HealEnded;

        public int Grenades => grenades;
        public int MaxGrenades
        {
            get
            {
                int baseMax = DataManager.Player != null ? DataManager.Player.MaxGrenades : fallbackMaxGrenades;
                return baseMax + Mathf.RoundToInt(UpgradeValue(UpgradeType.GrenadeCapacity));
            }
        }
        public int HealPacks => healPacks;
        public int MaxHealPacks => maxHealPacks;

        /// <summary>강화가 적용된 수류탄 능력치</summary>
        public GrenadeStats GrenadeStats { get; private set; }
        /// <summary>강화가 적용된 회복 팩 능력치</summary>
        public HealStats HealStats { get; private set; }

        public bool IsThrowing => throwing;
        /// <summary>던지는 동작 전체 시간 (애니메이션 재생 속도를 여기에 맞춘다)</summary>
        public float ThrowDuration => throwDuration;
        /// <summary>true 면 타이머 대신 애니메이션(드라이버)이 ReleaseGrenade() 를 부른다</summary>
        public bool ReleaseByAnimation { get => releaseByAnimationEvent; set => releaseByAnimationEvent = value; }
        /// <summary>수류탄이 생성될 손 (드라이버가 던지는 손 뼈를 넣는다)</summary>
        public Transform ThrowHand { get => throwHand; set => throwHand = value; }
        /// <summary>손에 든 수류탄 모형 (PlayerItemIK 가 넣는다). 보이는 동안은 손 뼈 대신 여기서 날아간다</summary>
        public Transform ThrowSpawn { get; set; }
        /// <summary>던질 수류탄 프리팹 (손에 든 모형을 만들 때 쓴다)</summary>
        public Grenade GrenadePrefab => grenadePrefab;
        public bool IsHealing => healing;
        /// <summary>회복 효과가 끝날 때까지 남은 시간(초)</summary>
        public float HealRemainingTime => healing ? Mathf.Max(0f, healEndTime - Time.time) : 0f;

        public bool AddGrenades(int amount)
        {
            int max = MaxGrenades;
            if (amount <= 0 || grenades >= max) return false;
            grenades = Mathf.Min(grenades + amount, max);
            PublishGrenades();
            return true;
        }

        public bool AddHealPacks(int amount)
        {
            if (amount <= 0 || healPacks >= maxHealPacks) return false;
            healPacks = Mathf.Min(healPacks + amount, maxHealPacks);
            PublishHealPacks();
            return true;
        }

        /// <summary>투척 시작. 수류탄이 없거나, 던지는 중이거나, 행동할 수 없으면 false</summary>
        public bool TryThrowGrenade()
        {
            if (throwing || grenades <= 0 || CanAct == false) return false;
            if (grenadePrefab == null)
            {
                Debug.LogWarning($"{name}: PlayerItems 에 수류탄 프리팹이 연결되지 않았습니다", this);
                return false;
            }

            throwing = true;
            released = false;
            throwStartTime = Time.time;
            if (gunSlot != null) gunSlot.TriggerUp();
            ThrowStarted?.Invoke();
            return true;
        }

        /// <summary>수류탄이 손을 떠나는 순간. 애니메이션 이벤트에서 불러도 된다 (한 번 던지기에 한 번만 동작)</summary>
        public void ReleaseGrenade()
        {
            if (throwing == false || released) return;
            released = true;

            Ray aim = gunSlot != null ? gunSlot.AimRay : new Ray(transform.position + Vector3.up * 1.5f, transform.forward);
            Vector3 spawn = SpawnPoint(aim);
            Vector3 aimPoint = gunSlot != null ? gunSlot.GetAimPoint(100f, grenadeHitMask) : aim.GetPoint(100f);
            Vector3 direction = (aimPoint - spawn).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            if (right.sqrMagnitude > 0.0001f) direction = Quaternion.AngleAxis(-upwardAngle, right.normalized) * direction;

            Vector3 velocity = direction * GrenadeStats.throwSpeed + player.Velocity * inheritVelocity;
            var grenade = Instantiate(grenadePrefab, spawn, Quaternion.LookRotation(direction));
            var context = new ProjectileContext(Faction.Player, gameObject, transform, grenadeDef != null ? grenadeDef.Id : 0, grenadeHitMask);
            grenade.Launch(GrenadeStats, context, grenadeDef, velocity, ownColliders);

            grenades--;
            PublishGrenades();
            GrenadeReleased?.Invoke();
        }

        /// <summary>회복 시작. 팩이 없거나, 이미 회복 중이거나, 체력이 가득이면 false (팩을 쓰지 않음)</summary>
        public bool TryUseHeal()
        {
            if (healing || throwing || healPacks <= 0 || CanAct == false) return false;
            if (player.CurrentHealth >= player.MaxHealth) return false;

            HealStats stats = HealStats;
            healing = true;
            healEndTime = Time.time + stats.duration;
            healBudget = stats.totalCap > 0 ? stats.totalCap : player.MaxHealth;
            healCarry = 0f;

            healPacks--;
            PublishHealPacks();
            HealStarted?.Invoke();
            return true;
        }

        /// <summary>강화 값으로 능력치를 다시 만든다. 최대 개수가 줄면 개수도 맞춘다</summary>
        public void RefreshStats()
        {
            grenadeDef = DataManager.Player != null ? DataManager.Player.Grenade : null;
            GrenadeStats = grenadeDef != null ? GrenadeStats.From(grenadeDef) : fallbackGrenadeStats;
            // 수류탄 · 회복 강화가 생기면 여기서 UpgradeValue(UpgradeType.xxx) 를 더한다

            HealStats = baseHealStats;

            if (grenades > MaxGrenades) grenades = MaxGrenades;
            PublishGrenades();
            PublishHealPacks();
        }
        #endregion

        #region 유니티 메시지
        private void Awake()
        {
            player = GetComponent<PlayerController>();
            gunSlot = GetComponent<GunSlot>();
            ownColliders = GetComponentsInChildren<Collider>(true);
        }

        private void OnEnable()
        {
            EventBus<UpgradePurchasedEvent>.Subscribe(OnUpgradePurchased);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
        }

        private void OnDisable()
        {
            EventBus<UpgradePurchasedEvent>.Unsubscribe(OnUpgradePurchased);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
        }

        private void Start()
        {
            ResetCounts();
            RefreshStats();
        }

        private void Update()
        {
            UpdateThrow();
            UpdateHeal(Time.deltaTime);
        }
        #endregion

        #region 내부
        // 공중(발이 땅에 닿지 않음)에서는 수류탄·회복을 쓸 수 없다
        private bool CanAct => player.IsAlive && player.IsRolling == false && player.IsExternallyControlled == false && player.IsOnGround;

        private void UpdateThrow()
        {
            if (throwing == false) return;
            float elapsed = Time.time - throwStartTime;
            if (released == false && releaseByAnimationEvent == false && elapsed >= releaseTime) ReleaseGrenade();
            if (elapsed < throwDuration) return;

            // 애니메이션 이벤트가 오지 않았으면 끝에서 던진다
            if (released == false) ReleaseGrenade();
            throwing = false;
        }

        /// <summary>
        /// 지속 시간 동안 초당 healPerSecond 씩 회복. 체력이 가득이면 쉬었다가 깎이면 이어서 회복.
        /// 이번 사용으로 회복한 양이 healBudget 에 닿거나 시간이 끝나면 멈춘다
        /// </summary>
        private void UpdateHeal(float dt)
        {
            if (healing == false) return;
            if (player.IsAlive == false) { StopHeal(); return; }

            if (player.CurrentHealth < player.MaxHealth)
            {
                healCarry += HealStats.healPerSecond * dt;
                int amount = Mathf.Min(Mathf.FloorToInt(healCarry), healBudget);
                if (amount > 0)
                {
                    int before = player.CurrentHealth;
                    player.Heal(amount);
                    healBudget -= player.CurrentHealth - before;
                    healCarry -= amount;
                }
            }
            else
            {
                healCarry = 0f;
            }

            if (healBudget <= 0 || Time.time >= healEndTime) StopHeal();
        }

        private void StopHeal()
        {
            if (healing == false) return;
            healing = false;
            HealEnded?.Invoke();
        }

        /// <summary>수류탄 생성 위치. 손에 든 모형이 보이면 그 자리, 아니면 손, 둘 다 없으면 조준선 앞. 벽 너머에 생기지 않게 막는다</summary>
        private Vector3 SpawnPoint(Ray aim)
        {
            Vector3 target = ThrowSpawn != null && ThrowSpawn.gameObject.activeInHierarchy ? ThrowSpawn.position
                : throwHand != null ? throwHand.position
                : aim.GetPoint(spawnForward);
            Vector3 from = aim.origin;
            Vector3 delta = target - from;
            float distance = delta.magnitude;
            if (distance > 0.0001f &&
                Physics.Raycast(from, delta / distance, out var hit, distance, grenadeHitMask, QueryTriggerInteraction.Ignore) &&
                hit.collider.transform.IsChildOf(transform) == false)
                return hit.point - delta / distance * 0.15f;
            return target;
        }

        private void ResetCounts()
        {
            grenades = Mathf.Clamp(DataManager.Player != null ? DataManager.Player.StartGrenades : fallbackStartGrenades, 0, MaxGrenades);
            healPacks = Mathf.Clamp(startHealPacks, 0, maxHealPacks);
            throwing = false;
            StopHeal();
        }

        private static float UpgradeValue(UpgradeType type)
            => ServiceLocator.TryGet<IUpgradeState>(out var upgrades) ? upgrades.GetTotalValue(type) : 0f;

        private void OnUpgradePurchased(UpgradePurchasedEvent evt) => RefreshStats();

        /// <summary>처음부터 재시작이면 개수 초기화. 다음 라운드 진행이면 유지 (BL-032, BL-033)</summary>
        private void OnRoundCleanup(RoundCleanupEvent evt)
        {
            if (evt.FullReset == false) return;
            ResetCounts();
            RefreshStats();
        }

        private void PublishGrenades() => EventBus<GrenadeCountChangedEvent>.Publish(new GrenadeCountChangedEvent(grenades, MaxGrenades));
        private void PublishHealPacks() => EventBus<HealPackCountChangedEvent>.Publish(new HealPackCountChangedEvent(healPacks, maxHealPacks));
        #endregion
    }
}
