using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Behaviour;
using UnityEngine;

namespace Scenes.HS.MonsterIntegrationLab.Runtime
{
    /// <summary>
    /// 테스트 전용. Play 중 컴포넌트 우클릭(ContextMenu)으로 몬스터에 피해·즉사·기절을 걸어 Animator 의 기절·사망 전이를 눈으로 확인한다.
    /// MonsterController 의 기존 public 경로(TakeDamage, StateMachine.Stun)만 부르므로 실제 게임과 같은 흐름을 탄다.
    /// MonsterController 와 같은 오브젝트에 붙인다.
    /// </summary>
    public sealed class MonsterDebugActions : MonoBehaviour
    {
        [Header("디버그 값")]
        [Tooltip("'피해 주기' 한 번에 줄일 체력")]
        [SerializeField, Min(1)] private int damageAmount = 10;
        [Tooltip("'기절시키기' 의 기절 시간 (s)")]
        [SerializeField, Min(0f)] private float stunSeconds = 2f;

        private MonsterController monster;

        private void Awake()
        {
            monster = GetComponent<MonsterController>();
        }

        /// <summary>damageAmount 만큼 피해. 체력이 0 이 되면 실제 사망 흐름(사망 이벤트, 시체 누움·침강 뒤 회수)을 탄다</summary>
        [ContextMenu("피해 주기")]
        private void Damage() => ApplyDamage(damageAmount);

        /// <summary>남은 체력 전부만큼 피해를 줘 바로 죽인다</summary>
        [ContextMenu("즉사")]
        private void Kill() => ApplyDamage(monster.CurrentHp);

        /// <summary>stunSeconds 동안 기절. 공격 중이면 공격이 취소되고 바로 Stunned 로 간다</summary>
        [ContextMenu("기절시키기")]
        private void Stun() => monster.StateMachine.Stun(stunSeconds);

        /// <summary>플레이어 총알과 같은 형태의 피해 정보를 만들어 MonsterController.TakeDamage 로 넘긴다</summary>
        private void ApplyDamage(int amount)
        {
            var info = new DamageInfo(amount, DamageKind.Bullet, Faction.Player, gameObject,
                                      transform.position, Vector3.zero);
            monster.TakeDamage(in info);
        }
    }
}
