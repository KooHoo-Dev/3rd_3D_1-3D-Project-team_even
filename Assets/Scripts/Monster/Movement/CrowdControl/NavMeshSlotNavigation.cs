using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 슬롯 기하 후보의 실제 이동 가능성 판단: 후보 투영과 요청 개체의 완전 경로 확인.
    /// SlotRegistry 가 슬롯 위치 투영(TryProject)과 도달 가능성(CanReach)에 쓴다.
    /// </summary>
    public sealed class NavMeshSlotNavigation
    {
        private readonly SlotSettings settings;
        private readonly NavMeshPath path;   // CanReach 용 경로 버퍼 (매번 할당하지 않도록 재사용)

        /// <summary>CrowdController.EnsureSlots 가 SlotRegistry 를 만들 때 같은 SlotSettings 로 함께 만든다.</summary>
        /// <param name="settings">NavMesh 질의에 쓸 AgentTypeId·AreaMask·SampleRadius</param>
        public NavMeshSlotNavigation(SlotSettings settings)
        {
            this.settings = settings;
            path = new NavMeshPath();
        }

        /// <summary>
        /// SampleRadius 안에서 가장 가까운 NavMesh 지점으로 후보를 투영한다.
        /// SlotRegistry.RefreshRange 가 슬롯 위치를 다시 계산할 때 부른다.
        /// </summary>
        /// <param name="candidate">링 위에서 기하학적으로 계산한 슬롯 후보 위치</param>
        /// <param name="position">투영된 NavMesh 위 위치. 실패하면 candidate 그대로</param>
        /// <returns>true = 투영 성공(슬롯 Valid 후보), false = NavMesh 밖(OffNavMesh)</returns>
        public bool TryProject(Vector3 candidate, out Vector3 position)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = settings.AgentTypeId, areaMask = settings.AreaMask };
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, settings.SampleRadius, filter))
            {
                position = hit.position;
                return true;
            }
            position = candidate;
            return false;
        }

        /// <summary>
        /// 요청 개체가 슬롯까지 실제로 갈 수 있는지 판단한다.
        /// SlotRegistry.TryRequest·TryFindInRing 이 슬롯을 주거나 유지하기 전에 부른다.
        /// </summary>
        /// <param name="origin">요청 개체의 현재 위치</param>
        /// <param name="destination">슬롯의 투영된 위치</param>
        /// <returns>NavMesh 경로가 끝까지 이어지면(PathComplete) true</returns>
        public bool CanReach(Vector3 origin, Vector3 destination)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = settings.AgentTypeId, areaMask = settings.AreaMask };
            return NavMesh.CalculatePath(origin, destination, filter, path) &&
                   path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
