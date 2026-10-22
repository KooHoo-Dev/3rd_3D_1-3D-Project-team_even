using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>위치 압박형(BL-011): 목표와의 수평 거리를 [min, max] 밴드에 유지. 방향 지시(Directive)는 무시한다</summary>
    public sealed class KeepDistancePolicy : IMovementPolicy
    {
        public KeepDistancePolicy(int seed) // 팩토리 시그니처 통일. seed 는 사용하지 않는다
        {
        }

        /// <summary>밴드 안이면 정지(Engaged), 밖이면 목표 기준 같은 방향의 밴드 중앙 거리로 이동 (후퇴 또는 접근)</summary>
        public MoveIntent Evaluate(in MovementContext ctx)
        {
            if (!ctx.TargetValid)
                return MoveIntent.Stop(MovementState.Idle);

            var t = ctx.Tuning;
            Vector3 self = ctx.SelfPosition;
            Vector3 target = ctx.TargetPosition;

            float dx = self.x - target.x;
            float dz = self.z - target.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);

            // 밴드 안(경계 포함)이면 제자리 유지
            if (d >= t.KeepDistanceMin && d <= t.KeepDistanceMax)
                return MoveIntent.Stop(MovementState.Engaged);

            // 목표와 겹치면 기본 방향(+z) 사용
            float dirX = 0f, dirZ = 1f;
            if (d >= 1e-4f)
            {
                dirX = dx / d;
                dirZ = dz / d;
            }

            // 너무 가깝거나 멀면 밴드 중앙 거리로 이동 (후퇴 또는 접근)
            float mid = (t.KeepDistanceMin + t.KeepDistanceMax) * 0.5f;
            var dest = new Vector3(target.x + dirX * mid, target.y, target.z + dirZ * mid);
            return new MoveIntent(MovementState.Approach, dest, t.MoveSpeed, 0.3f);
        }
    }
}
