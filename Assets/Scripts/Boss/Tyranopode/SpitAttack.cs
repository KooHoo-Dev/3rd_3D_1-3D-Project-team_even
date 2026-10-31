using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 침 뱉기 (원거리). 입에서 산성 덩어리를 날린다. 페이즈 2: 부채꼴로 여러 발.
    /// 발광 주머니 파괴: 한 발만, 피해 감소
    /// </summary>
    public sealed class SpitAttack : TyranopodeAttack
    {
        [SerializeField, Range(0f, 1f)] private float fireTime = 0.4f;

        private bool fired;

        protected override float WindupTime => Data.SpitWindup;
        protected override float RecoveryTime => Data.SpitRecovery;

        protected override bool MeetsStartCondition() => DistanceToTarget >= Data.MeleeRange;

        protected override void StartWindup()
        {
            fired = false;
            View.Play(TyranopodeView.Idle, 1.2f);
        }

        protected override void WindupTick(float deltaTime) => Move.Face(TargetPosition, 1.5f);

        protected override void OnActiveStarted() => View.Play(TyranopodeView.Spit, Owner.SpeedScale);

        protected override void OnActiveTick(float deltaTime)
        {
            Move.Face(TargetPosition);
            float n = View.NormalizedTime(TyranopodeView.Spit);
            if (fired == false && n >= fireTime)
            {
                fired = true;
                Fire();
            }
            if (n >= 1f) Finish();
        }

        private void Fire()
        {
            bool sacBroken = Owner.IsBroken(TyranopodePartType.Sac);
            int count = sacBroken ? 1 : Owner.IsPhase2 ? Data.Phase2SpitCount : 1;
            int damage = Mathf.RoundToInt(Data.SpitDamage * (sacBroken ? Data.SacBrokenSpitDamageMultiplier : 1f) * Owner.DamageScale);

            Vector3 origin = Owner.Mouth.position;
            Vector3 aim = (TargetChest - origin).normalized;
            for (int i = 0; i < count; i++)
            {
                float yaw = count == 1 ? 0f : Mathf.Lerp(-Data.SpitSpreadAngle, Data.SpitSpreadAngle, i / (count - 1f));
                Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * aim;
                var spit = Instantiate(Owner.SpitPrefab, origin, Quaternion.LookRotation(dir));
                spit.Launch(Owner, dir * Data.SpitSpeed, damage, Data.SpitRadius);
            }
        }
    }
}
