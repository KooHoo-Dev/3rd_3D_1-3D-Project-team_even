using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>
    /// 이동 실험실의 간단한 키보드 플레이어.
    /// 몬스터가 추적하는 대상(IPlayerTarget)이자 피격 대상(IDamageable)이다.
    /// 실제 플레이어가 구현되기 전까지의 임시 대역이며, 이식할 때는 Register/Unregister 만 걷어내면 된다.
    /// WASD 이동(시점 기준), Shift 달리기, 마우스 우클릭 드래그 시점 회전(TestFollowCamera 가 따라 돈다). 높이는 바닥(Terrain 및 다른 Collider)을 따라간다 (보급 테스트용 TesterPlayer 에서 이전).
    /// 이동은 kinematic Rigidbody 의 MovePosition(FixedUpdate) + 보간이라 화면에서 부드럽고, 트리거 충돌도 받을 수 있다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class TestPlayer : MonoBehaviour, IPlayerTarget, IDamageable
    {
        [Header("이동")]
        [Tooltip("걷기 속도 (m/s)")]
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [Tooltip("Shift 를 누르는 동안 걷기 속도에 곱하는 배율")]
        [SerializeField, Min(1f)] private float sprintMultiplier = 2.5f;
        [Tooltip("이동 방향으로 몸을 돌리는 속도 (도/s). 몬스터의 측면 공격(Flank)이 이 방향을 읽는다")]
        [SerializeField] private float turnSpeed = 540f;

        [Header("시야")]
        [Tooltip("플레이어 시야 카메라. ViewPoint 로 넘겨지며, 몬스터 이동(Think)이 이 카메라의 위치·방향·FOV 로 '시야 밖' 판정을 한다. 필수")]
        [SerializeField] private Camera viewCamera;

        [Header("시점 (마우스 우클릭 드래그)")]
        [Tooltip("마우스 1픽셀 이동당 시점 회전 각도 (도/픽셀)")]
        [SerializeField] private float lookSensitivity = 0.15f;
        [Tooltip("내려다보는 각도 하한 (도). 0 = 수평")]
        [SerializeField] private float minPitch = 10f;
        [Tooltip("내려다보는 각도 상한 (도). 90 = 바로 위")]
        [SerializeField] private float maxPitch = 85f;
        [Tooltip("Play 시작 시 내려다보는 각도 (도)")]
        [SerializeField] private float startPitch = 48f;

        [Header("바닥 높이 맞추기 (Terrain 포함 모든 Collider)")]
        [Tooltip("바닥 표면에서 transform.position.y 까지의 높이 (m). 실험실 타깃은 피벗이 발이라 기본 0")]
        [SerializeField] private float groundOffset = 0f;
        [Tooltip("현재 높이보다 이만큼 위까지의 표면은 올라설 수 있다 (m). 지붕/천장 위로 순간이동 방지")]
        [SerializeField] private float maxStepUp = 2f;
        [Tooltip("바닥으로 인식할 레이어")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("바닥으로 인식한 Collider 가 바뀔 때마다 콘솔에 출력하고, 레이를 Scene 뷰에 그린다")]
        [SerializeField] private bool debugGround;

        // 선언 시 생성: Awake 전(편집 모드 Gizmos 등)에도 null 이 아니다. PlayerController 와 같은 방식
        private readonly PlayerTrackingInfo playerTrackingInfo = new PlayerTrackingInfo();

        // ── 바닥 레이캐스트 ──
        private readonly RaycastHit[] groundHits = new RaycastHit[16];
        private Collider lastGround;   // debugGround 로그용: 직전에 바닥으로 인식한 Collider (null = 없음)

        // ── 이동 (Update 에서 읽은 입력을 FixedUpdate 가 적용) ──
        private Rigidbody body;
        private Vector3 moveDirection;   // 길이 최대 1 인 수평 방향. Vector3.zero = 입력 없음
        private bool isSprinting;

        // ── 시점 (우클릭 드래그로 바뀜, TestFollowCamera 가 읽음) ──
        private float viewYaw;     // 도. 0 = 월드 +Z 를 봄
        private float viewPitch;   // 도. minPitch ~ maxPitch

        /// <summary>우클릭 드래그로 바뀌는 시점 각도. 카메라(TestFollowCamera)가 읽어서 플레이어 주위를 돈다</summary>
        public float ViewYaw => viewYaw;
        public float ViewPitch => viewPitch;

        // ── IPlayerTarget ──
        public Transform Root => transform;
        // 시야 카메라의 transform. MonsterMovement.Think 가 여기서 Camera 를 꺼내 FOV 를 읽는다 (viewCamera 미지정 시 예외)
        public Transform ViewPoint => viewCamera.transform;

        public PlayerTrackingInfo PlayerTrackingInfo => playerTrackingInfo;

        // IDamageable 의 IsAlive
        public bool IsAlive => isActiveAndEnabled;

        // ── IDamageable: 몬스터 공격이 피해를 줄 때 쓰는 값 (공격 시 IDamageable 이 없으면 예외) ──
        public Faction Faction => Faction.Player;
        public void TakeDamage(in DamageInfo info) =>
            Debug.Log($"[Lab 플레이어] 피해 {info.Amount} ({info.Kind})", this);

        /// <summary>실제 게임과 같은 경로: 몬스터의 MonsterController.AcquireTarget 이 ServiceLocator 에서 꺼내 간다</summary>

        #region unity method

        private void OnEnable() => ServiceLocator.Register<IPlayerTarget>(this);

        private void OnDisable() => ServiceLocator.Unregister<IPlayerTarget>(this);

        /// <summary>
        /// Rigidbody 를 물리에 밀리지 않는 kinematic 으로 설정하고,
        /// 첫 LateUpdate 전에 몬스터가 읽어도 원점이 아닌 현재 위치를 보도록 값을 채운다
        /// </summary>
        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            viewPitch = Mathf.Clamp(startPitch, minPitch, maxPitch);

            playerTrackingInfo.Position = transform.position;
            playerTrackingInfo.Forward = transform.forward;
            playerTrackingInfo.Rotation = transform.rotation;
        }

        /// <summary>시작 위치를 지형 위로 맞춘다. 맵 위 아무 높이에 놓아도 바닥에 선다</summary>
        private void Start()
        {
            Vector3 start = body.position;
            start.y = GetGroundedY(start);
            body.position = start;
            transform.position = start;
        }

        /// <summary>
        /// 우클릭 드래그(시점), WASD(이동 방향), Shift(달리기) 입력을 읽어 둔다. 실제 이동은 FixedUpdate 가 한다.
        /// 이동 방향은 시점(yaw) 기준으로 돌려서, 카메라가 보는 쪽이 항상 W 가 되게 한다
        /// </summary>
        private void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null) return;
            if (keyboard == null) return;

            ReadView(mouse);
            moveDirection = Quaternion.Euler(0f, viewYaw, 0f) * ReadMoveDirection(keyboard);
            isSprinting = keyboard.leftShiftKey.isPressed;
        }

        /// <summary>입력 방향으로 이동하고 바닥 높이에 맞춘 뒤, 이동 방향으로 몸을 돌린다</summary>
        private void FixedUpdate()
        {
            if (moveDirection == Vector3.zero) return;

            Vector3 next = body.position + moveDirection * (GetMoveSpeed() * Time.fixedDeltaTime);
            next.y = GetGroundedY(next);
            body.MovePosition(next);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation,
                Quaternion.LookRotation(moveDirection, Vector3.up), turnSpeed * Time.fixedDeltaTime));
        }

        /// <summary>몬스터가 읽는 위치·방향·회전을 현재 transform 으로 맞춘다 (PlayerController.SetPlayerTrackingInfo 와 같은 역할)</summary>
        private void LateUpdate()
        {
            playerTrackingInfo.Position = transform.position;
            playerTrackingInfo.Forward = transform.forward;
            playerTrackingInfo.Rotation = transform.rotation;
        }

        #endregion

        /// <summary>우클릭을 누르고 있는 동안 마우스 이동량만큼 시점 각도(viewYaw/viewPitch)를 돌린다 (보급 테스트용 TesterPlayer 와 같은 방식)</summary>
        /// <param name="mouse">현재 마우스</param>
        private void ReadView(Mouse mouse)
        {
            if (mouse.rightButton.isPressed == false) return;

            Vector2 delta = mouse.delta.ReadValue();
            viewYaw += delta.x * lookSensitivity;
            viewPitch = Mathf.Clamp(viewPitch - delta.y * lookSensitivity, minPitch, maxPitch);
        }

        /// <summary>WASD 를 시점 회전 전의 XZ 방향으로 바꾼다 (W = +Z)</summary>
        /// <param name="keyboard">현재 키보드</param>
        /// <returns>길이 최대 1 인 수평 방향. 입력이 없으면 Vector3.zero</returns>
        private static Vector3 ReadMoveDirection(Keyboard keyboard)
        {
            float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            return Vector3.ClampMagnitude(new Vector3(x, 0f, z), 1f);
        }

        /// <summary>Shift 를 누르고 있으면 달리기 속도, 아니면 걷기 속도</summary>
        /// <returns>이번 물리 프레임 이동 속도 (m/s)</returns>
        private float GetMoveSpeed()
        {
            if (isSprinting) return moveSpeed * sprintMultiplier;
            return moveSpeed;
        }

        /// <summary>
        /// 해당 XZ 에서 아래로 레이를 쏴 가장 가까운 바닥(Terrain, 메시 등 모든 Collider) 높이 + offset.
        /// 자기 자신과 Trigger 는 무시하고, 맞은 게 없으면 현재 높이를 유지한다
        /// </summary>
        /// <param name="position">높이를 구할 위치. y 는 레이 시작 높이 기준(y + maxStepUp)으로 쓴다</param>
        /// <returns>서야 할 transform.position.y (m)</returns>
        private float GetGroundedY(Vector3 position)
        {
            Vector3 origin = new Vector3(position.x, position.y + maxStepUp, position.z);
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits, 2000f, groundMask, QueryTriggerInteraction.Ignore);

            float bestDistance = float.MaxValue;
            float groundY = position.y - groundOffset;
            Collider ground = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = groundHits[i];
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.distance >= bestDistance) continue;
                bestDistance = hit.distance;
                groundY = hit.point.y;
                ground = hit.collider;
            }

            if (debugGround) LogGroundChange(origin, ground, groundY, count);
            return groundY + groundOffset;
        }

        /// <summary>debugGround 용: 레이를 Scene 뷰에 그리고, 바닥 Collider 가 바뀌었을 때만 콘솔에 출력한다</summary>
        /// <param name="origin">레이 시작점</param>
        /// <param name="ground">이번에 바닥으로 인식한 Collider. null = 맞은 바닥 없음</param>
        /// <param name="groundY">바닥 표면 높이 (m)</param>
        /// <param name="hitCount">레이에 맞은 Collider 수</param>
        private void LogGroundChange(Vector3 origin, Collider ground, float groundY, int hitCount)
        {
            Color rayColor = Color.red;
            if (ground != null) rayColor = Color.green;
            Debug.DrawRay(origin, Vector3.down * 30f, rayColor);

            if (ground == lastGround) return;
            lastGround = ground;

            string groundName = "none";
            if (ground != null) groundName = $"{ground.name} (y={groundY:F2})";
            Debug.Log($"[Lab 플레이어] ground: {groundName} / rayOriginY={origin.y:F2} hits={hitCount}", ground);
        }
    }
}
