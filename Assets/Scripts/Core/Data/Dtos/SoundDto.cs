namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// SoundTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 SoundDef 쪽 책임.
    /// </summary>
    public sealed class SoundDto
    {
        public string Key = "";
        public string Category = "";  // SoundCategory enum
        public string Path = "";  // Resources 기준
        public float Volume;
        public bool Loop;
    }
}
