using HybridShooter.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace HybridShooter.UI
{
    /// <summary>
    /// 피격 시 화면 가장자리를 붉게 물들이는 HUD (BL-007).
    ///  - 피격 플래시: PlayerDamagedEvent 마다 피해량에 비례해 순간적으로 진해졌다가 빠르게 사라짐
    ///  - 저체력 상태: PlayerHpChangedEvent 로 체력 비율이 기준 이하이면 옅은 붉은 테두리가 맥박처럼 깜빡임
    /// 시야를 가리지 않도록 텍스처 중앙은 완전 투명이고, 합산 alpha 는 maxTotalAlpha 로 제한한다.
    /// 이벤트만 구독하므로 플레이어 구현을 직접 참조하지 않는다.
    /// </summary>
    public sealed class DamageVignetteHud : MonoBehaviour
    {
        [SerializeField] private Image vignette;
        [SerializeField] private Color color = new Color(0.75f, 0.04f, 0.04f);
        [Tooltip("플래시 + 저체력을 합친 최대 alpha. 시야 방해 방지용 상한")]
        [Range(0f, 1f)] [SerializeField] private float maxTotalAlpha = 0.5f;

        [Header("피격 플래시")]
        [Range(0f, 1f)] [SerializeField] private float minFlashAlpha = 0.2f;
        [Range(0f, 1f)] [SerializeField] private float maxFlashAlpha = 0.4f;
        [Tooltip("최대 체력 대비 이 비율 이상의 피해면 maxFlashAlpha")]
        [Range(0.01f, 1f)] [SerializeField] private float fullFlashDamageRatio = 0.25f;
        [Tooltip("플래시가 사라지는 속도 (alpha/초)")]
        [SerializeField] private float flashFadeSpeed = 0.9f;

        [Header("저체력")]
        [Range(0f, 1f)] [SerializeField] private float lowHealthRatio = 0.3f;
        [Tooltip("기준 체력일 때 / 체력 0 일 때 기본 alpha")]
        [Range(0f, 1f)] [SerializeField] private float lowHealthMinAlpha = 0.08f;
        [Range(0f, 1f)] [SerializeField] private float lowHealthMaxAlpha = 0.18f;
        [Range(0f, 0.5f)] [SerializeField] private float pulseAmplitude = 0.04f;
        [Tooltip("기준 체력일 때 / 체력 0 일 때 깜빡임 속도 (회/초)")]
        [SerializeField] private float minPulseRate = 0.9f;
        [SerializeField] private float maxPulseRate = 1.6f;
        [Tooltip("체력 변화 시 저체력 alpha 가 따라가는 속도 (alpha/초)")]
        [SerializeField] private float lowHealthBlendSpeed = 0.5f;

        private float flashAlpha;
        private float lowHealthAlpha;
        private float hpRatio = 1f;
        private int maxHp = 100;
        private float pulsePhase;

        /// <summary>현재 화면에 적용된 alpha (테스트·디버그용)</summary>
        public float CurrentAlpha => vignette != null && vignette.enabled ? vignette.color.a : 0f;
        public float MaxTotalAlpha => maxTotalAlpha;
        public float MinFlashAlpha => minFlashAlpha;
        public float MaxFlashAlpha => maxFlashAlpha;
        public float LowHealthRatio => lowHealthRatio;

        private void Awake()
        {
            if (vignette == null)
            {
                Debug.LogWarning($"[{nameof(DamageVignetteHud)}] Image 가 연결되지 않았습니다.", this);
                enabled = false;
                return;
            }
            vignette.raycastTarget = false;
            Apply(0f);
        }

        private void OnEnable()
        {
            EventBus<PlayerDamagedEvent>.Subscribe(OnPlayerDamaged);
            EventBus<PlayerHpChangedEvent>.Subscribe(OnPlayerHpChanged);
        }

        private void OnDisable()
        {
            EventBus<PlayerDamagedEvent>.Unsubscribe(OnPlayerDamaged);
            EventBus<PlayerHpChangedEvent>.Unsubscribe(OnPlayerHpChanged);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            flashAlpha = Mathf.MoveTowards(flashAlpha, 0f, flashFadeSpeed * dt);

            // 저체력: 0(기준 체력) ~ 1(체력 0)
            float danger = hpRatio < lowHealthRatio ? 1f - hpRatio / lowHealthRatio : -1f;
            float lowTarget = danger >= 0f ? Mathf.Lerp(lowHealthMinAlpha, lowHealthMaxAlpha, danger) : 0f;
            lowHealthAlpha = Mathf.MoveTowards(lowHealthAlpha, lowTarget, lowHealthBlendSpeed * dt);

            float pulse = 0f;
            if (lowHealthAlpha > 0f)
            {
                pulsePhase += Mathf.Lerp(minPulseRate, maxPulseRate, Mathf.Max(0f, danger)) * dt;
                pulsePhase %= 1f;
                // 0 ~ 1 사이 부드러운 맥박. 저체력 alpha 가 작을 때는 맥박도 같이 작게
                pulse = (0.5f - 0.5f * Mathf.Cos(pulsePhase * Mathf.PI * 2f)) * pulseAmplitude
                        * Mathf.Clamp01(lowHealthAlpha / Mathf.Max(lowHealthMinAlpha, 0.0001f));
            }
            else pulsePhase = 0f;

            Apply(Mathf.Min(maxTotalAlpha, lowHealthAlpha + pulse + flashAlpha));
        }

        // ── 이벤트 ──

        private void OnPlayerDamaged(PlayerDamagedEvent e)
        {
            if (e.Info.Amount <= 0) return;
            float t = Mathf.Clamp01((float)e.Info.Amount / Mathf.Max(1, maxHp) / fullFlashDamageRatio);
            // 연속 피격 시 누적되지 않고 더 큰 값으로 갱신 → 난사를 맞아도 화면이 덮이지 않음
            flashAlpha = Mathf.Max(flashAlpha, Mathf.Lerp(minFlashAlpha, maxFlashAlpha, t));
        }

        private void OnPlayerHpChanged(PlayerHpChangedEvent e)
        {
            if (e.Max > 0) maxHp = e.Max;
            hpRatio = e.Ratio;
        }

        // ── 표시 ──

        private void Apply(float alpha)
        {
            var c = color;
            c.a = alpha;
            vignette.color = c;
            // 완전히 투명할 때는 꺼서 전체 화면 오버드로를 없앰
            vignette.enabled = alpha > 0.001f;
        }
    }
}
