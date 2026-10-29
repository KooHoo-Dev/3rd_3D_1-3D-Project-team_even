using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 로켓·유탄 뒤에 붙는 꼬리 (불꽃·연기 파티클, TrailRenderer, 불빛).
    /// GunProjectile 이 생성해 탄에 붙이고, 탄이 터지거나 사라질 때 Detach 를 불러
    /// 이미 뿌려진 연기는 남겨 두고 새로 뿜는 것만 멈춘 뒤 다 사라지면 지운다
    /// </summary>
    public sealed class ProjectileTrail : MonoBehaviour
    {
        public void Detach()
        {
            transform.SetParent(null, true);

            float remaining = 0f;
            foreach (var ps in GetComponentsInChildren<ParticleSystem>())
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                remaining = Mathf.Max(remaining, ps.main.startLifetime.constantMax);
            }
            foreach (var trail in GetComponentsInChildren<TrailRenderer>())
            {
                trail.emitting = false;
                remaining = Mathf.Max(remaining, trail.time);
            }
            // 탄에 붙은 불빛은 탄과 함께 꺼진다 (폭발 불빛은 폭발 이펙트가 따로 낸다)
            foreach (var light in GetComponentsInChildren<Light>()) light.enabled = false;
            Destroy(gameObject, remaining + 0.1f);
        }
    }
}
