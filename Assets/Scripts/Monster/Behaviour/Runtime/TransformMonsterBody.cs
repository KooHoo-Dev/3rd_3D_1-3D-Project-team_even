using System;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>Transform 을 IMonsterBody 로 쓰는 어댑터. 회전은 수평으로만 한다</summary>
    public sealed class TransformMonsterBody : IMonsterBody
    {
        private readonly Transform transform;

        public TransformMonsterBody(Transform transform)
        {
            this.transform = transform != null ? transform : throw new ArgumentNullException(nameof(transform));
        }

        public Vector3 Position => transform.position;
        public Vector3 Forward => transform.forward;

        public void Face(Vector3 worldPoint)
        {
            Vector3 toPoint = worldPoint - transform.position;
            toPoint.y = 0f;
            if (toPoint.sqrMagnitude < 1e-8f) return; // 같은 위치: 바라볼 방향 없음
            transform.rotation = Quaternion.LookRotation(toPoint);
        }
    }
}
