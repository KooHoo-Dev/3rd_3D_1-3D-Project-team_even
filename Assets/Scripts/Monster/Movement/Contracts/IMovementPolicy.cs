namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 유형별 "어디로 갈지" 판단. MonsterMovement.Think 가 호출하고, 결과 의도는 실행기(IMoveExecutor)로 넘어간다.
    /// 개체마다 인스턴스 하나 (내부 상태 보유 가능).
    /// </summary>
    /// <remarks>
    /// 새 몬스터 유형 = 이 인터페이스 구현 1개 + MovementPolicyKind 값 1개 + 팩토리 case 1줄.
    /// CrowdCoordinator 는 수정하지 않는다.
    /// </remarks>
    public interface IMovementPolicy
    {
        /// <summary>현재 상황(ctx)을 보고 이동 의도를 정한다. MonsterMovement.Think 가 판단 주기마다 호출한다</summary>
        /// <param name="ctx">자기·목표 위치, 군중 지시, 유형별 수치, 시각을 담은 판단 입력</param>
        /// <returns>이번 판단의 이동 의도. 목표가 무효면 Idle 정지를 반환해야 한다</returns>
        MoveIntent Evaluate(in MovementContext ctx);
    }
}
