using UnityEngine;

namespace HybridShooter.Core.Battlefield.Tester
{
    /// <summary>
    /// SpawnPointFinder 를 씬에서 눈으로 확인하는 테스트 컴포넌트. 에디터(플레이 안 해도 됨)에서 쓴다.
    ///  - Inspector 우클릭(⋮) → "스폰 위치 찾기": TryFindSpawnPoint 를 한 번 호출하고 결과를 콘솔에 출력
    ///  - autoFindInEditor 를 켜면 플레이어·구역·몬스터를 옮길 때마다 다시 찾는다
    ///  - Scene 뷰 기즈모: 노란 원 = 구역, 파란 선 = 플레이어 위치, 초록 상자 = 찾은 스폰 위치(몬스터 사각형), 빨간 원 = 실패
    /// </summary>
    [ExecuteAlways]
    public sealed class SpawnPointFinderTester : MonoBehaviour
    {
        [Header("테스트 대상")]
        [Tooltip("검사할 SpawnPointFinder (Terrain·허용치는 이쪽 Inspector 에서 조절)")]
        [SerializeField] private SpawnPointFinder finder;
        [Tooltip("플레이어 위치로 쓸 Transform")]
        [SerializeField] private Transform player;
        [Tooltip("원형 구역 중심으로 쓸 Transform")]
        [SerializeField] private Transform zoneCenter;
        [Tooltip("원형 구역 반지름 (m)")]
        [SerializeField] private float zoneRadius = 15f;
        [Tooltip("스폰할 몬스터의 Collider. 씬에 있는 활성 오브젝트여야 크기가 나온다")]
        [SerializeField] private Collider monsterCollider;

        [Header("표시")]
        [Tooltip("켜면 값이나 위치가 바뀔 때마다 자동으로 다시 찾는다")]
        [SerializeField] private bool autoFindInEditor = true;
        [Tooltip("켜면 찾은 위치로 몬스터 오브젝트를 옮긴다 (발밑 = 지형 표면 가정)")]
        [SerializeField] private bool moveMonsterToResult;

        // ── 마지막 결과 ──
        private bool hasResult;
        private bool lastFound;
        private Vector3 lastSpawnPoint;

        private void Update()
        {
            if (autoFindInEditor == false) return;
            if (Application.isPlaying) return;
            if (HasAllReferences() == false) return;

            FindOnce(false);
        }

        // 자동 갱신은 Inspector 연결 도중에도 매 프레임 돌기 때문에, 연결이 끝나기 전에는 건너뛴다
        private bool HasAllReferences()
        {
            bool hasFinder = finder != null;
            bool hasPlayer = player != null;
            bool hasZoneCenter = zoneCenter != null;
            bool hasMonsterCollider = monsterCollider != null;
            return hasFinder && hasPlayer && hasZoneCenter && hasMonsterCollider;
        }

        [ContextMenu("스폰 위치 찾기")]
        private void FindFromMenu()
        {
            FindOnce(true);
        }

        /// <summary>TryFindSpawnPoint 를 한 번 호출해 결과를 기억하고, 필요하면 로그·몬스터 이동을 한다</summary>
        /// <param name="writeLog">true 면 결과를 콘솔에 출력</param>
        private void FindOnce(bool writeLog)
        {
            lastFound = finder.TryFindSpawnPoint(player.position, zoneCenter.position, zoneRadius, monsterCollider, out lastSpawnPoint);
            hasResult = true;

            if (writeLog) WriteResultLog();
            if (moveMonsterToResult && lastFound) monsterCollider.transform.position = lastSpawnPoint;
        }

        private void WriteResultLog()
        {
            if (lastFound == false)
            {
                Debug.LogWarning($"[SpawnPointFinderTester] 적합한 스폰 위치 없음 (구역 반지름 {zoneRadius}m, 몬스터 크기 {monsterCollider.bounds.size})");
                return;
            }

            Vector3 offset = lastSpawnPoint - player.position;
            offset.y = 0f;
            Debug.Log($"[SpawnPointFinderTester] 스폰 위치 {lastSpawnPoint}, 플레이어와 XZ 거리 {offset.magnitude:0.0}m, 몬스터 크기 {monsterCollider.bounds.size}");
        }

        private void OnDrawGizmos()
        {
            if (zoneCenter != null) DrawZone();
            if (player != null && zoneCenter != null) DrawPlayerLine();
            if (hasResult && monsterCollider != null) DrawResult();
        }

        private void DrawZone()
        {
            Gizmos.color = Color.yellow;
            DrawCircle(zoneCenter.position, zoneRadius);
        }

        private void DrawPlayerLine()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(zoneCenter.position, player.position);
            Gizmos.DrawWireSphere(player.position, 0.5f);
        }

        private void DrawResult()
        {
            if (lastFound == false)
            {
                Gizmos.color = Color.red;
                DrawCircle(zoneCenter.position, zoneRadius * 0.9f);
                return;
            }

            Vector3 size = monsterCollider.bounds.size;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(lastSpawnPoint + Vector3.up * size.y * 0.5f, size);
            Gizmos.DrawLine(player.position, lastSpawnPoint);
        }

        private static void DrawCircle(Vector3 center, float radius)
        {
            const int segmentCount = 64;
            Vector3 previousPoint = center + new Vector3(radius, 0f, 0f);
            for (int index = 1; index <= segmentCount; index++)
            {
                float angle = index * Mathf.PI * 2f / segmentCount;
                Vector3 point = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previousPoint, point);
                previousPoint = point;
            }
        }
    }
}
