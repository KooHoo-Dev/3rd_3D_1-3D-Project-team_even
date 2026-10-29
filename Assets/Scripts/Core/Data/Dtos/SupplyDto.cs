namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// SupplyTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 SupplyDef 쪽 책임.
    /// </summary>
    public sealed class SupplyDto
    {
        public int Id;
        public string SupplyType = "";  // SupplyType enum
        public int Amount;
        public int Weight;
        public string PrefabKey = "";
        public string MarkerKey = "";
        public string DropSfxKey = "";  // SoundTable.Key (투하, 착지 지점)
        public string CollectSfxKey = "";  // SoundTable.Key (획득)
    }
}
