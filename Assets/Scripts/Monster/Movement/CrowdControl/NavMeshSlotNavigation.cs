using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Monster.Movement
{
    /// <summary>후보 투영과 요청 개체의 완전 경로 확인.</summary>
    public sealed class NavMeshSlotNavigation : ISlotNavigation
    {
        private readonly SlotSettings settings;
        private readonly NavMeshPath path;

        public NavMeshSlotNavigation(SlotSettings settings)
        {
            this.settings = settings;
            path = new NavMeshPath();
        }

        /// <summary>SampleRadius 안에서 가장 가까운 NavMesh 지점으로 후보를 투영</summary>
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

        /// <summary>NavMesh 경로가 끝까지 이어지면(PathComplete) true</summary>
        public bool CanReach(Vector3 origin, Vector3 destination)
        {
            var filter = new NavMeshQueryFilter { agentTypeID = settings.AgentTypeId, areaMask = settings.AreaMask };
            return NavMesh.CalculatePath(origin, destination, filter, path) &&
                   path.status == NavMeshPathStatus.PathComplete;
        }
    }
}
