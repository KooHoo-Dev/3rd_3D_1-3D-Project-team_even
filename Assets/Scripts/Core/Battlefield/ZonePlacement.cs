using System;
using UnityEngine;

namespace HybridShooter.Core.Battlefield
{
    /// <summary>
    /// 원형 맵 경계 안에 원형 소형 경계(구역)를 놓을 위치를 계산한다.
    /// 오브젝트 생성·이동은 하지 않고, 호출자가 넘긴 값만으로 중심 위치를 돌려준다.
    /// </summary>
    public static class ZonePlacement
    {
        /// <summary>
        /// 소형 경계가 맵 경계를 넘지 않으면서 플레이어에 가장 가까운 소형 경계 중심을 찾는다.
        /// 수평(XZ) 평면의 원으로 계산하고, 플레이어가 여유 영역 밖이면 맵 중심→플레이어 방향으로 끌어당긴다.
        /// </summary>
        /// <param name="playerPosition">플레이어 위치 (Y는 무시)</param>
        /// <param name="mapCenter">맵 경계 원의 중심</param>
        /// <param name="mapRadius">맵 경계 반지름 (m)</param>
        /// <param name="zoneRadius">소형 경계 반지름 (m)</param>
        /// <returns>소형 경계 중심. Y = mapCenter.y</returns>
        /// <exception cref="ArgumentException">zoneRadius가 mapRadius보다 커서 맵 안에 놓을 수 없을 때</exception>
        public static Vector3 FindClosestCenter(Vector3 playerPosition, Vector3 mapCenter, float mapRadius, float zoneRadius)
        {
            if (zoneRadius > mapRadius)
            {
                throw new ArgumentException($"소형 경계 반지름({zoneRadius})이 맵 반지름({mapRadius})보다 커서 맵 안에 놓을 수 없다.");
            }

            float allowedDistance = mapRadius - zoneRadius;

            Vector3 centerToPlayer = playerPosition - mapCenter;
            centerToPlayer.y = 0f;

            if (centerToPlayer.magnitude <= allowedDistance)
            {
                return mapCenter + centerToPlayer;
            }

            return mapCenter + centerToPlayer.normalized * allowedDistance;
        }
    }
}
