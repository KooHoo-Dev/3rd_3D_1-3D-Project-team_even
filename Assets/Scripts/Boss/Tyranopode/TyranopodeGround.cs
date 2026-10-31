using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 바닥 높이 재기 (범위 표시·충격파 링이 경사진 지형을 따라 휘게).
    ///  - 터레인 위: Terrain.SampleHeight (물리 없이 빨라서 매 프레임 수백 번 재도 된다)
    ///  - 터레인 밖(바위·건물 메시): 위에서 아래로 레이를 쏴 처음 맞은 면. 보스·플레이어·몬스터 콜라이더는 건너뛴다
    /// </summary>
    public static class TyranopodeGround
    {
        private const float RayHeight = 40f;
        private const float RayLength = 80f;

        private static readonly List<Terrain> Terrains = new List<Terrain>();
        private static readonly RaycastHit[] Hits = new RaycastHit[8];
        private static int terrainFrame = -1;

        /// <summary>point 의 수평 위치에서 바닥 높이. 바닥을 못 찾으면 fallbackY</summary>
        public static float Height(Vector3 point, float fallbackY)
        {
            // 터레인 목록은 한 프레임에 한 번만 받는다
            if (terrainFrame != Time.frameCount)
            {
                terrainFrame = Time.frameCount;
                Terrain.GetActiveTerrains(Terrains);
            }
            foreach (var terrain in Terrains)
            {
                if (terrain == null) continue;
                Vector3 origin = terrain.GetPosition();
                Vector3 size = terrain.terrainData.size;
                if (point.x < origin.x || point.x > origin.x + size.x || point.z < origin.z || point.z > origin.z + size.z) continue;
                return terrain.SampleHeight(point) + origin.y;
            }
            return RaycastHeight(point, fallbackY);
        }

        private static float RaycastHeight(Vector3 point, float fallbackY)
        {
            Vector3 top = new Vector3(point.x, point.y + RayHeight, point.z);
            int count = Physics.RaycastNonAlloc(top, Vector3.down, Hits, RayLength, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            float height = fallbackY;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.GetComponentInParent<IDamageable>() != null) continue;
                if (Hits[i].distance >= nearest) continue;
                nearest = Hits[i].distance;
                height = Hits[i].point.y;
            }
            return height;
        }
    }
}
