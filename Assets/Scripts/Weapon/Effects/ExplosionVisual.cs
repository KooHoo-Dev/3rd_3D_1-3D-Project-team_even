using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 폭발 이펙트 프리팹 루트에 붙인다. GunExplosion 이 생성 직후 Fit(피해 반경)을 불러
    /// 보이는 크기를 실제 피해 범위(구)에 맞춘다.
    ///  - 하위 파티클은 Scaling Mode = Hierarchy 여야 루트 크기를 따라간다
    ///  - 불빛은 크기를 따라가지 않으므로 범위를 같은 비율로 늘린다
    ///  - 피해를 받은 대상마다 PlayHit 으로 맞은 지점에 이펙트를 낸다 (공중몹도 폭발에 닿았다는 게 보이게)
    /// </summary>
    public sealed class ExplosionVisual : MonoBehaviour
    {
        [Tooltip("배율 1 일 때 보이는 폭발 구의 반경(m). 이 값과 피해 반경의 비율로 키운다")]
        [SerializeField] private float baseRadius = 5f;
        [Tooltip("(선택) 폭발 피해를 받은 지점에 낼 이펙트. 로컬 +Z = 폭발 중심 → 대상. 크기는 피해 반경과 상관없이 그대로")]
        [SerializeField] private GameObject hitEffectPrefab;

        public void Fit(float radius)
        {
            float scale = radius / baseRadius;
            transform.localScale = Vector3.one * scale;
            foreach (var light in GetComponentsInChildren<Light>()) light.range *= scale;
        }

        /// <summary>GunExplosion 이 피해를 준 대상마다 부른다. direction = 폭발 중심 → 맞은 지점 (정규화)</summary>
        public void PlayHit(Vector3 point, Vector3 direction)
        {
            if (hitEffectPrefab == null) return;
            Destroy(Instantiate(hitEffectPrefab, point, Quaternion.LookRotation(direction)), 1f);
        }
    }
}
