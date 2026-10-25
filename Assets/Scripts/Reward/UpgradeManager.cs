using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Reward
{
    /// <summary>
    /// 라운드 종료 후 강화 선택 단계 담당 (BL-030, BL-031, BL-032).
    /// 보스를 처치하면 이번 라운드에 번 재화를 보여 주고, 세 강화 중 하나를 구매한 뒤 다음 라운드를 요청한다.
    ///
    ///   열기   : GameStateChangedEvent(Current = Reward) 수신. [임시] BossDefeatedEvent 로도 연다 (코어 구현 전까지)
    ///   구매   : TryPurchase → IUpgradeState.TryPurchase (재화 차감·레벨 상승·UpgradePurchasedEvent 는 Ledger 담당)
    ///   종료   : Finish → RewardSelectionFinishedEvent 발행 (다음 라운드 진행은 코어 담당)
    ///   획득액 : CurrencyChangedEvent 의 증가분(Delta > 0)을 합산. BossAppearedEvent 이전 = 군중전, 이후 = 보스전
    ///            RoundStartedEvent / RoundCleanupEvent(FullReset) 에서 0 으로
    ///
    /// 설계 포인트
    ///  - 라운드당 하나만 구매한다. 살 수 있는 강화가 있으면 하나를 사야 Finish 할 수 있고, 없으면 바로 건너뛸 수 있다.
    ///  - 비용·효과 수치는 UpgradeTable.tsv 값만 사용한다. (Inspector 에 같은 의미의 값을 두지 않음)
    ///  - 화면(UpgradeScreen)은 이 컴포넌트를 직접 참조하고 Changed 로 갱신한다. 전역 버스로 다시 알리지 않는다.
    ///  - 열린 동안 재화가 바뀌면 Changed 를 다시 알린다. → 보스 보상 지급과 화면 열기의 구독 순서와 상관없이 최종 표시값이 맞다.
    ///  - 씬에 하나만 동작한다. 두 개 이상 켜지면 먼저 켜진 것만 동작하고, 나머지는 경고 후 아무것도 하지 않는다.
    ///
    /// 다른 파트에 기대하는 것
    ///  - 보스는 등장할 때 BossAppearedEvent 를 발행한다. (없으면 보스전 획득액도 군중전으로 집계됨)
    ///  - 코어는 RewardSelectionFinishedEvent 를 받아 다음 라운드를 시작한다.
    ///  - 강화 화면에서의 커서 해제·사격 입력 차단은 플레이어 쪽이 GameState.Reward 를 보고 처리한다.
    /// </summary>
    public sealed class UpgradeManager : MonoBehaviour
    {
        /// <summary>강화 카드 한 장에 필요한 값 (런타임 조합 값, 정의 데이터 아님)</summary>
        public readonly struct UpgradeOption
        {
            public readonly UpgradeType Type;
            public readonly int Level, MaxLevel;
            /// <summary>현재 레벨의 강화 (Lv0 이면 null)</summary>
            public readonly UpgradeDef Current;
            /// <summary>다음에 살 강화 (최대 레벨이면 null)</summary>
            public readonly UpgradeDef Next;
            /// <summary>현재까지 누적된 값 / 구매 후 누적될 값</summary>
            public readonly float CurrentValue, NextValue;
            public readonly bool CanBuy;

            public bool IsMax => Next == null;

            public UpgradeOption(UpgradeType type, int level, int maxLevel, UpgradeDef current, UpgradeDef next,
                                 float currentValue, float nextValue, bool canBuy)
            {
                Type = type; Level = level; MaxLevel = maxLevel; Current = current; Next = next;
                CurrentValue = currentValue; NextValue = nextValue; CanBuy = canBuy;
            }
        }

        /// <summary>화면 표시 순서 = enum 순서 (재장전 속도 · 탄창 크기 · 수류탄 보유량)</summary>
        private static readonly UpgradeType[] types = (UpgradeType[])Enum.GetValues(typeof(UpgradeType));

        /// <summary>지금 동작 중인 Manager. 두 번째 Manager 가 켜져도 이중으로 열리지 않도록 하나만 기억한다</summary>
        private static UpgradeManager active;

        /// <summary>도메인 리로드를 꺼도 플레이 시작마다 비워지도록 (EventBus·ServiceLocator 와 같은 방식)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active = null;

        /// <summary>열림·구매·재화 변화 등 화면이 다시 그려야 할 때</summary>
        public event Action Changed;

        public IReadOnlyList<UpgradeType> Types => types;
        public bool IsOpen { get; private set; }
        public bool HasPurchased { get; private set; }
        /// <summary>이번 단계에서 구매한 강화 종류 (HasPurchased 일 때만 의미 있음)</summary>
        public UpgradeType PurchasedType { get; private set; }

        /// <summary>이번 라운드 군중전 / 보스전 획득 재화</summary>
        public int CrowdEarned { get; private set; }
        public int BossEarned { get; private set; }
        public int RoundEarned => CrowdEarned + BossEarned;

        public int Balance => ServiceLocator.TryGet<ICurrencyWallet>(out var wallet) ? wallet.Balance : 0;

        /// <summary>지금 살 수 있는 강화가 하나라도 있는지</summary>
        public bool AnyPurchasable
        {
            get
            {
                foreach (var type in types)
                    if (GetOption(type).CanBuy) return true;
                return false;
            }
        }

        /// <summary>다음 라운드로 넘어갈 수 있는지. 하나를 샀거나, 살 수 있는 강화가 없을 때</summary>
        public bool CanFinish => IsOpen && (HasPurchased || AnyPurchasable == false);

        /// <summary>보스 등장 이후인지. 이후에 들어온 재화는 보스전 획득으로 센다</summary>
        private bool bossPhase;

        /// <summary>
        /// 이벤트 구독 (OnDisable 과 짝).
        /// 이미 다른 Manager 가 동작 중이면 경고만 남기고 구독하지 않는다.
        /// </summary>
        private void OnEnable()
        {
            if (active != null && active != this)
            {
                Debug.LogWarning($"[UpgradeManager] 이미 동작 중인 Manager({active.name})가 있어 '{name}' 은(는) 동작하지 않습니다. 씬에 하나만 두세요.", this);
                return;
            }
            active = this;

            EventBus<GameStateChangedEvent>.Subscribe(OnGameStateChanged);
            EventBus<BossDefeatedEvent>.Subscribe(OnBossDefeated);
            EventBus<BossAppearedEvent>.Subscribe(OnBossAppeared);
            EventBus<RoundStartedEvent>.Subscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
            EventBus<CurrencyChangedEvent>.Subscribe(OnCurrencyChanged);
        }

        /// <summary>이벤트 구독 해제 (구독하지 않았던 중복 Manager 는 할 일 없음)</summary>
        private void OnDisable()
        {
            if (active != this) return;
            active = null;

            EventBus<GameStateChangedEvent>.Unsubscribe(OnGameStateChanged);
            EventBus<BossDefeatedEvent>.Unsubscribe(OnBossDefeated);
            EventBus<BossAppearedEvent>.Unsubscribe(OnBossAppeared);
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<CurrencyChangedEvent>.Unsubscribe(OnCurrencyChanged);
        }

        /// <summary>강화 카드 한 장의 현재 상태. 데이터가 없는 종류는 MaxLevel 0 (= 최대 레벨 취급)</summary>
        public UpgradeOption GetOption(UpgradeType type)
        {
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades) == false ||
                DataManager.UpgradeTracks.TryGet(type, out var track) == false)
                return new UpgradeOption(type, 0, 0, null, null, 0f, 0f, false);

            int level = upgrades.GetLevel(type);
            var current = level > 0 && level <= track.MaxLevel ? track.Levels[level - 1] : null;
            var next = track.GetNext(level);
            float currentValue = track.GetTotalValue(level);
            float nextValue = next != null ? currentValue + next.Value : currentValue;
            bool canBuy = IsOpen && HasPurchased == false && next != null && Balance >= next.Cost;

            return new UpgradeOption(type, level, track.MaxLevel, current, next, currentValue, nextValue, canBuy);
        }

        /// <summary>
        /// 강화 하나 구매. 이번 단계에서 이미 샀거나, 최대 레벨·재화 부족이면 false.
        /// 재화 차감과 레벨 상승은 IUpgradeState 가 한 번에 처리한다.
        /// </summary>
        public bool TryPurchase(UpgradeType type)
        {
            if (IsOpen == false || HasPurchased) return false;
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades) == false ||
                ServiceLocator.TryGet<ICurrencyWallet>(out var wallet) == false) return false;

            var next = upgrades.GetNext(type);
            if (upgrades.TryPurchase(type, wallet) == false) return false;

            HasPurchased = true;
            PurchasedType = type;
            Debug.Log($"[UpgradeManager] 강화 구매: {next.Name} (비용 {next.Cost}) → {type} Lv{upgrades.GetLevel(type)}, 잔액 {wallet.Balance}", this);
            Changed?.Invoke();
            return true;
        }

        /// <summary>강화 선택 종료. 닫고 RewardSelectionFinishedEvent 발행 (다음 라운드 시작은 코어 담당)</summary>
        public void Finish()
        {
            if (CanFinish == false) return;

            Debug.Log($"[UpgradeManager] 강화 선택 종료 ({(HasPurchased ? $"{PurchasedType} 구매" : "건너뛰기")})", this);
            Close();
            EventBus<RewardSelectionFinishedEvent>.Publish(new RewardSelectionFinishedEvent());
        }

        /// <summary>Reward 상태가 되면 연다. 다른 상태로 바뀌면(게임 오버 등) 닫는다</summary>
        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            if (e.Current == GameState.Reward) Open();
            else Close();
        }

        /// <summary>[임시] 코어가 GameState.Reward 를 발행하기 전까지 보스 처치로도 연다. 코어 구현 후 이 함수와 구독을 지운다</summary>
        private void OnBossDefeated(BossDefeatedEvent e) => Open();

        /// <summary>보스 등장 이후의 획득 재화는 보스전으로 센다</summary>
        private void OnBossAppeared(BossAppearedEvent e) => bossPhase = true;

        /// <summary>새 라운드 시작 → 라운드 획득액 초기화</summary>
        private void OnRoundStarted(RoundStartedEvent e) => ResetRoundEarnings();

        /// <summary>라운드 정리 → 열려 있으면 닫는다. 처음부터 재시작이면 획득액도 초기화</summary>
        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            if (e.FullReset) ResetRoundEarnings();
            Close();
        }

        /// <summary>증가분은 라운드 획득액에 더하고, 열려 있으면 화면을 갱신한다 (구매 차감·초기화는 Delta 가 0 이하라 제외)</summary>
        private void OnCurrencyChanged(CurrencyChangedEvent e)
        {
            if (e.Delta > 0)
            {
                if (bossPhase) BossEarned += e.Delta;
                else CrowdEarned += e.Delta;
            }
            if (IsOpen) Changed?.Invoke();
        }

        /// <summary>강화 선택 단계 시작. 이미 열려 있으면 무시 (임시 경로와 정식 경로가 함께 와도 한 번만)</summary>
        private void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            HasPurchased = false;
            Debug.Log($"[UpgradeManager] 강화 선택 열림: 이번 라운드 +{RoundEarned} (군중전 {CrowdEarned}, 보스전 {BossEarned}), 보유 {Balance}", this);
            Changed?.Invoke();
        }

        private void Close()
        {
            if (IsOpen == false) return;
            IsOpen = false;
            Changed?.Invoke();
        }

        private void ResetRoundEarnings()
        {
            CrowdEarned = 0;
            BossEarned = 0;
            bossPhase = false;
        }
    }
}
