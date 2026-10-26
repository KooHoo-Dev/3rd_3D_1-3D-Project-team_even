using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// AnimJitterProbe 의 첫 기록 시점: 애니메이터가 뼈를 움직인 뒤, IK(PlayerItemIK 9995 ~ FullBodyBipedIK 9999)보다 먼저.
    /// 실행 순서는 클래스마다 하나라 따로 둔다 (AnimJitterProbe 가 붙이고 지운다)
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(9990)]
    public class AnimJitterProbeHook : MonoBehaviour
    {
        public AnimJitterProbe Probe { get; set; }

        private void LateUpdate()
        {
            if (Probe != null) Probe.CaptureStage(AnimJitterProbe.StageAnim);
        }
    }
}
