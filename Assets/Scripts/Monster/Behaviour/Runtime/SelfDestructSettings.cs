using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 자폭형(SelfDestruct) 몬스터 프리팹에 붙이는 폭발 설정. 이펙트 프리팹과 풀, 처치 시 폭발·몬스터 피해 여부를 갖는다.
    /// MonsterController.Start 가 찾아 MonsterAttackFactory 로 넘기고, SelfDestructAttack 이 폭발 순간 풀에서 재생한다.
    /// </summary>
    /// <remarks>풀은 Awake 에서 만든다. MonsterController.Start 보다 먼저 준비된다.</remarks>
    public sealed class SelfDestructSettings : MonsterAttackSettingsComponent
    {
        [Header("폭발 이펙트")]
        [Tooltip("폭발 순간 몸 위치에 재생할 이펙트 프리팹 (Tools/HybridShooter/Setup Monster Explosion VFX 가 만든다). 반납까지 시간은 프리팹의 PooledEffect 수명")]
        [SerializeField] private PooledEffect explosionEffectPrefab;

        [Header("폭발 규칙")]
        [Tooltip("켜면 플레이어에게 처치될 때도 그 자리에서 폭발한다. 끄면 처치당하면 폭발 없이 죽는다")]
        [SerializeField] private bool explodeWhenKilled = true;
        [Tooltip("켜면 폭발이 반경 안 몬스터에게도 피해를 준다 (중립 진영 Faction.Neutral 피해). 끄면 플레이어만 맞는다")]
        [SerializeField] private bool damageMonsters = true;

        public EffectPool ExplosionPool { get; private set; }
        public bool ExplodeWhenKilled => explodeWhenKilled;
        public bool DamageMonsters => damageMonsters;

        /// <summary>폭발 이펙트 풀을 만든다</summary>
        private void Awake()
        {
            ExplosionPool = new EffectPool(explosionEffectPrefab);
        }
    }
}
