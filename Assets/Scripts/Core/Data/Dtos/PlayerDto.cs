namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// PlayerTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 PlayerDef 쪽 책임.
    /// </summary>
    public sealed class PlayerDto
    {
        public int Id;
        public string Name = "";
        public int MaxHp;
        public float MoveSpeed;
        public float LookSensitivity;
        public float PitchMin;
        public float PitchMax;
        public int RifleId;  // WeaponTable.Id
        public int GrenadeId;  // GrenadeTable.Id
        public int StartGrenades;
        public int MaxGrenades;
    }
}
