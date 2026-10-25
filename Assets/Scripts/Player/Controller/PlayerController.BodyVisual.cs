using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>보이는 몸(Body) yaw 맞추기</summary>
    public partial class PlayerController
    {
        /// <summary>
        /// 보이는 몸의 yaw 를 매 프레임 시선 yaw 로 맞춘다.
        /// 리지드바디 회전은 물리 박자(FixedUpdate)로만 바뀌어 카메라(매 프레임)보다 늦게, 들쭉날쭉 따라온다 → 3인칭에서 몸이 떨려 보임.
        /// 캡슐은 세로축 대칭이라 물리 회전이 조금 늦어도 충돌은 같다. 물리 회전(rb.MoveRotation)은 Forward·뒷걸음 감속 계산용으로 그대로 둔다
        /// </summary>
        private void UpdateBodyVisualYaw()
        {
            if (bodyVisual == null) return;
            if (facingMode == FacingMode.LockYaw && externalControl == false)
                bodyVisual.rotation = Quaternion.Euler(0f, lookYaw, 0f);
            else if (bodyVisual.localRotation != Quaternion.identity)
                bodyVisual.localRotation = Quaternion.identity; // FollowMove·외부 제어 중에는 리지드바디 회전을 그대로 따른다
        }
    }
}
