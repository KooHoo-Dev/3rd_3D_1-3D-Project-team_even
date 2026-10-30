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
        public int GradeId;  
        public string Role = "";  // MonsterRole enum
        public int MaxHp;
        public float MoveSpeed;
        public float DetectRange;  // -1 = 전장 전체
        public float BodyScale;
        public float Height;
        public float FlyHeight;
        public string MeshVariant = "";  // MeshVariant enum
        public int Reward;
        public string PrefabKey = "";
        public string IdleSfxKey = "";  // SoundTable.Key (빈칸 = 없음)
        public string LoopSfxKey = "";  // 이동·날갯짓 루프
        public string DeathSfxKey = "";
        public string StunSfxKey = "";  // 기절 시작 (돌진 벽 충돌)
        public string HiddenSfxKey = "";  // 기습형: 플레이어 근처·시야 밖일 때 공격 전 1번 (있으면 근처에서 울지 않음)
    }
}
