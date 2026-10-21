using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 벽넘기 / 장애물 오르기. 점프 키를 눌렀을 때 바로 앞에 적당한 높이의 장애물이 있으면 점프 대신 이걸 한다.
    ///
    /// 1) 감지 : 무릎 높이로 앞을 쏴서 벽을 찾고 → 벽 위에서 아래로 쏴서 윗면 높이를 잰다 → 벽 뒤도 쏴서 두께를 본다
    /// 2) 종류 : 얇고 낮으면 "넘기(0)", 아니면 높이에 따라 "낮은 벽 오르기(1) / 1m 오르기(2) / 2m 오르기(3)"
    /// 3) 이동 : 모터를 잠시 멈추고(kinematic) 애니메이션 루트모션으로 몸을 옮긴다.
    ///           클립은 정해진 높이(0.5/1/2m)용이라 위아래 이동만 실제 높이 비율로 늘이거나 줄인다.
    /// 4) 끝   : 애니메이터가 Vault 태그 상태를 벗어나면 모터에 다시 넘긴다. 오르기는 윗면보다 낮으면 살짝 올려 맞춘다.
    /// </summary>
    [RequireComponent(typeof(PlayerMotor), typeof(PlayerAnimator), typeof(CapsuleCollider))]
    public class PlayerVault : MonoBehaviour
    {
        [Header("감지")]
        [Tooltip("몸 앞 이 거리 안의 장애물만")]
        [SerializeField] private float detectDistance = 0.9f;
        [Tooltip("이보다 낮으면 그냥 걷거나 점프")]
        [SerializeField] private float minHeight = 0.35f;
        [SerializeField] private float maxHeight = 2.3f;
        [Tooltip("이 높이 이하이고 얇으면 오르지 않고 넘어간다")]
        [SerializeField] private float passMaxHeight = 1.2f;
        [Tooltip("이 두께 이하면 '얇은' 장애물")]
        [SerializeField] private float passMaxThickness = 0.8f;
        [Tooltip("윗면이 이 각도보다 기울어 있으면 올라설 수 없음")]
        [SerializeField] private float maxTopSlope = 35f;
        [SerializeField] private LayerMask obstacleMask = ~0;

        [Header("안전장치")]
        [Tooltip("애니메이션이 어떤 이유로 안 끝나도 이 시간 뒤엔 강제로 끝낸다")]
        [SerializeField] private float maxDuration = 3f;
        [SerializeField] private bool drawDebug;

        private PlayerMotor motor;
        private PlayerAnimator anim;
        private CapsuleCollider capsule;
        private AnimatorRootMotionRelay relay;

        private bool vaulting;
        private bool enteredState;
        private int vaultType;
        private float heightScale = 1f;
        private float topY;
        private float startTime;

        public bool IsVaulting => vaulting;

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();
            anim = GetComponent<PlayerAnimator>();
            capsule = GetComponent<CapsuleCollider>();
        }

        private void Start()
        {
            if (anim.HasAnimator == false) return;
            relay = anim.Animator.GetComponent<AnimatorRootMotionRelay>();
            if (relay == null) relay = anim.Animator.gameObject.AddComponent<AnimatorRootMotionRelay>();
            relay.AnimatorMoved += OnAnimatorMoved;
        }

        private void OnDestroy()
        {
            if (relay != null) relay.AnimatorMoved -= OnAnimatorMoved;
        }

        /// <summary>앞에 넘을 수 있는 장애물이 있으면 벽넘기를 시작하고 true. 없으면 false (호출 쪽이 일반 점프)</summary>
        public bool TryVault()
        {
            if (vaulting || anim.HasAnimator == false) return false;
            if (motor.IsGrounded == false || motor.IsRolling || motor.IsExternallyControlled) return false;
            if (Detect(out int type, out float height, out Vector3 wallNormal) == false) return false;

            vaulting = true;
            enteredState = false;
            vaultType = type;
            topY = transform.position.y + height;
            heightScale = Mathf.Clamp(height / PlayerAnimParams.VaultClipHeight(type), 0.5f, 1.8f);
            startTime = Time.time;

            motor.BeginExternalControl();
            // 장애물을 정면으로 바라보게
            Vector3 face = -wallNormal;
            face.y = 0f;
            if (face.sqrMagnitude > 0.0001f) motor.SetRotationImmediate(Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);

            anim.Animator.applyRootMotion = true;
            anim.PlayVault(type);
            return true;
        }

        private void Update()
        {
            if (vaulting == false) return;

            bool inState = anim.IsInVaultState();
            if (inState) enteredState = true;
            if ((enteredState && inState == false) || Time.time - startTime > maxDuration) Finish();
        }

        private void OnAnimatorMoved(Animator animator)
        {
            if (vaulting == false) return;
            Vector3 delta = animator.deltaPosition;
            delta.y *= heightScale;
            motor.MoveExternal(delta);
        }

        private void Finish()
        {
            vaulting = false;
            anim.Animator.applyRootMotion = false;

            // 오르기인데 윗면보다 낮게 끝났으면 올려 준다 (클립 높이와 실제 높이 차이 보정)
            if (vaultType != 0)
            {
                float gap = topY - transform.position.y;
                if (gap > 0f && gap < 0.6f) motor.MoveExternal(Vector3.up * (gap + 0.02f));
            }
            motor.EndExternalControl();
        }

        /// <summary>사망 등으로 즉시 중단</summary>
        public void Cancel()
        {
            if (vaulting) Finish();
        }

        // ───────────────────────── 감지 ─────────────────────────

        private bool Detect(out int type, out float height, out Vector3 wallNormal)
        {
            type = 0; height = 0f; wallNormal = Vector3.zero;

            Vector3 basePos = transform.position;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            fwd.Normalize();
            float reach = detectDistance + capsule.radius;

            // 1) 앞에 벽이 있나 (무릎 높이)
            Vector3 kneeOrigin = basePos + Vector3.up * Mathf.Max(0.25f, minHeight * 0.7f);
            if (CombatUtility.RaycastIgnoring(kneeOrigin, fwd, reach, transform, out var wall, obstacleMask) == false) return false;
            if (Vector3.Angle(wall.normal, Vector3.up) < 60f) return false; // 벽이 아니라 경사면
            wallNormal = wall.normal;

            // 2) 윗면 높이: 벽 안쪽 조금 위에서 아래로
            Vector3 inside = wall.point + fwd * 0.2f;
            float castTop = maxHeight + 0.4f;
            Vector3 topOrigin = new Vector3(inside.x, basePos.y + castTop, inside.z);
            if (CombatUtility.RaycastIgnoring(topOrigin, Vector3.down, castTop, transform, out var top, obstacleMask) == false) return false;
            height = top.point.y - basePos.y;
            if (height < minHeight || height > maxHeight) return false;
            if (Vector3.Angle(top.normal, Vector3.up) > maxTopSlope) return false;
            if (drawDebug) Debug.DrawLine(topOrigin, top.point, Color.cyan, 2f);

            // 3) 윗면 위에 몸이 들어갈 공간이 있나
            if (IsBlocked(top.point + Vector3.up * 0.05f, 1.0f)) return false;

            // 4) 두께: 벽 뒤쪽이 윗면보다 충분히 낮으면 "얇은 장애물"
            Vector3 far = wall.point + fwd * passMaxThickness;
            Vector3 farOrigin = new Vector3(far.x, top.point.y + 0.3f, far.z);
            bool thin = CombatUtility.RaycastIgnoring(farOrigin, Vector3.down, 4f, transform, out var farHit, obstacleMask) == false
                        || farHit.point.y < top.point.y - 0.4f;

            if (thin && height <= passMaxHeight) type = 0;
            else if (height <= 0.75f) type = 1;
            else if (height <= 1.5f) type = 2;
            else type = 3;
            return true;
        }

        private readonly Collider[] overlap = new Collider[16];

        /// <summary>바닥 point 에 몸(높이 bodyHeight)을 세웠을 때 무언가에 걸리는가 (자기 콜라이더 제외)</summary>
        private bool IsBlocked(Vector3 point, float bodyHeight)
        {
            float r = capsule.radius * 0.8f;
            Vector3 a = point + Vector3.up * (r + 0.05f);
            Vector3 b = point + Vector3.up * Mathf.Max(r + 0.05f, bodyHeight - r);
            int count = Physics.OverlapCapsuleNonAlloc(a, b, r, overlap, obstacleMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (overlap[i].transform.IsChildOf(transform) == false) return true;
            return false;
        }
    }
}
