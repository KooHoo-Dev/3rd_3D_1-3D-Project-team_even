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
        public string DisplayName = "";  // HUD·분석 표시 이름
        public int AmountMin;  // 아이템 지급 개수 최소 (획득 때 무작위)
        public int AmountMax;  // 아이템 지급 개수 최대
        public int WeaponId;  // WeaponTable.Id (SupplyType = Weapon 일 때만, Category = Supply. 그 외 0)
        public int Weight;
        public string BeaconColor = "";  // 빛기둥 색 #RRGGBB
        public string PrefabKey = "";
        public string MarkerKey = "";
        public string DropSfxKey = "";  // SoundTable.Key (투하, 착지 지점)
        public string AnnounceSfxKey = "";  // SoundTable.Key (투하 안내, 2D)
        public string CollectSfxKey = "";  // SoundTable.Key (획득)
    }
}
