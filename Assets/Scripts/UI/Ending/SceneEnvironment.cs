using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 장면 하나의 하늘·해(달)·안개·환경광. RenderSettings 는 씬에 하나뿐이라 Timeline·오브젝트 켜고 끄기로 바꿀 수 없어 실행 중에 옮겨 넣는다.
    /// 엔딩(새벽 착륙장 ↔ 우주)과 클리어 뒤 메인 메뉴(밤 → 새벽)가 함께 쓴다. 값은 빌더가 채운다 (EndingDawnLook)
    /// </summary>
    [Serializable]
    public struct SceneEnvironment
    {
        public Material Skybox;
        [Tooltip("하늘 셰이더가 해·달을 그리는 방향 (RenderSettings.sun)")] public Light Sun;
        public bool Fog;
        public Color FogColor;
        public float FogDensity;
        public Color AmbientSky;
        public Color AmbientEquator;
        public Color AmbientGround;

        public void Apply()
        {
            RenderSettings.skybox = Skybox;
            RenderSettings.sun = Sun;
            RenderSettings.fog = Fog;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            DynamicGI.UpdateEnvironment(); // 하늘 반사도 바로 바뀌게
        }
    }
}
