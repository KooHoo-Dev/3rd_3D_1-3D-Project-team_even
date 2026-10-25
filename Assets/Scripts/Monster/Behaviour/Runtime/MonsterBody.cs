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

        public MonsterBody(Transform transform)
        {
            this.transform = transform != null ? transform : throw new ArgumentNullException(nameof(transform));
        }

        /// <summary>월드 좌표 (m)</summary>
        public Vector3 Position => transform.position;

        /// <summary>정면 방향 (공격 판정 기준)</summary>
        public Vector3 Forward => transform.forward;

        /// <summary>대상 지점을 수평으로 바라본다</summary>
        public void Face(Vector3 worldPoint)
        {
            Vector3 toPoint = worldPoint - transform.position;
            toPoint.y = 0f;
            if (toPoint.sqrMagnitude < 1e-8f) return; // 같은 위치: 바라볼 방향 없음
            transform.rotation = Quaternion.LookRotation(toPoint);
        }
    }
}
