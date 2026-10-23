using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Reward.Tester
{
    /// <summary>
    /// 처치 보상(KillRewardCollector) 인게임 확인용 (EnemyKillRewardSystem 씬).
    /// 몬스터·보스·라운드 구현이 없어도, 그쪽이 발행할 이벤트를 키 입력으로 대신 발행해 재화 변화를 확인한다.
    ///
    ///   1 : 일반체 처치          (MonsterDiedEvent, weakMonsterId)
    ///   2 : 중간체 처치          (MonsterDiedEvent, midMonsterId)
    ///   3 : 보상 제외 처치       (MonsterDiedEvent, GrantReward = false)       → 재화 변화 없어야 함
    ///   4 : 보스 MonsterDied     (MonsterDiedEvent, bossMonsterId)              → 재화 변화 없어야 함 (이중 지급 방지)
    ///   5 : 보스 처치            (BossDefeatedEvent)                            → 보스 Reward + 현재 라운드 BossReward
    ///   R : 다음 라운드 시작     (RoundStartedEvent, 라운드 번호 +1)           → [임시] 라운드 공급원 대신
    ///   N : 다음 라운드 진행 정리 (RoundCleanupEvent, FullReset = false)       → 재화 유지
    ///   F : 처음부터 재시작 정리 (RoundCleanupEvent, FullReset = true)        → 재화 0, 강화 레벨 0, 라운드 정보 초기화
    ///   U : 강화 구매            (IUpgradeState.TryPurchase, testUpgradeType)  → 재화 차감, 강화 레벨 +1 (F 초기화 확인용)
    ///
    /// 화면 좌상단에 현재 재화·마지막 변화량·현재 라운드·키 안내를 표시하고, 재화가 바뀔 때마다 콘솔에 로그를 남긴다.
    /// </summary>
    public sealed class KillRewardTester : MonoBehaviour
    {
        [Tooltip("1 키로 처치할 일반체 Id (MonsterTable)")]
        [SerializeField] private int weakMonsterId = 1001;
        [Tooltip("2 키로 처치할 중간체 Id (MonsterTable)")]
        [SerializeField] private int midMonsterId = 2001;
        [Tooltip("4·5 키에 쓸 보스 Id (MonsterTable)")]
        [SerializeField] private int bossMonsterId = 3001;
        [Tooltip("U 키로 구매할 강화 종류 (UpgradeTable)")]
        [SerializeField] private UpgradeType testUpgradeType = UpgradeType.ReloadSpeed;
        [Tooltip("화면에 상태 표시")]
        [SerializeField] private bool showStatus = true;

        /// <summary>R 키로 마지막에 시작한 라운드 번호. 0 = 아직 시작 안 함</summary>
        private int testRound;
        /// <summary>마지막 CurrencyChangedEvent 의 변화량 (화면 표시용)</summary>
        private int lastDelta;
        private GUIStyle statusStyle;

        /// <summary>재화 변경 로그를 위해 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            EventBus<CurrencyChangedEvent>.Subscribe(OnCurrencyChanged);
        }

        /// <summary>재화 변경 구독 해제</summary>
        private void OnDisable()
        {
            EventBus<CurrencyChangedEvent>.Unsubscribe(OnCurrencyChanged);
        }

        /// <summary>키 입력을 읽어 해당 테스트 이벤트를 발행</summary>
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (Pressed(keyboard.digit1Key, keyboard.numpad1Key)) PublishMonsterDied(weakMonsterId, true);
            if (Pressed(keyboard.digit2Key, keyboard.numpad2Key)) PublishMonsterDied(midMonsterId, true);
            if (Pressed(keyboard.digit3Key, keyboard.numpad3Key)) PublishMonsterDied(weakMonsterId, false);
            if (Pressed(keyboard.digit4Key, keyboard.numpad4Key)) PublishMonsterDied(bossMonsterId, true);
            if (Pressed(keyboard.digit5Key, keyboard.numpad5Key)) PublishBossDefeated();
            if (keyboard.rKey.wasPressedThisFrame) PublishNextRoundStarted();
            if (keyboard.nKey.wasPressedThisFrame) PublishCleanup(false);
            if (keyboard.fKey.wasPressedThisFrame) PublishCleanup(true);
            if (keyboard.uKey.wasPressedThisFrame) PurchaseUpgrade();
        }

        /// <summary>현재 재화·라운드·키 안내를 화면 좌상단에 표시</summary>
        private void OnGUI()
        {
            if (showStatus == false) return;
            statusStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 20 };

            int balance = ServiceLocator.TryGet<ICurrencyWallet>(out var wallet) ? wallet.Balance : 0;
            string roundText = testRound > 0 ? $"{testRound} 라운드 (BossReward {DataManager.GetRound(testRound)?.BossReward ?? 0})" : "라운드 시작 전 (R 키)";
            string upgradeText = BuildUpgradeText();
            string text =
                $"재화 {balance}  (마지막 변화 {lastDelta:+0;-0;0})\n" +
                $"{roundText}\n" +
                $"강화 레벨  {upgradeText}\n" +
                "1 일반체  2 중간체  3 보상제외  4 보스MonsterDied  5 보스처치\n" +
                $"R 다음 라운드 시작  N 정리(유지)  F 정리(초기화)  U 강화 구매({testUpgradeType})";
            GUI.Label(new Rect(20f, 20f, 1000f, 150f), text, statusStyle);
        }

        /// <summary>숫자 키 또는 키패드 숫자 키가 이번 프레임에 눌렸는지</summary>
        private static bool Pressed(UnityEngine.InputSystem.Controls.KeyControl key, UnityEngine.InputSystem.Controls.KeyControl numpad)
            => key.wasPressedThisFrame || numpad.wasPressedThisFrame;

        /// <summary>몬스터 사망 이벤트 발행. grantReward = false 면 보상 제외 처치</summary>
        private void PublishMonsterDied(int monsterId, bool grantReward)
        {
            if (TryGetMonster(monsterId, out var def) == false) return;

            Debug.Log($"[KillRewardTester] MonsterDiedEvent 발행: {def.Name}({def.Id}), Reward {def.Reward}, GrantReward {grantReward}, IsBoss {def.IsBoss}", this);
            EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(def, transform.position, null, grantReward));
        }

        /// <summary>보스 처치 이벤트 발행</summary>
        private void PublishBossDefeated()
        {
            if (TryGetMonster(bossMonsterId, out var boss) == false) return;

            Debug.Log($"[KillRewardTester] BossDefeatedEvent 발행: {boss.Name}({boss.Id}), 보스 Reward {boss.Reward}, 현재 테스트 라운드 {testRound}", this);
            EventBus<BossDefeatedEvent>.Publish(new BossDefeatedEvent(boss, transform.position));
        }

        /// <summary>[임시] 라운드 공급원 대신 다음 라운드 시작 이벤트 발행</summary>
        private void PublishNextRoundStarted()
        {
            testRound++;
            var def = DataManager.GetRound(testRound);
            Debug.Log($"[KillRewardTester] RoundStartedEvent 발행: {testRound} 라운드, BossReward {def?.BossReward ?? 0}", this);
            EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(testRound, def));
        }

        /// <summary>라운드 정리 이벤트 발행. fullReset = true 면 테스트 라운드 번호도 처음으로</summary>
        private void PublishCleanup(bool fullReset)
        {
            if (fullReset) testRound = 0;
            Debug.Log($"[KillRewardTester] RoundCleanupEvent 발행: FullReset {fullReset}", this);
            EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(fullReset));
        }

        /// <summary>testUpgradeType 의 다음 레벨 강화를 구매. 재화 부족·최대 레벨이면 실패 로그</summary>
        private void PurchaseUpgrade()
        {
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades) == false ||
                ServiceLocator.TryGet<ICurrencyWallet>(out var wallet) == false) return;

            var next = upgrades.GetNext(testUpgradeType);
            if (upgrades.TryPurchase(testUpgradeType, wallet))
                Debug.Log($"[KillRewardTester] 강화 구매 성공: {next.Name} (비용 {next.Cost}) → {testUpgradeType} 레벨 {upgrades.GetLevel(testUpgradeType)}", this);
            else
                Debug.Log($"[KillRewardTester] 강화 구매 실패: {(next == null ? "최대 레벨" : $"재화 부족 (필요 {next.Cost}, 보유 {wallet.Balance})")}", this);
        }

        /// <summary>모든 강화 종류의 현재 레벨을 "ReloadSpeed 1  MagazineSize 0 ..." 형태로 만든다 (화면 표시용)</summary>
        private static string BuildUpgradeText()
        {
            if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades) == false) return "-";

            var sb = new System.Text.StringBuilder();
            foreach (UpgradeType type in System.Enum.GetValues(typeof(UpgradeType)))
                sb.Append(type).Append(' ').Append(upgrades.GetLevel(type)).Append("  ");
            return sb.ToString();
        }

        /// <summary>재화가 바뀔 때마다 콘솔에 잔액·변화량 출력</summary>
        private void OnCurrencyChanged(CurrencyChangedEvent e)
        {
            lastDelta = e.Delta;
            Debug.Log($"[KillRewardTester] 재화 변경: {e.Delta:+0;-0;0} → 잔액 {e.Balance}", this);
        }

        /// <summary>MonsterTable 에서 Id 로 몬스터 정의를 찾는다. 없으면 에러 로그</summary>
        private bool TryGetMonster(int id, out MonsterDef def)
        {
            if (DataManager.Monsters.TryGet(id, out def)) return true;
            Debug.LogError($"[KillRewardTester] MonsterTable 에 Id {id} 가 없습니다.", this);
            return false;
        }
    }
}
