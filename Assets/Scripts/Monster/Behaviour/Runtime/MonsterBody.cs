using System;
using UnityEngine;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 브레인(상태 머신)과 공격이 읽고 조작하는 몬스터 몸체 (위치·방향). Transform 을 감싼다.
    /// 회전은 수평으로만 한다
    /// </summary>
    public sealed class MonsterBody
    {
        private readonly Transform transform;
        private readonly NavMeshMoveExecutor executor;

        /// <summary>MonsterController.Start 가 자기 Transform 으로 만들어 상태 머신과 공격에 넘긴다</summary>
        /// <param name="transform">몬스터 루트 Transform</param>
        /// <exception cref="ArgumentNullException">transform 이 null(또는 파괴됨)일 때</exception>
        public MonsterBody(Transform transform) : this(transform, null) { }

        /// <summary>위치 제어가 필요한 공격에는 같은 루트의 이동 실행기를 연결한다.</summary>
        /// <param name="transform">몬스터 루트</param>
        /// <param name="executor">NavMesh 위치 제어 담당</param>
        public MonsterBody(Transform transform, NavMeshMoveExecutor executor)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            this.transform = transform;
            this.executor = executor;
        }

        // 공격 위치 제어에만 쓰는 좁은 통로. 기존 정지 공격은 executor 없이 사용할 수 있다.
        public Transform Root => transform;
        public UnityEngine.AI.NavMeshQueryFilter AttackNavigationFilter => executor.AttackNavigationFilter;
        public bool HasAttackMotion => executor.HasAttackMotion;
        public float AttackRadius => executor.AttackRadius;
        public float AttackHeight => executor.AttackHeight;
        public Vector3 AttackGroundOrigin => executor.AttackGroundOrigin;
        public bool OwnsCollider(Collider collider) => collider.transform.IsChildOf(transform);
        public void BeginAttackMotion(Vector3 ground, Func<Vector3> resolveInterruption) => executor.BeginAttackMotion(ground, resolveInterruption);
        public void MoveForAttack(Vector3 position) => executor.MoveForAttack(position);
        public void EndAttackMotion(Vector3 ground) => executor.EndAttackMotion(ground);

        // 월드 좌표 (m)
        public Vector3 Position => transform.position;

        // 정면 방향 (공격 판정 기준)
        public Vector3 Forward => transform.forward;

        /// <summary>
        /// 대상 지점 쪽으로 수평으로 일정 속도만큼 돈다. 한 번에 다 돌지 않고 이번 프레임 몫만 돈다.
        /// MonsterAttack.Tick 이 예고(Windup) 중 매 Tick 부른다.
        /// </summary>
        /// <param name="worldPoint">바라볼 월드 좌표. 높이는 무시한다. 같은 위치면 회전하지 않는다</param>
        /// <param name="degreesPerSecond">회전 속도 (도/초). 0 = 돌지 않는다</param>
        /// <param name="deltaTime">지난 Tick 부터 경과 시간 (s)</param>
        public void TurnToward(Vector3 worldPoint, float degreesPerSecond, float deltaTime)
        {
            Vector3 toPoint = worldPoint - transform.position;
            toPoint.y = 0f;
            if (toPoint.magnitude < 1e-4f) return; // 같은 위치: 바라볼 방향 없음

            Quaternion targetRotation = Quaternion.LookRotation(toPoint);
            float maxDegreesThisTick = degreesPerSecond * deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxDegreesThisTick);
        }
    }
}
