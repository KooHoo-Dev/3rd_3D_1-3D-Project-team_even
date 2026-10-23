namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 유형별 "어디로 갈지" 판단. 개체마다 인스턴스 하나 (내부 상태 보유 가능).
    /// 새 몬스터 유형 = 이 인터페이스 구현 1개 + MovementPolicyKind 값 1개 + 팩토리 case 1줄.
    /// CrowdCoordinator 는 수정하지 않는다.
    /// </summary>
    public interface IMovementPolicy
    {
        /// <summary>현재 상황(ctx)을 보고 이동 의도를 반환. 목표가 무효면 Idle 정지를 반환해야 한다</summary>
        MoveIntent Evaluate(in MovementContext ctx);
    }

    /// <summary>"실제로 움직이기". 지상(NavMesh)과 공중이 다르기 때문에만 분리한다</summary>
    public enum MoveApplyResult
    {
        PathSet,      // 새 경로 설정
        PathKept,     // 목적지 변화가 작아 기존 경로 유지
        Deferred,     // 재경로 최소 간격 미달로 보류
        Unreachable,  // 도달 불가
        OffNavMesh,   // 몬스터가 NavMesh 위에 없음
        Stopped,      // 정지 의도라 멈춤
    }

    public interface IMoveExecutor
    {
        /// <summary>의도를 실제 이동으로 옮기고 결과를 반환</summary>
        MoveApplyResult Apply(in MoveIntent intent);
        /// <summary>즉시 정지하고 경로를 비운다</summary>
        void Stop();
        /// <summary>현재 속도 (m/s). 애니메이션 연결용</summary>
        UnityEngine.Vector3 Velocity { get; }
        /// <summary>실행기의 현재 진행 상태 (도착·도달 불가 감지용)</summary>
        MoveExecutionState ExecutionState { get; }
    }
}
