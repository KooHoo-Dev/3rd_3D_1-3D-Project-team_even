using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 목숨 (IPlayerLives). 남은 목숨은 지금 목숨을 포함한다 (3 = 기회 3번, 부활 2번).
    ///  - 사망 후: 2 이상 남았으면 livesReviveDelay 뒤 1 소모하고 자동 부활, 1 이면 reviveWindow 뒤 최종 사망 (0 이 된다)
    ///  - 최대 목숨 = 난이도 기준(SetMaxLives, 시작 값은 PlayerTable.MaxLives) + 스테이지 클리어로 늘어난 몫(AddMaxLives)
    ///  - 처음부터 재시작: 늘어난 몫은 사라지고 현재 = 최대 (난이도 기준은 유지)
    ///  - 외부에서 직접 Revive() 를 부르면 목숨을 쓰지 않는다 (디버그·부활 아이템)
    /// 변화는 PlayerLivesChangedEvent 로 알린다 (HUD)
    /// </summary>
    public partial class PlayerController
    {
        public int Lives => lives;
        public int MaxLives => baseMaxLives + bonusMaxLives;

        /// <summary>디버그(PlayerDebugMode): 사망하면 목숨을 쓰지 않고 항상 자동 부활</summary>
        public bool DebugInfiniteLives { get; set; }

        public void SetMaxLives(int max, bool refill)
        {
            baseMaxLives = Mathf.Max(1, max);
            ClampLives(refill);
        }

        public void AddMaxLives(int amount, bool addLives)
        {
            if (amount <= 0) return;
            bonusMaxLives += amount;
            if (addLives) lives += amount;
            ClampLives(false);
        }

        public bool AddLives(int amount)
        {
            if (amount <= 0 || lives >= MaxLives) return false;
            lives = Mathf.Min(lives + amount, MaxLives);
            PublishLives();
            return true;
        }

        /// <summary>Awake 에서. 테이블(DataManager 는 씬 로드 전에 읽혀 있다)이 없으면 인스펙터 값</summary>
        private void InitLives()
        {
            var def = DataManager.Player;
            baseMaxLives = Mathf.Max(1, def != null ? def.MaxLives : fallbackMaxLives);
            bonusMaxLives = 0;
            lives = Mathf.Clamp(def != null ? def.StartLives : fallbackStartLives, 1, MaxLives);
        }

        /// <summary>처음부터 재시작 (ResetLife, LifeReset 전에): 늘어난 몫을 비우고 가득</summary>
        private void ResetLives()
        {
            bonusMaxLives = 0;
            lives = MaxLives;
            PublishLives();
        }

        /// <summary>최대를 넘지 않게. 최종 사망 전이면 최소 1 (지금 목숨)</summary>
        private void ClampLives(bool refill)
        {
            lives = refill ? MaxLives : Mathf.Min(lives, MaxLives);
            if (finalDead == false) lives = Mathf.Max(lives, 1);
            PublishLives();
        }

        /// <summary>
        /// UpdateLifeCycle 에서 (죽었고 부활 가능할 때만). 목숨이 남았으면 자동 부활, 마지막이면 최종 사망.
        /// 그 시점의 목숨을 보므로, 마지막 목숨으로 쓰러진 사이 AddLives 가 오면 부활로 바뀐다
        /// </summary>
        private void UpdateDeathOutcome()
        {
            float downed = Time.time - deathTime;
            if (lives > 1 || DebugInfiniteLives)
            {
                if (downed < livesReviveDelay) return;
                if (DebugInfiniteLives == false)
                {
                    lives--;
                    PublishLives();
                }
                Revive();
            }
            else if (DebugHoldFinalDeath == false && downed >= reviveWindow) FinalDeath();
        }

        private void PublishLives() => EventBus<PlayerLivesChangedEvent>.Publish(new PlayerLivesChangedEvent(lives, MaxLives));
    }
}
