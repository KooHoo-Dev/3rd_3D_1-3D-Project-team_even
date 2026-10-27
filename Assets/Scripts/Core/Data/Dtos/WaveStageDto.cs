namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// WaveStageTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 WaveStageDef 쪽 책임.
    /// </summary>
    public sealed class WaveStageDto
    {
        public int Id;
        public int Round;  // RoundTable.Round
        public float StartTime;  // 초, 라운드 시작 기준
        public float EndTime;  // 초, 라운드 시작 기준
        public string WaveList = "";  // WaveTable.Id 를 ';' 로 구분
    }
}
