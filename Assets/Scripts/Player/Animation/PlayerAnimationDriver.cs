using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 플레이어 애니메이터 드라이버. PlayerController 의 공개 상태를 **읽기만** 해서 Player_AC 파라미터를 채운다.
    /// 이동 코드와 애니메이션 코드를 분리하기 위한 컴포넌트 (Player 루트에 붙인다).
    ///
    /// 현재 단계(3-5): 대기 · 8방향 이동 · 전력질주 · 점프 · 낙하 · 착지 · 상체 자세 · 재장전 · 꺼내기 · 구르기 · 피격 · 사망
    ///  - MoveX / MoveY = 몸 기준 실제 속도(m/s). 블렌드트리 좌표가 클립 실제 속도라 그대로 넣는다
    ///  - 달리기 클립보다 빠르면 좌표는 달리기에 두고 MoveSpeedScale 로 재생 속도만 올린다 (상한 maxPlaybackScale)
    ///  - Sprint = 전력질주 입력 중이고 몸 정면 쪽으로 움직일 때만 (전력질주 클립은 정면만 있다)
    ///  - Jump = PlayerController.Jumped 순간, Grounded = 지상 또는 급경사 미끄럼
    ///  - Falling = 최대 하강 속도에 닿았을 때만 (높은 곳에서 떨어질 때. 보통 점프는 점프 동작 그대로 착지)
    ///  - LandType = 공중에서 가장 빨리 떨어진 속도로 착지 세기 결정 (최대 하강 속도까지 갔으면 무겁게)
    ///  - Aim = 0 들고 다니기 ↔ 1 조준 (조준 중이거나 막 쏜 뒤). Upper Pose 레이어(팔·머리)는 총이 있으면 항상 켬
    ///    → 이동·점프 동작은 다리·몸통에만 보이고 팔과 총은 이 자세
    ///  - Reload / Draw = 재장전 시작(EventBus) / 무기 교체(GunSlot.GunChanged). ActionSpeed 로 실제 시간에 맞춤.
    ///    ActionEnd = 장전 종료·취소, 꺼내기 중 조준·사격. 동작 중에는 ActionBlend 가 0 → 조준 IK 를 푼다
    ///  - Dodge = RollStarted (방향은 몸 기준, 재생 속도 = 구르기 + 회복 시간), Rolling = 물리 구르기 중
    ///  - Hit = PlayerDamagedEvent (살아 있을 때만, 맞은 쪽 방향), Die·Dead·DeathDir = PlayerController.Died (마지막으로 맞은 방향)
    /// 설계: Assets/Scenes/MS/Docs/AnimationDesign.md
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(50)] // PlayerController.Update(입력) 뒤, 애니메이터 갱신 전
    public class PlayerAnimationDriver : MonoBehaviour
    {
        [Tooltip("비워 두면 같은 오브젝트에서 찾는다")]
        [SerializeField] private PlayerController player;
        [Tooltip("몸(Body) 애니메이터. 비워 두면 자식에서 찾는다")]
        [SerializeField] private Animator animator;
        [Tooltip("클립 속도·조정값. 빌더 메뉴가 만든 PlayerAnimationProfile")]
        [SerializeField] private PlayerAnimationProfile profile;
        [Tooltip("비워 두면 같은 오브젝트에서 찾는다")]
        [SerializeField] private GunSlot gunSlot;

        private bool ready;
        private int upperPoseLayer = -1;
        private int upperActionLayer = -1;
        private float aimWeight;
        private float aimPoseUntil;            // 조준 없이 쏜 직후 이 시각까지 조준 자세 유지
        private PlayerAnimationProfile.WeaponSet currentSet;
        private bool wasGrounded = true;
        private float airMinVerticalSpeed;     // 공중에 있는 동안 가장 빠르게 떨어진 속도 (음수)
        private bool reloading;
        private bool drawing;
        private float drawEndTime;
        private float actionBlend = 1f;        // 1 = 동작 없음, 0 = 재장전·꺼내기 중
        private int hitReactionLayer = -1;
        private Vector2 lastHitDirection = Vector2.up; // 몸 기준 맞은 쪽 (x = 오른쪽, y = 앞). 사망 방향에도 쓴다

        public Animator Animator => animator;
        public PlayerAnimationProfile.WeaponSet CurrentSet => currentSet;
        /// <summary>0 = 들고 다니기, 1 = 조준 (부드럽게 변함). AimIK 가중치에도 쓴다</summary>
        public float AimWeight => aimWeight;
        /// <summary>상체 자세 레이어 가중치 (총이 없거나 사망·구르기 중이면 0)</summary>
        public float UpperPoseWeight => ready && upperPoseLayer >= 0 ? animator.GetLayerWeight(upperPoseLayer) : 0f;
        /// <summary>1 = 팔 동작 없음, 0 = 재장전·꺼내기 중 (조준 IK 를 이 비율로 줄인다)</summary>
        public float ActionBlend => actionBlend;
        public PlayerAnimationProfile Profile => profile;

        private void Awake()
        {
            if (player == null) player = GetComponent<PlayerController>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (gunSlot == null) gunSlot = GetComponent<GunSlot>();

            ready = player != null && animator != null && animator.runtimeAnimatorController != null && profile != null;
            if (ready == false)
            {
                Debug.LogWarning($"{name}: PlayerAnimationDriver 준비 안 됨 (PlayerController / Animator·Controller / Profile 확인). " +
                                 "Tools/HybridShooter/Build Player Animator 를 실행하세요", this);
                enabled = false;
                return;
            }

            // 위치는 리지드바디가 움직인다. 루트모션이 켜져 있으면 몸이 캡슐에서 빠져나간다
            animator.applyRootMotion = false;
            currentSet = profile.DefaultSet;
            animator.SetFloat(PlayerAnimNames.Hash.MoveSpeedScale, 1f);
            upperPoseLayer = animator.GetLayerIndex(PlayerAnimNames.Layers.UpperPose);
            upperActionLayer = animator.GetLayerIndex(PlayerAnimNames.Layers.UpperAction);
            hitReactionLayer = animator.GetLayerIndex(PlayerAnimNames.Layers.HitReaction);
            animator.SetFloat(PlayerAnimNames.Hash.DodgeSpeed, 1f);
            animator.SetFloat(PlayerAnimNames.Hash.ActionSpeed, 1f);
        }

        private void OnEnable()
        {
            if (ready == false) return;
            player.Jumped += OnJumped;
            player.RollStarted += OnRollStarted;
            player.Died += OnDied;
            EventBus<PlayerDamagedEvent>.Subscribe(OnDamaged);
            EventBus<WeaponFiredEvent>.Subscribe(OnWeaponFired);
            EventBus<ReloadStartedEvent>.Subscribe(OnReloadStarted);
            EventBus<ReloadFinishedEvent>.Subscribe(OnReloadFinished);
            if (gunSlot != null) gunSlot.GunChanged += OnGunChanged;
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.Jumped -= OnJumped;
                player.RollStarted -= OnRollStarted;
                player.Died -= OnDied;
            }
            EventBus<PlayerDamagedEvent>.Unsubscribe(OnDamaged);
            EventBus<WeaponFiredEvent>.Unsubscribe(OnWeaponFired);
            EventBus<ReloadStartedEvent>.Unsubscribe(OnReloadStarted);
            EventBus<ReloadFinishedEvent>.Unsubscribe(OnReloadFinished);
            if (gunSlot != null) gunSlot.GunChanged -= OnGunChanged;
        }

        private void Update()
        {
            if (ready == false) return;
            float dt = Time.deltaTime;
            UpdateLocomotion(dt);
            UpdateAirborne();
            UpdateUpperPose(dt);
            UpdateActions(dt);
            animator.SetBool(PlayerAnimNames.Hash.Rolling, player.IsRolling);
            if (hitReactionLayer >= 0) animator.SetLayerWeight(hitReactionLayer, profile.hitReactionWeight);
        }

        // 조준 없이 쏴도 쏘는 동안은 총을 겨눈 자세 (지금 든 총만 이 이벤트를 보낸다)
        private void OnWeaponFired(WeaponFiredEvent evt)
        {
            aimPoseUntil = Time.time + profile.aimPoseAfterFire;
            if (drawing) EndAction(); // 꺼내기 중에 쏘면 꺼내기를 끊고 사격 자세로
        }

        #region 구르기 · 피격 · 사망
        /// <summary>구르기: 몸 기준 방향 4방향 블렌드. 동작 길이 = 물리 구르기 + 일어나는 시간 (클립 평균 길이를 여기에 맞춘다)</summary>
        private void OnRollStarted(Vector3 worldDirection)
        {
            Vector3 local = animator.transform.InverseTransformDirection(worldDirection);
            Vector2 dir = new Vector2(local.x, local.z);
            if (dir.sqrMagnitude < 0.0001f) dir = Vector2.up;
            dir.Normalize();
            animator.SetFloat(PlayerAnimNames.Hash.DodgeX, dir.x);
            animator.SetFloat(PlayerAnimNames.Hash.DodgeY, dir.y);

            float duration = player.RollDuration + profile.dodgeRecoveryTime;
            float length = currentSet != null ? currentSet.dodgeClipLength : 0f;
            float speed = duration > 0f && length > 0f ? length / duration : 1f;
            animator.SetFloat(PlayerAnimNames.Hash.DodgeSpeed, Mathf.Clamp(speed, profile.dodgeSpeedRange.x, profile.dodgeSpeedRange.y));
            animator.SetTrigger(PlayerAnimNames.Hash.Dodge);

            // 구르는 동안 팔 동작(재장전·꺼내기)은 끝낸다 (전신 구르기)
            if (reloading || drawing) EndAction();
        }

        /// <summary>피격: 맞은 쪽(피해가 날아온 반대) 방향으로 움찔. 이번 피해로 죽었으면 사망 동작만</summary>
        private void OnDamaged(PlayerDamagedEvent evt)
        {
            lastHitDirection = HitSourceDirection(evt.Info);
            if (player.IsAlive == false) return;
            animator.SetFloat(PlayerAnimNames.Hash.HitX, lastHitDirection.x);
            animator.SetFloat(PlayerAnimNames.Hash.HitY, lastHitDirection.y);
            animator.SetTrigger(PlayerAnimNames.Hash.Hit);
        }

        /// <summary>사망: 마지막으로 맞은 방향으로 앞(0)·뒤(1)·왼쪽(2)·오른쪽(3)</summary>
        private void OnDied()
        {
            Vector2 d = lastHitDirection;
            int dir = Mathf.Abs(d.y) >= Mathf.Abs(d.x) ? (d.y >= 0f ? 0 : 1) : (d.x < 0f ? 2 : 3);
            if (reloading || drawing) EndAction();
            animator.ResetTrigger(PlayerAnimNames.Hash.Hit);
            animator.ResetTrigger(PlayerAnimNames.Hash.Dodge);
            animator.SetInteger(PlayerAnimNames.Hash.DeathDir, dir);
            animator.SetBool(PlayerAnimNames.Hash.Dead, true);
            animator.SetTrigger(PlayerAnimNames.Hash.Die);
        }

        /// <summary>몸 기준 맞은 쪽. 피해 방향(공격자 → 플레이어)의 반대. 없으면 공격자 위치, 그것도 없으면 정면</summary>
        private Vector2 HitSourceDirection(in DamageInfo info)
        {
            Vector3 from = -info.Direction;
            if (from.sqrMagnitude < 0.0001f && info.Attacker != null) from = info.Attacker.transform.position - transform.position;
            from.y = 0f;
            if (from.sqrMagnitude < 0.0001f) return Vector2.up;
            Vector3 local = animator.transform.InverseTransformDirection(from.normalized);
            Vector2 dir = new Vector2(local.x, local.z);
            return dir.sqrMagnitude < 0.0001f ? Vector2.up : dir.normalized;
        }
        #endregion

        #region 재장전 · 꺼내기
        /// <summary>재장전 시작: 클립 길이를 실제 재장전 시간에 맞춰 재생 (산탄총처럼 길면 느리게, 빠른 장전은 빠르게)</summary>
        private void OnReloadStarted(ReloadStartedEvent evt)
        {
            float length = currentSet != null ? currentSet.reloadClipLength : 0f;
            StartAction(PlayerAnimNames.Hash.Reload, length, evt.Duration);
            reloading = true;
            drawing = false;
        }

        /// <summary>장전 완료·취소 (쏘거나 무기를 바꾸면 취소된다): 동작을 바로 끝낸다</summary>
        private void OnReloadFinished(ReloadFinishedEvent evt)
        {
            if (reloading == false) return;
            reloading = false;
            EndAction();
        }

        /// <summary>무기 교체: 꺼내기 동작 (drawDuration 안에 끝나도록 빠르게). 총이 없어지면 재생하지 않는다</summary>
        private void OnGunChanged(int slot, Gun gun)
        {
            reloading = false;
            if (gun == null || profile.playDraw == false || currentSet == null) return;
            StartAction(PlayerAnimNames.Hash.Draw, currentSet.drawClipLength, profile.drawDuration);
            drawing = true;
            drawEndTime = Time.time + profile.drawDuration;
        }

        private void StartAction(int trigger, float clipLength, float duration)
        {
            float speed = duration > 0f && clipLength > 0f ? clipLength / duration : 1f;
            animator.SetFloat(PlayerAnimNames.Hash.ActionSpeed, Mathf.Clamp(speed, profile.actionSpeedRange.x, profile.actionSpeedRange.y));
            animator.ResetTrigger(PlayerAnimNames.Hash.ActionEnd);
            animator.ResetTrigger(PlayerAnimNames.Hash.Reload);
            animator.ResetTrigger(PlayerAnimNames.Hash.Draw);
            animator.SetTrigger(trigger);
        }

        private void EndAction()
        {
            drawing = false;
            animator.ResetTrigger(PlayerAnimNames.Hash.Reload);
            animator.ResetTrigger(PlayerAnimNames.Hash.Draw);
            animator.SetTrigger(PlayerAnimNames.Hash.ActionEnd);
        }

        /// <summary>
        /// 꺼내기 중 조준하면 끊는다. Upper Action 레이어가 동작 중(Empty 가 아님)이면 ActionBlend 를 0 으로 → 조준 IK 를 푼다
        /// (재장전 중 손이 탄창으로 가는데 IK 가 총구를 조준점으로 돌리면 몸이 비틀린다)
        /// </summary>
        private void UpdateActions(float dt)
        {
            if (drawing && (gunSlot != null && gunSlot.IsAiming || Time.time > drawEndTime + 0.1f)) EndAction();

            bool acting = false;
            if (upperActionLayer >= 0)
            {
                acting = animator.GetCurrentAnimatorStateInfo(upperActionLayer).shortNameHash != PlayerAnimNames.Hash.EmptyState
                         || (animator.IsInTransition(upperActionLayer)
                             && animator.GetNextAnimatorStateInfo(upperActionLayer).shortNameHash != PlayerAnimNames.Hash.EmptyState);
                animator.SetLayerWeight(upperActionLayer, upperPoseLayer >= 0 ? animator.GetLayerWeight(upperPoseLayer) : 1f);
            }
            float step = profile.actionBlendTime > 0f ? dt / profile.actionBlendTime : 1f;
            actionBlend = Mathf.MoveTowards(actionBlend, acting ? 0f : 1f, step);
        }
        #endregion

        /// <summary>
        /// 상체 자세: Aim 을 조준 여부에 맞춰 aimBlendTime 에 걸쳐 0 ↔ 1 로. 공중에서는 조준이 풀리므로(PlayerController) 들고 다니기 자세.
        /// 레이어는 총이 있으면 켜고, 사망·구르기·벽넘기 중에는 끈다 (그 동작은 전신이 맞다)
        /// </summary>
        private void UpdateUpperPose(float dt)
        {
            bool hasGun = gunSlot != null && gunSlot.CurrentGun != null;
            bool wantAim = hasGun && (gunSlot.IsAiming || Time.time < aimPoseUntil);
            float aimStep = profile.aimBlendTime > 0f ? dt / profile.aimBlendTime : 1f;
            aimWeight = Mathf.MoveTowards(aimWeight, wantAim ? 1f : 0f, aimStep);
            animator.SetFloat(PlayerAnimNames.Hash.Aim, aimWeight);

            if (upperPoseLayer < 0) return;
            bool on = hasGun && player.IsAlive && player.IsRolling == false && player.IsExternallyControlled == false;
            float layerStep = profile.upperPoseBlendTime > 0f ? dt / profile.upperPoseBlendTime : 1f;
            animator.SetLayerWeight(upperPoseLayer, Mathf.MoveTowards(animator.GetLayerWeight(upperPoseLayer), on ? 1f : 0f, layerStep));
        }

        private void OnJumped() => animator.SetTrigger(PlayerAnimNames.Hash.Jump);

        /// <summary>
        /// 지상·낙하·착지 세기. 착지하는 프레임에 LandType 을 먼저 정하고 Grounded 를 켠다 (같은 프레임에 전이가 고르도록).
        /// 급경사 미끄럼은 땅에 붙어 있는 중이라 지상으로 본다
        /// </summary>
        private void UpdateAirborne()
        {
            bool grounded = player.IsGrounded || player.IsSliding;
            float vy = player.Velocity.y;

            float terminal = player.MaxFallSpeed * profile.fallTerminalRatio;

            if (grounded == false)
            {
                airMinVerticalSpeed = wasGrounded ? Mathf.Min(0f, vy) : Mathf.Min(airMinVerticalSpeed, vy);
            }
            else if (wasGrounded == false)
            {
                float impact = -airMinVerticalSpeed;
                int landType = impact >= terminal ? 2 : impact >= profile.landMediumSpeed ? 1 : 0;
                animator.SetInteger(PlayerAnimNames.Hash.LandType, landType);
                airMinVerticalSpeed = 0f;
            }

            // 최대 하강 속도에 닿을 만큼 높은 곳에서 떨어질 때만 낙하 동작 (보통 점프·턱은 해당 없음)
            bool falling = grounded == false && terminal > 0f && vy <= -terminal;
            animator.SetBool(PlayerAnimNames.Hash.Grounded, grounded);
            animator.SetBool(PlayerAnimNames.Hash.Falling, falling);
            // 착지 직전에 눌러 소비되지 않은 점프 트리거가 다음 점프 때 엉뚱하게 발동하지 않게
            // (점프한 순간에는 PlayerController 가 바로 공중으로 바꾸므로 여기 걸리지 않는다)
            if (grounded && wasGrounded) animator.ResetTrigger(PlayerAnimNames.Hash.Jump);
            wasGrounded = grounded;
        }

        /// <summary>몸 기준 이동 속도 → MoveX / MoveY / MoveSpeedScale / Sprint</summary>
        private void UpdateLocomotion(float dt)
        {
            Vector3 velocity = player.Velocity;
            velocity.y = 0f;
            // 몸(Body)의 방향 기준. PlayerController 가 매 프레임 시선 yaw 로 돌려 둔다
            Vector3 local = animator.transform.InverseTransformDirection(velocity);
            Vector2 move = new Vector2(local.x, local.z);
            float speed = move.magnitude;

            float run = currentSet != null && currentSet.runSpeed > 0f ? currentSet.runSpeed : 2.5f;
            float scale = 1f;
            if (speed > run)
            {
                scale = Mathf.Min(speed / run, profile.maxPlaybackScale);
                move *= run / speed;
            }

            float damp = profile.moveDampTime;
            animator.SetFloat(PlayerAnimNames.Hash.MoveX, move.x, damp, dt);
            animator.SetFloat(PlayerAnimNames.Hash.MoveY, move.y, damp, dt);
            animator.SetFloat(PlayerAnimNames.Hash.MoveSpeedScale, scale, damp, dt);

            animator.SetBool(PlayerAnimNames.Hash.Moving, speed > profile.movingSpeed);

            bool forward = speed > 0.1f && local.z / speed >= profile.sprintForwardDot;
            // 공중에서는 바꾸지 않는다: 점프하면 PlayerController 의 전력질주가 꺼지는데(지상 조건), 그때 Sprint 가 꺼지면
            // 애니메이터가 점프 대신 달리기로 먼저 넘어가려 한다
            if (player.IsOnGround) animator.SetBool(PlayerAnimNames.Hash.Sprint, player.IsSprinting && forward);
        }
    }
}
