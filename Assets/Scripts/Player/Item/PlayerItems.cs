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
    ///  - PlayerController 입력 → TryThrowGrenade (G) / TryUseHeal (V)
    ///  - 수치 = 원본(GrenadeTable / 인스펙터) + 강화(IUpgradeState). 강화를 사면(UpgradePurchasedEvent) 다시 계산
    ///
    /// 애니메이션 · IK 연결
    ///  - ThrowStarted → PlayerAnimationDriver 가 Throw 동작을 던지기 직전 자세(throwStart)부터 throwDuration 에 맞춰 재생 (그동안 조준·왼손 IK 는 꺼진다)
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
        [Tooltip("수류탄이 생성될 손 (비우면 카메라 앞 spawnForward 지점)")]
        [SerializeField] private Transform throwHand;
        [SerializeField] private float spawnForward = 0.6f;

        [Header("투척 궤도 (조준한 바닥에 떨어진다)")]
        [Tooltip("최대 사거리(m, 수평). 하늘이나 이보다 먼 곳을 조준하면 이 거리의 바닥에 떨어진다")]
        [SerializeField] private float maxThrowRange = 20f;
        [Tooltip("최소 사거리(m, 수평). 발밑 바닥을 조준해도 이만큼은 앞으로 던진다")]
        [SerializeField] private float minThrowRange = 2f;
        [Tooltip("가장 짧은 비행 시간(초). 가까운 곳에 던질 때의 궤도 높이를 정한다")]
        [SerializeField] private float minFlightTime = 0.5f;
        [Tooltip("가장 긴 비행 시간(초). 먼 곳에 던질 때의 궤도 높이를 정한다 (폭발 시간보다 길어지지 않게 자동으로 줄인다)")]
        [SerializeField] private float maxFlightTime = 1.4f;
        [Tooltip("착지 후 폭발까지 최소로 남길 시간(초). 공중 폭발 방지")]
        [SerializeField] private float fuseMargin = 0.25f;
        [Tooltip("바닥을 찾을 때 목표 지점 위 몇 m 에서 아래로 검사할지")]
        [SerializeField] private float groundProbeHeight = 10f;

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
        /// <summary>마지막 투척의 출발점 · 목표 착지점 · 초기 속도 (디버그 기즈모용)</summary>
        public Vector3 LastThrowFrom { get; private set; }
        public Vector3 LastThrowTarget { get; private set; }
        public Vector3 LastThrowVelocity { get; private set; }
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
            Vector3 target = ResolveThrowTarget(aim);
            Vector3 velocity = SolveLaunchVelocity(spawn, target);
            LastThrowFrom = spawn;
            LastThrowTarget = target;
            LastThrowVelocity = velocity;

            Vector3 facing = velocity.sqrMagnitude > 0.0001f ? velocity : transform.forward;
            var grenade = Instantiate(grenadePrefab, spawn, Quaternion.LookRotation(facing));
            var context = new ProjectileContext(Faction.Player, gameObject, transform, grenadeDef != null ? grenadeDef.Id : 0, grenadeHitMask);
            grenade.Launch(GrenadeStats, context, grenadeDef, velocity, ownColliders);

            grenades--;
            PublishGrenades();
            GrenadeReleased?.Invoke();
        }

        /// <summary>회복 시작. 팩이 없거나, 이미 회복 중이거나, 체력이 가득이면 false (팩을 쓰지 않음). 실패하면 이유를 로그로 남긴다</summary>
        public bool TryUseHeal()
        {
            string fail = HealFailReason();
            if (fail != null)
            {
                Debug.Log($"{name}: 회복 팩 사용 실패 - {fail}", this);
                return false;
            }

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
        private bool CanAct => player.IsAlive && player.IsReviving == false && player.IsRolling == false && player.IsExternallyControlled == false && player.IsOnGround;

        /// <summary>지금 회복 팩을 쓸 수 없는 이유. 쓸 수 있으면 null (검사 순서 = CanAct 를 풀어 쓴 것)</summary>
        private string HealFailReason()
        {
            if (healing) return $"이미 회복 중 (남은 {HealRemainingTime:0.0}초)";
            if (throwing) return "수류탄을 던지는 중";
            if (healPacks <= 0) return "회복 팩이 없음";
            if (player.IsAlive == false) return "죽은 상태";
            if (player.IsReviving) return "부활 중";
            if (player.IsRolling) return "구르는 중";
            if (player.IsExternallyControlled) return "외부 제어 중 (컷신 등)";
            if (player.IsOnGround == false) return "공중에 떠 있음";
            if (player.CurrentHealth >= player.MaxHealth) return $"체력이 가득 참 ({player.CurrentHealth}/{player.MaxHealth})";
            return null;
        }

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

        /// <summary>
        /// 수류탄이 떨어질 지점.
        ///  - 조준선이 최대 사거리(수평) 안에서 맞은 곳이 있으면 그곳 (벽·몬스터도 그대로)
        ///  - 하늘이거나 더 멀면 → 조준 방향으로 최대 사거리 지점의 바닥
        ///  - 발밑 바닥이면 → 최소 사거리 지점의 바닥
        /// </summary>
        private Vector3 ResolveThrowTarget(Ray aim)
        {
            Vector3 origin = transform.position;
            Vector3 flatForward = Vector3.ProjectOnPlane(aim.direction, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            flatForward.Normalize();

            // 내려다볼수록 조준선이 길어지므로 수평 사거리보다 넉넉히 검사한다
            float probeRange = maxThrowRange * 2f + Vector3.Distance(aim.origin, origin);
            if (RaycastIgnoringSelf(aim.origin, aim.direction, probeRange, out var hit))
            {
                float flat = Vector3.ProjectOnPlane(hit.point - origin, Vector3.up).magnitude;
                if (flat <= maxThrowRange)
                {
                    bool isGround = Vector3.Dot(hit.normal, Vector3.up) > 0.5f;
                    if (flat >= minThrowRange || isGround == false) return hit.point;
                    return GroundAt(origin + flatForward * minThrowRange, origin.y);
                }
            }
            return GroundAt(origin + flatForward * maxThrowRange, origin.y);
        }

        /// <summary>point 위에서 아래로 바닥을 찾는다. 없으면 fallbackY 높이</summary>
        private Vector3 GroundAt(Vector3 point, float fallbackY)
        {
            Vector3 from = new Vector3(point.x, Mathf.Max(point.y, fallbackY) + groundProbeHeight, point.z);
            if (RaycastIgnoringSelf(from, Vector3.down, groundProbeHeight * 3f, out var hit)) return hit.point;
            return new Vector3(point.x, fallbackY, point.z);
        }

        /// <summary>가장 가까운, 내 몸이 아닌 충돌 지점</summary>
        private bool RaycastIgnoringSelf(Vector3 from, Vector3 direction, float range, out RaycastHit best)
        {
            best = default;
            bool found = false;
            var hits = Physics.RaycastAll(from, direction, range, grenadeHitMask, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (found && hit.distance >= best.distance) continue;
                best = hit;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// from 에서 던져 비행 시간 t 뒤에 정확히 to 에 닿는 초기 속도.
        /// t 는 수평 거리 / throwSpeed 를 [minFlightTime, maxFlightTime] 로 묶고, 폭발 시간보다 fuseMargin 만큼 짧게 한다
        /// (가까우면 낮고 빠르게, 멀면 높은 포물선)
        /// </summary>
        private Vector3 SolveLaunchVelocity(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float flat = new Vector2(delta.x, delta.z).magnitude;

            float longest = maxFlightTime;
            if (GrenadeStats.fuseTime > 0f) longest = Mathf.Min(longest, GrenadeStats.fuseTime - fuseMargin);
            longest = Mathf.Max(longest, 0.1f);
            float shortest = Mathf.Min(minFlightTime, longest);

            float t = Mathf.Clamp(flat / Mathf.Max(0.1f, GrenadeStats.throwSpeed), shortest, longest);
            return delta / t - 0.5f * t * Physics.gravity;
        }

        private void OnDrawGizmosSelected()
        {
            if (LastThrowVelocity == Vector3.zero) return;
            Gizmos.color = Color.yellow;
            Vector3 prev = LastThrowFrom;
            for (float t = 0.05f; t <= 3f; t += 0.05f)
            {
                Vector3 next = LastThrowFrom + LastThrowVelocity * t + 0.5f * t * t * Physics.gravity;
                Gizmos.DrawLine(prev, next);
                prev = next;
                if (next.y < LastThrowTarget.y - 1f) break;
            }
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(LastThrowTarget, 0.3f);
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
            throwing = false;
            StopHeal();
            if (evt.FullReset == false) return;
            ResetCounts();
            RefreshStats();
        }

        private void PublishGrenades() => EventBus<GrenadeCountChangedEvent>.Publish(new GrenadeCountChangedEvent(grenades, MaxGrenades));
        private void PublishHealPacks() => EventBus<HealPackCountChangedEvent>.Publish(new HealPackCountChangedEvent(healPacks, maxHealPacks));
        #endregion
    }
}
