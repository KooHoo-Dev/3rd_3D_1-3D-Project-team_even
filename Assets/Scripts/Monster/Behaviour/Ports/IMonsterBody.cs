using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>브레인이 읽고 조작하는 몬스터 몸체 (위치·방향)</summary>
    public interface IMonsterBody
    {
        /// <summary>월드 좌표 (m)</summary>
        Vector3 Position { get; }

        /// <summary>정면 방향 (공격 판정 기준)</summary>
        Vector3 Forward { get; }

        /// <summary>대상 지점을 수평으로 바라본다</summary>
        void Face(Vector3 worldPoint);
    }
}
