using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 추격형 도약의 높이·지면·장애물 조건. 비행 시간은 테이블에서 계산한 Active 전체 길이를 쓴다.
    /// 측면 침투형의 원거리 시야 밖 접근 속도·이동 클립 배율도 여기서 조절한다 (MonsterController 가 읽는다).
    /// </summary>
    public sealed class PounceSettings : MonsterAttackSettingsComponent
    {
        [Header("원거리 시야 밖 접근")]
        [Tooltip("원거리 시야 밖 접근 중 테이블 이동 속도에 곱하는 배율. 소환(조립) 때 1번 읽는다")]
        [SerializeField, Min(0.01f)] private float farApproachSpeedMultiplier = 1.2f;
        [Tooltip("원거리 시야 밖 접근 중 이동 클립 재생 배율(실제 속도 ÷ moveClipSpeed)에 곱하는 값. 1 = 변화 없음. 매 프레임 읽는다")]
        [SerializeField, Min(0.01f)] private float farApproachClipSpeedMultiplier = 1f;

        [Header("도약")]
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
        public float FarApproachSpeedMultiplier => farApproachSpeedMultiplier;
        public float FarApproachClipSpeedMultiplier => farApproachClipSpeedMultiplier;
    }
}
