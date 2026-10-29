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
        /// <summary>시작할 때 가진 예비 탄창 개수</summary>
        public int StartMagazines { get; }
        /// <summary>가질 수 있는 예비 탄창 최대 개수 (탄창 단위. 탄창 안 탄약 MagazineSize 와 별개)</summary>
        public int MaxMagazines { get; }
        public float ReloadTime { get; }
        public float Range { get; }
        /// <summary>탄 퍼짐 원뿔 반각(도)</summary>
        public float SpreadAngle { get; }
        /// <summary>보급 1회에 채우는 예비 탄창 비율. MaxMagazines × 비율을 내림, 최소 1 (0 = 충전 안 함). 스플래시 무기는 작게</summary>
        public float SupplyMagazineRatio { get; }
        /// <summary>Link 단계에서 채워짐 (키가 비었으면 null)</summary>
        public SoundDef FireSfx { get; internal set; }
        /// <summary>장전 시작</summary>
        public SoundDef ReloadSfx { get; internal set; }
        /// <summary>탄창·탄 넣기 (기본 총 = 장전 중간, 산탄총 = 한 발마다)</summary>
        public SoundDef ReloadInsertSfx { get; internal set; }
        /// <summary>장전 완료 (노리쇠·펌프). 취소되면 재생하지 않음</summary>
        public SoundDef ReloadEndSfx { get; internal set; }
        /// <summary>이 무기로 바꿔 들 때</summary>
        public SoundDef SwitchSfx { get; internal set; }
        /// <summary>빈 탄창으로 쏠 때 (예비 탄창이 있으면 이어서 자동 장전)</summary>
        public SoundDef DryFireSfx { get; internal set; }
        /// <summary>투사체 폭발 (로켓·유탄). null 이면 프리팹 ExplosionSettings.sfxKey</summary>
        public SoundDef ExplodeSfx { get; internal set; }
        internal string FireSfxKey { get; }
        internal string ReloadSfxKey { get; }
        internal string ReloadInsertSfxKey { get; }
        internal string ReloadEndSfxKey { get; }
        internal string SwitchSfxKey { get; }
        internal string DryFireSfxKey { get; }
        internal string ExplodeSfxKey { get; }

        internal WeaponDef(WeaponDto d, DataLoadReport r)
        {
            Id = d.Id;
            Name = d.Name;
            FireMode = ParseUtil.Enum<FireMode>(d.FireMode, r, Table, Id, nameof(d.FireMode));
            Damage = d.Damage;
            FireRate = d.FireRate;
            FireInterval = d.FireRate > 0f ? 60f / d.FireRate : 0f;
            MagazineSize = d.MagazineSize;
            StartMagazines = d.StartMagazines;
            MaxMagazines = d.MaxMagazines;
            ReloadTime = d.ReloadTime;
            Range = d.Range;
            SpreadAngle = d.SpreadAngle;
            SupplyMagazineRatio = d.SupplyMagazineRatio;
            FireSfxKey = d.FireSfxKey;
            ReloadSfxKey = d.ReloadSfxKey;
            ReloadInsertSfxKey = d.ReloadInsertSfxKey;
            ReloadEndSfxKey = d.ReloadEndSfxKey;
            SwitchSfxKey = d.SwitchSfxKey;
            DryFireSfxKey = d.DryFireSfxKey;
            ExplodeSfxKey = d.ExplodeSfxKey;

            r.Check(Damage >= 0, Table, Id, "Damage 는 0 이상");
            r.Check(FireRate > 0f, Table, Id, "FireRate(RPM) 는 0 초과");
            r.Check(MagazineSize > 0, Table, Id, "MagazineSize 는 1 이상");
            r.Check(StartMagazines >= 0 && MaxMagazines >= StartMagazines, Table, Id, "StartMagazines 는 0 이상, MaxMagazines 는 StartMagazines 이상");
            r.Check(ReloadTime > 0f, Table, Id, "ReloadTime 은 0 초과");
            r.Check(Range > 0f, Table, Id, "Range 는 0 초과");
            r.Check(SupplyMagazineRatio >= 0f && SupplyMagazineRatio <= 1f, Table, Id, "SupplyMagazineRatio 는 0 ~ 1");
        }
    }
}
