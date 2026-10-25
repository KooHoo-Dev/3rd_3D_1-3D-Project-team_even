using System;
using HybridShooter.Core.Data;
using UnityEngine;

namespace HybridShooter.Weapon
{
    public enum RifleFireMode { Single, Burst, Auto }

    /// <summary>
    /// 라이플 — 기본 주무기, 조정간(사격 모드 전환). (참고: 배틀필드·배틀그라운드 조정간, CoD 점사 소총)
    ///  - B 로 연사 → 점사 → 단발 순환. 시작 모드는 WeaponTable.FireMode
    ///  - 점사: 한 번 누르면 burstCount 발이 끝까지 나가고(손을 떼도), 다음 점사까지 burstCooldown 대기. 반동이 작다
    ///  - 단발: 누를 때마다 한 발, 연사보다 정확
    /// </summary>
    public class Rifle : Gun
    {
        [Header("라이플")]
        [Tooltip("B 로 순환할 사격 모드")]
        [SerializeField] private RifleFireMode[] availableModes = { RifleFireMode.Auto, RifleFireMode.Burst, RifleFireMode.Single };
        [SerializeField] private int burstCount = 3;
        [Tooltip("점사가 끝난 뒤 다음 점사까지 추가 대기")]
        [SerializeField] private float burstCooldown = 0.25f;
        [SerializeField, Range(0f, 1f)] private float burstRecoilMultiplier = 0.6f;
        [Tooltip("단발 모드 퍼짐 배율")]
        [SerializeField, Range(0f, 1f)] private float singleSpreadMultiplier = 0.6f;

        private int modeIndex;
        private int burstRemaining;

        /// <summary>UI 사격 모드 표시용</summary>
        public event Action<RifleFireMode> FireModeChanged;
        public RifleFireMode FireMode => availableModes.Length > 0 ? availableModes[modeIndex] : RifleFireMode.Auto;

        public Rifle()
        {
            bloomPerShot = 0.2f;
            maxBloom = 2.5f;
            moveSpreadPenalty = 1.5f;
            aimSpreadMultiplier = 0.35f;
            recoilPitch = 0.55f;
            recoilYaw = 0.25f;
            aimFovMultiplier = 0.75f;
            aimMoveSpeedMultiplier = 0.7f;
        }

        public override float CurrentSpread =>
            FireMode == RifleFireMode.Single ? base.CurrentSpread * singleSpreadMultiplier : base.CurrentSpread;

        protected override void OnInitialized()
        {
            var start = Def != null && Def.FireMode == Core.Data.FireMode.Single ? RifleFireMode.Single : RifleFireMode.Auto;
            int index = Array.IndexOf(availableModes, start);
            modeIndex = index >= 0 ? index : 0;
        }

        public override bool ToggleFireMode()
        {
            if (availableModes.Length < 2) return false;
            burstRemaining = 0;
            modeIndex = (modeIndex + 1) % availableModes.Length;
            FireModeChanged?.Invoke(FireMode);
            Debug.Log($"{DisplayName} 사격 모드: {FireMode}");
            return true;
        }

        protected override void OnTriggerDown()
        {
            if (FireMode != RifleFireMode.Burst)
            {
                TryFire();
                return;
            }
            if (burstRemaining > 0 || TryFire() == false) return;
            burstRemaining = burstCount - 1;
            if (burstRemaining <= 0) DelayNextShot(burstCooldown);
        }

        protected override void OnTick(float dt)
        {
            switch (FireMode)
            {
                case RifleFireMode.Auto:
                    if (IsTriggerHeld) TryFire();
                    break;
                case RifleFireMode.Burst:
                    if (burstRemaining <= 0) break;
                    if (Ammo <= 0 || IsReloading) { burstRemaining = 0; break; }
                    if (TryFire() && --burstRemaining == 0) DelayNextShot(burstCooldown);
                    break;
            }
        }

        protected override void OnUnequipped() => burstRemaining = 0;

        protected override Vector2 GetRecoilKick() =>
            FireMode == RifleFireMode.Burst ? base.GetRecoilKick() * burstRecoilMultiplier : base.GetRecoilKick();

        protected override void Shoot() => FireBullet(SpreadDirection());
    }
}
