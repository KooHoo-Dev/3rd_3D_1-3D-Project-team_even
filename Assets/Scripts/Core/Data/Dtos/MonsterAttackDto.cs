namespace HybridShooter.Core.Data.Dtos
{
    /// <summary>
    /// MonsterAttackTable.tsv 의 한 행. 필드 이름 = TSV 헤더 이름 (컬럼 추가/변경 시 둘 다 수정).
    /// DTO 는 "파일 모양 그대로"만 담는다. 검증·변환·참조 연결은 MonsterAttackDef 쪽 책임.
    /// </summary>
    public sealed class MonsterAttackDto
    {
        public int Id;  // MonsterId*100 + 순번
        public int MonsterId;  // MonsterTable.Id
        public string AttackKey = "";
        public string Name = "";
        public string PatternTag = "";  // BossPatternTag enum
        public int Damage;
        public int HitCount;
        public float Cooldown;
        public float TelegraphTime;
        public float RecoveryTime;
        public string Shape = "";  // AttackShape enum
        public float Range;
        public float Radius;
        public float Angle;
        public float Width;
        public float ProjectileSpeed;
        public int ProjectileCount;
        public float KnockbackDistance;
        public float WallHitStunTime;
        public int EffectId;  // StatusEffectTable.Id (0 = 없음)
        public string Trigger = "";  // TriggerType enum
        public float TriggerValue;
        public bool ClearsMinions;
        public string AnimTrigger = "";
        public string SfxKey = "";  // SoundTable.Key (빈칸 = 없음). 예고(Telegraph) 시작에 재생
    }
}
