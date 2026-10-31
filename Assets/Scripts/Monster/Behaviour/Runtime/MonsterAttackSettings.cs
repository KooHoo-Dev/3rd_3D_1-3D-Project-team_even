namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// MonsterController가 기존 공격 설정 컴포넌트를 모아 Factory에 전달하는 참조 묶음이다.
    /// Factory는 역할에 필요한 설정만 꺼내 구체 공격에 넘긴다.
    /// </summary>
    public readonly struct MonsterAttackSettings
    {
        public readonly PoisonAreaSettings PoisonArea;
        public readonly PounceSettings Pounce;
        public readonly ChargeSettings Charge;
        public readonly DiveSettings Dive;
        public readonly SelfDestructSettings SelfDestruct;

        /// <summary>기존 설정 컴포넌트를 보관한다. 컴포넌트 초기화와 풀 생성은 기존 소유자가 맡는다.</summary>
        /// <param name="poisonArea">독장판 설정. 해당 공격이 없으면 null</param>
        /// <param name="pounce">도약 설정. 해당 공격이 없으면 null</param>
        /// <param name="charge">돌진 설정. 해당 공격이 없으면 null</param>
        /// <param name="dive">급강하 설정. 해당 공격이 없으면 null</param>
        /// <param name="selfDestruct">자폭 폭발 연출 설정. 해당 공격이 없으면 null</param>
        public MonsterAttackSettings(PoisonAreaSettings poisonArea, PounceSettings pounce,
                                     ChargeSettings charge, DiveSettings dive, SelfDestructSettings selfDestruct)
        {
            PoisonArea = poisonArea;
            Pounce = pounce;
            Charge = charge;
            Dive = dive;
            SelfDestruct = selfDestruct;
        }
    }
}
