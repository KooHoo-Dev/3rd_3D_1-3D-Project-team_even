using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 사망 → 부활 대기 → 부활 또는 최종 사망, 그리고 부활 직후 보호.
    ///
    /// 흐름
    ///  - 사망 (체력 0): Died + PlayerDiedEvent. 최종 사망이 아니다 (몬스터는 대상을 놓는다)
    ///  - 목숨이 남았으면 자동으로, 아니면 외부에서 reviveWindow 안에 Revive() (PlayerController.Lives)
    ///  - Revive() → 부활 연출 (reviveDuration). 내내 무적, 입력(시선 포함)·무기·아이템 없음
    ///      · reviveBodyRestoreAt 전: 체력 0 그대로 사망 자세 (IsAlive == false)
    ///      · 그 순간 몸을 선 자세로 되돌리고 (ReviveBodyRestored), 끝날 때까지 체력 1 → 최대
    ///      · 끝: Revived + PlayerRevivedEvent (몬스터가 다시 대상을 잡는다) → 부활 직후 보호 시작
    ///  - 마지막 목숨으로 쓰러져 reviveWindow 안에 부활하지 않음: FinalDied + PlayerFinalDeathEvent (게임오버, 목숨 0). 이 뒤로 Revive 는 false
    ///  - 부활 직후 보호 (reviveBuffDuration): 받는 피해 대폭 경감, 스태미나 무한, 이동 속도 증가
    ///  - 처음부터 재시작 (RoundCleanupEvent.FullReset): 연출·보호를 끊고 체력·스태미나·시작 위치로 되돌린다 (LifeReset)
    /// 연출(이펙트)은 PlayerReviveEffect 가 IsReviving·ReviveProgress·IsReviveBuffActive 를 읽어 그린다
    /// </summary>
    public partial class PlayerController
    {
        /// <summary>부활 연출 시작 (아직 사망 자세)</summary>
        public event Action ReviveStarted;
        /// <summary>연출 중 몸이 사망 자세에서 선 자세로 돌아가는 순간 (애니메이터 초기화)</summary>
        public event Action ReviveBodyRestored;
        /// <summary>부활 연출이 끝나 조작할 수 있게 된 순간 (이어서 부활 직후 보호가 시작된다)</summary>
        public event Action Revived;
        /// <summary>부활 대기 시간이 지나 최종 사망이 확정된 순간</summary>
        public event Action FinalDied;
        /// <summary>재시작으로 체력·상태를 처음으로 되돌린 순간 (죽어 있었다면 애니메이터 초기화)</summary>
        public event Action LifeReset;

        /// <summary>부활 연출 중 (무적, 입력 없음)</summary>
        public bool IsReviving => isReviving;
        /// <summary>부활 연출 진행도 0~1 (연출 중이 아니면 0)</summary>
        public float ReviveProgress => isReviving && reviveCurrentDuration > 0f
            ? Mathf.Clamp01((Time.time - reviveStartTime) / reviveCurrentDuration) : 0f;
        /// <summary>ReviveProgress 가 이 값에 닿을 때 몸이 선 자세로 돌아간다</summary>
        public float ReviveBodyRestoreAt => reviveBodyRestoreAt;
        /// <summary>최종 사망 확정 (게임오버 대상, 더는 부활하지 않음)</summary>
        public bool IsFinallyDead => finalDead;
        /// <summary>지금 Revive 를 부르면 받아들여지는지 (죽었고, 최종 사망 전이고, 연출 중이 아님)</summary>
        public bool CanRevive => IsDead && finalDead == false && isReviving == false;
        /// <summary>마지막 목숨으로 쓰러져 최종 사망까지 남은 시간(초). 목숨이 남아 자동 부활할 상황이거나 부활할 수 없으면 0</summary>
        public float ReviveWindowRemaining => CanRevive && lives <= 1 && DebugInfiniteLives == false
            ? Mathf.Max(0f, deathTime + reviveWindow - Time.time) : 0f;

        /// <summary>부활 직후 보호 중 (피해 경감, 스태미나 무한, 이동 속도 증가)</summary>
        public bool IsReviveBuffActive => Time.time < reviveBuffEndTime;
        /// <summary>보호 남은 비율 1 → 0 (보호 중이 아니면 0)</summary>
        public float ReviveBuffRemaining01 => IsReviveBuffActive && reviveBuffDuration > 0f
            ? Mathf.Clamp01((reviveBuffEndTime - Time.time) / reviveBuffDuration) : 0f;
        public float ReviveBuffRemainingTime => IsReviveBuffActive ? reviveBuffEndTime - Time.time : 0f;

        /// <summary>디버그(PlayerDebugMode): 부활 대기 시간이 지나도 최종 사망으로 넘어가지 않는다</summary>
        public bool DebugHoldFinalDeath { get; set; }

        /// <summary>인스펙터의 reviveDuration 으로 부활. CanRevive 가 아니면 false</summary>
        public bool Revive() => Revive(reviveDuration);

        /// <summary>duration 초 동안 연출 후 부활 (0 = 연출 없이 즉시). CanRevive 가 아니면 false</summary>
        public bool Revive(float duration)
        {
            if (CanRevive == false) return false;

            isReviving = true;
            reviveBodyRestored = false;
            reviveStartTime = Time.time;
            reviveCurrentDuration = Mathf.Max(0f, duration);

            // 죽기 직전 입력이 남아 있다가 부활 직후 실행되지 않게 전부 비운다
            SetMoveInput(Vector3.zero, false);
            jumpRequestedTime = float.NegativeInfinity;
            isRolling = false;
            if (gunSlot != null)
            {
                gunSlot.TriggerUp();
                gunSlot.AimUp();
            }
            RestoreStamina();

            ReviveStarted?.Invoke();
            UpdateRevive();
            return true;
        }

        /// <summary>Update 에서 매 프레임 (입력 막힘·외부 제어와 상관없이 진행)</summary>
        private void UpdateLifeCycle()
        {
            UpdateRevive();
            if (CanRevive) UpdateDeathOutcome(); // 목숨으로 자동 부활 또는 최종 사망 (PlayerController.Lives)
        }

        /// <summary>Die 끝에서. 대기 시간이 0 이면 이 자리에서 바로 부활 또는 최종 사망</summary>
        private void OnDeathForRevive()
        {
            deathTime = Time.time;
            EndReviveBuff();
            EventBus<PlayerDiedEvent>.Publish(new PlayerDiedEvent());
            if (CanRevive) UpdateDeathOutcome();
        }

        private void FinalDeath()
        {
            finalDead = true;
            lives = 0;
            PublishLives();
            FinalDied?.Invoke();
            EventBus<PlayerFinalDeathEvent>.Publish(new PlayerFinalDeathEvent());
        }

        private void UpdateRevive()
        {
            if (isReviving == false) return;
            float p = reviveCurrentDuration <= 0f ? 1f : ReviveProgress;

            if (reviveBodyRestored == false && p >= reviveBodyRestoreAt) RestoreBodyForRevive();

            if (reviveBodyRestored)
            {
                // 몸이 돌아온 뒤 끝날 때까지 체력이 1 → 최대로 차오른다
                float fill = reviveBodyRestoreAt >= 1f ? 1f : Mathf.InverseLerp(reviveBodyRestoreAt, 1f, p);
                int hp = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, MaxHealth, fill)), 1, MaxHealth);
                if (hp != currentHealth)
                {
                    currentHealth = hp;
                    PublishHp();
                }
            }

            if (p >= 1f) FinishRevive();
        }

        private void RestoreBodyForRevive()
        {
            reviveBodyRestored = true;
            IsDead = false;
            currentHealth = Mathf.Max(currentHealth, 1);
            PublishHp();
            ReviveBodyRestored?.Invoke();
        }

        private void FinishRevive()
        {
            if (reviveBodyRestored == false) RestoreBodyForRevive();
            isReviving = false;
            currentHealth = MaxHealth;
            PublishHp();
            RestoreStamina();
            StartReviveBuff();
            Revived?.Invoke();
            EventBus<PlayerRevivedEvent>.Publish(new PlayerRevivedEvent());
        }

        #region 부활 직후 보호
        private void StartReviveBuff()
        {
            if (reviveBuffDuration <= 0f) return;
            reviveBuffEndTime = Time.time + reviveBuffDuration;
            AddTimedSpeedModifier(ReviveBuffSpeedKey, reviveBuffSpeedMultiplier, reviveBuffDuration);
        }

        private void EndReviveBuff()
        {
            reviveBuffEndTime = float.NegativeInfinity;
            RemoveSpeedModifier(ReviveBuffSpeedKey);
        }

        /// <summary>보호 중이면 줄인 피해량, 아니면 그대로 (TakeDamage)</summary>
        private int ApplyReviveBuff(int amount)
            => IsReviveBuffActive && amount > 0 ? Mathf.RoundToInt(amount * reviveBuffDamageMultiplier) : amount;
        #endregion

        #region 재시작
        private void SubscribeLifeEvents()
        {
            ServiceLocator.Register<IPlayerLives>(this);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
        }

        private void UnsubscribeLifeEvents()
        {
            ServiceLocator.Unregister<IPlayerLives>(this);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
        }

        /// <summary>처음부터 재시작이면 체력·스태미나·상태·위치를 처음으로 (BL-033). 다음 라운드 진행이면 그대로</summary>
        private void OnRoundCleanup(RoundCleanupEvent evt)
        {
            if (evt.FullReset == false) return;
            ResetLife();
            ClearVelocitySources(); // 보스가 Stop 없이 사라졌어도 남지 않게
            Teleport(spawnPosition);
            SetRotationImmediate(spawnYaw);
            SyncRigRotationNow();
        }

        private void ResetLife(bool resetLives = true)
        {
            isReviving = false;
            reviveBodyRestored = false;
            finalDead = false;
            IsDead = false;
            EndReviveBuff();
            currentHealth = MaxHealth;
            PublishHp();
            RestoreStamina();
            SetMoveInput(Vector3.zero, false);
            // LifeReset 전에: 받는 쪽이 복원된 목숨을 본다.
            if (resetLives) ResetLives();
            else PublishLives();
            LifeReset?.Invoke();
        }
        #endregion
    }
}
