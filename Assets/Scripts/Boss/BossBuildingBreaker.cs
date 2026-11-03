using System;
using UnityEngine;

namespace HybridShooter.Boss
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossController))]
    public sealed class BossBuildingBreaker : MonoBehaviour
    {
        private const float CheckInterval = 0.1f;

        [Tooltip("건물 접촉 검사에 사용할 기존 몸통·다리 BoxCollider")]
        [SerializeField] private BoxCollider[] contactColliders;
        [Tooltip("건물 콜라이더의 레이어 (Buildings)")]
        [SerializeField] private LayerMask buildingMask;

        private BossController boss;
        private Collider[] overlaps = new Collider[32];
        private float nextCheckTime;

        private void Awake()
        {
            boss = GetComponent<BossController>();
        }

        private void OnEnable()
        {
            nextCheckTime = 0f;
        }

        private void LateUpdate()
        {
            if (boss == null || boss.isActiveAndEnabled == false || boss.IsAlive == false) return;
            if (contactColliders == null || Time.time < nextCheckTime) return;

            nextCheckTime = Time.time + CheckInterval;

            foreach (BoxCollider contact in contactColliders)
            {
                if (contact == null || contact.enabled == false || contact.gameObject.activeInHierarchy == false)
                    continue;

                CheckBuildings(contact);
            }
        }

        private void CheckBuildings(BoxCollider contact)
        {
            Transform area = contact.transform;
            Vector3 center = area.TransformPoint(contact.center);
            Vector3 scale = area.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

            Vector3 halfExtents = Vector3.Scale(contact.size, scale) * 0.5f;
            int count = FindOverlaps(center, halfExtents, area.rotation);

            for (int i = 0; i < count; i++)
            {
                Collider hit = overlaps[i];
                overlaps[i] = null;
                if (hit == null) continue;

                var building = hit.GetComponentInParent<BreakableBuilding>();
                if (building == null || building.isActiveAndEnabled == false) continue;

                building.Break(hit.bounds.center);
            }
        }

        private int FindOverlaps(Vector3 center, Vector3 halfExtents, Quaternion rotation)
        {
            while (true)
            {
                int count = Physics.OverlapBoxNonAlloc(
                    center,
                    halfExtents,
                    overlaps,
                    rotation,
                    buildingMask,
                    QueryTriggerInteraction.Ignore);

                if (count < overlaps.Length) return count;

                // 버퍼가 가득 찼으면 늘린 뒤 다시 검사해 건물을 누락하지 않는다.
                Array.Resize(ref overlaps, overlaps.Length * 2);
            }
        }
    }
}
