using System;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 브레인(상태 머신)과 공격이 읽고 조작하는 몬스터 몸체 (위치·방향). Transform 을 감싼다.
    /// 회전은 수평으로만 한다
    /// </summary>
    public sealed class MonsterBody
    {
        private readonly Transform transform;

        /// <summary>MonsterController.Start 가 자기 Transform 으로 만들어 상태 머신과 공격에 넘긴다</summary>
        /// <param name="transform">몬스터 루트 Transform</param>
        /// <exception cref="ArgumentNullException">transform 이 null(또는 파괴됨)일 때</exception>
        public MonsterBody(Transform transform)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            this.transform = transform;
        }

        // 월드 좌표 (m)
        public Vector3 Position => transform.position;

        // 정면 방향 (공격 판정 기준)
        public Vector3 Forward => transform.forward;

        /// <summary>대상 지점을 수평으로 바라본다. 상태 머신(FaceTarget)과 공격 예고(StrikeAttack.OnWindup)가 부른다</summary>
        /// <param name="worldPoint">바라볼 월드 좌표. 높이는 무시한다. 같은 위치면 회전하지 않는다</param>
        public void Face(Vector3 worldPoint)
        {
            Vector3 toPoint = worldPoint - transform.position;
            toPoint.y = 0f;
            if (toPoint.magnitude < 1e-4f) return; // 같은 위치: 바라볼 방향 없음
            transform.rotation = Quaternion.LookRotation(toPoint);
        }
    }
}
