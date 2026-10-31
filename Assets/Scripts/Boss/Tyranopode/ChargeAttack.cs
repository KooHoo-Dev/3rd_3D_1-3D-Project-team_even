using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 돌진 (강공격). 예고 동안 플레이어 쪽으로 돌며 직사각형 표시 → 그 방향으로 빠르게 기어간다.
    ///  - 한 번만 맞힌다. 지나가는 길의 알은 깨진다
    ///  - 벽(NavMesh 가장자리)에 막히면 그로기 (다리 촉수 파괴 시 더 길게)
    ///  - 페이즈 2: Phase2ChargeCount 번 연속 (사이에 짧게 다시 조준)
    /// </summary>
    public sealed class ChargeAttack : TyranopodeAttack
    {
        [SerializeField, Min(0.1f)] private float crawlPlaySpeed = 2.2f;
        [SerializeField, Min(0f)] private float reaimTime = 0.6f;
        [Tooltip("이 비율보다 덜 움직인 프레임이 이어지면 벽에 막힌 것")]
        [SerializeField, Range(0.05f, 0.9f)] private float blockedRatio = 0.35f;
        [SerializeField, Min(0f)] private float blockedTime = 0.12f;

        private Vector3 direction;
        private float traveled;
        private float blockedFor;
        private bool hitDone;
        private int chargesLeft;
        private float reaimEndTime;
        private GroundTelegraph telegraph;

        protected override float WindupTime => Data.ChargeWindup;
        protected override float RecoveryTime => Data.ChargeRecovery;

        protected override bool MeetsStartCondition() => DistanceToTarget >= 8f;

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
            traveled = 0f;
            blockedFor = 0f;
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
            float moved = Move.MoveDelta(direction * step);
            traveled += moved;

            Vector3 front = transform.position + direction * 3f;
            if (hitDone == false && PlayerInCircle(front, Data.ChargeHitRadius))
            {
                hitDone = true;
                HitPlayer(Data.ChargeDamage, transform.position);
            }
            Owner.BreakEggsNear(front, Data.ChargeHitRadius);

            // 벽에 막힘 → 그로기
            blockedFor = moved < step * blockedRatio ? blockedFor + deltaTime : 0f;
            if (blockedFor >= blockedTime && traveled > 2f)
            {
                Owner.PlayImpactVfx(front, Data.ChargeHitRadius);
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
    }
}
