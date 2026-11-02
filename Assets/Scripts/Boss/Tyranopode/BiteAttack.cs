using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 물기. 가까우면 제자리 물기, 중거리면 뛰어들며 물기(거리 좁히기). 턱이 부서지면 고르지 않는다 (촉수가 대신)
    ///  - 예고: 제자리 = 앞 부채꼴, 점프 = 착지할 자리까지 직사각형 + 착지 물기 원
    ///    (원 = 뛰어서 내려앉는 곳: 내려찍기와 같은 규칙. 직사각형만 있으면 돌진 예고와 구분되지 않는다)
    /// </summary>
    public sealed class BiteAttack : TyranopodeAttack
    {
        private const float BiteAngle = 70f;
        /// <summary>착지 원이 예고 뒤 공중 시간 동안 더 남는 여유(초)</summary>
        private const float LandingAirTime = 0.4f;

        [Header("타격 진행도 (애니메이션 0~1)")]
        [SerializeField, Range(0f, 1f)] private float biteHitTime = 0.42f;
        [SerializeField, Range(0f, 1f)] private float jumpStartTime = 0.15f;
        [SerializeField, Range(0f, 1f)] private float jumpLandTime = 0.5f;
        [Tooltip("점프 물기: 플레이어 이 거리 앞에 내려앉는다")]
        [SerializeField, Min(0f)] private float landShortOf = 4f;

        private bool jump;
        private bool hitDone;
        private Vector3 jumpFrom;
        private Vector3 jumpTo;
        private float jumpReach;
        private GroundTelegraph telegraph;
        private GroundTelegraph landingTelegraph;

        protected override float WindupTime => Data.BiteWindup;
        protected override float RecoveryTime => Data.BiteRecovery;

        protected override float WeightMultiplier()
        {
            if (Owner.IsBroken(TyranopodePartType.Head)) return 0f;
            return DistanceToTarget > Data.BiteRange + 1f && Owner.IsBroken(TyranopodePartType.LegTentacle) ? Data.LegBrokenJumpWeightMultiplier : 1f;
        }

        protected override bool MeetsStartCondition() => DistanceToTarget <= Data.JumpBiteMaxDistance;

        protected override void StartWindup()
        {
            jump = DistanceToTarget > Data.BiteRange + 1f;
            hitDone = false;
            View.Play(TyranopodeView.Idle, 1.4f);

            telegraph = Telegraph();
            landingTelegraph = null;
            float time = Data.BiteWindup * Owner.WindupScale;
            if (jump)
            {
                jumpReach = JumpDistance();
                telegraph.ShowRect(transform.position, transform.forward, Data.JumpBiteWidth, jumpReach, time, DangerColor);
                // 착지 물기 원: 예고 + 공중 시간까지 남긴다
                landingTelegraph = Telegraph();
                landingTelegraph.ShowCircle(BiteCenter(transform.position + transform.forward * jumpReach, transform.forward),
                    Data.JumpBiteHitRadius, time + LandingAirTime, DangerColor);
            }
            else telegraph.ShowSector(transform.position, transform.forward, Data.BiteRange, BiteAngle, time, DangerColor);
        }

        protected override void WindupTick(float deltaTime)
        {
            Move.Face(TargetPosition, 1.5f);
            telegraph.Place(transform.position, transform.forward);
            if (landingTelegraph != null)
                landingTelegraph.Place(BiteCenter(transform.position + transform.forward * jumpReach, transform.forward), transform.forward);
        }

        /// <summary>뛰어갈 거리: 플레이어 landShortOf 앞까지</summary>
        private float JumpDistance() => Mathf.Max(0f, DistanceToTarget - landShortOf * Owner.BodyScale);

        /// <summary>body 자리에 내려앉았을 때 무는 원의 중심 (몸 앞 JumpBiteHitRadius)</summary>
        private Vector3 BiteCenter(Vector3 body, Vector3 forward) => body + Flat(forward).normalized * Data.JumpBiteHitRadius;

        protected override void OnActiveStarted()
        {
            if (jump)
            {
                // 길 표시는 거두고 착지 원은 실제 착지점(FindLanding 이 당긴 자리)으로 옮겨 내려앉을 때까지 둔다
                HideTelegraph(telegraph);
                Move.FaceInstant(TargetPosition);
                jumpFrom = transform.position;
                jumpTo = Move.FindLanding(jumpFrom, jumpFrom + DirectionToTarget * JumpDistance());
                if (landingTelegraph != null) landingTelegraph.Place(BiteCenter(jumpTo, transform.forward), transform.forward);
                View.Play(TyranopodeView.JumpBite, Owner.SpeedScale);
            }
            else
            {
                View.Play(TyranopodeView.Bite, Owner.SpeedScale);
            }
        }

        protected override void OnActiveTick(float deltaTime)
        {
            string state = jump ? TyranopodeView.JumpBite : TyranopodeView.Bite;
            float n = View.NormalizedTime(state);

            if (jump && n >= jumpStartTime && n <= jumpLandTime)
            {
                float t = Mathf.InverseLerp(jumpStartTime, jumpLandTime, n);
                // 공중이라 NavMesh 의 틈·돌멩이에 걸리지 않게 직접 옮긴다
                Move.MoveFree(Vector3.Lerp(jumpFrom, jumpTo, t) - transform.position, airborne: true, out _);
            }

            float hitTime = jump ? jumpLandTime : biteHitTime;
            if (hitDone == false && n >= hitTime)
            {
                hitDone = true;
                if (jump) Move.EndFree();
                Vector3 mouth = transform.position + transform.forward * (jump ? Data.JumpBiteHitRadius : 0f);
                bool hit = jump
                    ? PlayerInCircle(mouth, Data.JumpBiteHitRadius)
                    : PlayerInSector(transform.position, transform.forward, Data.BiteRange, BiteAngle);
                if (jump && landingTelegraph != null)
                {
                    landingTelegraph.Place(mouth, transform.forward);
                    Detonate(landingTelegraph);
                    landingTelegraph = null;
                }
                else if (jump) FlashCircle(mouth, Data.JumpBiteHitRadius);
                else Detonate(telegraph);
                Owner.RaiseSoundCue(TyranopodeSoundCue.BiteImpact, mouth);
                if (hit) HitPlayer(Data.BiteDamage, transform.position);
                if (jump) Shake(CameraShakePresets.BossFootstep, transform.position, 40f);
            }

            if (n >= 1f) Finish();
        }

        protected override void Stopped() => Move.EndFree();
    }
}
