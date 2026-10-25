using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 기관단총 — 근거리 완전 자동. (참고: CoD·배틀그라운드 SMG)
    ///  - 누르고 있는 동안 아주 빠르게 연사
    ///  - 뛰면서·지향 사격(hip fire)에 강함: 이동 퍼짐이 작고, 조준해도 이득이 적다
    ///  - 거리 감쇠가 크다 → 멀리서는 약하다
    ///  - 반동은 위로 작게, 좌우로 많이 흔들린다
    /// </summary>
    public class SMG : Gun
    {
        [Header("기관단총")]
        [Tooltip("이 거리까지는 100% 피해")]
        [SerializeField] private float falloffStart = 10f;
        [Tooltip("사거리 끝에서의 피해 비율")]
        [SerializeField, Range(0f, 1f)] private float minDamageRatio = 0.5f;

        public SMG()
        {
            equipTime = 0.25f;
            bloomPerShot = 0.12f;
            maxBloom = 2f;
            bloomRecovery = 8f;
            moveSpreadPenalty = 0.5f;
            airSpreadPenalty = 1.5f;
            aimSpreadMultiplier = 0.7f;
            recoilPitch = 0.25f;
            recoilYaw = 0.35f;
            recoilRecoverRatio = 0.7f;
            aimFovMultiplier = 0.85f;
            aimMoveSpeedMultiplier = 0.9f;
        }

        protected override void OnTick(float dt)
        {
            if (IsTriggerHeld) TryFire();
        }

        protected override int DamageAtDistance(float distance) =>
            FalloffDamage(Stats.damage, distance, falloffStart, Stats.range, minDamageRatio);

        protected override void Shoot() => FireBullet(SpreadDirection());
    }
}
