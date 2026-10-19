using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 플레이어 정의 (불변). 런타임 상태(현재 HP, 수류탄 수)는 Player 쪽 컴포넌트가 가진다.
    /// </summary>
    public sealed class PlayerDef
    {
        public const string Table = "PlayerTable";

        public int Id { get; }
        public string Name { get; }
        public int MaxHp { get; }
        public float MoveSpeed { get; }
        public float LookSensitivity { get; }
        /// <summary>상하 시야 제한(도). BL-002</summary>
        public float PitchMin { get; }
        public float PitchMax { get; }
        public int RifleId { get; }
        public int GrenadeId { get; }
        public int StartGrenades { get; }
        /// <summary>강화 전 기본 최대 보유 수. 실제 값 = MaxGrenades + 강화(GrenadeCapacity)</summary>
        public int MaxGrenades { get; }

        /// <summary>Link 단계에서 채워짐</summary>
        public WeaponDef Rifle { get; internal set; }
        public GrenadeDef Grenade { get; internal set; }

        internal PlayerDef(PlayerDto d, DataLoadReport r)
        {
            Id = d.Id;
            Name = d.Name;
            MaxHp = d.MaxHp;
            MoveSpeed = d.MoveSpeed;
            LookSensitivity = d.LookSensitivity;
            PitchMin = d.PitchMin;
            PitchMax = d.PitchMax;
            RifleId = d.RifleId;
            GrenadeId = d.GrenadeId;
            StartGrenades = d.StartGrenades;
            MaxGrenades = d.MaxGrenades;

            r.Check(MaxHp > 0, Table, Id, "MaxHp 는 1 이상");
            r.Check(MoveSpeed > 0f, Table, Id, "MoveSpeed 는 0 초과");
            r.Check(PitchMin < PitchMax, Table, Id, "PitchMin 은 PitchMax 보다 작아야 합니다");
            r.Check(StartGrenades >= 0 && StartGrenades <= MaxGrenades, Table, Id, "StartGrenades 는 0 ~ MaxGrenades");
        }
    }
}
