using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 공중(Air) 몬스터 프리팹에 붙이는 급강하 튜닝 값. Play 에서 자주 바꾸는 값을 한곳에 모은다.
    /// MonsterController.Start 가 찾아 MonsterAttackFactory 로 넘기고, DiveAttack 이 읽는다.
    /// </summary>
    public sealed class DiveSettings : MonoBehaviour
    {
        [Header("급강하")]
        [Tooltip("플레이어와 수평 거리가 이 안이면 급강하를 시작한다 (m). DiveAttack.Range")]
        [SerializeField, Min(0.5f)] private float entryDistance = 8f;
        [Tooltip("급강하 최저점의 플레이어 발 위 높이 (m)")]
        [SerializeField, Min(0f)] private float minHeight = 1f;
        [Tooltip("급강하·이탈 수평 속도 (m/s)")]
        [SerializeField, Min(0.1f)] private float diveSpeed = 10f;

        [Header("이탈·재정비")]
        [Tooltip("급강하 중심(플레이어 위치)에서 이 수평 거리까지 직진 이탈한다 (m)")]
        [SerializeField, Min(0f)] private float leaveDistance = 12f;
        [Tooltip("이탈을 마친 뒤 이 시간이 지나야 다시 급강하할 수 있다 (s)")]
        [SerializeField, Min(0f)] private float turnAwayTime = 1f;
        [Tooltip("순항 고도와 높이 차가 이 이하면 고도를 회복한 것으로 본다 (m)")]
        [SerializeField, Min(0f)] private float altitudeTolerance = 0.5f;

        public float EntryDistance => entryDistance;
        public float MinHeight => minHeight;
        public float DiveSpeed => diveSpeed;
        public float LeaveDistance => leaveDistance;
        public float TurnAwayTime => turnAwayTime;
        public float AltitudeTolerance => altitudeTolerance;
    }
}
