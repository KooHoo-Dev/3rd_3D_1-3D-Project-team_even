using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 촉수 폭풍 (페이즈 2 기믹, 강공격). 페이즈 전환 직후 반드시 한 번, 이후 쿨타임마다.
    /// StormWaves 번: 플레이어 쪽 부채꼴을 예고(StormTelegraph) → 내려찍기. 부채꼴 밖으로 피하거나 구르기로 넘긴다
    /// </summary>
    public sealed class TentacleStormAttack : TyranopodeAttack
    {
        private const float StormWeight = 4f;

        [SerializeField] private CameraShakeSettings slamShake = CameraShakePresets.Impact;

        private int wavesDone;
        private float slamTime;
        private float nextWaveTime;
        private Vector3 sectorForward;
        private bool waitingSlam;
        private GroundTelegraph telegraph;

        protected override float WindupTime => Data.StormWindup;
        protected override float RecoveryTime => Data.StormRecovery;
        protected override float CooldownTime => Data.StormCooldown;

        public override float GetWeight(float distance)
            => Data != null && Owner.IsPhase2 && IsReady ? StormWeight : 0f;

        protected override void StartWindup()
        {
            View.Play(TyranopodeView.Roar, 1.3f, 0.2f);
            Owner.PlayRoarVfx();
            Shake(CameraShakePresets.BossAppear, transform.position, 0f);
        }

        protected override void WindupTick(float deltaTime) => Move.Face(TargetPosition);

        protected override void OnActiveStarted()
        {
            wavesDone = 0;
            BeginWave();
        }

        private void BeginWave()
        {
            waitingSlam = true;
            sectorForward = DirectionToTarget;
            Move.FaceInstant(TargetPosition);
            slamTime = Time.time + Data.StormTelegraph;
            telegraph = Telegraph();
            telegraph.ShowSector(transform.position, sectorForward, Data.StormRadius, Data.StormSectorAngle, Data.StormTelegraph, DangerColor);
            View.Play(TyranopodeView.Idle, 1.5f);
        }

        protected override void OnActiveTick(float deltaTime)
        {
            if (waitingSlam)
            {
                if (Time.time < slamTime) return;
                waitingSlam = false;
                Slam();
                wavesDone++;
                nextWaveTime = Time.time + Data.StormInterval;
                return;
            }

            if (Time.time < nextWaveTime) return;
            if (wavesDone >= Data.StormWaves) Finish();
            else BeginWave();
        }

        private void Slam()
        {
            Detonate(telegraph);
            View.Play(TyranopodeView.TentacleSmash, 1.6f, 0.05f);
            Vector3 center = transform.position;
            if (PlayerInSector(center, sectorForward, Data.StormRadius, Data.StormSectorAngle)) HitPlayer(Data.StormDamage, center);
            Shake(slamShake, center, Data.StormRadius * 2f);
        }
    }
}
