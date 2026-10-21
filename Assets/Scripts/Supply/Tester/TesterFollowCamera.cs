using UnityEngine;

namespace HybridShooter.Supply.Tester
{
    /// <summary>보급 테스트용 카메라. 대상 뒤쪽 위에서 따라가며 내려다본다</summary>
    public sealed class TesterFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 18f, -16f);
        [SerializeField] private float followSharpness = 10f;

        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 desired = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
        }
    }
}
