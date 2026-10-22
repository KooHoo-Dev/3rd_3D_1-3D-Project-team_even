namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 유형별 "어디로 갈지" 판단. 개체마다 인스턴스 하나 (내부 상태 보유 가능).
    /// 새 몬스터 유형 = 이 인터페이스 구현 1개 + MovementPolicyKind 값 1개 + 팩토리 case 1줄.
    /// CrowdCoordinator 는 수정하지 않는다.
    /// </summary>
    public interface IMovementPolicy
    {
        MoveIntent Evaluate(in MovementContext ctx);
    }

    /// <summary>"실제로 움직이기". 지상(NavMesh)과 공중이 다르기 때문에만 분리한다</summary>
    public enum MoveApplyResult { PathSet, PathKept, Deferred, Unreachable, OffNavMesh, Stopped }

    public interface IMoveExecutor
    {
        MoveApplyResult Apply(in MoveIntent intent);
        void Stop();
        /// <summary>현재 속도 (m/s). 애니메이션 연결용</summary>
        UnityEngine.Vector3 Velocity { get; }
        MoveExecutionState ExecutionState { get; }
    }
}
