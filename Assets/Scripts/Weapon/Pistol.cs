using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 권총 — 반자동 보조 무기. (참고: CoD·발로란트 권총)
    ///  - 누를 때마다 한 발. 빨리 꺼낸다 → 주무기 탄이 떨어졌을 때 바꿔 들어 마무리하는 용도
    ///  - 연타하면 퍼짐이 빠르게 커지고, 잠깐 쉬면 금방 정확해진다 (리듬 있게 쏘면 정확)
    ///  - 입력 버퍼: 연사 간격보다 살짝 빨리 눌러도 기억했다가 쏜다 (클릭이 씹히는 느낌 방지)
    ///  - 예비 탄창 무한 (모든 탄이 떨어져도 끝까지 싸울 수 있게)
    /// </summary>
    public class Pistol : Gun
    {
        [Header("권총")]
        [Tooltip("쏠 수 없을 때 누른 클릭을 이 시간 동안 기억했다가 쏜다")]
        [SerializeField] private float inputBufferTime = 0.12f;

        private float bufferedUntil = float.NegativeInfinity;

        public Pistol()
        {
            infiniteMagazines = true;
            equipTime = 0.2f;
            bloomPerShot = 0.6f;
            maxBloom = 3f;
            bloomRecovery = 10f;
            moveSpreadPenalty = 1f;
            airSpreadPenalty = 2f;
            aimSpreadMultiplier = 0.5f;
            recoilPitch = 1.2f;
            recoilYaw = 0.3f;
            recoilRecoverRatio = 0.8f;
            recoilRecoverSpeed = 20f;
            aimFovMultiplier = 0.85f;
            aimMoveSpeedMultiplier = 0.9f;
        }

        protected override float MagazineUpgradeRate => 0.5f;

        protected override void OnTriggerDown()
        {
            if (TryFire() == false) bufferedUntil = Time.time + inputBufferTime;
        }

        protected override void OnTick(float dt)
        {
            if (bufferedUntil >= Time.time && IsReloading == false && TryFire())
                bufferedUntil = float.NegativeInfinity;
        }

        protected override void OnUnequipped() => bufferedUntil = float.NegativeInfinity;

        protected override void Shoot() => FireBullet(SpreadDirection());
    }
}
