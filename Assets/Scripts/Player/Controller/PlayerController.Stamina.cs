using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 스태미나: 달리기는 초당 소모, 구르기는 한 번에 소모. 쓰지 않고 staminaRegenDelay 가 지나면 회복.
    /// 바닥나면 sprintResumeRatio 까지 회복할 때까지 달릴 수 없다. 바뀔 때마다 PlayerStaminaChangedEvent
    /// </summary>
    public partial class PlayerController
    {
        public float CurrentStamina => stamina;
        public float MaxStamina => maxStamina;
        public float StaminaRatio => maxStamina > 0f ? stamina / maxStamina : 0f;
        /// <summary>바닥난 뒤 다시 달릴 수 있을 만큼 회복하기 전</summary>
        public bool IsStaminaExhausted => staminaExhausted;

        /// <summary>달리기 입력이 실제로 먹히는지 (바닥난 동안은 걷는다)</summary>
        private bool CanSprint => staminaExhausted == false && stamina > 0f;
        private bool HasRollStamina => stamina >= rollStaminaCost;

        public void RestoreStamina()
        {
            stamina = maxStamina;
            staminaExhausted = false;
            PublishStamina();
        }

        private void UpdateStamina(float dt)
        {
            if (IsDead) return;

            if (IsSprintingInput && isRolling == false) SpendStamina(sprintStaminaPerSecond * dt);
            else if (Time.time - lastStaminaUseTime >= staminaRegenDelay && stamina < maxStamina)
            {
                stamina = Mathf.Min(maxStamina, stamina + staminaRegenPerSecond * dt);
                if (staminaExhausted && stamina >= maxStamina * sprintResumeRatio) staminaExhausted = false;
            }

            if (Mathf.Approximately(stamina, publishedStamina) == false) PublishStamina();
        }

        private void SpendStamina(float amount)
        {
            if (amount <= 0f) return;
            stamina = Mathf.Max(0f, stamina - amount);
            lastStaminaUseTime = Time.time;
            if (stamina <= 0f) staminaExhausted = true;
        }

        private void PublishStamina()
        {
            publishedStamina = stamina;
            EventBus<PlayerStaminaChangedEvent>.Publish(new PlayerStaminaChangedEvent(stamina, maxStamina));
        }
    }
}
