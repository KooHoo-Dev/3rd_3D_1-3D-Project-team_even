using UnityEngine;

namespace HybridShooter.Atmosphere
{
    /// <summary>
    /// 씬 대기(안개·밤하늘·환경광·달빛) 수치 묶음.
    /// 에디터 메뉴 Tools/HybridShooter/Atmosphere/현재 씬에 외계 대기 적용 이 이 값을 씬의 RenderSettings·달빛·하늘 재질에 옮긴다.
    /// 값을 바꾼 뒤 메뉴를 다시 실행하면 반영된다 (런타임에는 읽지 않는다).
    ///
    /// 안개 (Exponential Squared): 보이는 정도 = exp(-(밀도 × 거리)²)
    ///   밀도 0.022 → 40m 약 46%, 60m 약 18%, 80m 약 5% 보임 (약 85m 부터 수평선이 사라진다)
    ///
    /// 밤이지만 플레이에 불편하지 않도록, 달빛과 환경광을 밝게 두고 하늘만 어둡게 한다.
    /// </summary>
    [CreateAssetMenu(menuName = "HybridShooter/Atmosphere Preset", fileName = "AtmospherePreset")]
    public sealed class AtmospherePreset : ScriptableObject
    {
        [Header("안개 (청회색 포자 안개, 밤하늘에 맞춰 살짝 푸른 보라)")]
        [Tooltip("안개 색. 하늘 지평선 색도 이 값으로 맞춘다")]
        public Color fogColor = new Color(0.31f, 0.36f, 0.43f);
        [Tooltip("Exponential Squared 밀도. 0.022 ≈ 85m 이후 거의 안 보임")]
        [Range(0.001f, 0.1f)] public float fogDensity = 0.022f;

        [Header("밤하늘")]
        [Tooltip("천정 색. 너무 어두우면 화면 위쪽이 검게 뭉개진다")]
        public Color skyZenithColor = new Color(0.16f, 0.17f, 0.30f);
        [Tooltip("지평선 아지랑이(안개 색) 높이 (시선 방향 y)")]
        [Range(0.01f, 1f)] public float skyHorizonBlend = 0.3f;
        [Range(0f, 10f)] public float starBrightness = 3f;
        [Range(0f, 1f)] public float starTwinkle = 0.6f;
        [ColorUsage(false, true)] public Color nebulaColorA = new Color(0.55f, 0.22f, 0.8f);
        [ColorUsage(false, true)] public Color nebulaColorB = new Color(0.15f, 0.6f, 0.7f);
        [Range(0f, 4f)] public float nebulaIntensity = 1f;

        [Header("거대한 달 (달빛 방향에 뜬다)")]
        [Tooltip("달 반지름 (도). 14 = 화면 세로의 약 절반")]
        [Range(1f, 30f)] public float moonRadius = 14f;
        public Color moonColor = new Color(0.88f, 0.9f, 0.95f);
        [Range(0f, 4f)] public float moonBrightness = 1.4f;
        [ColorUsage(false, true)] public Color moonGlowColor = new Color(0.55f, 0.68f, 1f);
        [Range(0f, 4f)] public float moonGlowIntensity = 0.8f;
        [Tooltip("눈동자 테두리 색 (HDR)")]
        [ColorUsage(false, true)] public Color moonEyeColor = new Color(2f, 0.25f, 0.1f);
        [Tooltip("0 = 평범한 달, 1 = 노골적인 눈")]
        [Range(0f, 1f)] public float moonEyeIntensity = 0.6f;

        [Header("환경광 (Gradient)")]
        public Color ambientSkyColor = new Color(0.40f, 0.45f, 0.58f);
        public Color ambientEquatorColor = new Color(0.30f, 0.33f, 0.40f);
        public Color ambientGroundColor = new Color(0.13f, 0.12f, 0.14f);
        [Range(0f, 2f)] public float reflectionIntensity = 0.7f;

        [Header("달빛 (Directional Light)")]
        public Color moonLightColor = new Color(0.72f, 0.82f, 1f);
        [Range(0f, 3f)] public float moonLightIntensity = 1f;
        [Tooltip("x = 고도 (달이 뜬 높이), y = 방위. 하늘의 달도 이 방향에 그려진다")]
        public Vector3 moonLightRotation = new Vector3(28f, -40f, 0f);
        [Range(0f, 1f)] public float moonLightShadowStrength = 0.75f;

        [Header("주변 연기 파티클")]
        [Tooltip("연기 색 (알파 = 진하기). 안개 색보다 조금 밝게 해야 형체가 보인다")]
        public Color smokeColor = new Color(0.38f, 0.43f, 0.50f, 0.32f);
    }
}
