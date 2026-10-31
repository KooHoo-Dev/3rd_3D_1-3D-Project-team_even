using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 포효 소환 기믹의 빛나는 포자. 보스가 하늘로 쏘면 떠올라 멈추고 주기적으로 파동을 낸다 (그때마다 잡몹이 벽을 넘어온다).
    /// 플레이어가 부수면 Destroyed → 파동이 전장을 휩쓸고 보스는 그로기 (SummonRoarAttack 이 처리).
    /// 시간이 다 되어 가면 BeginFailWarning: 빠르게 깜빡이며 내려온다
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class SummonBeacon : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(0.05f)] private float riseTime = 1f;
        [SerializeField, Min(0f)] private float pulseScale = 0.35f;
        [SerializeField, Min(0.05f)] private float pulseTime = 0.35f;
        [SerializeField, Min(0f)] private float failDropHeight = 6f;
        [SerializeField] private Light glow;
        [SerializeField] private GameObject pulseVfx;
        [SerializeField] private GameObject breakVfx;

        private Vector3 from;
        private Vector3 to;
        private float launchTime;
        private float pulseStartTime = -10f;
        private float failStartTime = -1f;
        private float failDuration;
        private int hp;
        private bool finished;
        private Vector3 baseScale;
        private float baseIntensity;

        public event Action<SummonBeacon> Destroyed;

        /// <summary>조명·연출이 모두 연결됐는지 (TyranopodeController 가 생성 순간 검사)</summary>
        public bool HasReferences => glow != null && pulseVfx != null && breakVfx != null;

        public Faction Faction => Faction.Monster;
        public bool IsAlive => finished == false;

        private void Awake()
        {
            baseScale = transform.localScale;
            baseIntensity = glow.intensity;
        }

        public void Launch(Vector3 start, Vector3 hoverPoint, int maxHp)
        {
            from = start;
            to = hoverPoint;
            hp = maxHp;
            launchTime = Time.time;
            transform.position = start;
        }

        public void Pulse()
        {
            pulseStartTime = Time.time;
            Destroy(Instantiate(pulseVfx, transform.position, Quaternion.identity), 3f);
        }

        public void BeginFailWarning(float duration)
        {
            failStartTime = Time.time;
            failDuration = Mathf.Max(0.05f, duration);
        }

        /// <summary>기믹이 끝나 조용히 사라진다 (실패·취소)</summary>
        public void Dismiss()
        {
            if (finished) return;
            finished = true;
            Destroy(gameObject);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (finished || this.CanBeHitBy(info.AttackerFaction) == false) return;
            hp -= info.Amount;
            bool broken = hp <= 0;
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(gameObject, info, broken));
            if (broken == false) return;

            finished = true;
            Destroy(Instantiate(breakVfx, transform.position, Quaternion.identity), 5f);
            Destroyed?.Invoke(this);
            Destroy(gameObject);
        }

        private void Update()
        {
            float rise = Mathf.Clamp01((Time.time - launchTime) / riseTime);
            Vector3 position = Vector3.Lerp(from, to, 1f - (1f - rise) * (1f - rise));

            float flicker = 1f;
            if (failStartTime >= 0f)
            {
                float t = Mathf.Clamp01((Time.time - failStartTime) / failDuration);
                position += Vector3.down * failDropHeight * t;
                flicker = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * Mathf.Lerp(6f, 30f, t)));
            }
            transform.position = position;

            float pulse = Mathf.Clamp01((Time.time - pulseStartTime) / pulseTime);
            float scale = 1f + pulseScale * Mathf.Sin(pulse * Mathf.PI);
            transform.localScale = baseScale * scale;
            glow.intensity = baseIntensity * scale * flicker;
        }
    }
}
