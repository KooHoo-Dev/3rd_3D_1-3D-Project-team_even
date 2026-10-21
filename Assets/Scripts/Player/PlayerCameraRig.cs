using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 카메라 시점(yaw/pitch)과 1인칭·3인칭 카메라 위치를 계산해 카메라에 적용한다.
    /// 플레이어 이동·입력을 전혀 모른다: 회전량은 AddLook 으로, 시점 전환 진행도는 SetBlend 로 받는다.
    ///
    /// Blend 0 = 1인칭(눈 위치), 1 = 3인칭(어깨 뒤). 그 사이는 두 위치를 보간 → 부드러운 전환.
    /// 카메라는 플레이어의 자식이 아닌 별도 오브젝트에 둔다. (Rigidbody 보간 + LateUpdate 로 떨림 방지)
    /// </summary>
    public class PlayerCameraRig : MonoBehaviour
    {
        [SerializeField] private Camera viewCamera;
        [Tooltip("따라갈 기준점. 플레이어 자식의 빈 오브젝트를 눈 높이(약 1.6m)에 둔다")]
        [SerializeField] private Transform pivot;

        [Header("시야 (PlayerTable 이 있으면 덮어씀)")]
        [SerializeField] private float sensitivity = 0.15f;
        [SerializeField] private float pitchMin = -80f;
        [SerializeField] private float pitchMax = 80f;

        [Header("1인칭")]
        [Tooltip("기준점으로부터의 오프셋 (yaw 기준 로컬). 몸 안쪽이 보이면 z 를 조금 앞으로")]
        [SerializeField] private Vector3 firstPersonOffset = new Vector3(0f, 0f, 0.1f);
        [SerializeField] private float firstPersonFov = 70f;

        [Header("3인칭")]
        [SerializeField] private float thirdPersonDistance = 4.5f;
        [Tooltip("어깨 너머 오프셋 (x = 오른쪽, y = 위)")]
        [SerializeField] private Vector2 thirdPersonShoulder = new Vector2(0.6f, 0.3f);
        [SerializeField] private float thirdPersonFov = 65f;
        [Tooltip("카메라가 벽/지형에 파묻히지 않게 당겨오는 대상. 플레이어 레이어는 빼 둘 것")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionRadius = 0.25f;
        [Tooltip("장애물이 사라졌을 때 카메라가 원래 거리로 돌아가는 시간")]
        [SerializeField] private float distanceRecoverTime = 0.25f;

        [Header("조준 줌 (우클릭)")]
        [Tooltip("조준 중 시야각 배율. 작을수록 크게 확대 (무기가 따로 값을 주면 그 값을 씀)")]
        [SerializeField, Range(0.2f, 1f)] private float aimFovMultiplier = 0.7f;
        [Tooltip("3인칭 조준 중 카메라 거리")]
        [SerializeField] private float aimThirdPersonDistance = 1.8f;
        [Tooltip("3인칭 조준 중 어깨 오프셋 (x = 오른쪽, y = 위)")]
        [SerializeField] private Vector2 aimThirdPersonShoulder = new Vector2(0.55f, 0.15f);
        [Tooltip("줌 인/아웃에 걸리는 시간(초)")]
        [SerializeField] private float aimZoomTime = 0.12f;

        private float yaw;
        private float pitch;
        private float blend;
        private float currentDistance;
        private float distanceVelocity;
        private float thirdPersonFovAdd;
        private bool aimRequested;
        private float aimFovTarget = -1f;
        private float aimZoom;
        private float aimZoomVelocity;
        private float aimFovCurrent = 0.7f;

        /// <summary>현재 목표 시점 (CameraModeSwitcher 가 설정)</summary>
        public CameraMode Mode { get; private set; } = CameraMode.FirstPerson;
        public float Blend => blend;
        public float Yaw => yaw;
        public float Pitch => pitch;
        public Camera ViewCamera => viewCamera;
        public Transform ViewPoint => viewCamera != null ? viewCamera.transform : transform;
        /// <summary>0 = 평소, 1 = 완전히 조준 줌</summary>
        public float AimZoom => aimZoom;

        private void Awake()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            currentDistance = thirdPersonDistance;
            if (pivot != null) yaw = pivot.eulerAngles.y;
        }

        private void Start()
        {
            var config = DataManager.Config;
            if (config.Has(GameConfig.BossCameraFovAdd)) thirdPersonFovAdd = config.GetFloat(GameConfig.BossCameraFovAdd);
        }

        /// <summary>감도·상하 제한 설정 (PlayerDef 값)</summary>
        public void Configure(float newSensitivity, float newPitchMin, float newPitchMax)
        {
            sensitivity = newSensitivity;
            pitchMin = newPitchMin;
            pitchMax = newPitchMax;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
        }

        /// <summary>마우스 픽셀 단위 회전량. x = 좌우(yaw), y = 상하(pitch, 위로 올리면 위를 봄)</summary>
        public void AddLook(Vector2 delta)
        {
            yaw = Mathf.Repeat(yaw + delta.x * sensitivity, 360f);
            pitch = Mathf.Clamp(pitch - delta.y * sensitivity, pitchMin, pitchMax);
        }

        public void SetMode(CameraMode mode) => Mode = mode;

        /// <summary>0 = 1인칭, 1 = 3인칭</summary>
        public void SetBlend(float value) => blend = Mathf.Clamp01(value);

        /// <summary>조준 줌 요청. 매 프레임 호출 (PlayerController). fovMultiplier 가 0 이하면 리그 기본값</summary>
        public void SetAiming(bool aiming, float fovMultiplier = -1f)
        {
            aimRequested = aiming;
            aimFovTarget = fovMultiplier;
        }

        private void LateUpdate()
        {
            if (viewCamera == null) return;

            // 조준 줌 진행도 (부드럽게)
            aimZoom = Mathf.SmoothDamp(aimZoom, aimRequested ? 1f : 0f, ref aimZoomVelocity, aimZoomTime);
            if (aimRequested) aimFovCurrent = aimFovTarget > 0f ? aimFovTarget : aimFovMultiplier;
            float zoomFov = Mathf.Lerp(1f, aimFovCurrent, aimZoom);

            Vector3 anchor = pivot != null ? pivot.position : transform.position;
            Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
            Quaternion lookRotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 position = anchor + yawRotation * firstPersonOffset;
            float fov = firstPersonFov;

            if (blend > 0.0001f)
            {
                position = Vector3.Lerp(position, ComputeThirdPersonPosition(anchor, yawRotation, lookRotation), blend);
                fov = Mathf.Lerp(firstPersonFov, thirdPersonFov + thirdPersonFovAdd, blend);
            }

            viewCamera.transform.SetPositionAndRotation(position, lookRotation);
            viewCamera.fieldOfView = fov * zoomFov;
        }

        private Vector3 ComputeThirdPersonPosition(Vector3 anchor, Quaternion yawRotation, Quaternion lookRotation)
        {
            // 조준 중엔 어깨 가까이 당겨 붙는다
            Vector2 shoulder = Vector2.Lerp(thirdPersonShoulder, aimThirdPersonShoulder, aimZoom);
            float distance = Mathf.Lerp(thirdPersonDistance, aimThirdPersonDistance, aimZoom);
            Vector3 origin = anchor + yawRotation * new Vector3(shoulder.x, shoulder.y, 0f);
            Vector3 back = lookRotation * Vector3.back;

            float allowed = distance;
            if (Physics.SphereCast(origin, collisionRadius, back, out var hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
                allowed = Mathf.Max(0f, hit.distance);

            // 장애물이 가까워질 땐 즉시, 멀어질 땐 부드럽게
            currentDistance = allowed < currentDistance
                ? allowed
                : Mathf.SmoothDamp(currentDistance, allowed, ref distanceVelocity, distanceRecoverTime);

            return origin + back * currentDistance;
        }
    }
}
