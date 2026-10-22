using HybridShooter.Monster.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>이동 실험실의 간단한 키보드 플레이어이자 IMovementTarget.</summary>
    public sealed class TransformMovementTarget : MonoBehaviour, IMovementTarget
    {
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField] private float turnSpeed = 540f;

        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;
        public bool IsValid => isActiveAndEnabled;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (input.sqrMagnitude < 0.001f) return;

            Vector3 direction = new Vector3(input.x, 0f, input.y).normalized;
            transform.position += direction * (moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), turnSpeed * Time.deltaTime);
        }
    }
}
