using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 점프 내려찍기 (중·원거리 → 가까이, 강공격). 예고 시작 때 플레이어 자리를 착지점으로 고정하고 원으로 표시 → 뛰어올라 내려찍는다.
    /// 착지 원 안의 알도 깨진다. 다리 촉수 파괴: 덜 고른다. 페이즈 2: 반경 증가
    /// </summary>
    public sealed class JumpSmashAttack : TyranopodeAttack
    {
        [Header("진행도 (애니메이션 0~1)")]
        [SerializeField, Range(0f, 1f)] private float jumpStartTime = 0.1f;
        [SerializeField, Range(0f, 1f)] private float impactTime = 0.47f;

        private Vector3 from;
        private Vector3 landing;
        private float plannedLeap;
        private GroundTelegraph telegraph;
        private bool impacted;

        protected override float WindupTime => Data.SmashWindup;
        protected override float RecoveryTime => Data.SmashRecovery;

        private float Radius => Data.SmashRadius * (Owner.IsPhase2 ? Data.Phase2SmashRadiusMultiplier : 1f);

        protected override float WeightMultiplier()
            => Owner.IsBroken(TyranopodePartType.LegTentacle) ? Data.LegBrokenJumpWeightMultiplier : 1f;

        protected override bool MeetsStartCondition() => DistanceToTarget >= Data.MeleeRange * 0.6f;

        protected override void StartWindup()
        {
            impacted = false;
            float leap = Mathf.Min(DistanceToTarget, Data.SmashMaxLeap);
            // 착지점은 NavMesh 위로 (바위 구멍·가파른 경사·맵 밖이면 보스 쪽으로 당긴다)
            landing = Move.FindLanding(transform.position, transform.position + DirectionToTarget * leap);
            plannedLeap = Flat(landing - transform.position).magnitude;
            View.Play(TyranopodeView.Idle, 1.6f);
            // 예고 + 공중 시간까지 채운다
            telegraph = Telegraph();
            telegraph.ShowCircle(landing, Radius, Data.SmashWindup * Owner.WindupScale + 0.4f, DangerColor);
        }

        protected override void WindupTick(float deltaTime) => Move.Face(landing, 1.5f);

        protected override void OnActiveStarted()
        {
            from = transform.position;
            Move.FaceInstant(landing);
            View.Play(TyranopodeView.JumpSmash, Owner.SpeedScale);
            Owner.RaiseSoundCue(TyranopodeSoundCue.JumpSmash, Owner.Mouth.position);
        }

        protected override void OnActiveTick(float deltaTime)
        {
            float n = View.NormalizedTime(TyranopodeView.JumpSmash);
            if (impacted == false && n >= jumpStartTime)
            {
                float t = Mathf.InverseLerp(jumpStartTime, impactTime, n);
                // 공중이라 NavMesh 의 틈·돌멩이에 걸리지 않게 직접 옮긴다
                Move.MoveFree(Vector3.Lerp(from, landing, t) - transform.position, airborne: true, out _);
            }

            if (impacted == false && n >= impactTime)
            {
                impacted = true;
                float leapt = Flat(transform.position - from).magnitude;
                if (plannedLeap > 3f && leapt < plannedLeap * 0.5f)
                    Debug.Log($"[Tyranopode] 내려찍기 도약이 짧음: {leapt:0.#}/{plannedLeap:0.#}m (출발 {from}, 착지 예정 {landing})", this);
                Move.EndFree();
                Detonate(telegraph);
                Owner.RaiseSoundCue(TyranopodeSoundCue.SmashImpact, landing);
                if (PlayerInCircle(landing, Radius)) HitPlayer(Data.SmashDamage, landing);
                Owner.BreakEggsNear(landing, Radius);
                Owner.PlayShockwave(landing, Radius);
                Shake(CameraShakePresets.Impact, landing, 50f);
            }

            if (n >= 1f) Finish();
        }

        protected override void Stopped() => Move.EndFree();
    }
}
