using System.Collections.Generic;
using HybridShooter.Core.Battlefield;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Core.Services
{
    /// <summary>
    /// 플레이 시작 시 기본 구현(Fallback)을 등록. 실제 구현이 Register 되면 그쪽이 우선.
    /// (SubsystemRegistration 에서 ServiceLocator 가 초기화된 "뒤"인 BeforeSceneLoad 에 실행)
    /// </summary>
    internal static class CoreBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterFallbacks()
        {
            ServiceLocator.SetFallback<ISoundService>(new NullSoundService());
            ServiceLocator.SetFallback<IMonsterFactory>(new ResourcesMonsterFactory());
            ServiceLocator.SetFallback<ISpawnZoneProvider>(new RingSpawnZoneProvider());
            ServiceLocator.SetFallback<ICurrencyWallet>(new CurrencyWallet());
            ServiceLocator.SetFallback<IUpgradeState>(new UpgradeLedger());

            var weaponUnlocks = new WeaponUnlockLedger();
            weaponUnlocks.BindEvents();
            ServiceLocator.SetFallback<IWeaponUnlocks>(weaponUnlocks);
        }
    }

    /// <summary>사운드 담당 구현 전까지 쓰는 빈 구현</summary>
    public sealed class NullSoundService : ISoundService
    {
        public void PlaySfx(string key, Vector3? position = null) { }
        public void PlaySfx(SoundDef sound, Vector3? position = null) { }
        public SoundHandle PlayLoop(SoundDef sound, Transform follow) => default;
        public SoundHandle PlayLoop(string key, Transform follow) => default;
        public void StopLoop(SoundHandle handle) { }
        public void PlayBgm(string key) { }
        public void StopBgm() { }
        public float MasterVolume { get => 1f; set { } }
        public float GetVolume(SoundCategory category) => 1f;
        public void SetVolume(SoundCategory category, float volume) { }
        public void Duck(SoundCategory category, float level, float holdTime) { }
    }

    /// <summary>몬스터 담당 풀링 구현 전까지 쓰는 기본 팩토리. def.Prefab 을 Instantiate</summary>
    public sealed class ResourcesMonsterFactory : IMonsterFactory
    {
        public GameObject Spawn(MonsterDef def, Vector3 position, Quaternion rotation)
        {
            var go = Object.Instantiate(def.Prefab, position, rotation);
            EventBus<MonsterSpawnedEvent>.Publish(new MonsterSpawnedEvent(def, go));
            return go;
        }

        public void Despawn(GameObject monster)
        {
            if (monster != null) Object.Destroy(monster);
        }
    }

    /// <summary>
    /// 전장 담당 구현 전까지 쓰는 기본 생성 위치: 플레이어 주변 원형 링.
    /// Config.SpawnRingRadius 거리에서 생성하므로 "시작 직후 플레이어 주변은 비우기"(BL-015)도 기본 충족.
    /// 지상 패턴은 NavMesh 위로 옮기고, 옮긴 점이 Config.SpawnSafeRadius 안이면 false 를 돌려준다 (B3).
    /// 씬에 SpawnPointFinder 가 켜져 있으면 링 포인트를 받아들이기 전에 그 맵 경계·장애물·NavMesh 판정을 통과해야 한다.
    /// 없으면 (테스트·실험 씬) 경계 판정 없이 NavMesh 위로만 옮긴다.
    /// </summary>
    public sealed class RingSpawnZoneProvider : ISpawnZoneProvider
    {
        private const float AirHeight = 8f;
        private const float NavMeshSampleDistance = 5f;  // m, SpawnPointFinder 없는 씬에서 링 위 점에서 NavMesh 를 찾는 반경
        private const float NestRadiusWithoutFinder = 6f;  // m, SpawnPointFinder 없는 씬의 둥지 반경

        /// <summary>
        /// MonsterSpawner 가 1마리 소환마다 부른다 (Nest 제외). 패턴별 링 포인트 → 경계·지면 판정 → 안전 반경 검사 → Air 면 띄움.
        /// </summary>
        /// <param name="pattern">소환 방향 규칙</param>
        /// <param name="groupSeed">Herd 방향을 정하는 값. 같은 값이면 같은 방향</param>
        /// <param name="position">소환 위치</param>
        /// <param name="rotation">플레이어를 바라보는 회전</param>
        /// <returns>false = 이번 점이 경계 밖·장애물·안전 반경 안이라 소환 불가. 다시 부르면 다른 점을 뽑는다</returns>
        public bool TryGetSpawnPose(SpawnPattern pattern, int groupSeed, out Vector3 position, out Quaternion rotation)
        {
            GetPlayer(out var playerPosition, out var forward);
            Vector3 ringPoint = PickRingPoint(pattern, groupSeed, playerPosition, forward);
            bool isAir = pattern == SpawnPattern.Air;
            return TryMakeSpawnPose(ringPoint, isAir, playerPosition, out position, out rotation);
        }

        /// <summary>
        /// Nest 묶음의 둥지 중심을 뽑는다. 링 위 아무 방향 한 점을 지상 소환점과 같은 판정으로 확정한다.
        /// SpawnNest 가 아직 중심이 없을 때 부르고, 성공하면 그 중심을 묶음 끝까지 쓴다.
        /// </summary>
        /// <param name="center">둥지 중심 (NavMesh 위)</param>
        /// <returns>false = 이번 점은 못 씀. 다시 부르면 다른 방향을 뽑는다</returns>
        public bool TryGetNestCenter(out Vector3 center)
        {
            GetPlayer(out var playerPosition, out _);
            float ringRadius = DataManager.Config.GetFloat(GameConfig.SpawnRingRadius, 35f);
            Vector3 ringPoint = PointOnCircle(playerPosition, Random.Range(0f, 360f), ringRadius);
            return TryMakeSpawnPose(ringPoint, false, playerPosition, out center, out _);
        }

        /// <summary>
        /// 둥지 중심 주위 원 안에서 면적 균등하게 한 점을 뽑아 지상 소환 위치로 확정한다. SpawnNest 가 1마리 소환마다 부른다.
        /// </summary>
        /// <param name="nestCenter">SpawnNest 가 저장한 둥지 중심</param>
        /// <param name="position">소환 위치</param>
        /// <param name="rotation">플레이어를 바라보는 회전</param>
        /// <returns>false = 이번 점은 못 씀. 다시 부르면 다른 점을 뽑는다</returns>
        public bool TryGetSpawnPoseAroundNest(Vector3 nestCenter, out Vector3 position, out Quaternion rotation)
        {
            GetPlayer(out var playerPosition, out _);
            Vector3 point = PickPointInCircle(nestCenter, GetNestRadius());
            return TryMakeSpawnPose(point, false, playerPosition, out position, out rotation);
        }

        /// <summary>
        /// 패턴별 방향 규칙으로 플레이어 주변 링 위 한 점을 뽑는다. 판정은 하지 않는다.
        /// </summary>
        /// <param name="pattern">소환 방향 규칙 (Surround·Air = 아무 방향, Flank = 측면~후방, Herd = groupSeed 방향)</param>
        /// <param name="groupSeed">Herd 방향을 정하는 값</param>
        /// <param name="playerPosition">링 중심</param>
        /// <param name="forward">플레이어 시선 방향 (Flank 기준)</param>
        /// <returns>링 위 점. Y = 플레이어 높이</returns>
        private static Vector3 PickRingPoint(SpawnPattern pattern, int groupSeed, Vector3 playerPosition, Vector3 forward)
        {
            float radius = DataManager.Config.GetFloat(GameConfig.SpawnRingRadius, 35f);
            float baseAngle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float angle;

            switch (pattern)
            {
                case SpawnPattern.Flank:   // 시야 밖: 측면~후방
                    angle = baseAngle + (Random.value < 0.5f ? 1f : -1f) * Random.Range(100f, 170f);
                    break;
                case SpawnPattern.Herd:    // 같은 groupSeed 는 한 방향에 밀집
                    angle = new System.Random(groupSeed).Next(0, 360) + Random.Range(-8f, 8f);
                    radius += Random.Range(0f, 5f);
                    break;
                default:                   // Surround, Air
                    angle = Random.Range(0f, 360f);
                    break;
            }

            return PointOnCircle(playerPosition, angle, radius);
        }

        /// <summary>
        /// 후보 점을 소환 위치로 확정한다: 지상이면 NavMesh·경계·장애물 판정, 공중이면 경계 판정 → 안전 반경 검사 → 공중이면 띄움 → 플레이어를 바라보는 회전.
        /// </summary>
        /// <param name="point">링·둥지에서 뽑은 후보 점</param>
        /// <param name="isAir">공중 소환이면 true (NavMesh·장애물 판정 없이 AirHeight 만큼 띄움)</param>
        /// <param name="playerPosition">안전 반경·바라볼 방향 기준</param>
        /// <param name="position">소환 위치</param>
        /// <param name="rotation">플레이어를 바라보는 회전. 실패 시 identity</param>
        /// <returns>모든 판정을 통과하면 true</returns>
        private static bool TryMakeSpawnPose(Vector3 point, bool isAir, Vector3 playerPosition, out Vector3 position, out Quaternion rotation)
        {
            position = point;
            rotation = Quaternion.identity;

            bool isPlaced;
            if (isAir) isPlaced = IsInsideMapWhenKnown(point);
            else isPlaced = TryPlaceOnGround(point, out position);
            if (isPlaced == false) return false;
            if (IsInsideSafeRadius(position, playerPosition)) return false;

            if (isAir) position.y += AirHeight;
            rotation = FacePlayer(position, playerPosition);
            return true;
        }

        /// <summary>
        /// 지상 후보 점을 설 수 있는 점으로 옮긴다. 씬에 SpawnPointFinder 가 있으면 그 판정(NavMesh·맵 경계·장애물)을 따르고, 없으면 NavMesh 위로만 옮긴다.
        /// </summary>
        /// <param name="point">후보 점</param>
        /// <param name="groundPoint">NavMesh 위로 옮긴 점</param>
        /// <returns>SpawnPointFinder 판정 실패면 false. Finder 가 없으면 항상 true</returns>
        private static bool TryPlaceOnGround(Vector3 point, out Vector3 groundPoint)
        {
            if (ServiceLocator.TryGet(out SpawnPointFinder finder)) return finder.TryGetGroundSpawnPoint(point, out groundPoint);

            groundPoint = SnapToNavMesh(point);
            return true;
        }

        /// <summary>
        /// 공중 후보 점이 맵 경계 안인지 본다. 씬에 SpawnPointFinder 가 없으면 경계를 모르므로 true.
        /// </summary>
        /// <param name="point">후보 점 (Y는 무시)</param>
        /// <returns>경계 안이거나 경계를 모르면 true</returns>
        private static bool IsInsideMapWhenKnown(Vector3 point)
        {
            if (ServiceLocator.TryGet(out SpawnPointFinder finder)) return finder.IsInsideMap(point);
            return true;
        }

        /// <summary>둥지 반경. 씬 SpawnPointFinder Inspector 값, 없으면 NestRadiusWithoutFinder</summary>
        /// <returns>반경 (m)</returns>
        private static float GetNestRadius()
        {
            if (ServiceLocator.TryGet(out SpawnPointFinder finder)) return finder.NestRadius;
            return NestRadiusWithoutFinder;
        }

        /// <summary>
        /// 원 안에서 면적 기준 균등하게 한 점을 뽑는다 (반지름을 그대로 Random 하면 안쪽에 몰려서 √ 를 씌운다).
        /// 둥지 주위 소환점을 뽑을 때 쓴다.
        /// </summary>
        /// <param name="center">원 중심</param>
        /// <param name="radius">원 반지름 (m)</param>
        /// <returns>원 안의 점. Y = center.y</returns>
        private static Vector3 PickPointInCircle(Vector3 center, float radius)
        {
            float distance = radius * Mathf.Sqrt(Random.value);
            return PointOnCircle(center, Random.Range(0f, 360f), distance);
        }

        /// <summary>center 에서 수평으로 angle 방향(0 = +Z, 시계 방향)으로 distance 만큼 떨어진 점</summary>
        /// <param name="center">기준점</param>
        /// <param name="angle">방향 (도)</param>
        /// <param name="distance">거리 (m)</param>
        /// <returns>Y = center.y 인 점</returns>
        private static Vector3 PointOnCircle(Vector3 center, float angle, float distance)
        {
            float radians = angle * Mathf.Deg2Rad;
            return center + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * distance;
        }

        /// <summary>소환 위치에서 플레이어를 수평으로 바라보는 회전</summary>
        /// <param name="position">소환 위치</param>
        /// <param name="playerPosition">플레이어 위치</param>
        /// <returns>회전. 두 점이 수평으로 겹치면 identity</returns>
        private static Quaternion FacePlayer(Vector3 position, Vector3 playerPosition)
        {
            Vector3 look = playerPosition - position;
            look.y = 0f;
            if (look.sqrMagnitude <= 0.001f) return Quaternion.identity;
            return Quaternion.LookRotation(look);
        }

        /// <summary>
        /// 지상 소환점을 가장 가까운 NavMesh 점으로 옮긴다. NavMeshAgent 가 NavMesh 밖에서 켜지면 움직이지 못하기 때문이다.
        /// SpawnPointFinder 가 없는 씬에서만 쓴다.
        /// </summary>
        /// <param name="point">링 위 소환점</param>
        /// <returns>반경 NavMeshSampleDistance 안의 NavMesh 점. 못 찾으면 원래 점 (NavMesh 없는 테스트 씬)</returns>
        private static Vector3 SnapToNavMesh(Vector3 point)
        {
            bool isOnNavMesh = NavMesh.SamplePosition(point, out NavMeshHit hit, NavMeshSampleDistance, NavMesh.AllAreas);
            if (isOnNavMesh == false) return point;
            return hit.position;
        }

        /// <summary>
        /// BL-015: 소환점이 플레이어 주변 생성 금지 반경(Config.SpawnSafeRadius) 안인지 본다. 높이 차이는 무시한다.
        /// </summary>
        /// <param name="point">소환점</param>
        /// <param name="playerPosition">플레이어 위치</param>
        /// <returns>수평(XZ) 거리가 반경보다 작으면 true</returns>
        private static bool IsInsideSafeRadius(Vector3 point, Vector3 playerPosition)
        {
            float safeRadius = DataManager.Config.GetFloat(GameConfig.SpawnSafeRadius, 15f);
            float horizontalDistance = Vector2.Distance(new Vector2(point.x, point.z), new Vector2(playerPosition.x, playerPosition.z));
            return horizontalDistance < safeRadius;
        }

        /// <summary>플레이어 기준 XZ 거리 SupplyDropMinDistance ~ SupplyDropMaxDistance 링 안. y 는 플레이어 높이 (지면 탐색은 보급 쪽에서)</summary>
        public bool TryGetSupplyDropPosition(out Vector3 position)
        {
            GetPlayer(out var center, out _);
            float min = DataManager.Config.GetFloat(GameConfig.SupplyDropMinDistance, 20f);
            float max = Mathf.Max(min, DataManager.Config.GetFloat(GameConfig.SupplyDropMaxDistance, 60f));
            // 면적 기준 균등 분포 (반지름을 그대로 Random 하면 안쪽에 몰린다)
            float distance = Mathf.Sqrt(Random.Range(min * min, max * max));
            float rad = Random.Range(0f, Mathf.PI * 2f);
            position = center + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * distance;
            return true;
        }

        private static void GetPlayer(out Vector3 position, out Vector3 forward)
        {
            if (ServiceLocator.TryGet<IPlayerTarget>(out var player) && player.Root != null)
            {
                position = player.Root.position;
                forward = player.ViewPoint != null ? player.ViewPoint.forward : player.Root.forward;
            }
            else
            {
                position = Vector3.zero;
                forward = Vector3.forward;
            }
        }
    }

    /// <summary>재화 지갑 기본 구현 (BL-029). 값이 바뀌면 CurrencyChangedEvent 발행</summary>
    public sealed class CurrencyWallet : ICurrencyWallet
    {
        public int Balance { get; private set; }

        public void Add(int amount)
        {
            if (amount <= 0) return;
            Balance += amount;
            EventBus<CurrencyChangedEvent>.Publish(new CurrencyChangedEvent(Balance, amount));
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || Balance < amount) return false;
            Balance -= amount;
            EventBus<CurrencyChangedEvent>.Publish(new CurrencyChangedEvent(Balance, -amount));
            return true;
        }

        public void ResetTo(int amount)
        {
            int delta = amount - Balance;
            Balance = amount;
            EventBus<CurrencyChangedEvent>.Publish(new CurrencyChangedEvent(Balance, delta));
        }
    }

    /// <summary>강화 상태 기본 구현 (BL-030, BL-031). 레벨만 기억하고 수치는 UpgradeTrackDef 에서 계산</summary>
    public sealed class UpgradeLedger : IUpgradeState
    {
        private readonly Dictionary<UpgradeType, int> levels = new Dictionary<UpgradeType, int>();

        public int GetLevel(UpgradeType type) => levels.TryGetValue(type, out var lv) ? lv : 0;

        public float GetTotalValue(UpgradeType type)
            => DataManager.UpgradeTracks.TryGet(type, out var track) ? track.GetTotalValue(GetLevel(type)) : 0f;

        public UpgradeDef GetNext(UpgradeType type)
            => DataManager.UpgradeTracks.TryGet(type, out var track) ? track.GetNext(GetLevel(type)) : null;

        public bool TryPurchase(UpgradeType type, ICurrencyWallet wallet)
        {
            var next = GetNext(type);
            if (next == null || wallet == null || wallet.TrySpend(next.Cost) == false) return false;
            levels[type] = next.Level;
            EventBus<UpgradePurchasedEvent>.Publish(new UpgradePurchasedEvent(next));
            return true;
        }

        public void ResetAll() => levels.Clear();
    }
}
