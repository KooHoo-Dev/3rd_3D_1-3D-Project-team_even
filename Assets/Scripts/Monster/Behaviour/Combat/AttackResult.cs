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
}
