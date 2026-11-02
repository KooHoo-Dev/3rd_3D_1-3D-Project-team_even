using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 촉수 폭풍 (페이즈 2 기믹, 강공격). 페이즈 전환 직후 반드시 한 번, 이후 쿨타임마다.
    /// 촉수를 길게 늘여(TyranopodeTentacleSweep) 좌·우 번갈아 StormWaves 번 바닥을 낮게 쓸어버린다.
    ///  - 예고(StormTelegraph): 쓸 쪽 촉수가 들리며 늘어나고, 바닥 부채꼴이 쓸고 올 방향으로 차오른다
    ///  - 쓸기(StormSweepTime): 촉수가 StormSectorAngle 만큼 지나간다. 판정은 보이는 촉수·채찍 끝 그대로
    ///    (보스 → 끝 선이 플레이어를 지나가면 맞음). 점프로 StormJumpClear 이상 떠 있거나, 구르기 무적이거나, 끝보다 멀면 안 맞는다
    ///  - 사거리: 예고 때 플레이어 거리 + StormChaseMargin (StormRadius ~ StormMaxRadius) → 뒤로 물러나서는 못 벗어난다
    ///  - 사이 간격(StormInterval): 촉수를 거두고 착지할 틈
    /// 쓸기는 항상 보스가 바라보는 쪽(플레이어 쪽) 반원에서 시작한다 → 화면 밖에서 오지 않는다.
    /// 처음 쓰는 촉수 폭풍: 첫 쓸기 예고를 StormFirstExtraTelegraph 만큼 늘이고 분석 화면이 회피 요령을 알려 준다.
    /// 점프로 한 번 넘기기 전까지는 쓸기마다 화면 아래 회피 안내를 띄우고, 촉수가 닿기 JumpCueLead 초 전에 JUMP! 신호를 준다
    /// (넘기면 학습 완료 → 이번 실행 동안 다시 띄우지 않는다)
    /// </summary>
    public sealed class TentacleStormAttack : TyranopodeAttack
    {
        private const float StormWeight = 4f;
        private const float DustInterval = 0.06f;
        private const float JumpCueLead = 0.3f;

        /// <summary>이번 실행에서 점프로 촉수를 넘겨 본 적이 있나 (회피 안내를 그만 띄운다)</summary>
        private static bool jumpLearned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => jumpLearned = false;

        private enum Stage { Raise, Sweep, Retract }

        [SerializeField] private CameraShakeSettings slamShake = CameraShakePresets.Impact;
        [Tooltip("쓸기 예고 동안 재생할 촉수 휘두르기 속도 (애니메이션은 분위기만, 실제 촉수 자세는 TyranopodeTentacleSweep 이 정한다)")]
        [SerializeField, Min(0.05f)] private float swingPlaySpeed = 0.6f;

        private Stage stage;
        private float stageStart;
        private float stageTime;
        private int sweepsDone;
        private bool fromLeft;
        private bool hitDone;
        private bool cued;
        private bool firstStorm = true;
        private float prevAngle;
        private float reach;
        private float nextDustTime;
        private Vector3 forward;
        private GroundTelegraph telegraph;

        protected override float WindupTime => Data.StormWindup;
        protected override float RecoveryTime => Data.StormRecovery;
        protected override float CooldownTime => Data.StormCooldown;

        private TyranopodeTentacleSweep Tentacle => Owner.TentacleSweep;
        private float HalfAngle => Data.StormSectorAngle * 0.5f;

        public override float GetWeight(float distance)
            => Data != null && Owner.IsPhase2 && IsReady ? StormWeight : 0f;

        protected override void StartWindup()
        {
            View.Play(TyranopodeView.Roar, 1.3f, 0.2f);
            Owner.PlayRoarVfx();
            Shake(CameraShakePresets.BossAppear, transform.position, 0f);
            if (firstStorm) Owner.ShowStormHint();
        }

        protected override void WindupTick(float deltaTime) => Move.Face(TargetPosition);

        protected override void OnActiveStarted()
        {
            sweepsDone = 0;
            // 첫 쓸기는 플레이어가 덜 움직여도 되는 쪽부터: 플레이어가 보스 기준 오른쪽에 있으면 왼쪽에서 시작
            fromLeft = Vector3.SignedAngle(transform.forward, Flat(TargetPosition - transform.position), Vector3.up) >= 0f;
            BeginRaise();
        }

        private void BeginRaise()
        {
            Move.FaceInstant(TargetPosition);
            forward = transform.forward;
            reach = SweepReach();
            hitDone = false;
            cued = false;
            if (jumpLearned == false) Owner.ShowEvadePrompt();
            float time = Data.StormTelegraph + (firstStorm && sweepsDone == 0 ? Data.StormFirstExtraTelegraph : 0f);
            SetStage(Stage.Raise, time);

            Tentacle.Raise(fromLeft ? TyranopodeTentacleSweep.Side.Left : TyranopodeTentacleSweep.Side.Right,
                Owner.IsBroken(TyranopodePartType.FrontTentacle));
            telegraph = Telegraph();
            telegraph.ShowSweep(transform.position, forward, reach, Data.StormSectorAngle, time, DangerColor, fromLeft);
            View.Play(fromLeft ? TyranopodeView.TentacleLeft : TyranopodeView.TentacleRight, swingPlaySpeed, 0.2f);
        }

        protected override void OnActiveTick(float deltaTime)
        {
            float k = Mathf.Clamp01((Time.time - stageStart) / stageTime);
            float startAngle = fromLeft ? -HalfAngle : HalfAngle;
            float endAngle = -startAngle;

            switch (stage)
            {
                case Stage.Raise:
                    // 시작 쪽으로 들어 올리며 늘어난다 (끝 무렵 다 늘어남)
                    Tentacle.SetAim(Direction(startAngle), reach * Mathf.SmoothStep(0.3f, 1f, k), Mathf.SmoothStep(0f, 1f, k * 1.5f), k);
                    if (k >= 1f) BeginSweep(startAngle);
                    return;

                case Stage.Sweep:
                {
                    float angle = Mathf.Lerp(startAngle, endAngle, Mathf.SmoothStep(0f, 1f, k));
                    Tentacle.SetAim(Direction(angle), reach, 1f, 1f);
                    CueJump(prevAngle, angle, k);
                    CheckHit(prevAngle, angle);
                    prevAngle = angle;
                    if (Time.time >= nextDustTime)
                    {
                        nextDustTime = Time.time + DustInterval;
                        Owner.PlaySweepDust(Tentacle.WhipEnd);
                    }
                    if (k >= 1f) BeginRetract();
                    return;
                }

                default:
                    Tentacle.SetAim(Direction(endAngle), reach * (1f - k * 0.7f), 1f - k, 1f - k);
                    if (k < 1f) return;
                    Tentacle.Release();
                    fromLeft = !fromLeft;
                    if (sweepsDone >= Data.StormWaves)
                    {
                        firstStorm = false;
                        Finish();
                    }
                    else BeginRaise();
                    return;
            }
        }

        private void BeginSweep(float startAngle)
        {
            HideTelegraphs();
            SetStage(Stage.Sweep, Data.StormSweepTime);
            prevAngle = startAngle;
            nextDustTime = 0f;
            View.Play(fromLeft ? TyranopodeView.TentacleLeft : TyranopodeView.TentacleRight, 1.4f, 0.05f);
            Shake(slamShake, transform.position, reach * 2f);
            Owner.RaiseSoundCue(TyranopodeSoundCue.StormSweep, transform.position);
        }

        private void BeginRetract()
        {
            sweepsDone++;
            SetStage(Stage.Retract, Data.StormInterval);
            View.Play(TyranopodeView.Idle, 1.2f, 0.3f);
        }

        private void SetStage(Stage next, float time)
        {
            stage = next;
            stageStart = Time.time;
            stageTime = Mathf.Max(0.01f, time);
        }

        /// <summary>
        /// 이번 쓸기 사거리: 플레이어 거리 + StormChaseMargin (StormRadius ~ StormMaxRadius) → 예고 동안 뒤로 물러나도 닿는다.
        /// 경기장이 있으면 보스에서 가장 먼 벽까지만 (그보다 멀리 뻗을 필요가 없다)
        /// </summary>
        private float SweepReach()
        {
            float distance = Flat(TargetPosition - transform.position).magnitude;
            float value = Mathf.Clamp(distance + Data.StormChaseMargin, Data.StormRadius, Data.StormMaxRadius);
            var arena = Owner.Minions;
            if (arena != null && arena.HasBounds)
                value = Mathf.Min(value, Flat(arena.Center - transform.position).magnitude + arena.FarRadius);
            return Mathf.Max(Data.StormRadius, value);
        }

        /// <summary>보스 정면에서 angle 도 돌린 수평 방향 (음수 = 왼쪽)</summary>
        private Vector3 Direction(float angle) => Quaternion.AngleAxis(angle, Vector3.up) * forward;

        /// <summary>회피 안내 중: 촉수가 지금 빠르기로 JumpCueLead 초 안에 플레이어 각도에 닿으면 JUMP!</summary>
        private void CueJump(float prev, float current, float progress)
        {
            if (jumpLearned || cued || hitDone) return;
            float speed = Mathf.Abs(current - prev) / Mathf.Max(Time.deltaTime, 0.0001f);
            float playerAngle = Vector3.SignedAngle(forward, Flat(TargetPosition - transform.position), Vector3.up);
            float remaining = (playerAngle - current) * (fromLeft ? 1f : -1f);
            // 쓸기 시작 직전처럼 아직 느리면 남은 시간으로 어림한다
            float eta = speed > 1f ? remaining / speed : Data.StormSweepTime * (1f - progress);
            if (remaining < 0f || eta > JumpCueLead) return;
            cued = true;
            Owner.CueJump();
        }

        /// <summary>이번 프레임 촉수가 prev → current 로 지나간 사이에 플레이어가 있었나 (보이는 끝까지, 떠 있으면 안 맞음)</summary>
        private void CheckHit(float prev, float current)
        {
            if (hitDone) return;
            Vector3 center = transform.position;
            Vector3 offset = Flat(TargetPosition - center);
            if (offset.magnitude > Tentacle.CurrentReach + PlayerRadius) return;

            float playerAngle = Vector3.SignedAngle(forward, offset, Vector3.up);
            if (playerAngle < Mathf.Min(prev, current) || playerAngle > Mathf.Max(prev, current)) return;

            // 낮게 쓴다: 점프로 떠 있으면 넘긴다
            float lift = TargetPosition.y - TyranopodeGround.Height(TargetPosition, TargetPosition.y);
            if (lift >= Data.StormJumpClear)
            {
                // 점프로 넘겼다: 처음이면 학습 완료
                hitDone = true;
                if (jumpLearned) return;
                jumpLearned = true;
                Owner.ShowEvadeLearned();
                return;
            }

            hitDone = true;
            HitPlayer(Data.StormDamage, center);
        }

        protected override void Stopped()
        {
            Tentacle.Release();
            Owner.HideEvadePrompt();
        }
    }
}
