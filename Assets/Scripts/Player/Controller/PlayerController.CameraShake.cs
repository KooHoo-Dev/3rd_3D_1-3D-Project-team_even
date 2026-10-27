using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.Player
{
    /// <summary>
    /// 카메라 흔들림 (거대 보스 등장·걸음 등).
    ///  - 화면만 흔든다: URP 가 카메라를 그리기 직전(beginCameraRendering)에 흔들림을 더하고, 다 그린 뒤(endCameraRendering) 되돌린다
    ///    → 조준선(GunSlot.AimRay)·IK·목 보호·벽 충돌·시점 전환은 흔들리지 않은 카메라를 본다
    ///  - 멀미 방지: Perlin 노이즈(부드러운 움직임), 위치·회전·기울기 상한, 여러 흔들림은 합쳐도 상한을 넘지 않음,
    ///    1인칭은 firstPersonShakeScale 만큼 약하게, 조준 확대 중에는 확대 배율만큼 약하게
    ///  - 바깥 호출: CameraShakeRequestedEvent (권장) 또는 Shake / StopShake. 보스 등장(BossAppearedEvent)은 자동
    /// </summary>
    public partial class PlayerController
    {
        #region 설정(인스펙터)
        [Header("카메라 흔들림")]
        [Tooltip("전체 배율 (설정 메뉴용). 0 = 끔")]
        [SerializeField, Range(0f, 1f)] private float shakeScale = 1f;
        [Tooltip("세기 1 일 때 최대 위치 이동(m, 3인칭 기준)")]
        [SerializeField] private float maxShakeOffset = 0.15f;
        [Tooltip("세기 1 일 때 최대 상하·좌우 회전(도, 3인칭 기준)")]
        [SerializeField] private float maxShakeAngle = 1.5f;
        [Tooltip("세기 1 일 때 최대 화면 기울기(도). 기울기는 멀미가 심해 작게")]
        [SerializeField] private float maxShakeRoll = 0.8f;
        [Tooltip("1인칭에서의 배율 (눈 카메라라 같은 흔들림도 더 크게 느껴진다)")]
        [SerializeField, Range(0f, 1f)] private float firstPersonShakeScale = 0.5f;
        [Tooltip("보스 등장(BossAppearedEvent) 때 자동으로 흔든다")]
        [SerializeField] private bool shakeOnBossAppear = true;
        [SerializeField] private CameraShakeSettings bossAppearShake = CameraShakePresets.BossAppear;
        #endregion

        private struct ActiveShake
        {
            public int Handle;
            public CameraShakeSettings Settings;
            public float StartTime;
            public float Seed;
        }

        private readonly List<ActiveShake> shakes = new List<ActiveShake>();
        private int nextShakeHandle = 1;
        private Vector3 shakeOffset;   // 카메라 기준 위치 이동(m)
        private Vector3 shakeAngles;   // 카메라 기준 pitch·yaw·roll(도)
        private Transform shakenCamera;
        private Vector3 shakenHomePos;
        private Quaternion shakenHomeRot;

        /// <summary>전체 흔들림 배율 0~1 (설정 메뉴용, 0 = 끔)</summary>
        public float ShakeScale { get => shakeScale; set => shakeScale = Mathf.Clamp01(value); }
        /// <summary>지금 흔들리는 중</summary>
        public bool IsShaking => shakes.Count > 0;

        /// <summary>
        /// 흔들림 시작. 반환값 = StopShake 에 넘길 핸들.
        /// Duration 0 이하면 StopShake 할 때까지 계속 흔든다 (보스가 걷는 내내 등)
        /// </summary>
        public int Shake(in CameraShakeSettings settings)
        {
            if (settings.Intensity <= 0f || settings.Frequency <= 0f) return 0;
            int handle = nextShakeHandle++;
            shakes.Add(new ActiveShake
            {
                Handle = handle,
                Settings = settings,
                StartTime = Time.time,
                Seed = Random.Range(0f, 1000f),
            });
            return handle;
        }

        /// <summary>그 흔들림을 fadeOut 동안 줄이며 끝낸다 (0 = 바로)</summary>
        public void StopShake(int handle, float fadeOut = 0.2f)
        {
            for (int i = 0; i < shakes.Count; i++)
            {
                if (shakes[i].Handle != handle) continue;
                var shake = shakes[i];
                float elapsed = Time.time - shake.StartTime;
                fadeOut = Mathf.Max(0f, fadeOut);
                if (shake.Settings.Duration > 0f && elapsed + fadeOut >= shake.Settings.Duration) return; // 이미 그보다 빨리 끝난다
                shake.Settings.Duration = elapsed + fadeOut;
                shake.Settings.FadeOut = fadeOut;
                if (fadeOut <= 0f) shakes.RemoveAt(i);
                else shakes[i] = shake;
                return;
            }
        }

        public void StopAllShakes()
        {
            shakes.Clear();
            shakeOffset = Vector3.zero;
            shakeAngles = Vector3.zero;
        }

        private void SubscribeShakeEvents()
        {
            EventBus<CameraShakeRequestedEvent>.Subscribe(OnCameraShakeRequested);
            EventBus<BossAppearedEvent>.Subscribe(OnBossAppearedShake);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        private void UnsubscribeShakeEvents()
        {
            EventBus<CameraShakeRequestedEvent>.Unsubscribe(OnCameraShakeRequested);
            EventBus<BossAppearedEvent>.Unsubscribe(OnBossAppearedShake);
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RestoreShakenCamera();
        }

        private void OnCameraShakeRequested(CameraShakeRequestedEvent e)
        {
            if (e.Settings.Duration <= 0f)
            {
                Debug.LogWarning($"{name}: CameraShakeRequestedEvent 는 Duration > 0 만 받습니다 (계속 흔들기는 PlayerController.Shake / StopShake)", this);
                return;
            }
            Shake(e.Settings);
        }

        private void OnBossAppearedShake(BossAppearedEvent e)
        {
            if (shakeOnBossAppear) Shake(bossAppearShake);
        }

        /// <summary>
        /// LateUpdate: 살아 있는 흔들림을 모두 합쳐 이번 프레임 위치·회전 흔들림을 정한다.
        /// 각 흔들림 = 노이즈(-1~1) × 무게(세기² × 켜짐/꺼짐 × 거리 감쇠). 무게 합이 1 을 넘으면 1 로 나눠 상한을 지킨다
        /// </summary>
        private void UpdateCameraShake()
        {
            shakeOffset = Vector3.zero;
            shakeAngles = Vector3.zero;
            if (shakes.Count == 0) return;

            float now = Time.time;
            Vector3 listener = transform.position;
            Vector3 position = Vector3.zero;
            Vector3 angles = Vector3.zero;
            float totalWeight = 0f;

            for (int i = shakes.Count - 1; i >= 0; i--)
            {
                var shake = shakes[i];
                var s = shake.Settings;
                float elapsed = now - shake.StartTime;
                if (s.Duration > 0f && elapsed >= s.Duration)
                {
                    shakes.RemoveAt(i);
                    continue;
                }

                float weight = s.Intensity * s.Intensity * ShakeEnvelope(s, elapsed) * ShakeFalloff(s, listener);
                if (weight <= 0f) continue;

                float t = elapsed * s.Frequency;
                position += new Vector3(ShakeNoise(shake.Seed, 0f, t), ShakeNoise(shake.Seed, 1f, t), 0f) * weight;
                angles += new Vector3(ShakeNoise(shake.Seed, 2f, t), ShakeNoise(shake.Seed, 3f, t), ShakeNoise(shake.Seed, 4f, t)) * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f) return;
            float normalize = totalWeight > 1f ? 1f / totalWeight : 1f;
            // 1인칭은 약하게, 조준 확대 중에는 확대 배율만큼 (좁은 화면에서는 같은 각도도 크게 보인다)
            float scale = shakeScale * normalize * Mathf.Lerp(1f, firstPersonShakeScale, FirstPersonBlend) * aimFovCurrent;

            shakeOffset = position * (maxShakeOffset * scale);
            shakeAngles = new Vector3(angles.x * maxShakeAngle, angles.y * maxShakeAngle, angles.z * maxShakeRoll) * scale;
        }

        /// <summary>켜짐(FadeIn)·꺼짐(FadeOut) 중 작은 쪽. Duration 0 이하면 꺼짐 없음</summary>
        private static float ShakeEnvelope(in CameraShakeSettings s, float elapsed)
        {
            float fadeIn = s.FadeIn > 0f ? Mathf.Clamp01(elapsed / s.FadeIn) : 1f;
            float fadeOut = s.Duration > 0f && s.FadeOut > 0f ? Mathf.Clamp01((s.Duration - elapsed) / s.FadeOut) : 1f;
            return Mathf.Min(fadeIn, fadeOut);
        }

        /// <summary>진원에서 멀수록 약하게 (Radius 0 = 감쇠 없음). 가까운 쪽이 더 빨리 세지도록 제곱</summary>
        private static float ShakeFalloff(in CameraShakeSettings s, Vector3 listener)
        {
            if (s.Radius <= 0f) return 1f;
            float k = 1f - Mathf.Clamp01(Vector3.Distance(listener, s.Source) / s.Radius);
            return k * k;
        }

        /// <summary>Perlin 노이즈 -1~1. 축마다 다른 줄을 읽는다</summary>
        private static float ShakeNoise(float seed, float axis, float t) =>
            (Mathf.PerlinNoise(seed + axis * 17.31f, t) - 0.5f) * 2f;

        /// <summary>플레이어 카메라를 그리기 직전: 흔들림을 더한다 (씬 뷰 등 다른 카메라는 건드리지 않는다)</summary>
        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (rigReady == false || (cam != fpCamera && cam != tpCamera)) return;
            if (shakeOffset == Vector3.zero && shakeAngles == Vector3.zero) return;

            RestoreShakenCamera();
            Transform t = cam.transform;
            t.GetLocalPositionAndRotation(out shakenHomePos, out shakenHomeRot);
            shakenCamera = t;
            t.SetPositionAndRotation(t.position + t.rotation * shakeOffset, t.rotation * Quaternion.Euler(shakeAngles));
        }

        /// <summary>다 그린 뒤: 원래 자리로 (다음 프레임 조준·계산은 흔들리지 않은 값)</summary>
        private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
        {
            if (shakenCamera != null && cam.transform == shakenCamera) RestoreShakenCamera();
        }

        private void RestoreShakenCamera()
        {
            if (shakenCamera == null) return;
            shakenCamera.SetLocalPositionAndRotation(shakenHomePos, shakenHomeRot);
            shakenCamera = null;
        }
    }
}
