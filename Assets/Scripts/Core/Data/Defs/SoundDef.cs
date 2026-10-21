using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;

namespace HybridShooter.Core.Data.Defs
{
    public sealed class SoundDef
    {
        public const string Table = "SoundTable";

        public string Key { get; }
        public SoundCategory Category { get; }
        /// <summary>Resources.Load 경로 (확장자 제외)</summary>
        public string Path { get; }
        public float Volume { get; }
        public bool Loop { get; }

        internal SoundDef(SoundDto d, DataLoadReport r)
        {
            Key = d.Key;
            Category = ParseUtil.Enum<SoundCategory>(d.Category, r, Table, Key, nameof(d.Category));
            Path = d.Path;
            Volume = d.Volume;
            Loop = d.Loop;

            r.Check(Volume >= 0f && Volume <= 1f, Table, Key, "Volume 은 0~1");
        }
    }
}
