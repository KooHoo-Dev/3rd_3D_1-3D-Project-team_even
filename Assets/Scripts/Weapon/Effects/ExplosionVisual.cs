using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 폭발 이펙트 프리팹 루트에 붙인다. GunExplosion 이 생성 직후 Fit(피해 반경)을 불러
    /// 보이는 크기를 실제 피해 범위(구)에 맞춘다.
    ///  - 하위 파티클은 Scaling Mode = Hierarchy 여야 루트 크기를 따라간다
    ///  - 불빛은 크기를 따라가지 않으므로 범위를 같은 비율로 늘린다
    /// </summary>
    public sealed class ExplosionVisual : MonoBehaviour
    {
        [Tooltip("배율 1 일 때 보이는 폭발 구의 반경(m). 이 값과 피해 반경의 비율로 키운다")]
        [SerializeField] private float baseRadius = 5f;

        public void Fit(float radius)
        {
            float scale = radius / baseRadius;
            transform.localScale = Vector3.one * scale;
            foreach (var light in GetComponentsInChildren<Light>()) light.range *= scale;
        }
    }
}
