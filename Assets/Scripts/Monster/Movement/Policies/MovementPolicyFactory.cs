using System;

namespace HybridShooter.Monster.Movement
{
    /// <summary>MovementPolicyKind → 정책 인스턴스. 새 유형 추가 시 case 1줄 추가</summary>
    public static class MovementPolicyFactory
    {
        /// <summary>kind 에 맞는 정책 인스턴스를 만든다. MonsterMovement 가 활성화 때 개체마다 한 번 호출한다</summary>
        /// <param name="kind">MovementProfile 에서 고른 정책 종류</param>
        /// <param name="seed">선회 선택 등 난수 시드 (개체별로 다르게)</param>
        /// <returns>새 정책 인스턴스</returns>
        /// <exception cref="ArgumentOutOfRangeException">구현되지 않은 kind 일 때</exception>
        public static IMovementPolicy Create(MovementPolicyKind kind, int seed)
        {
            switch (kind)
            {
                case MovementPolicyKind.Melee: return new MeleePolicy(seed);
                case MovementPolicyKind.Flank: return new FlankPolicy(seed);
                case MovementPolicyKind.KeepDistance: return new KeepDistancePolicy(seed);
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "구현되지 않은 이동 정책");
            }
        }
    }
}
