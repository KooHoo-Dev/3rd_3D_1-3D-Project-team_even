using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 자폭형(SelfDestruct) 몬스터 프리팹에 붙이는 폭발 연출 설정. 이펙트 프리팹을 갖고, 그 풀을 만든다.
    /// MonsterController.Start 가 찾아 MonsterAttackFactory 로 넘기고, SelfDestructAttack 이 폭발 순간 풀에서 재생한다.
    /// </summary>
    /// <remarks>풀은 Awake 에서 만든다. MonsterController.Start 보다 먼저 준비된다.</remarks>
    public sealed class SelfDestructSettings : MonsterAttackSettingsComponent
    {
        [Header("폭발 이펙트")]
        [Tooltip("폭발 순간 몸 위치에 재생할 이펙트 프리팹 (Tools/HybridShooter/Setup Monster Explosion VFX 가 만든다). 반납까지 시간은 프리팹의 PooledEffect 수명")]
        [SerializeField] private PooledEffect explosionEffectPrefab;

        public EffectPool ExplosionPool { get; private set; }

        /// <summary>폭발 이펙트 풀을 만든다</summary>
        private void Awake()
        {
            ExplosionPool = new EffectPool(explosionEffectPrefab);
        }
    }
}
