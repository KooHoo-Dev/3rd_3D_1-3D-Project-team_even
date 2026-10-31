using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 자폭형(SelfDestruct) 몬스터 프리팹에 붙이는 폭발 연출 설정. 이펙트 프리팹과 수명을 한곳에 모은다.
    /// MonsterController.Start 가 찾아 MonsterAttackFactory 로 넘기고, SelfDestructAttack 이 폭발 순간 읽는다.
    /// </summary>
    public sealed class SelfDestructSettings : MonoBehaviour
    {
        [Header("폭발 이펙트")]
        [Tooltip("폭발 순간 몸 위치에 생성할 이펙트 프리팹 (Tools/HybridShooter/Setup Monster Explosion VFX 가 만든다)")]
        [SerializeField] private GameObject explosionEffectPrefab;
        [Tooltip("이펙트 생성 후 파괴까지 시간 (s). 연기가 다 사라지는 시간보다 길게 둔다")]
        [SerializeField, Min(0.1f)] private float effectLifetime = 4f;

        public GameObject ExplosionEffectPrefab => explosionEffectPrefab;
        public float EffectLifetime => effectLifetime;
    }
}
