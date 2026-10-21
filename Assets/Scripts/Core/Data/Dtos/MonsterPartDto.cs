namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// MonsterPartTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 MonsterPartDef 쪽 책임.
    /// </summary>
    public sealed class MonsterPartDto
    {
        public int Id;
        public int MonsterId;  // MonsterTable.Id
        public string PartKey = "";
        public string Name = "";
        public float DamageMultiplier;
        public string Exposure = "";  // PartExposure enum
        public int PartHp;  // 0 = 파괴 불가
        public int LinkedAttackId;  // MonsterAttackTable.Id (0 = 없음)
        public int TransformToMonsterId;  // MonsterTable.Id (0 = 없음)
    }
}
