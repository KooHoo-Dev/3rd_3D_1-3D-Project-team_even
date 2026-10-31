using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// Tyranopode 애니메이터·발광 연출. BossView(대기·피격·사망)와 같은 Animator 를 쓴다.
    ///  - 상태 이름은 클립 이름 그대로 ("Base Layer.Bite"). 컨트롤러는 TyranopodePrefabBuilder 가 만든다
    ///  - 모든 상태의 재생 속도는 PlaySpeed 파라미터를 따른다 (돌진 중 빠르게, 그로기 정지 등)
    ///  - 에셋 클립이 모두 반복 재생이라, 사망·그로기는 지정 진행도에서 멈춘다
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class TyranopodeView : MonoBehaviour
    {
        public const string Idle = "Base Layer.Idle";
        public const string Locomotion = "Base Layer.Locomotion";
        public const string CrawlForward = "Base Layer.CrawlForward";
        public const string Bite = "Base Layer.Bite";
        public const string JumpBite = "Base Layer.JumpBite";
        public const string JumpSmash = "Base Layer.JumpTentaclesSmashAttack";
        public const string TentacleLeft = "Base Layer.TentacleAttackLeft";
        public const string TentacleRight = "Base Layer.TentacleAttackRight";
        public const string Tentacle2Hit = "Base Layer.Tentacles2HitCombo";
        public const string Tentacle3Hit = "Base Layer.Tentacles3HitCombo";
        public const string TentacleSmash = "Base Layer.TentaclesSmashAttack";
        public const string Spit = "Base Layer.Spit";
        public const string Roar = "Base Layer.RoarExhaleGas";
        public const string Death = "Base Layer.Death";

        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
        private static readonly int PlaySpeedHash = Animator.StringToHash("PlaySpeed");
        private static readonly int LocomotionHash = Animator.StringToHash(Locomotion);
        private static readonly int DeathHash = Animator.StringToHash(Death);
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("이 속도(m/s)로 움직일 때 기어가기 애니메이션을 1배속으로 재생")]
        [SerializeField, Min(0.1f)] private float crawlReferenceSpeed = 6f;
        [Tooltip("그로기: 사망 애니메이션을 이 진행도에서 멈춘다 (쓰러진 자세)")]
        [SerializeField, Range(0.1f, 1f)] private float groggyPoseTime = 0.55f;
        [Tooltip("사망: 이 진행도에서 멈춘다")]
        [SerializeField, Range(0.1f, 1f)] private float deathPoseTime = 0.97f;
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField, Min(0f)] private float hitFlashBoost = 1.5f;
        [SerializeField, Min(0.01f)] private float hitFlashTime = 0.12f;
        [Tooltip("발광 색·세기가 바뀔 때 걸리는 시간(초)")]
        [SerializeField, Min(0.01f)] private float emissionBlendTime = 1.5f;

        private Animator animator;
        private BossController boss;
        private MaterialPropertyBlock block;
        private Color baseEmission = Color.white;
        private float emissionLevel = 1f;
        private float currentLevel = 1f;
        private Color emissionTint = Color.white;
        private Color currentTint = Color.white;
        private float flashEndTime;
        private bool groggy;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            boss = GetComponent<BossController>();
            if (bodyRenderer == null) throw new System.InvalidOperationException("[Tyranopode] TyranopodeView.bodyRenderer 가 비어 있습니다.");
            baseEmission = bodyRenderer.sharedMaterial.GetColor(EmissionColorId);
            block = new MaterialPropertyBlock();
        }

        /// <summary>상태를 부드럽게 전환해 처음부터 재생한다</summary>
        public void Play(string state, float speed = 1f, float fade = 0.12f)
        {
            animator.SetFloat(PlaySpeedHash, speed);
            animator.CrossFadeInFixedTime(Animator.StringToHash(state), fade, 0, 0f);
        }

        public void SetSpeed(float speed) => animator.SetFloat(PlaySpeedHash, speed);

        /// <summary>state 의 normalizedTime 자세로 멈춘다 (등장 연출: 날아오는 동안 점프 자세를 진행도에 맞춰 넘긴다)</summary>
        public void Pose(string state, float normalizedTime)
        {
            animator.SetFloat(PlaySpeedHash, 0f);
            animator.Play(Animator.StringToHash(state), 0, normalizedTime);
        }

        /// <summary>state 를 normalizedTime 부터 이어 재생한다</summary>
        public void PlayFrom(string state, float normalizedTime, float speed = 1f)
        {
            animator.SetFloat(PlaySpeedHash, speed);
            animator.Play(Animator.StringToHash(state), 0, normalizedTime);
        }

        /// <summary>
        /// 지금(전환 중이면 전환 대상) 상태가 state 일 때 진행도. 반복 재생이라 1 을 넘으면 한 바퀴 끝. 다른 상태면 -1
        /// </summary>
        public float NormalizedTime(string state)
        {
            int hash = Animator.StringToHash(state);
            if (animator.IsInTransition(0))
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                return next.fullPathHash == hash ? next.normalizedTime : -1f;
            }
            var current = animator.GetCurrentAnimatorStateInfo(0);
            return current.fullPathHash == hash ? current.normalizedTime : -1f;
        }

        public bool IsFinished(string state) => NormalizedTime(state) >= 1f;

        /// <summary>이동 블렌드 (localVelocity = 몸 기준 속도 m/s). 이동 상태가 아니면 전환한다</summary>
        public void SetLocomotion(Vector3 localVelocity)
        {
            float speed = localVelocity.magnitude;
            Vector3 dir = speed > 0.05f ? localVelocity / speed : Vector3.zero;
            float blend = Mathf.Clamp01(speed / crawlReferenceSpeed);
            animator.SetFloat(MoveXHash, dir.x * blend, 0.15f, Time.deltaTime);
            animator.SetFloat(MoveZHash, dir.z * blend, 0.15f, Time.deltaTime);

            bool inLocomotion = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0).fullPathHash == LocomotionHash
                : animator.GetCurrentAnimatorStateInfo(0).fullPathHash == LocomotionHash;
            float playSpeed = Mathf.Clamp(speed / crawlReferenceSpeed, 0.8f, 2.5f);
            if (inLocomotion == false) Play(Locomotion, playSpeed, 0.2f);
            else SetSpeed(playSpeed);
        }

        public void PlayGroggy()
        {
            groggy = true;
            Play(Death, 1.2f, 0.15f);
        }

        public void EndGroggy()
        {
            groggy = false;
            Play(Idle, 1f, 0.5f);
        }

        /// <summary>발광 세기 0~1 (발광 주머니가 부서지면 빛을 잃는다). 천천히 바뀐다</summary>
        public void SetEmissionLevel(float level) => emissionLevel = Mathf.Clamp01(level);

        /// <summary>발광 색 (원래 색에 곱한다. 페이즈 2 = 붉게). 천천히 바뀐다</summary>
        public void SetEmissionTint(Color tint) => emissionTint = tint;

        public void Flash() => flashEndTime = Time.time + hitFlashTime;

        private void Update()
        {
            // 반복 클립이라 쓰러진 자세에서 멈춘다
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.fullPathHash == DeathHash && animator.IsInTransition(0) == false)
            {
                bool dead = boss != null && boss.IsAlive == false;
                float pose = dead ? deathPoseTime : groggyPoseTime;
                if ((dead || groggy) && state.normalizedTime >= pose) SetSpeed(0f);
            }

            UpdateEmission();
        }

        private void UpdateEmission()
        {
            float blend = Time.deltaTime / emissionBlendTime;
            currentLevel = Mathf.MoveTowards(currentLevel, emissionLevel, blend);
            currentTint = Color.Lerp(currentTint, emissionTint, blend * 3f);
            float boost = Time.time < flashEndTime ? hitFlashBoost : 0f;
            bodyRenderer.GetPropertyBlock(block);
            block.SetColor(EmissionColorId, baseEmission * currentTint * (currentLevel + boost));
            bodyRenderer.SetPropertyBlock(block);
        }
    }
}
