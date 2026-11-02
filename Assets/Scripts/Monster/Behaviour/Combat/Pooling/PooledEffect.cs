using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 풀에서 꺼내 한 번 재생하고, 수명이 지나면 스스로 풀에 돌아가는 이펙트 (폭발·피). 이펙트 프리팹 루트에 붙인다.
    /// EffectPool.Play 가 꺼낸 직후 Play 를 부른다. 반납 시계를 이펙트 자신이 들고 있어서,
    /// 이펙트를 만든 몬스터가 먼저 회수(비활성)돼도 끝까지 재생된 뒤 반납된다.
    /// </summary>
    public sealed class PooledEffect : MonoBehaviour
    {
        [Tooltip("재생 시작부터 풀 반납까지 시간 (s). 가장 오래 남는 파티클(연기 등)이 다 사라지는 시간보다 길게 둔다")]
        [SerializeField, Min(0.1f)] private float lifetime = 2f;

        private EffectPool owner;   // 반납할 풀. Play 가 넣는다
        private float returnTime;   // 반납 예정 시각 (s, Time.time 기준)

        /// <summary>
        /// 하위 파티클을 모두 처음부터 다시 재생하고 반납 시각을 정한다. EffectPool.Play 가 위치·회전을 맞춘 직후 부른다.
        /// </summary>
        /// <param name="pool">수명이 끝나면 돌아갈 풀</param>
        public void Play(EffectPool pool)
        {
            owner = pool;
            returnTime = Time.time + lifetime;
            foreach (ParticleSystem particles in GetComponentsInChildren<ParticleSystem>())
            {
                particles.Clear();
                particles.Play();
            }
        }

        /// <summary>반납 시각이 되면 풀에 돌아간다 (풀이 끈다)</summary>
        private void Update()
        {
            if (Time.time < returnTime) return;
            owner.Return(this);
        }
    }
}
