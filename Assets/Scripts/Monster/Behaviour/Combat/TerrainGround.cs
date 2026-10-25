using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 씬의 Terrain 지면 높이를 구한다. 원거리 공격의 목표점과 독장판 피해 조건이 같은 방법을 쓰게 한다.
    /// 씬에 Terrain 이 1개라고 가정한다 (Terrain.activeTerrain).
    /// </summary>
    public static class TerrainGround
    {
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
