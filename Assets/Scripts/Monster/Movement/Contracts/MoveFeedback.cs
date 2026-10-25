namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 실행기(IMoveExecutor)가 보고하는 이동 진행 상태. MonsterMovement 가 도달 불가 슬롯 거절에 사용.
    /// Idle 경로 없음, Moving 이동 중, Arrived 도착, Pending 경로 계산 중, Unreachable 도달 불가, OffNavMesh 몬스터가 NavMesh 밖.
    /// </summary>
    public enum MoveExecutionState { Idle, Moving, Arrived, Pending, Unreachable, OffNavMesh }
}
