namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// RoundTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 RoundDef 쪽 책임.
    /// </summary>
    public sealed class RoundDto
    {
        public int Round;  // 1부터 = 진행 번호
        public string Type = "CrowdToBoss";  // RoundType (CrowdToBoss/Annihilation/AnnihilationToBoss/Survival)
        public string WaveRounds;  // WaveStageTable.RoundNo 후보를 ';' 로 구분 ("2;3;4;5"). 라운드 시작마다 하나를 무작위로 고른다
        public int BossId;  // BossTable.Id (라운드 보스, RoundBossEncounter 가 고른다). CrowdToBoss·AnnihilationToBoss 만 사용, 그 외 0
        public float BossSpawnTimeMin;
        public float BossSpawnTimeMax;
        public float PhaseTime;  // 초. Annihilation·AnnihilationToBoss = 소환 시간 · Survival = 생존 시간 · CrowdToBoss = 0 (안 씀)
        public float KillRatio;  // 섬멸 목표 비율 (0~1). 목표 처치 수 = 고른 편성이 PhaseTime 안에 소환하는 처치 가능 몬스터 수 × 비율(내림). 섬멸 유형만, 그 외 0
        public float SupplyStartTime;
        public float SupplyInterval;
        public int BossReward;
        public string CrowdBgm;  // SoundTable 키 (Category Bgm). 라운드 시작부터 재생. 비우면 GameSoundRouter 기본 곡
        public string BossBgm;  // SoundTable 키 (Category Bgm). 보스 등장부터 재생. 보스 없는 라운드·비우면 GameSoundRouter 기본 곡
    }
}
