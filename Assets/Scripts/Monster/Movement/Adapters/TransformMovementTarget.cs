using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>IMovementTarget 의 Transform 기반 구현. 테스트·샌드박스용 대체 목표</summary>
    public sealed class TransformMovementTarget : MonoBehaviour, IMovementTarget
    {
        [Tooltip("정면 기준 Transform (비우면 자기 자신)")]
        [SerializeField] private Transform view;
        [SerializeField] private bool valid = true;

        public Vector3 Position => transform.position;

        /// <summary>정면을 XZ 평면으로 펼친 단위 벡터. 거의 0 이면 transform.forward</summary>
        public Vector3 Forward
        {
            get
            {
                var source = view != null ? view : transform;
                var flat = Vector3.ProjectOnPlane(source.forward, Vector3.up);
                return flat.sqrMagnitude < 1e-6f ? transform.forward : flat.normalized;
            }
        }

        public bool IsValid => isActiveAndEnabled && valid;

        public void SetValid(bool v) => valid = v;
    }
}
