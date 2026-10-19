using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>수류탄 정의 (BL-005)</summary>
    public sealed class GrenadeDef
    {
        public const string Table = "GrenadeTable";

        public int Id { get; }
        public string Name { get; }
        public int Damage { get; }
        /// <summary>폭발 반경(m)</summary>
        public float Radius { get; }
        /// <summary>투척 후 폭발까지(초)</summary>
        public float FuseTime { get; }
        public float ThrowSpeed { get; }
        public string PrefabKey { get; }
        public string ExplodeSfxKey { get; }

        internal GrenadeDef(GrenadeDto d, DataLoadReport r)
        {
            Id = d.Id;
            Name = d.Name;
            Damage = d.Damage;
            Radius = d.Radius;
            FuseTime = d.FuseTime;
            ThrowSpeed = d.ThrowSpeed;
            PrefabKey = d.PrefabKey;
            ExplodeSfxKey = d.ExplodeSfxKey;

            r.Check(Damage > 0, Table, Id, "Damage 는 1 이상");
            r.Check(Radius > 0f, Table, Id, "Radius 는 0 초과");
            r.Check(FuseTime >= 0f, Table, Id, "FuseTime 은 0 이상");
        }
    }
}
