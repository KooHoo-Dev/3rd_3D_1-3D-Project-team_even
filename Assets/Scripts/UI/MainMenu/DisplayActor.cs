using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>
    /// 메인 메뉴 대치 장면(디오라마) 캐릭터. 게임 시스템 없이 Animator 만 돌린다.
    ///  - 시작 시 Idle 을 랜덤 위치(normalizedTime)·재생 속도로 시작 → 여러 마리가 똑같이 움직이지 않게
    ///  - 비행형은 놓인 높이에서 위아래로 천천히 호버링
    ///  - PlayThreat(): 위협·포효 상태를 한 번 재생하고 끝나면 Idle 로 돌아간다 (QUIT 호버 연출)
    ///  - SetFloat(): Animator 파라미터 보간 (플레이어 Aim 포즈 등)
    ///  - 발광: Emission 이 켜진 머티리얼(눈·발광부)을 emissionBoost 배로 (MaterialPropertyBlock 이라 공유 머티리얼은 그대로). EmissionBoost 로 연출 중 바꿀 수 있다
    /// *_Display 프리팹은 Tools > MainMenu > Rebuild Diorama 가 게임 프리팹에서 게임 스크립트·물리를 걷어내고 만든다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DisplayActor : MonoBehaviour
    {
        [Tooltip("비우면 자식에서 찾는다")]
        [SerializeField] private Animator animator;

        [Header("애니메이션 (Animator 상태 이름, 0번 레이어)")]
        [Tooltip("비우면 컨트롤러 기본 상태를 그대로 둔다 (시작 위치·속도만 흩뜨림)")]
        [SerializeField] private string idleState = "IdleBreathe";
        [Tooltip("위협·포효. 비우면 PlayThreat 를 무시")]
        [SerializeField] private string threatState;
        [SerializeField] private float crossFade = 0.25f;
        [Tooltip("재생 속도 무작위 범위")]
        [SerializeField] private Vector2 speedRange = new Vector2(0.9f, 1.1f);

        [Header("호버링 (비행형만)")]
        [SerializeField] private bool hover;
        [SerializeField] private float hoverAmplitude = 0.3f;
        [Tooltip("한 번 오르내리는 시간(초)")]
        [SerializeField] private float hoverPeriod = 3f;

        [Header("발광")]
        [Tooltip("Emission 이 켜진 머티리얼의 발광 배율 (1 = 원래대로). 어두운 장면에서 눈이 빛나 보이게")]
        [SerializeField] private float emissionBoost = 2.5f;

        [Header("소리")]
        [Tooltip("PlayThreat 때 이 위치에서 재생 (SoundTable 키, 비우면 없음)")]
        [SerializeField] private string threatSoundKey;

        private int idleHash;
        private int threatHash;
        private bool threatening;
        private Vector3 basePosition;
        private float hoverPhase;
        private float currentBoost = 1f;
        /// <summary>발광 머티리얼 슬롯 (렌더러, 머티리얼 순번, 원래 Emission 색)</summary>
        private readonly List<(Renderer Renderer, int Index, Color Color)> emissive = new List<(Renderer, int, Color)>();
        private MaterialPropertyBlock block;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        public bool HasThreat => threatHash != 0;

        private void Awake()
        {
            CollectEmissive();
            EmissionBoost = emissionBoost;

            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false; // 제자리 연출 (Root Motion 클립이 섞여도 밀려나지 않게)
            idleHash = ResolveState(idleState);
            threatHash = ResolveState(threatState);
        }

        private void Start()
        {
            basePosition = transform.localPosition;
            hoverPhase = Random.value * Mathf.PI * 2f;
            if (animator == null) return;

            animator.speed = Random.Range(speedRange.x, speedRange.y);
            if (idleHash != 0) animator.Play(idleHash, 0, Random.value);
            else animator.Play(animator.GetCurrentAnimatorStateInfo(0).fullPathHash, 0, Random.value);
        }

        private void Update()
        {
            if (hover && hoverPeriod > 0f)
            {
                float y = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / hoverPeriod + hoverPhase) * hoverAmplitude;
                transform.localPosition = basePosition + Vector3.up * y;
            }

            // 위협 상태는 대부분 반복하지 않는 클립 → 끝나면 Idle 로
            if (threatening && animator != null && animator.IsInTransition(0) == false)
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.shortNameHash == threatHash && state.normalizedTime >= 0.95f)
                {
                    threatening = false;
                    if (idleHash != 0) animator.CrossFadeInFixedTime(idleHash, crossFade, 0);
                }
            }
        }

        /// <summary>위협·포효 한 번. 이미 위협 중이거나 상태가 없으면 false</summary>
        public bool PlayThreat()
        {
            if (animator == null || threatHash == 0 || threatening) return false;
            threatening = true;
            animator.CrossFadeInFixedTime(threatHash, crossFade, 0);
            if (string.IsNullOrEmpty(threatSoundKey) == false && ServiceLocator.TryGet<ISoundService>(out var sound))
                sound.PlaySfx(threatSoundKey, transform.position);
            return true;
        }

        /// <summary>발광 배율 (1 = 원래 머티리얼). 발광 머티리얼이 없으면 아무 일도 없다</summary>
        public float EmissionBoost
        {
            get => currentBoost;
            set
            {
                currentBoost = value;
                if (emissive.Count == 0) return;
                block ??= new MaterialPropertyBlock();
                foreach (var (renderer, index, color) in emissive)
                {
                    if (renderer == null) continue;
                    renderer.GetPropertyBlock(block, index);
                    block.SetColor(EmissionColorId, color * value);
                    renderer.SetPropertyBlock(block, index);
                }
            }
        }

        /// <summary>디스플레이 기본 발광 배율 (Inspector 값)</summary>
        public float DefaultEmissionBoost => emissionBoost;

        private void CollectEmissive()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || material.IsKeywordEnabled("_EMISSION") == false || material.HasProperty(EmissionColorId) == false) continue;
                    var color = material.GetColor(EmissionColorId);
                    if (color.maxColorComponent > 0f) emissive.Add((renderer, i, color));
                }
            }
        }

        /// <summary>Animator float 파라미터 (없으면 무시)</summary>
        public void SetFloat(string parameter, float value)
        {
            if (animator == null) return;
            foreach (var p in animator.parameters)
                if (p.name == parameter && p.type == AnimatorControllerParameterType.Float)
                {
                    animator.SetFloat(parameter, value);
                    return;
                }
        }

        public float GetFloat(string parameter) => animator != null ? animator.GetFloat(parameter) : 0f;

        /// <summary>이름 → 해시. 0번 레이어에 없으면 경고 후 0</summary>
        private int ResolveState(string stateName)
        {
            if (string.IsNullOrEmpty(stateName)) return 0;
            int hash = Animator.StringToHash(stateName);
            if (animator.HasState(0, hash)) return hash;
            Debug.LogWarning($"[DisplayActor] {name}: Animator 에 '{stateName}' 상태가 없습니다", this);
            return 0;
        }
    }
}
