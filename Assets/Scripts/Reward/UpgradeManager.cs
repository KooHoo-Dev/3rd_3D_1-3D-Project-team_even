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
    /// 라운드 종료 후 강화 상점 담당 (BL-030, BL-031, BL-032). 기획: Docs/Design/UpgradeSystemExpansion_Proposal.md (v3)
    /// 보스를 처치하면 이번 라운드에 번 재화를 보여 주고, 무작위 카드(종류 + 등급) 중 재화가 허락하는 만큼 구매한 뒤 다음 라운드를 요청한다.
    ///
    ///   열기   : GameStateChangedEvent(Current = Reward) 수신 → 카드 OfferCount 장을 뽑는다.
    ///   구매   : TryPurchase(카드 번호) → IUpgradeState.TryPurchase (재화 차감·레벨 상승·UpgradePurchasedEvent 는 Ledger 담당). 산 카드는 SOLD
    ///   리롤   : TryReroll → ICurrencyWallet.TrySpend(고정 비용) 후 SOLD 가 아닌 카드만 다시 뽑는다
    ///   종료   : Finish → RewardSelectionFinishedEvent 발행 (다음 라운드 진행은 코어 담당)
    ///   획득액 : CurrencyChangedEvent 의 증가분(Delta > 0)을 합산. BossAppearedEvent 이전 = 군중전, 이후 = 보스전
    ///            RoundStartedEvent / RoundCleanupEvent(FullReset) 에서 0 으로
    ///
    /// 설계 포인트
    ///  - 카드 한 장 = 어떤 강화의 다음 1레벨 + 등급. 한 화면에 같은 종류는 한 장만 (산 카드 종류도 화면에 있는 동안 다시 나오지 않음).
    ///  - 종류는 UpgradeTable.Weight, 등급은 UpgradeGradeTable.Weight 로 뽑는다. 러키걸(LuckyGirl) 강화는 에픽 등급 가중치에 (1 + 값) 을 곱한다.
    ///  - 비용은 레벨로만 정해진다 (등급은 증가량만 다르다). 카드 수·리롤 비용은 ConfigTable 값만 쓴다.
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
            /// <summary>카드가 있는지. false(default) = 빈 칸 (살 수 있는 강화가 카드 수보다 적을 때)</summary>
            public readonly bool HasOffer;
            public bool IsEmpty => HasOffer == false;
            public readonly UpgradeType Type;
            public readonly UpgradeGrade Grade;
            /// <summary>등급 이름·색 (데이터가 없으면 null)</summary>
            public readonly UpgradeGradeDef GradeDef;
            public readonly int Level, MaxLevel;
            /// <summary>이 카드로 살 강화. SOLD 면 방금 산 레벨</summary>
            public readonly UpgradeDef Next;
            /// <summary>구매 전 누적 값 / 이 카드를 사면 누적될 값</summary>
            public readonly float CurrentValue, NextValue;
            /// <summary>이 카드 등급으로 추가되는 값</summary>
            public readonly float GradeValue;
            public readonly bool IsSold, CanBuy;

            public UpgradeOption(UpgradeType type, UpgradeGrade grade, UpgradeGradeDef gradeDef, int level, int maxLevel, UpgradeDef next,
                                 float currentValue, bool isSold, bool canBuy)
            {
                HasOffer = true;
                Type = type; Grade = grade; GradeDef = gradeDef; Level = level; MaxLevel = maxLevel; Next = next;
                GradeValue = next != null ? next.GetValue(grade) : 0f;
                CurrentValue = currentValue; NextValue = currentValue + GradeValue;
                IsSold = isSold; CanBuy = canBuy;
            }
        }

        /// <summary>상점 카드 한 칸 (런타임 상태)</summary>
        private struct Slot
        {
            public bool HasOffer;
            public UpgradeType Type;
            public UpgradeGrade Grade;
            public bool Sold;
            /// <summary>SOLD 카드 표시용: 산 레벨 (구매 직후 레벨)</summary>
            public int SoldLevel;
        }

        /// <summary>지금 동작 중인 Manager. 두 번째 Manager 가 켜져도 이중으로 열리지 않도록 하나만 기억한다</summary>
        private static UpgradeManager active;

        /// <summary>도메인 리로드를 꺼도 플레이 시작마다 비워지도록 (EventBus·ServiceLocator 와 같은 방식)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active = null;

        private static readonly UpgradeType[] allTypes = (UpgradeType[])Enum.GetValues(typeof(UpgradeType));
        private static readonly UpgradeGrade[] allGrades = (UpgradeGrade[])Enum.GetValues(typeof(UpgradeGrade));

        /// <summary>열림·구매·리롤·재화 변화 등 화면이 다시 그려야 할 때</summary>
        public event Action Changed;

        /// <summary>강화 종류 전체 (enum 순서). 보유 강화 목록 표시용</summary>
        public IReadOnlyList<UpgradeType> Types => allTypes;
        public bool IsOpen { get; private set; }
        /// <summary>이번 단계에서 구매한 횟수 (열릴 때 0)</summary>
        public int PurchaseCount { get; private set; }
        public bool HasPurchased => PurchaseCount > 0;
        /// <summary>이번 단계에서 리롤한 횟수 (열릴 때 0)</summary>
        public int RerollCount { get; private set; }

        /// <summary>상점 카드 수 (ConfigTable.UpgradeOfferCount)</summary>
        public int OfferCount => Mathf.Max(1, DataManager.Config.GetInt(GameConfig.UpgradeOfferCount, 3));
        /// <summary>리롤 비용 (ConfigTable.UpgradeRerollCost, 고정)</summary>
        public int RerollCost => Mathf.Max(0, DataManager.Config.GetInt(GameConfig.UpgradeRerollCost, 5));

        /// <summary>이번 라운드 군중전 / 보스전 획득 재화</summary>
        public int CrowdEarned { get; private set; }
        public int BossEarned { get; private set; }
        public int RoundEarned => CrowdEarned + BossEarned;

        public int Balance => ServiceLocator.TryGet<ICurrencyWallet>(out var wallet) ? wallet.Balance : 0;

        /// <summary>지금 살 수 있는 카드가 하나라도 있는지</summary>
        public bool AnyPurchasable
        {
            get
            {
                for (int i = 0; i < slots.Count; i++)
                    if (GetOption(i).CanBuy) return true;
                return false;
            }
        }

        /// <summary>리롤할 수 있는지: 열려 있고, SOLD 아닌 칸과 뽑을 후보가 있고, 재화가 충분할 때</summary>
        public bool CanReroll
        {
            get
            {
                if (IsOpen == false || Balance < RerollCost) return false;
                bool anyOpenSlot = false;
                foreach (var slot in slots) if (slot.Sold == false) { anyOpenSlot = true; break; }
                return anyOpenSlot && BuildCandidates(SoldTypes()).Count > 0;
            }
        }

        /// <summary>다음 라운드로 넘어갈 수 있는지. 열려 있으면 언제든 (RequirePurchaseToFinish 면 1장 이상 산 뒤)</summary>
        public bool CanFinish => IsOpen && (RequirePurchaseToFinish == false || HasPurchased);

        /// <summary>
        /// 튜토리얼(강화 체험)용: 1장 이상 사야 닫을 수 있다. 열려 있는 동안만 유효하고 닫힐 때 풀린다
        /// </summary>
        public bool RequirePurchaseToFinish { get; set; }

        /// <summary>
        /// 튜토리얼(강화 체험)용: 다음 리롤 1회에서 다시 뽑는 카드의 등급을 grade 로 고정한다 (종류는 그대로 무작위).
        /// 리롤에 쓰이거나 닫히면 풀린다. null = 해제. 본편 확률(UpgradeGradeTable·러키걸)에는 영향 없음
        /// </summary>
        public void ForceNextRerollGrade(UpgradeGrade? grade)
        {
            forcedRerollGrade = grade;
            Changed?.Invoke();
        }

        /// <summary>보스 등장 이후인지. 이후에 들어온 재화는 보스전 획득으로 센다</summary>
        private bool bossPhase;
        /// <summary>다음 리롤에서 쓸 고정 등급 (ForceNextRerollGrade)</summary>
        private UpgradeGrade? forcedRerollGrade;

        private readonly List<Slot> slots = new List<Slot>();

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
            EventBus<BossAppearedEvent>.Unsubscribe(OnBossAppeared);
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            EventBus<CurrencyChangedEvent>.Unsubscribe(OnCurrencyChanged);
        }

        /// <summary>index 번 카드의 현재 상태. 범위 밖이거나 빈 칸이면 IsEmpty</summary>
        public UpgradeOption GetOption(int index)
        {
            if (index < 0 || index >= slots.Count || slots[index].HasOffer == false ||
                ServiceLocator.TryGet<IUpgradeState>(out var upgrades) == false ||
                DataManager.UpgradeTracks.TryGet(slots[index].Type, out var track) == false)
                return default;

            var slot = slots[index];
            DataManager.UpgradeGrades.TryGet(slot.Grade, out var gradeDef);
            if (slot.Sold)
            {
                // 산 카드: 산 레벨과 그 직전 누적 값을 그대로 보여 준다
                int boughtLevel = slot.SoldLevel;
                var bought = boughtLevel >= 1 && boughtLevel <= track.MaxLevel ? track.Levels[boughtLevel - 1] : null;
                float before = upgrades.GetTotalValue(slot.Type) - (bought != null ? bought.GetValue(slot.Grade) : 0f);
                return new UpgradeOption(slot.Type, slot.Grade, gradeDef, boughtLevel - 1, track.MaxLevel, bought, before, true, false);
            }

            int level = upgrades.GetLevel(slot.Type);
            var next = track.GetNext(level);
            bool canBuy = IsOpen && next != null && Balance >= next.Cost;
            return new UpgradeOption(slot.Type, slot.Grade, gradeDef, level, track.MaxLevel, next, upgrades.GetTotalValue(slot.Type), false, canBuy);
        }

        /// <summary>type 의 현재 레벨 (보유 강화 표시용)</summary>
        public int GetLevel(UpgradeType type) => ServiceLocator.TryGet<IUpgradeState>(out var upgrades) ? upgrades.GetLevel(type) : 0;

        /// <summary>type 의 level 레벨을 산 등급 (보유 강화 표시용, 안 샀으면 null)</summary>
        public UpgradeGrade? GetGrade(UpgradeType type, int level)
            => ServiceLocator.TryGet<IUpgradeState>(out var upgrades) ? upgrades.GetGrade(type, level) : null;

        /// <summary>
        /// index 번 카드 구매. 닫혀 있거나 빈 칸·SOLD·최대 레벨·재화 부족이면 false. 재화가 남아 있으면 다른 카드를 계속 살 수 있다.
        /// 재화 차감과 레벨 상승은 IUpgradeState 가 한 번에 처리한다.
        /// </summary>
        public bool TryPurchase(int index)
        {
            if (IsOpen == false || index < 0 || index >= slots.Count) return false;
            var slot = slots[index];
            if (slot.HasOffer == false || slot.Sold) return false;
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades) == false ||
                ServiceLocator.TryGet<ICurrencyWallet>(out var wallet) == false) return false;

            var next = upgrades.GetNext(slot.Type);
            if (upgrades.TryPurchase(slot.Type, slot.Grade, wallet) == false) return false;

            slot.Sold = true;
            slot.SoldLevel = upgrades.GetLevel(slot.Type);
            slots[index] = slot;
            PurchaseCount++;
            Debug.Log($"[UpgradeManager] 강화 구매: {next.Name} {slot.Grade} (비용 {next.Cost}) → {slot.Type} Lv{slot.SoldLevel}, 잔액 {wallet.Balance}", this);
            Changed?.Invoke();
            return true;
        }

        /// <summary>리롤: 고정 비용을 내고 SOLD 가 아닌 카드만 종류·등급을 다시 뽑는다. 할 수 없으면 false</summary>
        public bool TryReroll()
        {
            if (CanReroll == false || ServiceLocator.TryGet<ICurrencyWallet>(out var wallet) == false) return false;
            if (wallet.TrySpend(RerollCost) == false) return false;

            RerollCount++;
            RollSlots(keepSold: true, forcedRerollGrade);
            forcedRerollGrade = null;
            Debug.Log($"[UpgradeManager] 리롤 {RerollCount}회 (비용 {RerollCost}) → {DescribeSlots()}, 잔액 {wallet.Balance}", this);
            Changed?.Invoke();
            return true;
        }

        /// <summary>강화 선택 종료. 닫고 RewardSelectionFinishedEvent 발행 (다음 라운드 시작은 코어 담당)</summary>
        public void Finish()
        {
            if (CanFinish == false) return;

            Debug.Log($"[UpgradeManager] 강화 선택 종료 ({(HasPurchased ? $"{PurchaseCount}회 구매" : "건너뛰기")}, 리롤 {RerollCount}회)", this);
            Close();
            EventBus<RewardSelectionFinishedEvent>.Publish(new RewardSelectionFinishedEvent());
        }

        /// <summary>Reward 상태가 되면 연다. 다른 상태로 바뀌면(게임 오버 등) 닫는다</summary>
        private void OnGameStateChanged(GameStateChangedEvent e)
        {
            if (e.Current == GameState.Reward) Open();
            else Close();
        }

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

        /// <summary>증가분은 라운드 획득액에 더하고, 열려 있으면 화면을 갱신한다 (구매·리롤 차감·초기화는 Delta 가 0 이하라 제외)</summary>
        private void OnCurrencyChanged(CurrencyChangedEvent e)
        {
            if (e.Delta > 0)
            {
                if (bossPhase) BossEarned += e.Delta;
                else CrowdEarned += e.Delta;
            }
            if (IsOpen) Changed?.Invoke();
        }

        /// <summary>강화 선택 단계 시작. 카드를 새로 뽑는다. 이미 열려 있으면 무시 (임시 경로와 정식 경로가 함께 와도 한 번만)</summary>
        private void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            PurchaseCount = 0;
            RerollCount = 0;
            slots.Clear();
            for (int i = 0; i < OfferCount; i++) slots.Add(default);
            RollSlots(keepSold: false);
            Debug.Log($"[UpgradeManager] 강화 선택 열림: 이번 라운드 +{RoundEarned} (군중전 {CrowdEarned}, 보스전 {BossEarned}), 보유 {Balance}, 카드 {DescribeSlots()}", this);
            Changed?.Invoke();
        }

        private void Close()
        {
            if (IsOpen == false) return;
            IsOpen = false;
            RequirePurchaseToFinish = false;
            forcedRerollGrade = null;
            Changed?.Invoke();
        }

        private void ResetRoundEarnings()
        {
            CrowdEarned = 0;
            BossEarned = 0;
            bossPhase = false;
        }

        // ── 뽑기 ──

        /// <summary>SOLD 가 아닌 칸을 다시 뽑는다 (keepSold = false 면 모든 칸). 후보가 모자라면 빈 칸. forcedGrade 가 있으면 등급은 그것으로</summary>
        private void RollSlots(bool keepSold, UpgradeGrade? forcedGrade = null)
        {
            var used = keepSold ? SoldTypes() : new HashSet<UpgradeType>();
            var candidates = BuildCandidates(used);
            for (int i = 0; i < slots.Count; i++)
            {
                if (keepSold && slots[i].Sold) continue;
                if (candidates.Count == 0)
                {
                    slots[i] = default;
                    continue;
                }
                int pick = PickWeighted(candidates);
                var track = candidates[pick];
                candidates.RemoveAt(pick); // 한 화면에 같은 종류는 한 장만
                slots[i] = new Slot { HasOffer = true, Type = track.UpgradeType, Grade = forcedGrade ?? RollGrade() };
            }
        }

        /// <summary>지금 화면에 SOLD 로 남아 있는 강화 종류 (리롤 때 다시 나오지 않게)</summary>
        private HashSet<UpgradeType> SoldTypes()
        {
            var sold = new HashSet<UpgradeType>();
            foreach (var slot in slots) if (slot.HasOffer && slot.Sold) sold.Add(slot.Type);
            return sold;
        }

        /// <summary>카드로 나올 수 있는 강화: 데이터가 있고, Weight > 0, 최대 레벨이 아니고, exclude 에 없는 것</summary>
        private static List<UpgradeTrackDef> BuildCandidates(HashSet<UpgradeType> exclude)
        {
            var list = new List<UpgradeTrackDef>();
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades) == false) return list;
            foreach (var type in allTypes)
            {
                if (exclude.Contains(type) || DataManager.UpgradeTracks.TryGet(type, out var track) == false) continue;
                if (track.Weight > 0 && track.GetNext(upgrades.GetLevel(type)) != null) list.Add(track);
            }
            return list;
        }

        private static int PickWeighted(List<UpgradeTrackDef> candidates)
        {
            int total = 0;
            foreach (var c in candidates) total += c.Weight;
            int roll = UnityEngine.Random.Range(0, total);
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= candidates[i].Weight;
                if (roll < 0) return i;
            }
            return candidates.Count - 1;
        }

        /// <summary>등급 뽑기. 러키걸 강화 값만큼 에픽 등급의 가중치를 키운다</summary>
        private static UpgradeGrade RollGrade()
        {
            float luck = ServiceLocator.TryGet<IUpgradeState>(out var upgrades) ? upgrades.GetTotalValue(UpgradeType.LuckyGirl) : 0f;
            float total = 0f;
            foreach (var grade in allGrades) total += GradeWeight(grade, luck);
            if (total <= 0f) return UpgradeGrade.Common;

            float roll = UnityEngine.Random.value * total;
            foreach (var grade in allGrades)
            {
                roll -= GradeWeight(grade, luck);
                if (roll < 0f) return grade;
            }
            return allGrades[allGrades.Length - 1];
        }

        private static float GradeWeight(UpgradeGrade grade, float luck)
        {
            if (DataManager.UpgradeGrades.TryGet(grade, out var def) == false) return 0f;
            return grade == UpgradeGrade.Epic ? def.Weight * (1f + luck) : def.Weight;
        }

        /// <summary>로그용 "ReloadSpeed(Epic), MaxHealth(Common) SOLD, -"</summary>
        private string DescribeSlots()
        {
            var parts = new List<string>(slots.Count);
            foreach (var slot in slots)
                parts.Add(slot.HasOffer ? $"{slot.Type}({slot.Grade}){(slot.Sold ? " SOLD" : "")}" : "-");
            return string.Join(", ", parts);
        }
    }
}
