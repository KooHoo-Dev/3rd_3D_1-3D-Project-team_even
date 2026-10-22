using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 몬스터 체력 (부위 없음, 단일 체력). 라이플·수류탄이 IDamageable 로 찾아 피해를 준다.
    /// 맞을 때마다 DamageDealtEvent 를 발행하고, 0 이 되면 Depleted 를 1번 알린다.
    /// </summary>
    public sealed class MonsterHealth : MonoBehaviour, IDamageable
    {
        public int MaxHp { get; private set; }
        public int CurrentHp { get; private set; }

        public Faction Faction => Faction.Monster;
        public bool IsAlive => CurrentHp > 0;

        /// <summary>체력이 0 이 된 순간 1번</summary>
        public event Action Depleted;

        public void Initialize(int maxHp)
        {
            if (maxHp <= 0) throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "최대 체력은 1 이상");
            MaxHp = maxHp;
            CurrentHp = maxHp;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (MaxHp == 0) throw new InvalidOperationException($"{nameof(MonsterHealth)}.Initialize 전에 피해를 받았다");
            if (IsAlive == false) return;

            CurrentHp = Mathf.Max(0, CurrentHp - info.Amount);
            bool isKill = IsAlive == false;
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(gameObject, info, isKill));
            if (isKill) Depleted?.Invoke();
        }
    }
}
