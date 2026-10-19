namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// UpgradeTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 UpgradeDef 쪽 책임.
    /// </summary>
    public sealed class UpgradeDto
    {
        public int Id;
        public string UpgradeType = "";  // UpgradeType enum
        public int Level;
        public int Cost;
        public float Value;  // 레벨별 증가량(누적)
        public string Name = "";
        public string Description = "";
    }
}
