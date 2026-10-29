using System;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 씬의 Terrain 지면 높이를 구한다. 원거리 공격의 목표점과 독장판 피해 조건이 같은 방법을 쓰게 한다.
    /// 씬에 Terrain 이 1개라고 가정한다 (Terrain.activeTerrain).
    /// </summary>
    public static class TerrainGround
    {
        /// <summary>도약 목표 또는 취소 위치 아래의 첫 물리 지면을 찾는다. 자기 자신·플레이어·트리거는 제외한다.</summary>
        public static bool TryGroundBelow(Vector3 origin, float depth, int layerMask,
                                          Transform self, Transform player, out Vector3 ground)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, depth, layerMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            ground = default;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(self)) continue;
                if (player != null && hit.collider.transform.IsChildOf(player)) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                ground = hit.point;
            }
            return nearest < float.PositiveInfinity;
        }

        /// <summary>물리 지면 근처에서 같은 Agent 종류·영역의 NavMesh 좌표를 찾는다.</summary>
        public static bool TryNavMeshPoint(Vector3 point, float distance, NavMeshQueryFilter filter, out Vector3 ground)
        {
            bool found = NavMesh.SamplePosition(point, out NavMeshHit hit, distance, filter);
            ground = hit.position;
            return found;
        }

        /// <summary>착지/복구 목표점에 가장 가까운 유효 NavMesh 좌표. 조회 책임은 이 지형 클래스가 가진다.</summary>
        /// <exception cref="InvalidOperationException">씬에 해당 Agent 가 사용할 NavMesh 가 없을 때</exception>
        public static Vector3 ClosestNavMeshPoint(Vector3 destination, NavMeshQueryFilter filter)
        {
            // 임의의 반경으로 가까운 NavMesh 를 놓치지 않도록 실제 베이크 좌표 전체를 덮는 반경을 구한다.
            // 평상시 이동에는 사용하지 않고, 유효 지면에서 벗어난 착지/취소 복구에만 호출한다.
            Vector3[] vertices = NavMesh.CalculateTriangulation().vertices;
            float radius = 0f;
            foreach (Vector3 vertex in vertices) radius = Mathf.Max(radius, Vector3.Distance(destination, vertex));
            if (vertices.Length == 0 || TryNavMeshPoint(destination, radius + 1f, filter, out Vector3 ground) == false)
                throw new InvalidOperationException($"착지 목표 {destination} 주변에 사용 가능한 NavMesh 가 없다");
            return ground;
        }

        /// <summary>주어진 위치의 XZ 에서 Terrain 표면 높이를 구한다</summary>
        /// <param name="position">월드 위치. y 는 무시한다</param>
        /// <returns>Terrain 표면의 월드 y (m)</returns>
        public static float HeightAt(Vector3 position)
        {
            Terrain terrain = Terrain.activeTerrain;
            return terrain.SampleHeight(position) + terrain.transform.position.y;
        }

        /// <summary>주어진 위치 바로 아래(또는 위)의 Terrain 표면 점. PoisonAreaAttack 이 예고 시작에 목표점으로 쓴다</summary>
        /// <param name="position">월드 위치. y 는 무시한다</param>
        /// <returns>같은 XZ, y = Terrain 표면 높이인 점</returns>
        public static Vector3 PointUnder(Vector3 position) => new Vector3(position.x, HeightAt(position), position.z);
    }
}
