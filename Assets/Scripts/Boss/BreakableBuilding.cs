using UnityEngine;

namespace HybridShooter.Boss
{
    [DisallowMultipleComponent]
    public sealed class BreakableBuilding : MonoBehaviour
    {
        [SerializeField] private GameObject breakEffectPrefab;
        [SerializeField, Min(0.1f)] private float effectLifetime = 3f;

        private bool broken;

        public void Break(Vector3 effectPosition)
        {
            if (broken) return;
            broken = true;

            if (breakEffectPrefab != null)
            {
                GameObject effect = Instantiate(breakEffectPrefab, effectPosition, Quaternion.identity);
                Destroy(effect, effectLifetime);
            }

            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
