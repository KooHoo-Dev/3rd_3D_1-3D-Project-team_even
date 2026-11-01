using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 원거리(독장판) 몬스터 프리팹에 붙이는 설정. 투사체·장판·표식 프리팹과 Inspector 값(장판 틱 간격·지속·루프 소리 포함)을 갖고, 각각의 풀을 만든다.
    /// 장판 반경·틱 피해·포탄 속도는 MonsterAttackTable(Radius·Damage·ProjectileSpeed)에서 AttackSpec 으로 온다.
    /// MonsterController.Start 가 찾아 MonsterAttackFactory 로 넘기고, PoisonAreaAttack·투사체·장판이 읽는다.
    /// </summary>
    /// <remarks>풀은 Awake 에서 만든다. MonsterController.Start 보다 먼저 준비된다.</remarks>
    public sealed class PoisonAreaSettings : MonsterAttackSettingsComponent
    {
        [Header("프리팹")]
        [Tooltip("독 포탄")]
        [SerializeField] private PoisonProjectile projectilePrefab;
        [Tooltip("독장판. MeshFilter 필요. 코드가 지름 1m 원판 메시를 Terrain 에 맞춰 만들고 XZ 크기를 맞춤")]
        [SerializeField] private PoisonZone zonePrefab;
        [Tooltip("떨어질 자리 예고 표식. MeshFilter 필요. 코드가 지름 1m 원판 메시를 Terrain 에 맞춰 만들고 XZ 크기를 맞춤")]
        [SerializeField] private Transform markerPrefab;

        [Header("발사")]
        [Tooltip("몸 위치 수평 발사 너비 (m)")]
        [SerializeField, Min(0f)] private float launchWidth = 1f;
        [Tooltip("몸 위치 위 발사 높이 (m)")]
        [SerializeField, Min(0f)] private float launchHeight = 1f;
        [Tooltip("포물선 최고 높이. 발사점·착탄점을 잇는 직선 위로 올라가는 높이 (m)")]
        [SerializeField, Min(0f)] private float arcHeight = 4f;

        [Header("독장판 피해 (틱 1회 피해량은 MonsterAttackTable.Damage)")]
        [Tooltip("틱 간격 (s). 첫 틱은 장판이 깔리는 순간")]
        [SerializeField, Min(0.01f)] private float tickInterval = 1f;
        [Tooltip("장판 지속 시간 (s). 끝나면 반납")]
        [SerializeField, Min(0.01f)] private float zoneDuration = 5f;

        [Header("독장판 피해 조건")]
        [Tooltip("플레이어 Y 와 플레이어 XZ 의 Terrain 높이 차이 허용값 ± (m). 바위·다리 위면 이 값을 넘어 피해가 없다")]
        [SerializeField, Min(0f)] private float zoneHeightTolerance = 0.5f;

        [Header("독장판 표시")]
        [Tooltip("장판·표식 원판을 Terrain 표면 위로 띄우는 높이 (m). 너무 작으면 지형과 겹쳐 깜빡인다")]
        [SerializeField, Min(0f)] private float surfaceLift = 0.05f;
        [Tooltip("장판이 깔려 있는 동안 반복 재생할 소리 (SoundTable Key). 비우면 소리 없음")]
        [SerializeField] private string zoneLoopSfxKey = "mon_poison_zone_loop";

        public float LaunchWidth => launchWidth;
        public float LaunchHeight => launchHeight;
        public float ArcHeight => arcHeight;
        public float TickInterval => tickInterval;
        public float ZoneDuration => zoneDuration;
        public float ZoneHeightTolerance => zoneHeightTolerance;
        public float SurfaceLift => surfaceLift;
        public string ZoneLoopSfxKey => zoneLoopSfxKey;

        public SimplePool<PoisonProjectile> ProjectilePool { get; private set; }
        public SimplePool<PoisonZone> ZonePool { get; private set; }
        public SimplePool<Transform> MarkerPool { get; private set; }

        /// <summary>프리팹마다 풀을 1개씩 만든다</summary>
        private void Awake()
        {
            ProjectilePool = new SimplePool<PoisonProjectile>(projectilePrefab);
            ZonePool = new SimplePool<PoisonZone>(zonePrefab);
            MarkerPool = new SimplePool<Transform>(markerPrefab);
        }
    }
}
