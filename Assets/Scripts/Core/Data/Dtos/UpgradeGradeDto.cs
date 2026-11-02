namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// UpgradeGradeTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환은 UpgradeGradeDef 쪽 책임.
    /// </summary>
    public sealed class UpgradeGradeDto
    {
        public string Grade = "";  // UpgradeGrade enum
        public string Name = "";
        public string Color = "";  // #RRGGBB
        public int Weight;
    }
}
