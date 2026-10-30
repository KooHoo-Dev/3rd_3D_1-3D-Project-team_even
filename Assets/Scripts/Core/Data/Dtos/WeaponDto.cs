namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// WeaponTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 WeaponDef 쪽 책임.
    /// </summary>
    public sealed class WeaponDto
    {
        public int Id;
        public string Name = "";
        public string FireMode = "";  // FireMode enum
        public int Damage;
        public float FireRate;  // RPM
        public int MagazineSize;
        public int StartMagazines;  // 시작 예비 탄창 개수
        public int MaxMagazines;  // 최대 예비 탄창 개수 (탄창 단위)
        public float ReloadTime;
        public float Range;
        public float SpreadAngle;
        public string FireSfxKey = "";  // SoundTable.Key
        public string ReloadSfxKey = "";  // SoundTable.Key (장전 시작)
        public string ReloadInsertSfxKey = "";  // SoundTable.Key (탄창·탄 넣기)
        public string ReloadEndSfxKey = "";  // SoundTable.Key (장전 완료)
        public string SwitchSfxKey = "";  // SoundTable.Key (무기 교체)
        public string DryFireSfxKey = "";  // SoundTable.Key (빈 탄창으로 쏠 때)
        public string ExplodeSfxKey = "";  // SoundTable.Key (투사체 폭발. 비우면 프리팹 값)
        public float SupplyMagazineRatio;  // 보급 1회 예비 탄창 충전 비율 (MaxMagazines × 비율, 내림·최소 1)
        public string Category = "";  // WeaponCategory enum
        public string IconKey = "";  // Resources/UI/WeaponIcons/{IconKey} 스프라이트 (비우면 HUD 가 이름 표시)
        public int UnlockOrder;  // 0 = 처음부터 해금 · 1~ = 스테이지(보스) 클리어마다 이 순서로 해금
    }
}
