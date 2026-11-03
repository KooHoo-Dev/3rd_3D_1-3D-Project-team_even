using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Reward
{
    /// <summary>
    /// 처치 보상 자동 획득 담당 (BL-029).
    /// 몬스터·보스가 죽으면 그 자리에서 바로 지갑(ICurrencyWallet)에 재화를 더한다. → 전장에서 줍는 단계가 없다.
    ///
    ///   일반 몬스터 : MonsterDiedEvent 수신 → MonsterDef.Reward 지급         (MonsterTable.tsv 의 Reward 열)
    ///   보스        : BossDefeatedEvent 수신 → 보스 Reward + RoundDef.BossReward 지급 (RoundTable.tsv 의 BossReward 열)
    ///   게임 씬 시작   : Awake → 재화 0, 강화 초기화 (지갑·강화는 앱 실행 내내 살아 있어 메인 메뉴를 거쳐 다시 들어와도 남기 때문)
    ///   처음부터 재시작 : RoundCleanupEvent(FullReset = true) 수신 → 재화를 0 으로, 구매한 강화를 전부 초기화 (BL-033)
    ///   다음 라운드 진행 : RoundCleanupEvent(FullReset = false) → 재화·강화 유지 (강화 선택 화면에서 사용)
    ///
    /// 설계 포인트
    ///  - 처치 수를 세지 않는다. 처치 할당량은 군중전 종료·보스 등장 조건이 아니므로 이 컴포넌트는 진행 신호를 발행하지 않는다.
    ///  - 보상 수치는 기존 테이블 값만 사용한다. (Inspector 에 같은 의미의 값을 두지 않음)
    ///  - 재화 변경 UI 갱신은 지갑이 발행하는 CurrencyChangedEvent 를 구독하면 된다. (여기서 따로 알리지 않음)
    ///  - 씬에 하나만 동작한다. 두 개 이상 켜지면 먼저 켜진 것만 보상을 지급하고, 나머지는 경고 후 아무것도 하지 않는다. (이중 지급 방지)
    ///    먼저 켜진 것이 꺼지면 남은 것이 자동으로 이어받지 않으므로, 씬에는 하나만 두는 것이 원칙이다.
    ///
    /// 다른 파트에 기대하는 것 (발행하는 쪽 책임)
    ///  - 보상 없이 사라지는 몬스터(라운드 정리, 풀 회수, 보스 등장 공격으로 정리)는 GrantReward = false 로 발행하거나 발행하지 않는다.
    ///  - 같은 몬스터의 사망을 두 번 발행하지 않는다.
    ///  - 보스는 반드시 BossDefeatedEvent 를 발행한다. (MonsterDiedEvent 를 함께 발행해도 보스 분은 여기서 무시하므로 중복 지급 없음)
    /// </summary>
    public sealed class KillRewardCollector : MonoBehaviour
    {
        /// <summary>FullReset(처음부터 재시작) 시 되돌릴 재화 값</summary>
        private const int StartBalance = 0;

        /// <summary>
        /// 지금 진행 중인 라운드 정의. 보스 추가 보상(BossReward)을 읽는 데만 쓴다.
        /// [임시] RoundStartedEvent 가 라운드 시작 때 발행된다고 가정한다.
        ///        라운드 담당 구현에 따라 이벤트·필드 이름이 바뀌면 OnRoundStarted 와 GetBossRoundReward 만 고치면 된다.
        /// </summary>
        private RoundDef currentRound;

        /// <summary>지금 보상을 지급하고 있는 Collector. 두 번째 Collector 가 켜져도 이중 지급하지 않도록 하나만 기억한다</summary>
        private static KillRewardCollector active;

        /// <summary>도메인 리로드를 꺼도 플레이 시작마다 비워지도록 (EventBus·ServiceLocator 와 같은 방식)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => active = null;

        /// <summary>
        /// 게임 씬이 열릴 때 이전 판의 재화·강화를 버린다. 지갑·강화(CoreBootstrap 기본 구현)는 앱 실행 동안 하나라
        /// 메인 메뉴 → 게임 시작으로 씬을 다시 열어도 값이 남는다. 무기·수류탄이 Start 에서 강화 값을 읽기 전에 비우려고 Awake 에서 한다.
        /// </summary>
        private void Awake() => ResetRunProgress();

        /// <summary>
        /// 이벤트 구독 (OnDisable 과 짝).
        /// 이미 다른 Collector 가 동작 중이면 경고만 남기고 구독하지 않는다.
        /// </summary>
        private void OnEnable()
        {
            if (active != null && active != this)
            {
                Debug.LogWarning($"[KillRewardCollector] 이미 동작 중인 Collector({active.name})가 있어 '{name}' 은(는) 보상을 지급하지 않습니다. 씬에 하나만 두세요.", this);
                return;
            }
            active = this;

            EventBus<MonsterDiedEvent>.Subscribe(OnMonsterDied);
            EventBus<BossDefeatedEvent>.Subscribe(OnBossDefeated);
            EventBus<RoundStartedEvent>.Subscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
        }

        /// <summary>이벤트 구독 해제. 꺼진 오브젝트가 보상을 지급하지 않도록 (구독하지 않았던 중복 Collector 는 할 일 없음)</summary>
        private void OnDisable()
        {
            if (active != this) return;
            active = null;

            EventBus<MonsterDiedEvent>.Unsubscribe(OnMonsterDied);
            EventBus<BossDefeatedEvent>.Unsubscribe(OnBossDefeated);
            EventBus<RoundStartedEvent>.Unsubscribe(OnRoundStarted);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
        }

        /// <summary>
        /// 일반 몬스터 처치 → 해당 몬스터의 Reward 만큼 재화 지급.
        /// 보상 제외(GrantReward = false)·보스는 건너뛴다. 보스는 OnBossDefeated 에서 한 번에 지급한다.
        /// </summary>
        private void OnMonsterDied(MonsterDiedEvent e)
        {
            if (e.GrantReward == false || e.Def == null) return;
            if (e.Def.IsBoss) return;

            Grant(e.Def.Reward);
        }

        /// <summary>
        /// 보스 처치 → 보스 자체 보상(MonsterDef.Reward) + 이번 라운드 추가 보상(RoundDef.BossReward)을 합쳐 지급.
        /// </summary>
        private void OnBossDefeated(BossDefeatedEvent e)
        {
            int bossReward = e.Boss != null ? e.Boss.Reward : 0;
            Grant(bossReward + GetBossRoundReward());
        }

        /// <summary>[임시] 라운드 시작 시 현재 라운드 정의를 기억해 둔다</summary>
        private void OnRoundStarted(RoundStartedEvent e)
        {
            currentRound = e.Def;
        }

        /// <summary>
        /// 라운드 정리.
        /// FullReset = true(처음부터 재시작)면 재화를 StartBalance 로 되돌린다.
        /// FullReset = false(다음 라운드)면 재화를 그대로 둔다. → 처치가 많을수록 다음 강화에 쓸 재화가 늘어난다.
        /// </summary>
        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            if (e.FullReset == false) return;

            currentRound = null;
            ResetRunProgress();
        }

        /// <summary>재화를 StartBalance 로, 구매한 강화를 전부 처음으로 되돌린다. 씬 시작(Awake)과 처음부터 재시작(FullReset)이 부른다</summary>
        private static void ResetRunProgress()
        {
            if (ServiceLocator.TryGet<ICurrencyWallet>(out var wallet)) wallet.ResetTo(StartBalance);
            // 강화 레벨도 처음으로. 무기·수류탄은 GetTotalValue 로 능력치를 다시 계산한다
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades)) upgrades.ResetAll();
        }

        /// <summary>
        /// [임시] 이번 라운드의 보스 추가 보상. 라운드 정보를 아직 못 받았으면 경고를 남기고 0.
        /// 라운드 공급 방식이 확정되면 이 함수 안만 바꾼다.
        /// </summary>
        private int GetBossRoundReward()
        {
            if (currentRound == null)
            {
                Debug.LogWarning("[KillRewardCollector] 현재 라운드 정보가 없어 보스 추가 보상(BossReward)을 생략합니다. (RoundStartedEvent 발행 확인)", this);
                return 0;
            }
            return currentRound.BossReward;
        }

        /// <summary>지갑에 재화를 더한다. 0 이하는 무시 (지갑이 CurrencyChangedEvent 를 발행해 UI 가 갱신됨)</summary>
        private void Grant(int amount)
        {
            if (amount <= 0) return;
            if (ServiceLocator.TryGet<ICurrencyWallet>(out var wallet)) wallet.Add(amount);
        }
    }
}
