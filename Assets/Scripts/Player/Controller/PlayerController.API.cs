using System;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>바깥에서 쓰는 이벤트·프로퍼티·메서드</summary>
    public partial class PlayerController
    {
        public event Action Jumped;
        public event Action Landed;
        /// <summary>인자 = 구르는 월드 방향</summary>
        public event Action<Vector3> RollStarted;
        public event Action RollEnded;
        /// <summary>체력이 0 이 된 순간 한 번 (애니메이션 등)</summary>
        public event Action Died;
        /// <summary>시점 전환이 끝났을 때 알림 (UI 등)</summary>
        public event Action<ViewMode> ViewModeChanged;

        public bool IsGrounded => isGrounded;
        /// <summary>발이 땅에 닿아 있음 (걸을 수 있는 땅 또는 급경사 미끄럼). 공중이면 조준·수류탄·회복을 못 한다</summary>
        public bool IsOnGround => isGrounded || isSliding;
        /// <summary>바닥으로 보는 레이어 (발 IK 도 같은 레이어를 쓴다)</summary>
        public LayerMask GroundMask => groundMask;
        /// <summary>최대 하강 속도(m/s, 양수)</summary>
        public float MaxFallSpeed => maxFallSpeed;
        public bool IsRolling => isRolling;
        /// <summary>급경사(maxSlopeAngle 초과)에 닿아 미끄러지는 중</summary>
        public bool IsSliding => isSliding;
        /// <summary>발 → 발밑 땅 거리(m). 3m 보다 멀거나 땅이 없으면 무한대</summary>
        public float GroundDistance => groundDistance;
        public bool IsInvulnerable => isRolling && Time.time - rollStartTime < rollInvulnerableTime;
        public bool IsSprinting => sprintRequested && CanSprint && isGrounded && isRolling == false && HorizontalSpeed > 0.1f;
        /// <summary>구르는 중이 아니고, 지상이고, 쿨다운이 끝났고, 스태미나가 rollStaminaCost 이상</summary>
        public bool CanRoll => isRolling == false && isGrounded && Time.time >= rollReadyTime && HasRollStamina;
        public Vector3 Velocity => rb.linearVelocity;
        public float HorizontalSpeed { get { var v = rb.linearVelocity; return new Vector2(v.x, v.z).magnitude; } }
        public float WalkSpeed { get => walkSpeed; set => walkSpeed = Mathf.Max(0f, value); }

        public Vector3 Forward => rb.rotation * Vector3.forward;
        public float RollDuration => rollDuration;
        public float SprintMultiplier => sprintMultiplier;
        /// <summary>벽넘기 등으로 바깥(애니메이션 루트모션)이 위치를 움직이는 중. 이 동안 모터는 물리를 건드리지 않는다</summary>
        public bool IsExternallyControlled => externalControl;

        public ViewMode CurrentViewMode => viewMode;
        public bool IsFirstPerson => viewMode == ViewMode.FirstPerson;
        /// <summary>시점 전환 연출 중 (이 동안 전환 입력은 무시된다)</summary>
        public bool CameraChanging { get; private set; }
        /// <summary>지금 화면을 그리는 카메라의 Transform</summary>
        public Transform CurrentCameraTransform => currentCameraTransform;
        /// <summary>0 = 3인칭, 1 = 1인칭. 시점 전환 연출 중에는 그 진행도를 따라 부드럽게 변한다 (1인칭 무기 IK 가중치)</summary>
        public float FirstPersonBlend => CameraChanging
            ? (transitionTarget == ViewMode.FirstPerson ? transitionT : 1f - transitionT)
            : (IsFirstPerson ? 1f : 0f);
        /// <summary>1인칭 눈(FP 카메라의 원래 자리). 전환 연출로 카메라가 날아가는 중에도 눈 자리는 그대로다</summary>
        public Pose FirstPersonEyePose => rigReady
            ? new Pose(FirstParsonCam.parent.TransformPoint(fpCamHomePos), FirstParsonCam.parent.rotation * fpCamHomeRot)
            : new Pose(transform.position, transform.rotation);
        /// <summary>1인칭 카메라</summary>
        public Camera FirstPersonCamera => fpCamera;

        public float LookYaw => lookYaw;
        public float LookPitch => lookPitch;
        /// <summary>현재 시선 회전 (yaw + pitch). 조준·발사 방향 계산에 쓰면 된다</summary>
        public Quaternion LookRotation => Quaternion.Euler(lookPitch, lookYaw, 0f);

        /// <summary>디버그(애니메이션 떨림 측정): 키보드·마우스 입력을 읽지 않는다. 이동·시선은 바깥에서 SetMoveInput·DebugSetLook 으로 넣는다</summary>
        public bool DebugBlockInput { get; set; }
        /// <summary>디버그(1인칭 목 측정): null 이 아니면 neckPivotWeight 대신 이 값</summary>
        public float? DebugNeckPivotOverride { get; set; }

        /// <summary>디버그: 시선(yaw·pitch)을 바로 정하고 몸도 그 yaw 로 고정한다 (입력을 막았을 때 쓴다)</summary>
        public void DebugSetLook(float yaw, float pitch)
        {
            lookYaw = Mathf.Repeat(yaw, 360f);
            lookPitch = Mathf.Clamp(pitch, minPitch, MaxPitch);
            SyncRigRotationNow();
            LockFacing(lookYaw);
        }

        public Transform Root => rootTransform;
        public Transform AimPoint => aimPoint;
        public Transform ViewPoint => viewPoint != null ? viewPoint : (aimPoint != null ? aimPoint : transform);
        
        private readonly PlayerTrackingInfo playerTrackingInfo = new PlayerTrackingInfo();
        /// <summary>몬스터가 읽는 위치·방향·회전. FixedUpdate 에서 SetPlayerTrackingInfo 로 갱신된다</summary>
        public PlayerTrackingInfo PlayerTrackingInfo => playerTrackingInfo;

        public void SetPlayerTrackingInfo()
        {
            playerTrackingInfo.Position = transform.position;
            playerTrackingInfo.Rotation = transform.rotation;
            playerTrackingInfo.Forward = transform.forward;
        }
        
        /// <summary>worldDirection: 월드 기준, 수평, 크기 0~1 (스틱 기울기 반영)</summary>
        public void SetMoveInput(Vector3 worldDirection, bool sprint)
        {
            worldDirection.y = 0f;
            moveDirection = Vector3.ClampMagnitude(worldDirection, 1f);
            sprintRequested = sprint;
        }

        /// <summary>몸을 이 방향(yaw)에 고정. 1인칭·3인칭 공통으로 카메라 yaw 를 넘긴다</summary>
        public void LockFacing(float yaw) { facingMode = FacingMode.LockYaw; lockedYaw = yaw; }

        /// <summary>이동(구르기) 방향으로 몸을 돌림. 지금은 쓰지 않지만 나중에 자유 시점이 필요하면 호출</summary>
        public void FollowMoveFacing() => facingMode = FacingMode.FollowMove;

        public void RequestJump() => jumpRequestedTime = Time.time;

        /// <summary>시선을 이만큼 돌린다 (총 반동). pitch 음수 = 위로, yaw 양수 = 오른쪽</summary>
        public void AddLookOffset(float pitch, float yaw)
        {
            lookYaw = Mathf.Repeat(lookYaw + yaw, 360f);
            lookPitch = Mathf.Clamp(lookPitch + pitch, minPitch, MaxPitch);
            SyncRigRotationNow();
        }

        /// <summary>조준 확대. fovMultiplier 1 = 원래 시야, sensitivityMultiplier = 마우스 감도 배율</summary>
        public void SetAimZoom(float fovMultiplier, float sensitivityMultiplier)
        {
            aimFovTarget = Mathf.Clamp(fovMultiplier, 0.05f, 1f);
            aimSensitivity = Mathf.Clamp(sensitivityMultiplier, 0.05f, 1f);
        }

        /// <summary>방향은 호출 쪽이 정한다 (4방향 스냅 등). 지상이 아니거나 쿨다운·스태미나 부족이면 false</summary>
        public bool TryRoll(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (CanRoll == false || worldDirection.sqrMagnitude < 0.0001f) return false;

            SpendStamina(rollStaminaCost);
            isRolling = true;
            rollDirection = worldDirection.normalized;
            rollStartTime = Time.time;
            rollReadyTime = Time.time + rollDuration + rollCooldown;
            RollStarted?.Invoke(rollDirection);
            return true;
        }

        /// <summary>
        /// 지속되는 속도 배율 (스태미나 바닥 등). 같은 key 는 덮어쓰고, 1 이면 해제.
        /// </summary>
        public void SetSpeedModifier(string key, float multiplier)
        {
            RemoveModifier(key);
            if (Mathf.Approximately(multiplier, 1f) == false)
                modifiers.Add(new SpeedModifier { Key = key, Multiplier = multiplier, EndTime = -1f });
        }

        /// <summary>
        /// 시간제한 속도 배율. multiplier &lt; 1 감속, &gt; 1 가속. 같은 key 를 다시 걸면 시간이 갱신된다 (피격 연타 시 중첩되지 않음)
        /// </summary>
        public void AddTimedSpeedModifier(string key, float multiplier, float duration)
        {
            RemoveModifier(key);
            modifiers.Add(new SpeedModifier { Key = key, Multiplier = multiplier, EndTime = Time.time + duration });
        }

        public void RemoveSpeedModifier(string key) => RemoveModifier(key);

        public void Teleport(Vector3 position)
        {
            rb.position = position;
            rb.linearVelocity = Vector3.zero;
            isRolling = false;
        }

        /// <summary>
        /// 위치 제어를 바깥에 넘긴다 (벽넘기). Rigidbody 를 kinematic 으로 바꿔 충돌에 밀리지 않게 하고,
        /// 이후 <see cref="MoveExternal"/> 로 옮긴다. 끝나면 반드시 <see cref="EndExternalControl"/>.
        /// </summary>
        public void BeginExternalControl()
        {
            if (externalControl) return;
            externalControl = true;
            isRolling = false;
            moveDirection = Vector3.zero;
            rb.linearVelocity = Vector3.zero;
            savedInterpolation = rb.interpolation;
            // kinematic + 직접 위치 지정 시 보간이 켜져 있으면 한 프레임 늦게 따라와 떨린다
            rb.interpolation = RigidbodyInterpolation.None;
            rb.isKinematic = true;
        }

        /// <summary>외부 제어 중에만 의미 있음. 월드 기준으로 이만큼 이동 (루트모션 delta)</summary>
        public void MoveExternal(Vector3 delta)
        {
            if (externalControl == false) return;
            Vector3 p = rb.position + delta;
            rb.position = p;
            transform.position = p;
        }

        /// <summary>외부 제어 중 몸 방향을 즉시 바꾼다</summary>
        public void SetRotationImmediate(float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            rb.rotation = r;
            transform.rotation = r;
            lockedYaw = yaw;
            lookYaw = yaw;
        }

        public void EndExternalControl()
        {
            if (externalControl == false) return;
            externalControl = false;
            rb.isKinematic = false;
            rb.interpolation = savedInterpolation;
            rb.linearVelocity = Vector3.zero;
            jumpRequestedTime = float.NegativeInfinity;
        }
    }
}
