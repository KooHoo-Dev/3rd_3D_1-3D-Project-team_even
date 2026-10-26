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

        private void Awake()
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
    }
}