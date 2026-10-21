using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 스태미나 수치만 관리한다. 달리기·구르기가 무엇을 하는지는 모른다.
    /// 바닥나면 Exhausted 상태가 되고, resumeRatio 까지 회복해야 풀린다. (바닥난 직후 깜빡이며 다시 달리는 것 방지)
    /// </summary>
    public class PlayerStamina : MonoBehaviour
    {
        [SerializeField] private float max = 100f;
        [Tooltip("마지막 소비 후 회복이 시작되기까지 걸리는 시간(초)")]
        [SerializeField] private float regenDelay = 0.8f;
        [SerializeField] private float regenPerSecond = 25f;
        [Tooltip("바닥난 뒤 이 비율까지 회복해야 다시 달리기·구르기 가능")]
        [SerializeField, Range(0f, 1f)] private float resumeRatio = 0.25f;

        private float current;
        private float lastUseTime = float.NegativeInfinity;

        public float Max => max;
        public float Current => current;
        public float Ratio => max > 0f ? current / max : 0f;
        public bool IsExhausted { get; private set; }

        private void Awake() => current = max;

        private void Start() => Publish();

        private void Update()
        {
            if (current >= max || Time.time - lastUseTime < regenDelay) return;
            Set(current + regenPerSecond * Time.deltaTime);
        }

        /// <summary>달리기처럼 매 프레임 조금씩. 모자라면 남은 만큼만 쓰고 false</summary>
        public bool Drain(float perSecond) => TrySpendInternal(perSecond * Time.deltaTime, allowPartial: true);

        /// <summary>구르기처럼 한 번에. 모자라면 아무것도 쓰지 않고 false</summary>
        public bool TrySpend(float amount) => TrySpendInternal(amount, allowPartial: false);

        public bool Has(float amount) => IsExhausted == false && current >= amount;

        /// <summary>스태미나를 가득 채움 (라운드 재시작 등)</summary>
        public void Refill() { IsExhausted = false; Set(max); }

        private bool TrySpendInternal(float amount, bool allowPartial)
        {
            if (IsExhausted || current <= 0f) return false;
            if (allowPartial == false && current < amount) return false;

            lastUseTime = Time.time;
            Set(current - amount);
            if (current <= 0f) IsExhausted = true;
            return true;
        }

        private void Set(float value)
        {
            float clamped = Mathf.Clamp(value, 0f, max);
            if (Mathf.Approximately(clamped, current)) return;
            current = clamped;
            if (IsExhausted && Ratio >= resumeRatio) IsExhausted = false;
            Publish();
        }

        private void Publish() => EventBus<PlayerStaminaChangedEvent>.Publish(new PlayerStaminaChangedEvent(current, max));
    }
}
