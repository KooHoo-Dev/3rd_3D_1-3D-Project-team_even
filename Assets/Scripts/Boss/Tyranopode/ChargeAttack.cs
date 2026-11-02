using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 돌진 (강공격). 예고 동안 플레이어 쪽으로 돌며 직사각형 표시 → 그 방향으로 빠르게 기어간다.
    ///  - 한 번만 맞힌다. 지나가는 길의 알은 깨진다
    ///  - 몸통이 실제 벽에 정면으로 박으면 그로기 (다리 촉수 파괴 시 더 길게). 비스듬히 스치면 벽을 따라 미끄러진다.
    ///    경기장 원(어림값)에는 막히지 않는다
    ///    NavMesh 의 작은 틈·낮은 돌멩이는 타고 넘고, 근처에 NavMesh 가 없는 낭떠러지에서는 그로기 없이 멈춘다 (TyranopodeLocomotion.MoveFree)
    ///  - 고를 때·출발 직전에 앞길(clearPathDistance)을 미리 본다: 바로 막힐 길이면 고르지 않고, 출발 직전에 막혔으면 달리지 않고 끝낸다 (콤보 없이)
    ///  - 페이즈 2: Phase2ChargeCount 번 연속 (사이에 짧게 다시 조준)
    /// </summary>
    public sealed class ChargeAttack : TyranopodeAttack
    {
        [SerializeField, Min(0.1f)] private float crawlPlaySpeed = 2.2f;
        [SerializeField, Min(0f)] private float reaimTime = 0.6f;
        [Tooltip("출발 직후 이 거리(m)보다 덜 달렸을 때 막히면 부딪힌 게 아니라 그냥 멈춘다")]
        [SerializeField, Min(0f)] private float minImpactDistance = 2f;
        [Tooltip("플레이어 쪽으로 이 거리(m) 안에 정면으로 박을 벽·낭떠러지가 있으면 돌진을 고르지 않는다 (출발 직전에도 다시 본다)")]
        [SerializeField, Min(0f)] private float clearPathDistance = 8f;

        private Vector3 direction;
        private float traveled;
        private bool hitDone;
        private int chargesLeft;
        private float reaimEndTime;
        private GroundTelegraph telegraph;

        protected override float WindupTime => Data.ChargeWindup;
        protected override float RecoveryTime => Data.ChargeRecovery;

        // 바위 뒤의 플레이어처럼 출발하자마자 막힐 길이면 고르지 않는다 (다른 패턴이 골라진다)
        protected override bool MeetsStartCondition()
            => DistanceToTarget >= 8f && Move.ProbePath(DirectionToTarget, clearPathDistance) == TyranopodeLocomotion.MoveStop.None;

        protected override void StartWindup()
        {
            chargesLeft = Owner.IsPhase2 ? Data.Phase2ChargeCount : 1;
            View.Play(TyranopodeView.Idle, 1.8f);
            telegraph = Telegraph();
            telegraph.ShowRect(transform.position, transform.forward, Data.ChargeHitRadius * 2f, Data.ChargeMaxDistance,
                Data.ChargeWindup * Owner.WindupScale, DangerColor);
        }

        protected override void WindupTick(float deltaTime)
        {
            Move.Face(TargetPosition, 1.5f);
            telegraph.Place(transform.position, transform.forward);
        }

        protected override void OnActiveStarted() => BeginRun();

        private void BeginRun()
        {
            HideTelegraphs();
            Move.FaceInstant(TargetPosition);
            direction = transform.forward;
            // 예고·재조준 동안 돌아서 길이 막혔으면 출발하지 않는다
            var blocked = Move.ProbePath(direction, clearPathDistance);
            if (blocked != TyranopodeLocomotion.MoveStop.None)
            {
                Debug.Log($"[Tyranopode] 돌진 취소: 출발 방향 {clearPathDistance:0.#}m 안이 막힘 ({blocked})", this);
                EndWithoutCombo();
                return;
            }
            traveled = 0f;
            hitDone = false;
            reaimEndTime = 0f;
            chargesLeft--;
            View.Play(TyranopodeView.CrawlForward, crawlPlaySpeed * Owner.SpeedScale, 0.08f);
        }

        protected override void OnActiveTick(float deltaTime)
        {
            if (reaimEndTime > 0f)
            {
                Move.Face(TargetPosition, 2f);
                telegraph.Place(transform.position, transform.forward);
                if (Time.time >= reaimEndTime) BeginRun();
                return;
            }

            float step = Data.ChargeSpeed * Owner.SpeedScale * deltaTime;
            var stop = Move.MoveFree(direction * step, airborne: false, out float moved);
            traveled += moved;

            Vector3 front = transform.position + direction * 3f;
            if (hitDone == false && PlayerInCircle(front, Data.ChargeHitRadius))
            {
                hitDone = true;
                HitPlayer(Data.ChargeDamage, transform.position);
            }
            Owner.BreakEggsNear(front, Data.ChargeHitRadius);

            if (stop != TyranopodeLocomotion.MoveStop.None)
            {
                string what = stop == TyranopodeLocomotion.MoveStop.Wall && Move.LastWallCollider != null
                    ? $" — 벽: {Move.LastWallCollider.name} ({Move.LastWallPoint})"
                    : "";
                Debug.Log($"[Tyranopode] 돌진 멈춤: {stop} ({traveled:0.#}m){what}", Move.LastWallCollider != null ? Move.LastWallCollider : this);
                // 낭떠러지·출발 직후는 부딪힌 게 아니다
                if (stop == TyranopodeLocomotion.MoveStop.Edge || traveled < minImpactDistance)
                {
                    EndWithoutCombo();
                    return;
                }
                // 벽·경기장 경계에 박힘 → 그로기
                Owner.PlayImpactVfx(front, Data.ChargeHitRadius);
                Owner.RaiseSoundCue(TyranopodeSoundCue.WallHit, front);
                Shake(CameraShakePresets.Impact, front, 60f);
                float groggy = Data.WallGroggyTime * (Owner.IsBroken(TyranopodePartType.LegTentacle) ? Data.LegBrokenWallGroggyMultiplier : 1f);
                Finish();
                Owner.EnterGroggy(groggy);
                return;
            }

            if (traveled < Data.ChargeMaxDistance) return;

            if (chargesLeft > 0)
            {
                // 다음 돌진: 잠깐 멈춰 다시 조준
                reaimEndTime = Time.time + reaimTime * Owner.WindupScale;
                View.Play(TyranopodeView.Idle, 1.8f);
                telegraph = Telegraph();
                telegraph.ShowRect(transform.position, transform.forward, Data.ChargeHitRadius * 2f, Data.ChargeMaxDistance,
                    reaimTime * Owner.WindupScale, DangerColor);
                return;
            }
            Finish();
        }

        /// <summary>제대로 달리지 못하고 끝남 (막힌 출발·낭떠러지): 곧바로 다음 공격이 이어 붙지 않게 한다 (착지 원 등이 갑자기 뜨지 않게)</summary>
        private void EndWithoutCombo()
        {
            Owner.SkipNextCombo();
            Finish();
        }

        protected override void Stopped() => Move.EndFree();
    }
}
