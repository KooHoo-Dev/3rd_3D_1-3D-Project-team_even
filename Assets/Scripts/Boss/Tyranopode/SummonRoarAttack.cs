using HybridShooter.Core.Contracts;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 포효 소환 기믹.
    ///  1) 포효 (광풍 연출·화면 흔들림)
    ///  2) 빛나는 포자를 하늘로 쏜다. 그동안 알 잡몹이 사방의 벽을 넘어 끝없이 몰려온다 (초당 SummonRate, 최대 SummonMaxAlive).
    ///     포자는 SummonInterval 마다 맥박치고, 보스는 외피가 굳어 무적 (분석 화면이 무적·약점 포자를 알려 준다)
    ///  3) 포자를 부수면: 강한 파동이 WaveCount 번 퍼지며 잡몹을 가까운 쪽부터 날려 보내고, 보스는 그로기
    ///  4) 시간 안에 못 부수면: 포자가 깜빡이며 내려와 터진다 (구르기로 피할 수 있는 큰 피해). 잡몹은 남는다
    ///  5) 이번에 부른 잡몹(포효 무리)이 살아 있는 동안 플레이어가 죽으면: 무리가 하나둘 쓰러지고, 부활 뒤 그 에너지가 보스로
    ///     빨려 들어가 보호막이 된다 (TyranopodeController.OnPlayerDied — 포자가 사라진 뒤라도 무리가 남아 있으면)
    /// 체력이 SummonStartHpRatio 이하가 된 뒤 쿨타임마다 가장 먼저 고른다. 머리 파괴: 몰려오는 속도 감소
    /// </summary>
    public sealed class SummonRoarAttack : TyranopodeAttack
    {
        private const float PriorityWeight = 1000f;

        [Tooltip("포자가 멈출 위치: 보스와 경기장 중심 사이 비율 (0 = 보스 위, 1 = 중심 위)")]
        [SerializeField, Range(0f, 1f)] private float hoverTowardCenter = 0.5f;
        [SerializeField, Min(0f)] private float recovery = 0.5f;
        [SerializeField] private CameraShakeSettings roarShake = CameraShakePresets.BossAppear;

        private SummonBeacon beacon;
        private float activeStartTime;
        private float nextPulseTime;
        private float spawnBudget;
        private bool failWarned;

        protected override float WindupTime => Data.SummonWindup;
        protected override float RecoveryTime => recovery;
        protected override float CooldownTime => Data.SummonCooldown;

        public override float GetWeight(float distance)
        {
            if (Data == null || IsReady == false || Boss.HpRatio > Data.SummonStartHpRatio) return 0f;
            return PriorityWeight;
        }

        protected override void StartWindup()
        {
            View.Play(TyranopodeView.Roar, 1f, 0.2f);
            Owner.PlayRoarVfx();
            Shake(roarShake, transform.position, 0f);
        }

        protected override void WindupTick(float deltaTime) => Move.Face(TargetPosition, 0.5f);

        protected override void OnActiveStarted()
        {
            Vector3 origin = Owner.Mouth.position;
            Vector3 hover = Vector3.Lerp(transform.position, Owner.Minions.Center, hoverTowardCenter);
            hover.y = transform.position.y + Data.BeaconHeight;

            beacon = Instantiate(Owner.BeaconPrefab, origin, Quaternion.identity);
            beacon.Launch(origin, hover, Data.BeaconHp);
            beacon.Destroyed += OnBeaconDestroyed;
            Owner.BeginSummonImmunity(beacon);

            View.Play(TyranopodeView.Idle, 0.6f, 0.3f);
            activeStartTime = Time.time;
            nextPulseTime = Time.time + 0.8f; // 떠오른 뒤 첫 맥박
            spawnBudget = 0f;
            failWarned = false;
        }

        protected override void OnActiveTick(float deltaTime)
        {
            float elapsed = Time.time - activeStartTime;

            if (Time.time >= nextPulseTime)
            {
                nextPulseTime = Time.time + Data.SummonInterval;
                beacon.Pulse();
                Owner.RaiseSoundCue(TyranopodeSoundCue.SporePulse, beacon.transform.position);
            }

            // 사방에서 끊임없이 (초당 SummonRate 마리를 프레임마다 나눠서)
            float rate = Data.SummonRate * (Owner.IsBroken(TyranopodePartType.Head) ? Data.HeadBrokenSummonMultiplier : 1f);
            spawnBudget += rate * deltaTime;
            int count = Mathf.FloorToInt(spawnBudget);
            if (count > 0)
            {
                spawnBudget -= count;
                Owner.Minions.SpawnAtWall(Data.SummonMinionIds, count, Data.SummonMaxAlive, TargetPosition);
            }

            if (failWarned == false && elapsed >= Data.BeaconDuration - Data.BeaconFailWarnTime)
            {
                failWarned = true;
                beacon.BeginFailWarning(Data.BeaconFailWarnTime);
            }

            if (elapsed >= Data.BeaconDuration) Fail();
        }

        private void Fail()
        {
            Vector3 point = beacon.transform.position;
            ReleaseBeacon(dismiss: true);
            Owner.PlayBeaconFailVfx(point);
            Owner.RaiseSoundCue(TyranopodeSoundCue.SporeFail, point);
            Shake(CameraShakePresets.Impact, point, 0f);
            HitPlayer(Data.BeaconFailDamage, point);
            // 이 폭발로 플레이어가 죽었다면 EndByPlayerDeath 가 이미 끝냈다
            if (Phase == SkillPhase.Active) Finish();
        }

        private void OnBeaconDestroyed(SummonBeacon destroyed)
        {
            Vector3 point = destroyed.transform.position;
            ReleaseBeacon(dismiss: false);
            Owner.RaiseSoundCue(TyranopodeSoundCue.SporeBreak, point);
            Owner.StartBeaconWaves(point);
            Finish();
            Owner.EnterGroggy(Data.GroggyTime);
        }

        /// <summary>포자가 떠 있는데 플레이어가 죽었다 (무리 흡수는 컨트롤러가 처리) → 기믹을 끝낸다</summary>
        public void EndByPlayerDeath()
        {
            if (Phase == SkillPhase.Active) Finish();
        }

        protected override void Stopped()
        {
            Owner.EndSummonImmunity();
            ReleaseBeacon(dismiss: true);
        }

        private void ReleaseBeacon(bool dismiss)
        {
            if (beacon == null) return;
            beacon.Destroyed -= OnBeaconDestroyed;
            if (dismiss) beacon.Dismiss();
            beacon = null;
        }
    }
}
