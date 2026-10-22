using UnityEngine;

namespace HybridShooter.Monster.Movement
{
    /// <summary>
    /// 몬스터 유형별 이동 수치. MVP 는 ScriptableObject, 이후 팀 MonsterDef(TSV) 어댑터로 바꿀 수 있다.
    /// </summary>
    [CreateAssetMenu(menuName = "HybridShooter/Monster/Movement Profile", fileName = "MovementProfile")]
    public sealed class MovementProfile : ScriptableObject
    {
        [SerializeField] private MovementPolicyKind policyKind = MovementPolicyKind.Melee;
        [Tooltip("근접 허용 토큰(과밀 제어) 대상인가")]
        [SerializeField] private bool usesEngageToken = true;

        [Header("Common")]
        [SerializeField, Min(0f)] private float moveSpeed = 3.5f;
        [SerializeField, Min(0f)] private float attackRange = 1.8f;

        [Header("Slot Reaction")]
        [SerializeField, Min(0f)] private float reactionDelayMin = 0.1f;
        [SerializeField, Min(0f)] private float reactionDelayMax = 0.4f;
        [SerializeField, Min(0f)] private float reactionChangeDistance = 0.5f;

        [Header("Wait / Orbit (BL-016)")]
        [SerializeField, Range(0f, 1f)] private float orbitChance = 0.5f;
        [SerializeField, Min(0f)] private float orbitAngularSpeedDeg = 25f;
        [SerializeField, Min(0f)] private float minWaitSeconds = 1.0f;

        [Header("Keep Distance (BL-011)")]
        [SerializeField, Min(0f)] private float keepDistanceMin = 12f;
        [SerializeField, Min(0f)] private float keepDistanceMax = 18f;

        [Header("Flank (BL-012)")]
        [SerializeField, Range(0f, 180f)] private float flankViewHalfAngleDeg = 50f;
        [SerializeField, Min(0f)] private float flankRadius = 10f;
        [SerializeField, Min(1f)] private float flankSpeedMultiplier = 1.6f;

        public MovementPolicyKind PolicyKind => policyKind;
        public bool UsesEngageToken => usesEngageToken;
        public float MoveSpeed => moveSpeed;
        public float AttackRange => attackRange;
        public float ReactionDelayMin => reactionDelayMin;
        public float ReactionDelayMax => reactionDelayMax;
        public float ReactionChangeDistance => reactionChangeDistance;
        public float MinWaitSeconds => minWaitSeconds;

        public MovementTuning ToTuning() => new MovementTuning(
            moveSpeed, attackRange, orbitChance, orbitAngularSpeedDeg,
            keepDistanceMin, keepDistanceMax,
            flankViewHalfAngleDeg, flankRadius, flankSpeedMultiplier);

        private void OnValidate()
        {
            if (reactionDelayMax < reactionDelayMin)
                Debug.LogWarning($"[{name}] reactionDelayMax({reactionDelayMax}) < reactionDelayMin({reactionDelayMin})", this);
            if (keepDistanceMax < keepDistanceMin)
                Debug.LogWarning($"[{name}] keepDistanceMax({keepDistanceMax}) < keepDistanceMin({keepDistanceMin})", this);
        }
    }
}
