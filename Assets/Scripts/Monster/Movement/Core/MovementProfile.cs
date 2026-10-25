using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 몬스터 유형별 이동 수치. MVP 는 ScriptableObject, 이후 팀 MonsterDef(TSV) 어댑터로 바꿀 수 있다.
    /// </summary>
    [CreateAssetMenu(menuName = "HybridShooter/Monster/Movement Profile", fileName = "MovementProfile")]
    public sealed class MovementProfile : ScriptableObject
    {
        [Header("Policy")]
        [SerializeField] private MovementPolicyKind policyKind = MovementPolicyKind.Melee;
        [Tooltip("근접 허용 토큰(과밀 제어) 대상인가")]
        [SerializeField] private bool usesEngageToken = true;

        [Header("Common")]
        [SerializeField, Min(0f)] private float moveSpeed = 3.5f;        // m/s. 개체별 수치(SetCombatTuning)가 있으면 그 값이 우선
        [SerializeField, Min(0f)] private float attackRange = 1.8f;      // m. 개체별 수치(SetCombatTuning)가 있으면 그 값이 우선

        [Header("Slot Reaction (슬롯 목적지가 바뀔 때 뜸 들이는 시간·민감도)")]
        [SerializeField, Min(0f)] private float reactionDelayMin = 0.1f;        // s
        [SerializeField, Min(0f)] private float reactionDelayMax = 0.4f;        // s. 0 이면 반응 지연 없음
        [SerializeField, Min(0f)] private float reactionChangeDistance = 0.5f;  // m. 슬롯 목적지가 이만큼 바뀌어야 지연 반응

        [Header("Wait / Orbit (BL-016)")]
        [SerializeField, Range(0f, 1f)] private float orbitChance = 0.5f;          // 대기 진입 시 선회를 고를 확률
        [SerializeField, Min(0f)] private float orbitAngularSpeedDeg = 25f;  // deg/s
        [SerializeField, Min(0f)] private float minWaitSeconds = 1.0f;      // s

        [Header("Keep Distance (BL-011)")]
        [SerializeField, Min(0f)] private float keepDistanceMin = 12f;  // m
        [SerializeField, Min(0f)] private float keepDistanceMax = 18f;  // m

        [Header("Flank (BL-012)")]
        [SerializeField, Range(0f, 180f)] private float flankViewHalfAngleDeg = 50f;  // 도, 목표 시야 반각
        [SerializeField, Min(0f)] private float flankRadius = 10f;               // m, 측면 경유점 반경
        [SerializeField, Min(1f)] private float flankSpeedMultiplier = 1.6f;     // 시야 밖 돌입 속도 배율

        public MovementPolicyKind PolicyKind => policyKind;
        public bool UsesEngageToken => usesEngageToken;
        public float MoveSpeed => moveSpeed;
        public float AttackRange => attackRange;
        public float ReactionDelayMin => reactionDelayMin;
        public float ReactionDelayMax => reactionDelayMax;
        public float ReactionChangeDistance => reactionChangeDistance;
        public float MinWaitSeconds => minWaitSeconds;

        /// <summary>정책에 넘길 수치 복사본 생성 (정책이 ScriptableObject 에 의존하지 않게). MonsterMovement 가 활성화 때 호출한다</summary>
        /// <returns>이 profile 의 수치를 담은 MovementTuning</returns>
        public MovementTuning ToTuning() => new MovementTuning(
            moveSpeed, attackRange, orbitChance, orbitAngularSpeedDeg,
            keepDistanceMin, keepDistanceMax,
            flankViewHalfAngleDeg, flankRadius, flankSpeedMultiplier);

        /// <summary>Inspector 수정 시 min/max 역전을 경고</summary>
        private void OnValidate()
        {
            if (reactionDelayMax < reactionDelayMin)
                Debug.LogWarning($"[{name}] reactionDelayMax({reactionDelayMax}) < reactionDelayMin({reactionDelayMin})", this);
            if (keepDistanceMax < keepDistanceMin)
                Debug.LogWarning($"[{name}] keepDistanceMax({keepDistanceMax}) < keepDistanceMin({keepDistanceMin})", this);
        }
    }
}
