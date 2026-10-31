using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>추격형 도약의 높이·지면·장애물 조건. 비행 시간은 테이블에서 계산한 Active 전체 길이를 쓴다.</summary>
    public sealed class PounceSettings : MonsterAttackSettingsComponent
    {
        [SerializeField, Min(0f)] private float arcHeight = 1.5f;
        [SerializeField, Min(0f)] private float hitHeightTolerance = 0.75f;
        [Tooltip("목표 위에서 지면을 찾기 시작하는 높이 (m)")]
        [SerializeField, Min(0.01f)] private float groundProbeHeight = 2f;
        [Tooltip("목표 또는 공중 취소 위치 아래로 지면을 찾는 최대 깊이 (m)")]
        [SerializeField, Min(0.01f)] private float groundProbeDepth = 10f;
        [SerializeField, Min(0.01f)] private float navMeshTolerance = 0.5f;
        [Tooltip("Default 건물도 포함. 자기 자신과 플레이어는 소속 Transform 으로 제외")]
        [SerializeField] private LayerMask groundMask = 969;
        [SerializeField] private LayerMask obstacleMask = 969;

        public float ArcHeight => arcHeight;
        public float HitHeightTolerance => hitHeightTolerance;
        public float GroundProbeHeight => groundProbeHeight;
        public float GroundProbeDepth => groundProbeDepth;
        public float NavMeshTolerance => navMeshTolerance;
        public int GroundMask => groundMask;
        public int ObstacleMask => obstacleMask;
    }
}
