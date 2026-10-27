namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// WaveSpawnerTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 WaveSpawnerDef 쪽 책임.
    /// </summary>
    public sealed class WaveSpawnerDto
    {
        public int Id;
        public int MonsterId;  // MonsterTable.Id (보스 불가)
        public int Count;
        public float StartDelay;  // 초, 웨이브 시작 후 첫 스폰까지
        public float SpawnInterval;  // 초, 0 = 동시 스폰
        public string SpawnPattern = "";  // SpawnPattern enum
    }
}
