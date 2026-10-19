namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// GrenadeTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 GrenadeDef 쪽 책임.
    /// </summary>
    public sealed class GrenadeDto
    {
        public int Id;
        public string Name = "";
        public int Damage;
        public float Radius;
        public float FuseTime;
        public float ThrowSpeed;
        public string PrefabKey = "";
        public string ExplodeSfxKey = "";  // SoundTable.Key
    }
}
