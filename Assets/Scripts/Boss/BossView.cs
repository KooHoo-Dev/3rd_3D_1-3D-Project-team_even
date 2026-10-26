using System;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(Animator))]
    public class BossView : MonoBehaviour
    {
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

        private Animator animator;
        private BossMovement movement;

        protected virtual void Awake()
        {
            animator = GetComponent<Animator>();
            movement = GetComponent<BossMovement>();
        }

        private void Update()
        {
            animator.SetBool(IsMovingHash, movement != null && movement.IsMoving);
        }

        private void OnDisable()
        {
            if (animator != null) animator.SetBool(IsMovingHash, false);
        }
        
        public void PlayState(string fullStateName)
        {
            animator.Play(Animator.StringToHash(fullStateName),0, 0f);
        }
        
        public bool IsStateFinished(string fullStateName)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);

            return state.fullPathHash == Animator.StringToHash(fullStateName)
                   && state.normalizedTime >= 1f;
        }
    }
}