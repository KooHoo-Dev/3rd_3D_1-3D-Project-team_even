namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// MonsterTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 MonsterDef 쪽 책임.
    /// </summary>
    public sealed class MonsterDto
    {
        public int Id;
        public string Key = "";
        public string Name = "";
        public int GradeId;  // MonsterGradeTable.GradeId
        public string Role = "";  // MonsterRole enum
        public int MaxHp;
        public float MoveSpeed;
        public float RunSpeed;
        public float DetectRange;  // -1 = 전장 전체
        public float BodyScale;
        public float Height;
        public float FlyHeight;
        public string MeshVariant = "";  // MeshVariant enum
        public int Reward;
        public string PrefabKey = "";
    }
}
