using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using UnityEngine;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 몬스터 정의. 일반 몬스터와 보스가 같은 테이블을 쓰고 Grade/Role 로 구분한다.
    /// 공격은 MonsterAttackTable, 약점·부위는 MonsterPartTable, 보스 페이즈는 BossPhaseTable 에서 Link 단계에 연결된다.
    /// 방어도는 없다. 피해 = 무기 Damage × 부위 DamageMultiplier.
    /// </summary>
    public sealed class MonsterDef
    {
        public const string Table = "MonsterTable";

        public int Id { get; }
        public string Key { get; }
        public string Name { get; }
        public int GradeId { get; }
        public MonsterRole Role { get; }
        public bool IsBoss => Role == MonsterRole.Boss;
        public bool IsFlying => Role == MonsterRole.Air;
        public int MaxHp { get; }
        public float MoveSpeed { get; }
        /// <summary>탐지 거리(m). -1 = 전장 전체</summary>
        public float DetectRange { get; }
        public bool DetectsAll => DetectRange < 0f;
        public float BodyScale { get; }
        public float Height { get; }
        /// <summary>비행 고도(m). 지상 유닛은 0</summary>
        public float FlyHeight { get; }
        public MeshVariant MeshVariant { get; }
        /// <summary>처치 시 자동 획득 재화 (BL-029)</summary>
        public int Reward { get; }
        /// <summary>생성할 프리팹 (Resources 에서 로드된 원본. Instantiate 해서 사용)</summary>
        public GameObject Prefab { get; }

        /// <summary>Link 단계에서 채워짐 (키가 비었으면 null). 평상시 울음</summary>
        public SoundDef IdleSfx { get; internal set; }
        /// <summary>살아 있는 동안 계속 나는 소리 (이동·날갯짓)</summary>
        public SoundDef LoopSfx { get; internal set; }
        public SoundDef DeathSfx { get; internal set; }
        /// <summary>기절 시작 (돌진 벽 충돌). 키가 비었으면 null</summary>
        public SoundDef StunSfx { get; internal set; }
        /// <summary>기습형 신호 (플레이어 근처·시야 밖일 때 공격 전 1번). 있으면 근처에서 울음을 내지 않는다. 키가 비었으면 null</summary>
        public SoundDef HiddenSfx { get; internal set; }
        internal string IdleSfxKey { get; }
        internal string LoopSfxKey { get; }
        internal string DeathSfxKey { get; }
        internal string StunSfxKey { get; }
        internal string HiddenSfxKey { get; }

        /// <summary>GradeId 에서 계산 (1 → Weak, 2 → Mid, 3 → Boss)</summary>
        public MonsterGrade Grade { get; internal set; }
        /// <summary>테이블 순서 유지</summary>
        public IReadOnlyList<MonsterAttackDef> Attacks { get; private set; } = Array.Empty<MonsterAttackDef>();
        /// <summary>기본 공격 (PatternTag None)</summary>
        public IReadOnlyList<MonsterAttackDef> BasicAttacks { get; private set; } = Array.Empty<MonsterAttackDef>();
        /// <summary>보스 등장 공격 (없으면 null)</summary>
        public MonsterAttackDef EntranceAttack { get; private set; }
        public IReadOnlyList<MonsterPartDef> Parts { get; private set; } = Array.Empty<MonsterPartDef>();
        /// <summary>보스 페이즈 (Phase 오름차순). 일반 몬스터는 비어 있음</summary>
        public IReadOnlyList<BossPhaseDef> Phases { get; private set; } = Array.Empty<BossPhaseDef>();

        internal MonsterDef(MonsterDto d, DataLoadReport r)
        {
            Id = d.Id;
            Key = d.Key;
            Name = d.Name;
            GradeId = d.GradeId;
            Grade = (MonsterGrade)( GradeId - 1 );
            Role = ParseUtil.Enum<MonsterRole>(d.Role, r, Table, Id, nameof(d.Role));
            MaxHp = d.MaxHp;
            MoveSpeed = d.MoveSpeed;
            DetectRange = d.DetectRange;
            BodyScale = d.BodyScale;
            Height = d.Height;
            FlyHeight = d.FlyHeight;
            MeshVariant = ParseUtil.Enum<MeshVariant>(d.MeshVariant, r, Table, Id, nameof(d.MeshVariant));
            Reward = d.Reward;
            Prefab = ParseUtil.Asset<GameObject>(d.PrefabKey, r, Table, Id, nameof(d.PrefabKey));
            IdleSfxKey = d.IdleSfxKey;
            LoopSfxKey = d.LoopSfxKey;
            DeathSfxKey = d.DeathSfxKey;
            StunSfxKey = d.StunSfxKey;
            HiddenSfxKey = d.HiddenSfxKey;

            r.Check(Enum.IsDefined(typeof(MonsterGrade), Grade), Table, Id, "GradeId 는 1(Weak)~3(Boss) 중 하나");
            r.Check(string.IsNullOrEmpty(Key) == false, Table, Id, "Key 가 비었습니다");
            r.Check(MaxHp > 0, Table, Id, "MaxHp 는 1 이상");
            r.Check(MoveSpeed > 0f, Table, Id, "MoveSpeed 는 0 초과");
            r.Check(DetectRange > 0f || DetectRange == -1f, Table, Id, "DetectRange 는 0 초과 또는 -1(전장 전체)");
            r.Check(BodyScale > 0f && Height > 0f, Table, Id, "BodyScale, Height 는 0 초과");
            r.Check(IsFlying ? FlyHeight > 0f : FlyHeight == 0f, Table, Id, "FlyHeight 는 Air 역할만 0 초과");
            r.Check(Reward >= 0, Table, Id, "Reward 는 0 이상");
        }

        internal void SetLinks(List<MonsterAttackDef> attacks, List<MonsterPartDef> parts, List<BossPhaseDef> phases)
        {
            Attacks = attacks;
            Parts = parts;
            Phases = phases;
            var basics = new List<MonsterAttackDef>();
            foreach (var a in attacks)
            {
                if (a.IsEntrance) EntranceAttack = a;
                else if (a.IsSpecial == false) basics.Add(a);
            }
            BasicAttacks = basics;
        }

        /// <summary>AttackKey 로 공격 찾기 (없으면 null)</summary>
        public MonsterAttackDef FindAttack(string attackKey)
        {
            foreach (var a in Attacks)
                if (a.AttackKey == attackKey) return a;
            return null;
        }

        /// <summary>PartKey 로 부위 찾기 (없으면 null)</summary>
        public MonsterPartDef FindPart(string partKey)
        {
            foreach (var p in Parts)
                if (p.PartKey == partKey) return p;
            return null;
        }

        /// <summary>현재 체력 비율(0~1)에 해당하는 보스 페이즈. 페이즈가 없으면 null</summary>
        public BossPhaseDef GetPhase(float hpRatio01)
        {
            if (Phases.Count == 0) return null;
            float percent = hpRatio01 * 100f;
            var result = Phases[0];
            foreach (var p in Phases)
                if (percent <= p.HpThresholdPercent) result = p;
            return result;
        }
    }
}
