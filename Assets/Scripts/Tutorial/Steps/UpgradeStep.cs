using System.Collections;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using HybridShooter.Reward;
using UnityEngine;

namespace HybridShooter.Tutorial.Steps
{
    /// <summary>
    /// ⑤ 강화 체험. 강화 구역에 들어오면 실제 강화 화면(UpgradeManager + UpgradeScreen)을 연다 (상호작용 없음).
    ///  - 여는 법은 본편과 같다: GameStateChangedEvent(→ Reward) 발행, 닫히면 RewardSelectionFinishedEvent → GameStateChangedEvent(Reward → CrowdBattle)
    ///  - 다음 리롤 1회는 에픽 확정(UpgradeManager.ForceNextRerollGrade) → 에픽 등급이 있다는 것을 보여 준다
    ///  - 1장 사야 닫을 수 있다(RequirePurchaseToFinish). 재화가 리롤 + 1레벨 카드 값보다 적으면 모자란 만큼 채워 준다
    /// 산 강화는 튜토리얼이 끝날 때 Director 가 초기화한다
    /// </summary>
    public sealed class UpgradeStep : TutorialStep
    {
        public override string Title => "장비 개조";

        private UpgradeManager manager;
        private bool finished;
        private bool opened;

        public override IEnumerator Run()
        {
            D.Allow(PlayerAbility.Movement); // 강화 화면이 열린 동안은 게임 흐름 차단(Reward)이 따로 막는다
            Objective("obj_upgrade");
            if (D.UpgradeZone != null)
            {
                D.ShowDestination(D.UpgradeZone, "wp_upgrade");
                yield return D.WaitUntil(() => D.UpgradeZone.Reached, null);
                D.HideDestination();
            }

            manager = Object.FindAnyObjectByType<UpgradeManager>();
            if (manager == null)
            {
                Debug.LogWarning("[Tutorial] 씬에 UpgradeManager 가 없어 강화 체험을 건너뜁니다. Setup Tutorial Scene 을 다시 실행하세요.", D);
            }
            else
            {
                bool toppedUp = TopUpFunds();
                EventBus<RewardSelectionFinishedEvent>.Subscribe(OnRewardSelectionFinished);
                D.Hud.ClearObjective();
                D.Hud.SetSubtitleAtTop(true); // 강화 화면(정렬 100)이 아래쪽 버튼을 쓰므로 자막은 위로
                opened = true;
                EventBus<GameStateChangedEvent>.Publish(new GameStateChangedEvent(GameState.CrowdBattle, GameState.Reward));
                manager.RequirePurchaseToFinish = true;
                manager.ForceNextRerollGrade(UpgradeGrade.Epic);

                yield return D.Say("vo_tut_05_open");
                if (toppedUp) yield return D.Say("vo_tut_05_funds");
                D.SayNow("vo_tut_05_reroll");
                yield return D.WaitUntil(() => manager.RerollCount > 0 || manager.HasPurchased, "vo_tut_05_reroll_hint");
                if (manager.RerollCount > 0) D.SayNow("vo_tut_05_epic");
                yield return D.WaitUntil(() => manager.HasPurchased, "vo_tut_05_buy_hint");
                D.SayNow("vo_tut_05_bought");
                yield return D.WaitUntil(() => finished, null);
                Close();
            }

            if (D.UpgradeGate != null) D.UpgradeGate.Open();
        }

        public override void Cleanup()
        {
            EventBus<RewardSelectionFinishedEvent>.Unsubscribe(OnRewardSelectionFinished);
            Close();
        }

        /// <summary>강화 화면을 닫은 뒤 게임 흐름을 되돌린다 (입력 잠금 해제·사운드 스냅샷 복귀)</summary>
        private void Close()
        {
            if (opened == false) return;
            opened = false;
            if (manager != null && manager.IsOpen) manager.Finish();
            EventBus<GameStateChangedEvent>.Publish(new GameStateChangedEvent(GameState.Reward, GameState.CrowdBattle));
            D.Hud.SetSubtitleAtTop(false);
        }

        /// <summary>리롤 1회 + 가장 비싼 1레벨 카드 1장을 살 수 있게 재화를 채운다. 채웠으면 true</summary>
        private bool TopUpFunds()
        {
            if (ServiceLocator.TryGet<ICurrencyWallet>(out var wallet) == false) return false;
            int maxCost = 0;
            foreach (var track in DataManager.UpgradeTracks.All)
            {
                var first = track.GetNext(0);
                if (first != null && first.Cost > maxCost) maxCost = first.Cost;
            }
            int need = manager.RerollCost + maxCost;
            if (wallet.Balance >= need) return false;
            wallet.Add(need - wallet.Balance);
            return true;
        }

        private void OnRewardSelectionFinished(RewardSelectionFinishedEvent e)
        {
            finished = true;
            D.MarkProgress();
        }
    }
}
