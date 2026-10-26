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

    /// <summary>IMoveExecutor.Apply 의 처리 결과. MonsterMovement 가 "실행 중인 슬롯" 추적에 사용한다</summary>
    public enum MoveApplyResult
    {
        PathSet,      // 새 경로 설정
        PathKept,     // 목적지 변화가 작아 기존 경로 유지
        Deferred,     // 재경로 최소 간격 미달로 보류
        Unreachable,  // 도달 불가
        OffNavMesh,   // 몬스터가 NavMesh 위에 없음
        Stopped,      // 정지 의도라 멈춤
    }

    /// <summary>
    /// "실제로 움직이기". MonsterMovement 가 정책의 의도를 넘기면 실제 이동으로 옮긴다.
    /// 지상(NavMesh)과 공중이 다르기 때문에만 분리한다.
    /// </summary>
    public interface IMoveExecutor
    {
        /// <summary>의도를 실제 이동으로 옮긴다. MonsterMovement 가 의도를 확정할 때 호출한다</summary>
        /// <param name="intent">정책이 낸 이동 의도. HasDestination 이 false 면 정지</param>
        /// <returns>경로 설정·유지·보류·도달 불가·NavMesh 밖·정지 중 처리 결과</returns>
        MoveApplyResult Apply(in MoveIntent intent);
        /// <summary>즉시 정지하고 경로를 비운다</summary>
        void Stop();
        /// <summary>
        /// 경로 없이 몸을 정해진 양만큼 직접 옮긴다. 벽(이동 가능 영역 경계)에 막히면 막힌 지점까지만 옮긴다.
        /// MonsterMovement.MoveDuringAttack 이 공격·준비 동작 중(정지 상태)에 호출한다.
        /// </summary>
        /// <param name="displacement">이번 틱 이동량 (월드, m)</param>
        /// <returns>true = 벽에 막혔다</returns>
        bool MoveBy(UnityEngine.Vector3 displacement);
        // 현재 속도 (m/s). 애니메이션 연결용
        UnityEngine.Vector3 Velocity { get; }
        // 실행기의 현재 진행 상태 (도착·도달 불가 감지용)
        MoveExecutionState ExecutionState { get; }
        // 몸 반경 (m). 군중 제어의 슬롯 장애물 판정용
        float Radius { get; }
    }
}
