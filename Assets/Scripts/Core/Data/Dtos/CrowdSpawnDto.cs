namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// CrowdSpawnTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 CrowdSpawnRuleDef 쪽 책임.
    /// </summary>
    public sealed class CrowdSpawnDto
    {
        public int Id;
        public int Round;  // 0 = 모든 라운드
        public int MonsterId;
        public float StartTime;
        public float EndTime;  // 0 = 보스 등장까지
        public float Interval;
        public int CountPerSpawn;
        public int MaxAlive;
        public string SpawnPattern = "";  // SpawnPattern enum
    }
}
