using UnityEngine;

namespace HybridShooter.Boss
{
    public class BossAttackRange
    {
        private Transform instance;
        private float heightOffset;

        public void Show(Transform prefab, Vector3 center, float radius, float heightOffset = 0.03f)
        {
            Clear();
            
            if (prefab == null) return;

            this.heightOffset = heightOffset;

            instance = Object.Instantiate(prefab, center + Vector3.up * heightOffset, Quaternion.identity);

            SetRadius(radius);
        }
        
        public void SetPosition(Vector3 center)
        {
            if (instance == null) return;

            instance.position = center + Vector3.up * heightOffset;
        }

        public void SetRadius(float radius)
        {
            if (instance == null) return;

            float diameter = Mathf.Max(0f, radius) * 2f;

            instance.localScale = new Vector3(diameter, 1f, diameter);
        }
        
        public void Clear()
        {
            if (instance != null)
            {
                Object.Destroy(instance.gameObject);
            }

            instance = null;
        }
    }
}