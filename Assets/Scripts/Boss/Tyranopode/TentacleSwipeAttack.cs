using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 앞 촉수 휘두르기 (근거리). 왼쪽·오른쪽 한 번, 2연타, 3연타 중 하나.
    ///  - 페이즈 2: 연타 위주. 앞 촉수 파괴: 3연타 없음, 피해 감소
    ///  - 예고: 보스 앞 부채꼴 (예고 동안 플레이어 쪽으로 돈다). 연타 사이에는 천천히 다시 조준
    /// </summary>
    public sealed class TentacleSwipeAttack : TyranopodeAttack
    {
        private enum Variant { Left, Right, Combo2, Combo3 }

        [Header("타격 진행도 (애니메이션 0~1)")]
        [SerializeField] private float[] singleHitTimes = { 0.45f };
        [SerializeField] private float[] combo2HitTimes = { 0.3f, 0.62f };
        [SerializeField] private float[] combo3HitTimes = { 0.22f, 0.48f, 0.74f };
        [SerializeField, Range(0f, 2f)] private float comboTurnSpeed = 0.4f;

        private Variant variant;
        private string state;
        private float[] hitTimes;
        private int hitIndex;
        private GroundTelegraph telegraph;

        protected override float WindupTime => Data.TentacleWindup;
        protected override float RecoveryTime => Data.TentacleRecovery;

        protected override bool MeetsStartCondition() => DistanceToTarget <= Data.TentacleRange + 2f;

        protected override void StartWindup()
        {
            variant = PickVariant();
            (state, hitTimes) = variant switch
            {
                Variant.Left => (TyranopodeView.TentacleLeft, singleHitTimes),
                Variant.Right => (TyranopodeView.TentacleRight, singleHitTimes),
                Variant.Combo2 => (TyranopodeView.Tentacle2Hit, combo2HitTimes),
                _ => (TyranopodeView.Tentacle3Hit, combo3HitTimes),
            };
            hitIndex = 0;

            View.Play(TyranopodeView.Idle, 1.4f);
            telegraph = Telegraph();
            telegraph.ShowSector(transform.position, transform.forward, Data.TentacleRange, Data.TentacleAngle, WindupDuration, DangerColor);
        }

        private float WindupDuration => Data.TentacleWindup * Owner.WindupScale;

        protected override void WindupTick(float deltaTime)
        {
            Move.Face(TargetPosition);
            telegraph.Place(transform.position, transform.forward);
        }

        protected override void OnActiveStarted()
        {
            // 예고 표시는 첫 타격 때 그 모양 그대로 터진다
            telegraph.Place(transform.position, transform.forward);
            View.Play(state, Owner.SpeedScale);
        }

        protected override void OnActiveTick(float deltaTime)
        {
            float n = View.NormalizedTime(state);
            if (variant >= Variant.Combo2) Move.Face(TargetPosition, comboTurnSpeed);

            if (PassedHit(n, hitTimes, ref hitIndex))
            {
                Owner.RaiseSoundCue(TyranopodeSoundCue.Swipe, transform.position);
                if (telegraph != null)
                {
                    Detonate(telegraph);
                    telegraph = null;
                }
                else FlashSector(transform.position, transform.forward, Data.TentacleRange, Data.TentacleAngle);

                if (PlayerInSector(transform.position, transform.forward, Data.TentacleRange, Data.TentacleAngle))
                {
                    float scale = Owner.IsBroken(TyranopodePartType.FrontTentacle) ? Data.TentacleBrokenDamageMultiplier : 1f;
                    HitPlayer(Mathf.RoundToInt(Data.TentacleDamage * scale), transform.position);
                }
            }

            if (n >= 1f) Finish();
        }

        private Variant PickVariant()
        {
            bool broken = Owner.IsBroken(TyranopodePartType.FrontTentacle);
            float roll = Random.value;
            if (Owner.IsPhase2)
                return roll < 0.55f && broken == false ? Variant.Combo3 : Variant.Combo2;
            if (roll < 0.25f) return Variant.Left;
            if (roll < 0.5f) return Variant.Right;
            if (roll < 0.85f || broken) return Variant.Combo2;
            return Variant.Combo3;
        }
    }
}
