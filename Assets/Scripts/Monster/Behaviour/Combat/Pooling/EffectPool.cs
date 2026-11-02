using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 이펙트 프리팹 1개의 재사용 풀. SimplePool 에 "한 번 재생 → 수명 뒤 자동 반납"을 더한다.
    /// 설정 컴포넌트(SelfDestructSettings, MonsterDamageFeedback)가 Awake 에서 이펙트마다 1개씩 만들고,
    /// 이펙트가 필요한 순간 Play 한 줄로 쓴다. 반납은 PooledEffect 가 스스로 한다.
    /// </summary>
    public sealed class EffectPool
    {
        private readonly SimplePool<PooledEffect> pool;

        /// <summary>풀을 만든다. 이펙트는 처음 재생할 때 만든다</summary>
        /// <param name="prefab">PooledEffect 가 붙은 이펙트 프리팹</param>
        /// <exception cref="System.ArgumentNullException">prefab 이 null 일 때 (SimplePool 이 던진다)</exception>
        public EffectPool(PooledEffect prefab)
        {
            pool = new SimplePool<PooledEffect>(prefab);
        }

        /// <summary>쉬고 있는 이펙트를 꺼내 position·rotation 에 놓고 처음부터 재생한다</summary>
        /// <param name="position">재생할 월드 위치</param>
        /// <param name="rotation">재생 방향. 이펙트 프리팹의 +Z 가 이 방향을 향한다</param>
        /// <returns>재생을 시작한 이펙트. 크기를 바꿀 때만 쓴다 (자폭 폭발이 반경에 맞춰 줄인다)</returns>
        public PooledEffect Play(Vector3 position, Quaternion rotation)
        {
            PooledEffect effect = pool.Get(position);
            effect.transform.rotation = rotation;
            effect.Play(this);
            return effect;
        }

        /// <summary>이펙트를 끄고 풀에 돌려놓는다. PooledEffect 가 수명이 끝날 때 부른다</summary>
        /// <param name="effect">이 풀에서 꺼낸 이펙트</param>
        public void Return(PooledEffect effect)
        {
            pool.Return(effect);
        }
    }
}
