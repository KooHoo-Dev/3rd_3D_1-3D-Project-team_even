namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 공격 한 주기가 정상 완료(Completed)된 뒤 몬스터가 갈 곳. AttackState.FinishAttackCycle 이 읽는다.
    /// </summary>
    public enum AfterAttack
    {
        StayInAttack,       // Attack 에 머물며 다음 틱에 판단 (기본)
        RestInIdle,         // Idle 에서 준비 동작 후 다시 공격 (돌진)
        RepositionInChase,  // Chase 에서 재정비가 끝날 때까지 공격하지 않음 (공중)
        Die,                // 자폭 판정 직후 보상 없이 사망
    }
}
