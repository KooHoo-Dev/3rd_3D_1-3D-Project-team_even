namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// RoundTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 RoundDef 쪽 책임.
    /// </summary>
    public sealed class RoundDto
    {
        public int Round;  // 1부터 = 진행 번호
        public string Type = "CrowdToBoss";  // RoundType (CrowdToBoss/Annihilation/Survival)
        public int WaveRound;  // WaveStageTable.RoundNo (이 라운드의 웨이브 편성)
        public int BossId;  // BossTable.Id (라운드 보스, RoundBossEncounter 가 고른다). CrowdToBoss 만 사용, 그 외 0
        public float BossSpawnTimeMin;
        public float BossSpawnTimeMax;
        public float PhaseTime;  // 초. Annihilation = 소환 시간 · Survival = 생존 시간 · CrowdToBoss = 0 (안 씀)
        public int KillTarget;  // Annihilation 의 목표 처치 수. 그 외 0
        public float SupplyStartTime;
        public float SupplyInterval;
        public int BossReward;
    }
}
