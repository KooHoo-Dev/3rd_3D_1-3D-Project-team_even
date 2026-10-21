namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// BossPhaseTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 BossPhaseDef 쪽 책임.
    /// </summary>
    public sealed class BossPhaseDto
    {
        public int Id;
        public int MonsterId;  // MonsterTable.Id
        public int Phase;  // 1부터
        public int HpThresholdPercent;
        public string PatternSequence = "";  // MonsterAttackTable.Id 목록 (; 구분)
        public float CooldownMultiplier;
        public bool ElectricSweep;
        public string VisualState = "";
    }
}
