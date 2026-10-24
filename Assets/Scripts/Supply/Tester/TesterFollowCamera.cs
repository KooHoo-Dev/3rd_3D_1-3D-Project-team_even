using UnityEngine;

namespace HybridShooter.Supply.Tester
{
    /// <summary>
    /// 보급 테스트용 카메라. 대상 뒤쪽 위에서 따라가며 내려다본다.
    /// 대상이 TesterPlayer 면 우클릭 드래그로 정해진 시점(yaw/pitch)대로 대상 주위를 돈다.
    /// </summary>
    public sealed class TesterFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 18f, -16f);
        [SerializeField] private float followSharpness = 10f;

        private TesterPlayer player;

        private void Awake()
        {
            if (target != null) player = target.GetComponent<TesterPlayer>();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired = player != null
                ? target.position + Quaternion.Euler(player.ViewPitch, player.ViewYaw, 0f) * new Vector3(0f, 0f, -offset.magnitude)
                : target.position + offset;

            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followSharpness * Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
        }
    }
}
