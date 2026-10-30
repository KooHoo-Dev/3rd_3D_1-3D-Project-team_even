using System;
using UnityEngine;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(Animator))]
    public class BossView : MonoBehaviour
    {
        // 기본 상태 애니메이션 직렬화 필드 (인스펙터 연결 필수)
        [SerializeField] private string idleState;
        [SerializeField] private string hitState;
        [SerializeField] private string deathState;
        
        private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");

        private Animator animator;
        private BossMovement movement;
        private int hitStartedFrame = -1;

        public bool IsHitFinished => Time.frameCount > hitStartedFrame && IsStateFinished(hitState);
        
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
        
        public void ResetState()
        {
            hitStartedFrame = -1;
            animator.SetBool(IsMovingHash, false);
            PlayIdle();
        }
        
        public void PlayIdle()
        {
            PlayState(idleState);
        }
        
        public void PlayHit()
        {
            hitStartedFrame = Time.frameCount;
            animator.SetBool(IsMovingHash, false);
            PlayState(hitState);
        }
        
        public void PlayDeath()
        {
            animator.SetBool(IsMovingHash, false);
            PlayState(deathState);
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