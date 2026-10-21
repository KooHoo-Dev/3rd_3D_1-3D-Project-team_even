namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// RoundTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 RoundDef 쪽 책임.
    /// </summary>
    public sealed class RoundDto
    {
        public int Round;  // 1부터
        public int BossId;  // MonsterTable.Id
        public float BossSpawnTimeMin;
        public float BossSpawnTimeMax;
        public float SupplyStartTime;
        public float SupplyInterval;
        public int BossReward;
    }
}
