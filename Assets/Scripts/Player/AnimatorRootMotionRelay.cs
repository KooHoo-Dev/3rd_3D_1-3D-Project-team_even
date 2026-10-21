using System;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// Animator 가 붙은 모델 오브젝트에 둔다. 루트모션(애니메이션이 직접 캐릭터를 움직이는 양)을 플레이어 루트로 전달한다.
    /// OnAnimatorMove 는 Animator 와 같은 오브젝트에서만 불리기 때문에 이 중계 컴포넌트가 필요하다.
    /// 이 컴포넌트가 있으면 Unity 는 루트모션을 스스로 적용하지 않고, 받은 쪽(PlayerVault)이 Rigidbody 를 옮긴다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class AnimatorRootMotionRelay : MonoBehaviour
    {
        private Animator animator;

        /// <summary>applyRootMotion 이 켜져 있을 때 매 프레임</summary>
        public event Action<Animator> AnimatorMoved;

        private void Awake() => animator = GetComponent<Animator>();

        private void OnAnimatorMove()
        {
            if (animator.applyRootMotion) AnimatorMoved?.Invoke(animator);
        }
    }
}
