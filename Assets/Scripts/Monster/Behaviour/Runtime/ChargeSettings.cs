using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 돌진형(Rusher) 몬스터 프리팹에 붙이는 돌진 튜닝 값. Play 에서 자주 바꾸는 값을 한곳에 모은다.
    /// MonsterController.Start 가 찾아 MonsterAttackFactory 로 넘기고, ChargeAttack 이 읽는다.
    /// </summary>
    public sealed class ChargeSettings : MonoBehaviour
    {
        [Header("돌진")]
        [Tooltip("돌진 속도 (m/s)")]
        [SerializeField, Min(0.1f)] private float chargeSpeed = 12f;
        [Tooltip("돌진 중 플레이어 쪽으로 방향을 보정하는 속도 (도/초). 예고 회전보다 느리게 둔다. 너무 빠르면 플레이어 주위를 맴돌아 통과하지 못한다")]
        [SerializeField, Min(0f)] private float chargeTurnSpeed = 45f;
        [Tooltip("접촉·통과 뒤 감속률 (m/s²). 접촉과 통과 모두 같은 값")]
        [SerializeField, Min(0.1f)] private float brakeDeceleration = 15f;
        [Tooltip("플레이어를 지나치지 못해도 이 거리(m)를 달리면 감속한다")]
        [SerializeField, Min(0f)] private float maxChargeDistance = 30f;
        [Tooltip("돌진 중 Move 클립을 1배속으로 재생할 때의 이동 속도 (m/s). 재생 배율 = 돌진 속도 ÷ 이 값")]
        [SerializeField, Min(0.01f)] private float chargeClipSpeed = 18f;

        [Header("Idle 준비 동작")]
        [Tooltip("휴식 중 플레이어 쪽으로 도는 속도 (도/초)")]
        [SerializeField, Min(0f)] private float prepTurnSpeed = 90f;
        [Tooltip("휴식 중 정면으로 천천히 걷는 속도 (m/s)")]
        [SerializeField, Min(0f)] private float prepMoveSpeed = 1.5f;

        [Header("회피")]
        [Tooltip("NavMeshAgent 회피 우선순위 (낮을수록 우선). 상태와 무관하게 항상 이 값으로 고정한다. 슬롯 몬스터(40~60)보다 앞서도록 30대를 쓴다")]
        [SerializeField, Range(0, 99)] private int avoidancePriority = 30;

        public float ChargeSpeed => chargeSpeed;
        public float ChargeTurnSpeed => chargeTurnSpeed;
        public float BrakeDeceleration => brakeDeceleration;
        public float MaxChargeDistance => maxChargeDistance;
        public float ChargeClipSpeed => chargeClipSpeed;
        public float PrepTurnSpeed => prepTurnSpeed;
        public float PrepMoveSpeed => prepMoveSpeed;
        public int AvoidancePriority => avoidancePriority;
    }
}
