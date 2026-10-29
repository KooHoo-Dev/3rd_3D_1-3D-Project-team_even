using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace HybridShooter.Atmosphere
{
    /// <summary>
    /// 이 컴포넌트가 있는 씬에서만 메인 카메라의 포스트 프로세싱을 켠다 (Player 프리팹은 건드리지 않음).
    /// 1인칭/3인칭 전환처럼 Camera.main 이 바뀌면 새 카메라에 다시 적용하고, 이전 카메라는 원래 값으로 되돌린다.
    /// Dithering 은 안개·하늘의 부드러운 그라디언트에 생기는 색 띠(banding)를 줄인다.
    /// </summary>
    public sealed class CameraPostProcessSwitch : MonoBehaviour
    {
        [SerializeField] private bool dithering = true;
        [SerializeField] private AntialiasingMode antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        private UniversalAdditionalCameraData current;
        private bool savedPostProcessing;
        private bool savedDithering;
        private AntialiasingMode savedAntialiasing;

        private void LateUpdate()
        {
            var cam = Camera.main;
            var data = cam != null ? cam.GetUniversalAdditionalCameraData() : null;
            if (data == current) return;

            Restore();
            current = data;
            if (current == null) return;

            savedPostProcessing = current.renderPostProcessing;
            savedDithering = current.dithering;
            savedAntialiasing = current.antialiasing;
            current.renderPostProcessing = true;
            current.dithering = dithering;
            current.antialiasing = antialiasing;
        }

        private void OnDisable()
        {
            Restore();
            current = null;
        }

        /// <summary>적용했던 카메라를 원래 설정으로 되돌린다</summary>
        private void Restore()
        {
            if (current == null) return;
            current.renderPostProcessing = savedPostProcessing;
            current.dithering = savedDithering;
            current.antialiasing = savedAntialiasing;
        }
    }
}
