using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 몬스터 유형별 이동 수치. MVP 는 ScriptableObject, 이후 팀 MonsterDef(TSV) 어댑터로 바꿀 수 있다.
    /// 이동 속도·공격 사거리·원거리 접근 속도 배율은 개체별 수치라 여기 두지 않고 ToTuning 인자로 받는다.
    /// </summary>
    [CreateAssetMenu(menuName = "HybridShooter/Monster/Movement Profile", fileName = "MovementProfile")]
    public sealed class MovementProfile : ScriptableObject
    {
        [Header("Policy")]
        [SerializeField] private MovementPolicyKind policyKind = MovementPolicyKind.Melee;
        [Tooltip("근접 허용 토큰(과밀 제어) 대상인가")]
        [SerializeField] private bool usesEngageToken = true;

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
        [Tooltip("이 거리(m) 이하면 시야 밖 우회를 멈추고 슬롯·토큰 흐름으로 돌입한다. flankRadius 보다 커야 한다")]
        [SerializeField, Min(0f)] private float flankSwitchDistance = 15f;       // m
        [Tooltip("돌입 중 거리가 전환 거리 + 이 폭(m)을 넘어야 다시 우회한다. 경계에서 목적지가 깜빡이는 것을 막는다")]
        [SerializeField, Min(0f)] private float flankSwitchBand = 2f;            // m

        public MovementPolicyKind PolicyKind => policyKind;
        public bool UsesEngageToken => usesEngageToken;
        public float ReactionDelayMin => reactionDelayMin;
        public float ReactionDelayMax => reactionDelayMax;
        public float ReactionChangeDistance => reactionChangeDistance;
        public float MinWaitSeconds => minWaitSeconds;

        /// <summary>
        /// 정책에 넘길 수치 복사본 생성 (정책이 ScriptableObject 에 의존하지 않게).
        /// MonsterMovement 가 활성화·SetCombatTuning 때 개체별 수치를 넘겨 호출한다.
        /// </summary>
        /// <param name="moveSpeed">개체별 이동 속도 (m/s, 테이블 MoveSpeed)</param>
        /// <param name="attackRange">개체별 공격 사거리 (m, 첫 공격 Range)</param>
        /// <param name="flankApproachSpeedMultiplier">원거리 시야 밖 접근 속도 배율 (PounceSettings, 없으면 1)</param>
        /// <returns>이 profile 의 수치와 개체별 수치를 담은 MovementTuning</returns>
        public MovementTuning ToTuning(float moveSpeed, float attackRange, float flankApproachSpeedMultiplier) => new MovementTuning(
            moveSpeed, attackRange, orbitChance, orbitAngularSpeedDeg,
            keepDistanceMin, keepDistanceMax,
            flankRadius, flankApproachSpeedMultiplier, flankSwitchDistance, flankSwitchBand);

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
