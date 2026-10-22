namespace HybridShooter.Monster.Movement
{
    /// <summary>실행기(IMoveExecutor)가 보고하는 이동 진행 상태. MonsterMovement 가 도달 불가 슬롯 거절에 사용</summary>
    public enum MoveExecutionState { Idle, Moving, Arrived, Pending, Unreachable, OffNavMesh }
}
