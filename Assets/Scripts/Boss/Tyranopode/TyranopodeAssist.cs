using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 숨은 난이도 보정. 플레이어가 힘들수록(체력·남은 목숨·이번 보스전 사망 횟수) Struggle 이 0 → 1 로 천천히 오른다.
    ///  - 공격 사이 간격 ×DelayMultiplier, 연속 공격 확률 ×ComboMultiplier, 강공격 가중치 ×HeavyMultiplier
    ///  - 예고 시간·피해량·보스 체력은 건드리지 않는다 (눈치채기 쉬움)
    /// </summary>
    public sealed class TyranopodeAssist
    {
        private const float HpWeight = 0.6f;
        private const float LivesWeight = 0.4f;

        private readonly TyranopodeDef def;
        private float hpRatio = 1f;
        private int startLives = -1;

        /// <summary>0 = 여유, 1 = 매우 힘듦 (부드럽게 따라감)</summary>
        public float Struggle { get; private set; }
        public float DelayMultiplier => Mathf.Lerp(1f, def.AssistMaxDelayMultiplier, Struggle);
        public float ComboMultiplier => 1f - def.AssistComboReduce * Struggle;
        public float HeavyMultiplier => 1f - def.AssistHeavyReduce * Struggle;

        public TyranopodeAssist(TyranopodeDef def)
        {
            this.def = def;
        }

        public void Begin()
        {
            EventBus<PlayerHpChangedEvent>.Subscribe(OnPlayerHpChanged);
            startLives = ServiceLocator.TryGet(out IPlayerLives lives) ? lives.Lives : -1;
        }

        public void End() => EventBus<PlayerHpChangedEvent>.Unsubscribe(OnPlayerHpChanged);

        public void Tick(float deltaTime)
        {
            float target = HpWeight * (1f - hpRatio);

            if (ServiceLocator.TryGet(out IPlayerLives lives))
            {
                if (startLives < 0) startLives = lives.Lives;
                float livesRatio = lives.MaxLives > 1 ? Mathf.Clamp01((lives.Lives - 1f) / (lives.MaxLives - 1f)) : 1f;
                int deaths = Mathf.Max(0, startLives - lives.Lives);
                target += LivesWeight * (1f - livesRatio) + deaths * def.AssistPerDeath;
            }

            target = Mathf.Clamp01(target);
            float rate = def.AssistSmoothTime > 0f ? deltaTime / def.AssistSmoothTime : 1f;
            Struggle = Mathf.MoveTowards(Struggle, target, rate);
        }

        private void OnPlayerHpChanged(PlayerHpChangedEvent e) => hpRatio = e.Max > 0 ? e.Ratio : 1f;
    }
}
