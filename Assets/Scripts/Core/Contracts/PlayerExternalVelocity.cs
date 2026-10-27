using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    /// <summary>
    /// 외부 속도 소스에 넘기는 이번 물리 프레임의 플레이어 상태 (읽기 전용).
    /// 소스는 이 값으로 "이번 프레임에 더할 속도"를 직접 계산한다
    /// </summary>
    public readonly struct PlayerMotionContext
    {
        /// <summary>발 위치 (리지드바디)</summary>
        public readonly Vector3 Position;
        /// <summary>이번 프레임 모터(걷기·달리기·구르기·미끄럼)가 정한 속도. 외부 속도는 빠져 있다</summary>
        public readonly Vector3 MotorVelocity;
        /// <summary>이동 입력 방향 (월드 수평, 길이 0~1). 입력이 없으면 zero</summary>
        public readonly Vector3 MoveDirection;
        /// <summary>
        /// 기준 달리기 속도(m/s) = 걷기 × 달리기 배율 × 속도 배율(업그레이드 등), 최대 걷기 속도 상한 적용.
        /// 스태미나 바닥·뒷걸음 감속은 반영하지 않는다 ("제대로 달리면 낼 수 있는 속도")
        /// </summary>
        public readonly float RunSpeed;
        public readonly bool IsGrounded;
        /// <summary>물리 프레임 시간 (Time.fixedDeltaTime)</summary>
        public readonly float DeltaTime;

        public PlayerMotionContext(Vector3 position, Vector3 motorVelocity, Vector3 moveDirection, float runSpeed, bool isGrounded, float deltaTime)
        {
            Position = position;
            MotorVelocity = motorVelocity;
            MoveDirection = moveDirection;
            RunSpeed = runSpeed;
            IsGrounded = isGrounded;
            DeltaTime = deltaTime;
        }
    }

    /// <summary>
    /// 플레이어에게 수평 속도를 더하는 소스 (보스 흡입·밀어내기 등). IPlayerExternalVelocity.AddVelocitySource 로 붙인다.
    ///  - 플레이어가 매 물리 프레임 GetVelocity 를 불러, 이동(모터) 속도 위에 결과를 "더한다". 이동 입력은 살아 있어 반대로 달리면 버틴다
    ///  - 힘(AddForce)이 아니라 속도(m/s)다. 모터가 매 프레임 속도를 목표로 되돌리므로 힘은 대부분 지워진다
    ///  - 여러 소스가 붙어 있으면 모두 합친다 (플레이어 쪽 합계 상한 있음). 벽·경사 처리는 플레이어가 한다
    ///  - UnityEngine.Object(MonoBehaviour 등)로 구현했다면 파괴될 때 저절로 빠진다
    /// </summary>
    public interface IPlayerVelocitySource
    {
        /// <summary>true 가 되면 플레이어가 목록에서 뺀다. 매 물리 프레임 확인한다 (쓰러짐 중에도)</summary>
        bool IsFinished { get; }

        /// <summary>
        /// 이번 물리 프레임에 더할 수평 속도(m/s). y 는 무시한다.
        /// 플레이어가 쓰러짐·부활 연출·벽넘기 중이면 불리지 않는다 (붙어 있는 상태는 유지)
        /// </summary>
        Vector3 GetVelocity(in PlayerMotionContext context);

        /// <summary>
        /// 플레이어가 스스로 뺄 때 알림: 처음부터 재시작, 플레이어 비활성화, 이 소스에서 예외 발생.
        /// IsFinished 로 끝났거나 RemoveVelocitySource 로 직접 뗀 경우에는 불리지 않는다
        /// </summary>
        void OnRemovedByPlayer();
    }
}
