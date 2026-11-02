using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// Nest 묶음 하나의 둥지. 링 위 한 지점(둥지 중심)을 한 번 뽑아 저장하고, 묶음의 모든 몬스터를 그 주위 원 안에 소환하게 한다.
    /// MonsterWave 가 같은 StartDelay 의 Nest 스포너들에 하나를 나눠 주고, 각 MonsterSpawner 가 1마리 소환마다 TryGetSpawnPose 를 부른다.
    /// </summary>
    /// <remarks>
    /// 중심은 첫 소환 때 뽑는다 (웨이브 생성 시점의 플레이어 위치가 아니라 실제 소환 시점 기준).
    /// 중심을 못 뽑으면 이번 요청은 false 이고, 다음 요청에서 다시 뽑는다. 한 번 뽑은 중심은 묶음 끝까지 바꾸지 않는다.
    /// </remarks>
    public sealed class SpawnNest
    {
        private readonly ISpawnZoneProvider spawnZones;

        // ── 둥지 중심 ──
        private bool hasCenter;  // false = 아직 못 뽑음
        private Vector3 center;

        /// <summary>
        /// 아직 중심이 없는 둥지를 만든다.
        /// </summary>
        /// <param name="spawnZones">중심·주위 소환 위치 계산 (보통 ServiceLocator 의 RingSpawnZoneProvider)</param>
        public SpawnNest(ISpawnZoneProvider spawnZones)
        {
            this.spawnZones = spawnZones;
        }

        /// <summary>
        /// 둥지 주위 원 안의 소환 위치를 한 번 뽑는다. 중심이 없으면 먼저 중심을 뽑는다.
        /// </summary>
        /// <param name="position">소환 위치</param>
        /// <param name="rotation">플레이어를 바라보는 회전</param>
        /// <returns>false = 중심 또는 이번 점을 못 구함. 다시 부르면 된다</returns>
        public bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            if (hasCenter == false) hasCenter = spawnZones.TryGetNestCenter(out center);
            if (hasCenter == false)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            return spawnZones.TryGetSpawnPoseAroundNest(center, out position, out rotation);
        }
    }
}
