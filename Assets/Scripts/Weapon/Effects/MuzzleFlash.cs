using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 총구 화염의 불빛. Gun 이 발사할 때마다 총구 자리에 생성하고 1초 뒤 지운다.
    ///  - 불빛(Light)은 짧게 번쩍였다가 빠르게 꺼진다
    ///  - 파티클(화염·불똥·연기)은 각 ParticleSystem 의 Play On Awake 로 재생된다.
    ///    모양이 반복돼 보이지 않게 크기·회전 무작위는 각 파티클의 Start Size / Start Rotation 범위로 준다
    ///  - 총구의 자식으로 두지 않고 IK 가 끝난 뒤(LateUpdate 10001) 총구를 따라간다.
    ///    자식이면 파티클 갱신(애니메이션 직후, IK 전) 때 총구가 애니메이션 자세(총을 비스듬히 든 자세)에 있어
    ///    월드에 남는 불똥·연기가 화면에 보이는 총(IK 뒤)과 다른 자리·방향에서 터진다
    /// </summary>
    [DefaultExecutionOrder(10001)]
    public sealed class MuzzleFlash : MonoBehaviour
    {
        [SerializeField] private Light flashLight;
        [Tooltip("불빛이 꺼지는 데 걸리는 시간")]
        [SerializeField] private float lightDuration = 0.06f;

        private float lightIntensity;
        private float age;
        private Transform follow;

        /// <summary>IK 가 끝난 총구 자리를 매 프레임 따라간다 (총이 사라지면 그 자리에 남는다)</summary>
        public void Follow(Transform target) => follow = target;

        private void Awake()
        {
            if (flashLight != null) lightIntensity = flashLight.intensity;
        }

        private void OnEnable()
        {
            age = 0f;
            if (flashLight == null) return;
            flashLight.intensity = lightIntensity;
            flashLight.enabled = true;
        }

        private void Update()
        {
            if (flashLight == null || flashLight.enabled == false) return;
            age += Time.deltaTime;
            float t = age / Mathf.Max(0.001f, lightDuration);
            if (t >= 1f) flashLight.enabled = false;
            else flashLight.intensity = lightIntensity * (1f - t) * (1f - t);
        }

        private void LateUpdate()
        {
            if (follow != null) transform.SetPositionAndRotation(follow.position, follow.rotation);
        }
    }
}
