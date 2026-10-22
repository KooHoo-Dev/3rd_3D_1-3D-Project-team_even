using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 임시 이동: Target 을 향해 수평 직선으로 다가간다.
    /// 길찾기·지형 충돌·군중 제어 없음. 높이(y)는 바꾸지 않는다.
    /// Target 과 MoveSpeed 는 MonsterController 가 넣는다.
    /// Monster/Movement 모듈을 붙일 때 IMonsterMover 의 다른 구현으로 교체한다.
    /// </summary>
    public sealed class SimpleChaseMover : MonoBehaviour, IMonsterMover
    {
        [SerializeField, Min(0f)] private float moveSpeed = 3.5f;      // m/s
        [SerializeField, Min(0f)] private float stopDistance = 1.2f;   // m, 대상과 이 거리 안이면 멈춘다
        [SerializeField, Min(0f)] private float turnSpeedDeg = 540f;   // deg/s

        private bool stopped;

        /// <summary>추적 대상. null 이면 제자리</summary>
        public IPlayerTarget Target { get; set; }

        /// <summary>m/s</summary>
        public float MoveSpeed
        {
            get => moveSpeed;
            set => moveSpeed = Mathf.Max(0f, value);
        }

        public bool IsStopped => stopped;
        public void Stop() => stopped = true;
        public void Resume() => stopped = false;

        private void Update()
        {
            if (stopped || Target == null) return;

            Vector3 toTarget = Target.Position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance < 0.0001f) return;

            Vector3 direction = toTarget / distance;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), turnSpeedDeg * Time.deltaTime);

            float step = Mathf.Min(moveSpeed * Time.deltaTime, distance - stopDistance);
            if (step > 0f) transform.position += direction * step;
        }
    }
}
