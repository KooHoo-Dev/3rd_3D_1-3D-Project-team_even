using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 땅울림. 촉수로 땅을 내리쳐 넓은 범위의 플레이어 화면을 흔들고 총 퍼짐을 잠깐 키운다 (사격은 막지 않는다).
    /// 점프로 공중에 있거나 구르기 무적이면 피한다 (IPlayerGroundTremor). 보스 바로 곁은 약한 피해
    /// </summary>
    public sealed class TremorAttack : TyranopodeAttack
    {
        [SerializeField, Range(0f, 1f)] private float impactTime = 0.45f;
        [SerializeField] private CameraShakeSettings tremorShake = new CameraShakeSettings(0.6f, 0.9f, 14f, 0.02f, 0.5f);

        private bool impacted;
        private GroundTelegraph nearTelegraph;
        private GroundTelegraph farTelegraph;

        protected override float WindupTime => Data.TremorWindup;
        protected override float RecoveryTime => Data.TremorRecovery;
        protected override float CooldownTime => Data.TremorCooldown;

        protected override void StartWindup()
        {
            impacted = false;
            View.Play(TyranopodeView.Idle, 1.6f);
            float time = Data.TremorWindup * Owner.WindupScale;
            // 가까운 피해 범위는 진하게, 땅울림 범위는 옅게
            nearTelegraph = Telegraph();
            nearTelegraph.ShowCircle(transform.position, Data.TremorNearRadius, time, DangerColor);
            farTelegraph = Telegraph();
            farTelegraph.ShowCircle(transform.position, Data.TremorRadius, time, new Color(1f, 0.8f, 0.2f, 0.12f));
        }

        protected override void OnActiveStarted() => View.Play(TyranopodeView.TentacleSmash, Owner.SpeedScale);

        protected override void OnActiveTick(float deltaTime)
        {
            float n = View.NormalizedTime(TyranopodeView.TentacleSmash);
            if (impacted == false && n >= impactTime)
            {
                impacted = true;
                Detonate(nearTelegraph);
                Detonate(farTelegraph, new Color(1f, 0.8f, 0.35f, 0.3f), dust: false);
                Impact();
            }
            if (n >= 1f) Finish();
        }

        private void Impact()
        {
            Vector3 center = transform.position;
            Owner.PlayShockwave(center, Data.TremorRadius);
            if (PlayerInCircle(center, Data.TremorNearRadius)) HitPlayer(Data.TremorNearDamage, center);

            bool applied = PlayerInCircle(center, Data.TremorRadius)
                           && ServiceLocator.TryGet(out IPlayerGroundTremor tremor)
                           && tremor.ApplyGroundTremor(Data.TremorSpread, Data.TremorDuration, tremorShake);
            // 피했거나 범위 밖이면 약하게만 흔든다
            if (applied == false) Shake(CameraShakePresets.Light, center, Data.TremorRadius * 2f);
        }
    }
}
