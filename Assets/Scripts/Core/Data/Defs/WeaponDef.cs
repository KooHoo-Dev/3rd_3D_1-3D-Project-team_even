using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>라이플 정의 (BL-003, BL-004). 강화 적용 값은 IUpgradeState 와 조합해서 계산한다.</summary>
    public sealed class WeaponDef
    {
        public const string Table = "WeaponTable";

        public int Id { get; }
        public string Name { get; }
        public FireMode FireMode { get; }
        public int Damage { get; }
        /// <summary>분당 발사 수 (RPM)</summary>
        public float FireRate { get; }
        /// <summary>발사 간격(초) = 60 / FireRate</summary>
        public float FireInterval { get; }
        public int MagazineSize { get; }
        public float ReloadTime { get; }
        public float Range { get; }
        /// <summary>탄 퍼짐 원뿔 반각(도)</summary>
        public float SpreadAngle { get; }
        public string PrefabKey { get; }
        public string FireSfxKey { get; }
        public string ReloadSfxKey { get; }

        internal WeaponDef(WeaponDto d, DataLoadReport r)
        {
            Id = d.Id;
            Name = d.Name;
            FireMode = ParseUtil.Enum<FireMode>(d.FireMode, r, Table, Id, nameof(d.FireMode));
            Damage = d.Damage;
            FireRate = d.FireRate;
            FireInterval = d.FireRate > 0f ? 60f / d.FireRate : 0f;
            MagazineSize = d.MagazineSize;
            ReloadTime = d.ReloadTime;
            Range = d.Range;
            SpreadAngle = d.SpreadAngle;
            PrefabKey = d.PrefabKey;
            FireSfxKey = d.FireSfxKey;
            ReloadSfxKey = d.ReloadSfxKey;

            r.Check(Damage >= 0, Table, Id, "Damage 는 0 이상");
            r.Check(FireRate > 0f, Table, Id, "FireRate(RPM) 는 0 초과");
            r.Check(MagazineSize > 0, Table, Id, "MagazineSize 는 1 이상");
            r.Check(ReloadTime > 0f, Table, Id, "ReloadTime 은 0 초과");
            r.Check(Range > 0f, Table, Id, "Range 는 0 초과");
        }
    }
}
