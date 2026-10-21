using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 게임 상태 → Animator 파라미터 번역기. 애니메이션에 관한 코드는 전부 여기에만 있다.
    /// 다른 컴포넌트는 "재장전 애니 틀어줘(PlayReload)" 처럼 의도만 말하고, 파라미터 이름은 모른다.
    ///
    ///  매 프레임 자동: 모터 속도 → MoveX/MoveY(블렌드 트리 방향), 바닥 여부, 낙하, 달리기, 조준 자세
    ///  이벤트 자동:   모터 점프/구르기, 체력 피격/사망/부활
    ///  요청:          무기 종류·사격·재장전·꺼내기·투척·회복·벽넘기 (PlayerWeapons 등이 호출)
    ///
    /// IK 를 켤지 말지도 여기서 판단한다 (상태 태그 NoIK / NoAim). PlayerHandIK 가 읽어 간다.
    /// 파라미터 이름은 PlayerAnimParams 참고.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor))]
    public class PlayerAnimator : MonoBehaviour
    {
        [Tooltip("비워 두면 자식에서 찾는다")]
        [SerializeField] private Animator animator;

        [Header("부드럽게")]
        [Tooltip("MoveX/MoveY 가 실제 속도를 따라가는 시간(초). 클수록 방향 전환이 부드럽고 느리다")]
        [SerializeField] private float moveDampTime = 0.1f;
        [Tooltip("조준 자세로 바뀌는 속도")]
        [SerializeField] private float aimBlendSpeed = 8f;
        [Tooltip("사격 후 이 시간 동안은 조준 버튼을 안 눌러도 총을 든 자세 유지")]
        [SerializeField] private float aimAfterFireTime = 0.6f;
        [Tooltip("발이 땅에서 떨어진 뒤 이 시간이 지나야 낙하 모션. 잔 요철에서 깜빡이는 것 방지")]
        [SerializeField] private float freeFallDelay = 0.15f;
        [Tooltip("달리기 클립의 원래 속도(m/s). 이보다 빨리 움직이면 이동 애니를 그만큼 빠르게 재생해 발 미끄러짐을 줄인다")]
        [SerializeField] private float runClipSpeed = 3.8f;
        [SerializeField] private float maxMoveAnimSpeed = 1.5f;

        private PlayerMotor motor;
        private PlayerHealth health;
        private Rigidbody rb;
        private readonly Dictionary<string, float> clipLengths = new Dictionary<string, float>();

        private int weaponType;
        private bool aimHeld;
        private float lastFireTime = float.NegativeInfinity;
        private float aimBlend;
        private float airTime;
        private int upperLayer = -1;
        private int hitLayer = -1;
        private Vector3 lastHitDirectionLocal = Vector3.forward;

        public Animator Animator => animator;
        public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;
        public int WeaponType => weaponType;
        /// <summary>0~1 현재 조준 자세 정도 (조준 IK 세기)</summary>
        public float AimBlend => aimBlend;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            health = GetComponent<PlayerHealth>();
            rb = GetComponent<Rigidbody>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (animator == null)
            {
                Debug.LogWarning("[PlayerAnimator] 자식에서 Animator 를 찾지 못했습니다. 애니메이션 없이 동작합니다.", this);
                return;
            }
            // 평소 이동은 Rigidbody 가 맡는다. 벽넘기 때만 PlayerVault 가 잠깐 켠다
            animator.applyRootMotion = false;
            CacheController();
        }

        private void CacheController()
        {
            clipLengths.Clear();
            if (HasAnimator == false)
            {
                Debug.LogWarning("[PlayerAnimator] Animator 에 컨트롤러가 없습니다. Tools > Player > 2. Build Animator Controller 를 실행하세요.", this);
                return;
            }
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
                if (clip != null) clipLengths[clip.name] = clip.length;
            upperLayer = animator.GetLayerIndex(PlayerAnimParams.UpperBodyLayer);
            hitLayer = animator.GetLayerIndex(PlayerAnimParams.HitLayer);
        }

        private void OnEnable()
        {
            motor.Jumped += OnJumped;
            motor.RollStarted += OnRollStarted;
            if (health != null)
            {
                health.Damaged += OnDamaged;
                health.Died += OnDied;
                health.Revived += OnRevived;
            }
        }

        private void OnDisable()
        {
            motor.Jumped -= OnJumped;
            motor.RollStarted -= OnRollStarted;
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.Died -= OnDied;
                health.Revived -= OnRevived;
            }
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        private void Update()
        {
            if (HasAnimator == false) return;
            float dt = Time.deltaTime;

            // 1) 이동 방향: 월드 속도를 "몸 기준"으로 바꾼다 → 앞(+Y)·뒤(-Y)·옆(±X). 단위는 m/s 그대로
            Vector3 v = rb.linearVelocity;
            Vector3 local = Quaternion.Inverse(transform.rotation) * new Vector3(v.x, 0f, v.z);
            if (motor.IsExternallyControlled) local = Vector3.zero;
            animator.SetFloat(PlayerAnimParams.MoveXId, local.x, moveDampTime, dt);
            animator.SetFloat(PlayerAnimParams.MoveYId, local.z, moveDampTime, dt);
            float planarSpeed = new Vector2(local.x, local.z).magnitude;
            animator.SetFloat(PlayerAnimParams.MoveAnimSpeedId,
                runClipSpeed > 0f ? Mathf.Clamp(planarSpeed / runClipSpeed, 1f, maxMoveAnimSpeed) : 1f);

            // 2) 바닥 · 공중
            bool grounded = motor.IsGrounded || motor.IsExternallyControlled;
            airTime = grounded ? 0f : airTime + dt;
            animator.SetBool(PlayerAnimParams.GroundedId, grounded);
            animator.SetBool(PlayerAnimParams.FreeFallId, airTime >= freeFallDelay);
            animator.SetFloat(PlayerAnimParams.VerticalSpeedId, v.y);
            if (grounded) animator.ResetTrigger(PlayerAnimParams.JumpId);

            // 3) 달리기 (조준 중이면 달리기 자세 대신 조준 자세)
            animator.SetBool(PlayerAnimParams.SprintingId, motor.IsSprinting && aimBlend < 0.5f);

            // 4) 조준 자세: 조준 버튼 or 방금 쏨
            bool wantAim = aimHeld || Time.time - lastFireTime < aimAfterFireTime;
            aimBlend = Mathf.MoveTowards(aimBlend, wantAim ? 1f : 0f, aimBlendSpeed * dt);
            animator.SetFloat(PlayerAnimParams.AimBlendId, aimBlend);
        }

        // ───────────────────────── 자동 이벤트 ─────────────────────────

        private void OnJumped()
        {
            if (HasAnimator) animator.SetTrigger(PlayerAnimParams.JumpId);
        }

        private void OnRollStarted(Vector3 worldDirection)
        {
            if (HasAnimator == false) return;
            Vector3 local = Quaternion.Inverse(transform.rotation) * worldDirection;
            animator.SetFloat(PlayerAnimParams.RollXId, local.x);
            animator.SetFloat(PlayerAnimParams.RollYId, local.z);
            // 구르기 애니메이션 길이를 모터의 구르기 시간에 맞춘다
            animator.SetFloat(PlayerAnimParams.RollSpeedId,
                SpeedFor(PlayerAnimParams.DodgeClip(weaponType, "Forward"), motor.RollDuration / 0.85f, 0.5f, 3f));
            animator.SetTrigger(PlayerAnimParams.RollId);
        }

        private void OnDamaged(DamageInfo info)
        {
            // 공격이 날아온 쪽(몸 기준). Direction 은 "공격자 → 나" 이므로 반대로 뒤집는다
            Vector3 from = -info.Direction;
            from.y = 0f;
            if (from.sqrMagnitude < 0.0001f) from = transform.forward;
            lastHitDirectionLocal = (Quaternion.Inverse(transform.rotation) * from).normalized;

            if (HasAnimator == false || health.IsAlive == false) return;
            if (motor.IsRolling || motor.IsExternallyControlled) return; // 구르기·벽넘기 중엔 움찔하지 않음
            animator.SetFloat(PlayerAnimParams.HitXId, lastHitDirectionLocal.x);
            animator.SetFloat(PlayerAnimParams.HitYId, lastHitDirectionLocal.z);
            animator.SetTrigger(PlayerAnimParams.HitId);
        }

        private void OnDied()
        {
            if (HasAnimator == false) return;
            SetFiring(false);
            animator.SetInteger(PlayerAnimParams.DeathDirectionId, DirectionIndex(lastHitDirectionLocal));
            animator.SetBool(PlayerAnimParams.DeadId, true);
            animator.SetTrigger(PlayerAnimParams.DieId);
            // 사망 모션은 온몸으로. 상체 동작 레이어는 끈다
            SetActionLayersWeight(0f);
        }

        private void OnRevived()
        {
            if (HasAnimator == false) return;
            // 죽어 있는 동안 쌓인 트리거가 부활 직후 튀어나오지 않게 비운다
            foreach (int id in new[] { PlayerAnimParams.JumpId, PlayerAnimParams.RollId, PlayerAnimParams.FireId, PlayerAnimParams.ReloadId,
                                       PlayerAnimParams.DrawId, PlayerAnimParams.ThrowId, PlayerAnimParams.HealId, PlayerAnimParams.HitId,
                                       PlayerAnimParams.DieId, PlayerAnimParams.VaultId })
                animator.ResetTrigger(id);
            animator.SetBool(PlayerAnimParams.DeadId, false);
            SetActionLayersWeight(1f);
        }

        // ───────────────────────── 요청 ─────────────────────────

        public void SetWeaponType(int type)
        {
            weaponType = Mathf.Clamp(type, 0, PlayerAnimParams.WeaponTypeCount - 1);
            if (HasAnimator) animator.SetInteger(PlayerAnimParams.WeaponTypeId, weaponType);
        }

        public void SetAimHeld(bool held) => aimHeld = held;

        /// <summary>연사 무기 사격 중 (누르고 있는 동안 true)</summary>
        public void SetFiring(bool firing)
        {
            if (HasAnimator) animator.SetBool(PlayerAnimParams.FiringId, firing);
            if (firing) lastFireTime = Time.time;
        }

        /// <summary>한 발 쏠 때마다. 단발 무기는 사격 모션을 한 번 재생</summary>
        public void PlayFireShot()
        {
            lastFireTime = Time.time;
            if (HasAnimator && PlayerAnimParams.IsLoopFireWeapon(weaponType) == false)
                animator.SetTrigger(PlayerAnimParams.FireId);
        }

        public void PlayReload(float duration)
            => PlayAction(PlayerAnimParams.ReloadId, ClipLength(PlayerAnimParams.ReloadClip(weaponType)) * PlayerAnimParams.ActionExitTime, duration);

        public void PlayDraw(float duration)
            => PlayAction(PlayerAnimParams.DrawId, ClipLength(PlayerAnimParams.DrawClip(weaponType)) * PlayerAnimParams.ActionExitTime, duration);

        public void PlayThrow(float duration)
            => PlayAction(PlayerAnimParams.ThrowId, ClipLength(PlayerAnimParams.ThrowClip(weaponType)) * PlayerAnimParams.ActionExitTime, duration);

        public void PlayHeal(float duration)
        {
            float length = ClipLength(PlayerAnimParams.HealRaiseClip(weaponType))
                         + ClipLength(PlayerAnimParams.HealHoldClip(weaponType)) * PlayerAnimParams.HealHoldPortion;
            PlayAction(PlayerAnimParams.HealId, length, duration);
        }

        public void PlayVault(int vaultType)
        {
            if (HasAnimator == false) return;
            SetFiring(false);
            animator.SetInteger(PlayerAnimParams.VaultTypeId, vaultType);
            animator.SetTrigger(PlayerAnimParams.VaultId);
        }

        /// <summary>벽넘기 상태에 들어가 있거나 들어가는 중인가</summary>
        public bool IsInVaultState()
        {
            if (HasAnimator == false) return false;
            return animator.GetCurrentAnimatorStateInfo(0).IsTag(PlayerAnimParams.TagVault)
                || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsTag(PlayerAnimParams.TagVault));
        }

        // ───────────────────────── IK 판단 (PlayerHandIK 가 읽음) ─────────────────────────

        /// <summary>왼손을 총에 붙일지 (0~1). 재장전·투척·회복·벽넘기·사망 중엔 0</summary>
        public float HandIKTarget
        {
            get
            {
                if (HasAnimator == false || (health != null && health.IsAlive == false)) return 0f;
                if (LayerHasTag(0, PlayerAnimParams.TagNoIK) || LayerHasTag(0, PlayerAnimParams.TagVault)) return 0f;
                if (upperLayer >= 0 && LayerHasTag(upperLayer, PlayerAnimParams.TagNoIK)) return 0f;
                return 1f;
            }
        }

        /// <summary>총구를 조준점으로 돌릴지 (0~1). 달리기·구르기 중엔 0, 평소엔 조준 자세 정도</summary>
        public float AimIKTarget
        {
            get
            {
                if (HandIKTarget <= 0f) return 0f;
                if (LayerHasTag(0, PlayerAnimParams.TagNoAim)) return 0f;
                return aimBlend;
            }
        }

        /// <summary>총을 조준선 쪽으로 들어도 되는 상태인가 (달리기·구르기·손 동작 중이면 false)</summary>
        public bool AimPostureAllowed => HandIKTarget > 0f && LayerHasTag(0, PlayerAnimParams.TagNoAim) == false;

        // ───────────────────────── 보조 ─────────────────────────

        private void PlayAction(int triggerId, float clipLength, float duration)
        {
            if (HasAnimator == false) return;
            animator.SetFloat(PlayerAnimParams.ActionSpeedId, duration > 0.01f && clipLength > 0f
                ? Mathf.Clamp(clipLength / duration, 0.25f, 4f) : 1f);
            animator.SetTrigger(triggerId);
        }

        private float SpeedFor(string clipName, float duration, float min, float max)
        {
            float length = ClipLength(clipName);
            return length > 0f && duration > 0.01f ? Mathf.Clamp(length / duration, min, max) : 1f;
        }

        private float ClipLength(string clipName) => clipLengths.TryGetValue(clipName, out float length) ? length : 0f;

        private bool LayerHasTag(int layer, string tag)
        {
            if (animator.GetCurrentAnimatorStateInfo(layer).IsTag(tag)) return true;
            return animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).IsTag(tag);
        }

        private void SetActionLayersWeight(float weight)
        {
            if (upperLayer >= 0) animator.SetLayerWeight(upperLayer, weight);
            if (hitLayer >= 0) animator.SetLayerWeight(hitLayer, weight);
        }

        /// <summary>몸 기준 방향 → 0 앞 · 1 뒤 · 2 왼쪽 · 3 오른쪽</summary>
        private static int DirectionIndex(Vector3 local)
        {
            if (Mathf.Abs(local.z) >= Mathf.Abs(local.x)) return local.z >= 0f ? 0 : 1;
            return local.x < 0f ? 2 : 3;
        }
    }
}
