namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// WaveTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 WaveDef 쪽 책임.
    /// </summary>
    public sealed class WaveDto
    {
        public int Id;
        public float StartTime;  // 초, 스테이지 시작 기준 상대 시간
        public string SpawnList = "";  // WaveSpawnerTable.Id 를 ';' 로 구분
    }
}
