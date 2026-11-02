using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 침 연사 (원거리). 산성 담즙 주머니를 SpitInterval 간격으로 SpitCount 발, 쏘는 순간의 플레이어 자리에 포물선으로 떨어뜨린다.
    /// 떨어질 자리마다 예고 원이 비행 시간(SpitFlightTime) 동안 차오른다 → 계속 움직이면 피할 수 있다 (앞질러 노리지 않는다).
    /// 페이즈 2: Phase2SpitBonus 발 더, 간격 Phase2SpitInterval. 머리 파괴: HeadBrokenSpitReduce 발 덜. 발광 주머니 파괴: 피해 감소
    /// </summary>
    public sealed class SpitAttack : TyranopodeAttack
    {
        private static readonly Color AcidColor = new Color(0.65f, 1f, 0.2f, 0.45f);

        [Tooltip("첫 발을 뱉는 진행도 (Spit 애니메이션 0~1). 다음 발마다 이 자세부터 다시 재생해 고개를 튕긴다")]
        [SerializeField, Range(0f, 1f)] private float fireTime = 0.4f;
        [Tooltip("마지막 발 뒤 공격을 끝내기까지(초)")]
        [SerializeField, Min(0f)] private float afterLastShot = 0.35f;

        private bool started;
        private int shotsLeft;
        private float nextShotTime;

        protected override float WindupTime => Data.SpitWindup;
        protected override float RecoveryTime => Data.SpitRecovery;

        protected override bool MeetsStartCondition() => DistanceToTarget >= Data.MeleeRange;

        private int ShotCount => Data.SpitCount
                                 + (Owner.IsPhase2 ? Data.Phase2SpitBonus : 0)
                                 - (Owner.IsBroken(TyranopodePartType.Head) ? Data.HeadBrokenSpitReduce : 0);

        private float Interval => Owner.IsPhase2 ? Data.Phase2SpitInterval : Data.SpitInterval;

        protected override void StartWindup()
        {
            started = false;
            View.Play(TyranopodeView.Idle, 1.2f);
        }

        protected override void WindupTick(float deltaTime) => Move.Face(TargetPosition, 1.5f);

        protected override void OnActiveStarted()
        {
            shotsLeft = ShotCount;
            View.Play(TyranopodeView.Spit, Owner.SpeedScale);
        }

        protected override void OnActiveTick(float deltaTime)
        {
            Move.Face(TargetPosition);
            if (started == false)
            {
                if (View.NormalizedTime(TyranopodeView.Spit) < fireTime) return;
                started = true;
                FireOne();
                return;
            }

            if (shotsLeft > 0)
            {
                if (Time.time < nextShotTime) return;
                // 뱉는 자세부터 다시: 발마다 고개를 튕긴다
                View.PlayFrom(TyranopodeView.Spit, fireTime, Owner.SpeedScale * 1.3f);
                FireOne();
                return;
            }
            if (Time.time >= nextShotTime - Interval + afterLastShot) Finish();
        }

        private void FireOne()
        {
            shotsLeft--;
            nextShotTime = Time.time + Interval;
            Owner.RaiseSoundCue(TyranopodeSoundCue.Spit, Owner.Mouth.position);

            bool sacBroken = Owner.IsBroken(TyranopodePartType.Sac);
            int damage = Mathf.RoundToInt(Data.SpitDamage * (sacBroken ? Data.SacBrokenSpitDamageMultiplier : 1f) * Owner.DamageScale);

            // 쏘는 순간 플레이어가 선 바닥
            Vector3 target = TargetPosition;
            target.y = TyranopodeGround.Height(target, target.y);
            var marker = Owner.RentTelegraph();
            marker.ShowCircle(target, Data.SpitRadius, Data.SpitFlightTime, AcidColor);

            Vector3 origin = Owner.Mouth.position;
            var spit = Instantiate(Owner.SpitPrefab, origin, Quaternion.LookRotation(Flat(target - origin) + Vector3.up));
            spit.Launch(Owner, target, Data.SpitFlightTime, Data.SpitArcHeight, damage, Data.SpitRadius, marker);
        }
    }
}
