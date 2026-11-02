using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    public sealed class BossDef
    {
        public const string Table = "BossTable";
        
        public int Id;
        public int MaxHp { get; }
        public float MoveSpeed { get; }
        public float PatternCooldown { get; }
        /// <summary>보스 프리팹의 Resources 경로 (확장자 없음). RoundBossEncounter 가 라운드 보스를 만들 때 쓴다</summary>
        public string Prefab { get; }
        // MonsterPartTable 에서 BossId 가 이 보스인 행들. Link 단계에서 채워진다
        public IReadOnlyList<MonsterPartDef> Parts { get; private set; } = Array.Empty<MonsterPartDef>();

        public BossDef(BossDto dto, DataLoadReport report)
        {
            Id = dto.Id;
            MaxHp = dto.MaxHp;
            MoveSpeed = dto.MoveSpeed;
            PatternCooldown = dto.PatternCooldown;
            Prefab = dto.Prefab?.Trim() ?? string.Empty;

            report.Check(Id > 0, Table, Id, "Id는 1 이상이어야 합니다.");
            report.Check(MaxHp > 0, Table, Id, "MaxHP는 1 이상이어야 합니다.");
            report.Check(MoveSpeed >= 0f, Table, Id, "MoveSpeed는 0 이상이어야 합니다.");
            report.Check(PatternCooldown >= 0f, Table, Id, "PatternCooldown은 0 이상이어야 합니다.");
            report.Check(Prefab.Length > 0, Table, Id, "Prefab(Resources 경로)이 비어 있습니다.");
        }

        /// <summary>DataManager 의 Link 단계가 이 보스의 부위 목록을 넣는다</summary>
        /// <param name="parts">MonsterPartTable 에서 BossId 가 이 보스인 행들</param>
        internal void SetParts(List<MonsterPartDef> parts)
        {
            Parts = parts;
        }

        /// <summary>보스 컨트롤러가 부위 배율·체력을 읽을 때 PartKey 로 부위를 찾는다</summary>
        /// <param name="partKey">MonsterPartTable.PartKey (Tyranopode 는 TyranopodePartType 이름)</param>
        /// <returns>찾은 부위. 없으면 null</returns>
        public MonsterPartDef FindPart(string partKey)
        {
            foreach (var part in Parts)
                if (part.PartKey == partKey) return part;
            return null;
        }
    }
}
