namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// StatusEffectTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 StatusEffectDef 쪽 책임.
    /// </summary>
    public sealed class StatusEffectDto
    {
        public int Id;
        public string Key = "";
        public string Name = "";
        public string Type = "";  // EffectType enum
        public float Value;
        public float Duration;
        public float TickInterval;
        public bool IsZone;
        public string LoopSfxKey = "";  // SoundTable.Key. 구역 효과가 깔려 있는 동안 반복
    }
}
