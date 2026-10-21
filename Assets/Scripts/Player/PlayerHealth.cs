using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 플레이어 체력. 몬스터·보스는 IDamageable 로, 회복 팩은 IHealable 로만 접근한다 (이 클래스 이름을 몰라도 됨).
    ///
    ///  TakeDamage ─→ (구르기 무적 / 피격 직후 무적이면 무시) → HP 감소
    ///             ─→ PlayerDamagedEvent · PlayerHpChangedEvent 발행, Damaged 이벤트(애니메이션 피격 반응)
    ///             ─→ HP 0 이면 PlayerDiedEvent, Died 이벤트(사망 애니메이션, 조작 중지)
    ///  Heal ──────→ HP 증가 (가득 차 있으면 false)
    ///
    /// 최대 HP 는 PlayerTable.MaxHp, 피격 후 무적 시간은 ConfigTable.PlayerInvincibleTime.
    /// </summary>
    public class PlayerHealth : MonoBehaviour, IDamageable, IHealable
    {
        [Tooltip("PlayerTable 이 없을 때의 최대 HP")]
        [SerializeField] private int fallbackMaxHp = 100;
        [Tooltip("ConfigTable 이 없을 때 피격 후 무적 시간(초)")]
        [SerializeField] private float fallbackInvincibleTime = 0.5f;

        private int maxHp;
        private int currentHp;
        private float invincibleTime;
        private float invincibleUntil = float.NegativeInfinity;
        private PlayerMotor motor;

        /// <summary>피해를 받은 직후 (애니메이션 피격 반응용)</summary>
        public event Action<DamageInfo> Damaged;
        public event Action Died;
        public event Action Revived;

        public Faction Faction => Faction.Player;
        public bool IsAlive => currentHp > 0;
        public int Current => currentHp;
        public int Max => maxHp;
        public bool IsFull => currentHp >= maxHp;
        /// <summary>구르기 무적 또는 피격 직후 무적</summary>
        public bool IsInvulnerable => Time.time < invincibleUntil || (motor != null && motor.IsInvulnerable);

        private void Awake()
        {
            motor = GetComponent<PlayerMotor>();

            PlayerDef def = DataManager.Player;
            maxHp = def != null ? def.MaxHp : fallbackMaxHp;
            var config = DataManager.Config;
            invincibleTime = config != null && config.Has(GameConfig.PlayerInvincibleTime)
                ? config.GetFloat(GameConfig.PlayerInvincibleTime)
                : fallbackInvincibleTime;
            currentHp = maxHp;
        }

        private void Start() => PublishHp();

        public void TakeDamage(in DamageInfo info)
        {
            if (IsAlive == false || IsInvulnerable || info.Amount <= 0) return;

            currentHp = Mathf.Max(0, currentHp - info.Amount);
            invincibleUntil = Time.time + invincibleTime;

            EventBus<PlayerDamagedEvent>.Publish(new PlayerDamagedEvent(info));
            PublishHp();
            Damaged?.Invoke(info);

            if (currentHp == 0)
            {
                EventBus<PlayerDiedEvent>.Publish(new PlayerDiedEvent());
                Died?.Invoke();
            }
        }

        public bool Heal(int amount)
        {
            if (IsAlive == false || amount <= 0 || IsFull) return false;
            currentHp = Mathf.Min(maxHp, currentHp + amount);
            PublishHp();
            return true;
        }

        /// <summary>처음부터 다시 시작할 때. HP 가득 + 살아남</summary>
        public void Revive()
        {
            bool wasDead = IsAlive == false;
            currentHp = maxHp;
            invincibleUntil = float.NegativeInfinity;
            PublishHp();
            if (wasDead) Revived?.Invoke();
        }

        private void PublishHp() => EventBus<PlayerHpChangedEvent>.Publish(new PlayerHpChangedEvent(currentHp, maxHp));

#if UNITY_EDITOR
        [ContextMenu("테스트: 앞에서 20 피해")]
        private void DebugDamageFront() => TakeDamage(new DamageInfo(20, DamageKind.MonsterAttack, Faction.Monster, null,
            transform.position + transform.forward, -transform.forward));

        [ContextMenu("테스트: 즉사")]
        private void DebugKill() => TakeDamage(new DamageInfo(9999, DamageKind.MonsterAttack, Faction.Monster, null,
            transform.position + transform.forward, -transform.forward));

        [ContextMenu("테스트: 부활")]
        private void DebugRevive() => Revive();
#endif
    }
}
