using HybridShooter.Core.Contracts;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>공격 1회가 끝난 이유</summary>
    public enum AttackResult
    {
        None,       // 아직 끝난 공격 없음
        Completed,  // 예고 → 공격 → 회복을 모두 마침
        Cancelled,  // 처치 등으로 중간에 끊김
        WallHit,    // 돌진 중 벽에 부딪힘 → 기절
    }

    /// <summary>
    /// AttackState 가 쓰는 공격 전략. 예고 → 공격 → 회복 진행은 구현체가 맡는다.
    /// </summary>
    public interface IMonsterAttack
    {
        /// <summary>공격 상태로 들어가는 수평 거리 (m)</summary>
        float Range { get; }

        /// <summary>예고·공격·회복 중 하나에 있으면 true</summary>
        bool IsAttacking { get; }

        AttackResult LastResult { get; }

        /// <summary>LastResult 가 WallHit 일 때 기절 시간 (s)</summary>
        float WallHitStunTime { get; }

        /// <summary>쿨다운·발동 조건 확인</summary>
        bool CanStart(float now);

        void Begin(IPlayerTarget target, float now);
        void Tick(float now, float deltaTime);
        void Cancel();
    }
}
