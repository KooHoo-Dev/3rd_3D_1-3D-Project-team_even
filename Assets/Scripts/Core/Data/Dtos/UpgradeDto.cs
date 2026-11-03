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
        public string GradeValues = "";  // 등급별 증가량 (하급;일반;에픽), 레벨마다 누적
        public int Weight;               // 상점 카드 뽑기 가중치
        public string Name = "";
        public string Description = "";  // {0} = 등급 값
    }
}
