using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 몬스터가 추적하는 대상(플레이어)의 최소 정보.
    /// 테스트: TransformMovementTarget(WASD 대체 객체) / 실제: 플레이어 어댑터로 교체한다.
    /// 판단 주기마다 CrowdCoordinator 가 읽는다(pull).
    /// </summary>
    public interface IMovementTarget
    {
        /// <summary>월드 좌표 (m)</summary>
        Vector3 Position { get; }

        /// <summary>시야 정면 단위 벡터. 측면 침투형의 "시야 밖" 판정용</summary>
        Vector3 Forward { get; }

        /// <summary>false 면 (사망 등) 모든 몬스터가 Idle 로 정지한다</summary>
        bool IsValid { get; }
    }

    public static class MovementTargetUtility
    {
        /// <summary>인터페이스 참조로 남아 있는 파괴된 Unity 객체도 정상적인 목표 상실로 처리한다.</summary>
        public static bool IsValid(IMovementTarget target) => target != null &&
            (!(target is Object unityObject) || unityObject != null) && target.IsValid;
    }
}
