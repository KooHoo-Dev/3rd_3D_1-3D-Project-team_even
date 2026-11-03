using System.Collections.Generic;
using System.Linq;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;

namespace HybridShooter.Core.Services
{
    /// <summary>
    /// 무기 해금 기본 구현. CoreBootstrap 이 IWeaponUnlocks 로 등록하고 BindEvents 로 이벤트를 받게 한다.
    ///  - WeaponTable.UnlockOrder = 0 (권총·돌격소총·라이플·기관단총) 과 Supply 무기는 항상 해금
    ///  - StageClearedEvent(Round) 마다 UnlockOrder 가 그 라운드 번호 이하인 잠긴 무기를 해금 → WeaponUnlockedEvent
    ///    (UnlockOrder = 해금 라운드. 보스 유무와 관계없이 라운드 클리어 = Reward 진입)
    ///  - 해금은 메모리에만 둔다 → 앱(에디터는 Play) 실행 동안만 유지, 메인 메뉴를 거쳐 다시 시작해도 남고 껐다 켜면 사라진다
    ///    (CoreBootstrap 이 실행마다 새 Ledger 를 만든다. 저장하지 않는다)
    /// 해금한 무기는 다음 출발 때 무기 선택 화면(LoadoutPanel)에서 고를 수 있다.
    /// 해금은 RunDirector 의 StageClearedEvent 로만 일어난다 (테스터가 직접 발행하는 BossDefeatedEvent 로는 해금되지 않음)
    /// </summary>
    public sealed class WeaponUnlockLedger : IWeaponUnlocks
    {
        private readonly HashSet<int> unlocked = new HashSet<int>();

        /// <summary>StageClearedEvent 를 받아 해금한다. 플레이 시작마다 EventBus 가 비워지므로 CoreBootstrap 이 다시 부른다</summary>
        public void BindEvents()
        {
            EventBus<StageClearedEvent>.Unsubscribe(OnStageCleared);
            EventBus<StageClearedEvent>.Subscribe(OnStageCleared);
        }

        public void UnbindEvents() => EventBus<StageClearedEvent>.Unsubscribe(OnStageCleared);

        public bool IsUnlocked(int weaponId)
        {
            if (DataManager.Weapons.TryGet(weaponId, out var def) && IsAlwaysUnlocked(def)) return true;
            return unlocked.Contains(weaponId);
        }

        /// <summary>처음부터 해금된 무기 (UnlockOrder 0 또는 보급 전용)</summary>
        public static bool IsAlwaysUnlocked(WeaponDef def) =>
            def.UnlockOrder <= 0 || def.Category == WeaponCategory.Supply;

        /// <summary>다음에 해금될 무기 (UnlockOrder 가 가장 작은 잠긴 무기, 같으면 Id 순). 모두 해금됐으면 null</summary>
        public WeaponDef PeekNext() =>
            DataManager.Weapons
                .Where(w => IsUnlocked(w.Id) == false)
                .OrderBy(w => w.UnlockOrder).ThenBy(w => w.Id)
                .FirstOrDefault();

        /// <summary>다음 무기 하나를 해금한다. 해금한 무기 (모두 해금됐으면 null)</summary>
        public WeaponDef UnlockNext()
        {
            var next = PeekNext();
            if (next == null) return null;

            unlocked.Add(next.Id);
            EventBus<WeaponUnlockedEvent>.Publish(new WeaponUnlockedEvent(next));
            return next;
        }

        /// <summary>해금 기록을 지운다 (기본 무기만 남음). 테스트·디버그용</summary>
        public void ResetAll() => unlocked.Clear();

        public void RestoreUnlocked(IReadOnlyCollection<int> weaponIds)
        {
            unlocked.Clear();
            foreach (int id in weaponIds) unlocked.Add(id);
        }

        /// <summary>
        /// 라운드를 클리어하면 UnlockOrder 가 그 라운드 번호 이하인 잠긴 무기를 모두 해금한다.
        /// 앞 라운드 해금을 놓친 경우(라운드 점프 테스터 등)도 함께 채운다. StageClearedEvent 가 부른다.
        /// </summary>
        /// <param name="clearedRound">방금 클리어한 진행 라운드 번호 (1부터)</param>
        /// <returns>이번에 해금한 무기 목록 (UnlockOrder·Id 순). 없으면 빈 목록</returns>
        public List<WeaponDef> UnlockForRound(int clearedRound)
        {
            var newlyUnlocked = DataManager.Weapons
                .Where(w => IsUnlocked(w.Id) == false && w.UnlockOrder <= clearedRound)
                .OrderBy(w => w.UnlockOrder).ThenBy(w => w.Id)
                .ToList();
            if (newlyUnlocked.Count == 0) return newlyUnlocked;

            foreach (var weapon in newlyUnlocked) unlocked.Add(weapon.Id);
            foreach (var weapon in newlyUnlocked) EventBus<WeaponUnlockedEvent>.Publish(new WeaponUnlockedEvent(weapon));
            return newlyUnlocked;
        }

        /// <summary>지금 ServiceLocator 가 돌려주는 구현만 해금한다 (테스트가 따로 Ledger 를 등록하면 실제 진행은 건드리지 않음)</summary>
        private void OnStageCleared(StageClearedEvent e)
        {
            if (ServiceLocator.TryGet<IWeaponUnlocks>(out var active) && ReferenceEquals(active, this) == false) return;
            UnlockForRound(e.Round);
        }
    }
}
