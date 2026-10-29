using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    /// <summary>
    /// 등장 공격 착지 충격파(ShockWave 프리팹 루트).
    /// 바닥 데칼에 경과 시간을 넣고, 섬광 라이트를 줄이고, 생성 순간 카메라 흔들림을 요청한다.
    /// 파티클(흙먼지·파편·불꽃)은 자식 ParticleSystem 이 Play On Awake 로 알아서 재생된다.
    /// EntranceAttack 이 3초 뒤 Destroy 하므로 모든 연출은 3초 안에 끝나게 맞춰져 있다.
    /// </summary>
    public class EntranceShockwave : MonoBehaviour
    {
        private static readonly int ElapsedId = Shader.PropertyToID("_Elapsed");

        [Header("바닥")]
        [SerializeField] private Renderer groundWave;
        [Tooltip("ClypeosaurusTable 의 EntranceRadius 와 맞춘다 (바닥 링이 퍼지는 끝)")]
        [SerializeField, Min(0.1f)] private float radius = 20f;
        [SerializeField, Min(0.1f)] private float projectionHeight = 12f;

        [Header("섬광")]
        [SerializeField] private Light flash;
        [SerializeField, Min(0f)] private float flashIntensity = 40f;
        [SerializeField, Min(0.01f)] private float flashDuration = 0.35f;

        [Header("카메라 흔들림")]
        [SerializeField] private bool shakeCamera = true;
        [SerializeField] private CameraShakeSettings shake = new CameraShakeSettings(0.85f, 0.8f, 5f, 0.02f, 0.45f);
        [Tooltip("이 거리 밖에서는 흔들리지 않는다 (0 = 거리 무관)")]
        [SerializeField, Min(0f)] private float shakeRadius = 70f;

        private MaterialPropertyBlock block;
        private float elapsed;

        public float Radius => radius;

        public void Initialize(float entranceRadius)
        {
            radius = entranceRadius;
            ApplyRadius();
        }
        
        private void OnEnable()
        {
            elapsed = 0f;
            ApplyGround();
            UpdateFlash();

            if (shakeCamera && shake.Duration > 0f)
            {
                EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(shake.At(transform.position, shakeRadius)));
            }
        }

        private void Update()
        {
            elapsed += Time.deltaTime;
            ApplyGround();
            UpdateFlash();
        }

        private void ApplyGround()
        {
            if (groundWave == null) return;

            block ??= new MaterialPropertyBlock();
            groundWave.GetPropertyBlock(block);
            block.SetFloat(ElapsedId, elapsed);
            groundWave.SetPropertyBlock(block);
        }

        private void UpdateFlash()
        {
            if (flash == null) return;

            float k = 1f - Mathf.Clamp01(elapsed / flashDuration);
            flash.intensity = flashIntensity * k * k;
            flash.enabled = k > 0f;
        }

        private void OnValidate()
        {
            ApplyRadius();
        }

        /// <summary>바닥 데칼 상자 크기를 피해 반경에 맞춘다 (에디터 빌더에서도 호출)</summary>
        public void ApplyRadius()
        {
            if (groundWave == null) return;
            groundWave.transform.localScale = new Vector3(radius * 2f, projectionHeight, radius * 2f);
        }
    }
}
