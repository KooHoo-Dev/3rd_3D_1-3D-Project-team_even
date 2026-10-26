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
        [SerializeField, Min(0f)] private float flankRadius = 10f;               // m, 측면 경유점 반경
        [SerializeField, Min(1f)] private float flankSpeedMultiplier = 1.6f;     // 시야 밖 돌입 속도 배율
        [Tooltip("이 거리(m) 이하면 시야 밖 우회를 멈추고 슬롯·토큰 흐름으로 돌입한다. flankRadius 보다 커야 한다")]
        [SerializeField, Min(0f)] private float flankSwitchDistance = 15f;       // m
        [Tooltip("돌입 중 거리가 전환 거리 + 이 폭(m)을 넘어야 다시 우회한다. 경계에서 목적지가 깜빡이는 것을 막는다")]
        [SerializeField, Min(0f)] private float flankSwitchBand = 2f;            // m

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
            flankRadius, flankSpeedMultiplier, flankSwitchDistance, flankSwitchBand);

        /// <summary>Inspector 수정 시 min/max 역전과, 측면 경유점이 전환 거리 밖에 있는 설정을 경고</summary>
        private void OnValidate()
        {
            if (reactionDelayMax < reactionDelayMin)
                Debug.LogWarning($"[{name}] reactionDelayMax({reactionDelayMax}) < reactionDelayMin({reactionDelayMin})", this);
            if (keepDistanceMax < keepDistanceMin)
                Debug.LogWarning($"[{name}] keepDistanceMax({keepDistanceMax}) < keepDistanceMin({keepDistanceMin})", this);
            if (flankRadius >= flankSwitchDistance)
                Debug.LogWarning($"[{name}] flankRadius({flankRadius}) >= flankSwitchDistance({flankSwitchDistance}): 경유점 도착 전에 돌입 모드로 넘어가지 못한다", this);
        }
    }
}
