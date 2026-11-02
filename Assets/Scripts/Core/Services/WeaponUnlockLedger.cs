using System;
using System.Collections.Generic;
using System.Linq;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Core.Services
{
    /// <summary>
    /// 무기 영구 해금 기본 구현. CoreBootstrap 이 IWeaponUnlocks 로 등록하고 BindEvents 로 이벤트를 받게 한다.
    ///  - WeaponTable.UnlockOrder = 0 (권총·돌격소총·라이플·기관단총) 과 Supply 무기는 항상 해금
    ///  - StageClearedEvent(Round) 마다 UnlockOrder 가 그 라운드 번호 이하인 잠긴 무기를 해금 → 저장 → WeaponUnlockedEvent
    ///    (UnlockOrder = 해금 라운드. 보스 유무와 관계없이 라운드 클리어 = Reward 진입)
    ///  - 해금한 무기 Id 를 PlayerPrefs(Unlock.Weapons, "2001,2004") 에 저장해 게임을 껐다 켜도 유지
    /// 해금한 무기는 다음 출발 때 무기 선택 화면(LoadoutPanel)에서 고를 수 있다.
    /// 해금은 RunDirector 의 StageClearedEvent 로만 일어난다 (테스터가 직접 발행하는 BossDefeatedEvent 로는 해금되지 않음)
    /// </summary>
    public sealed class WeaponUnlockLedger : IWeaponUnlocks
    {
        public const string DefaultPrefsKey = "Unlock.Weapons";

        private readonly string prefsKey;
        private readonly HashSet<int> unlocked = new HashSet<int>();
        private bool loaded;

        /// <param name="prefsKey">저장 키. 테스트는 다른 키를 써서 실제 진행을 건드리지 않는다</param>
        public WeaponUnlockLedger(string prefsKey = DefaultPrefsKey)
        {
            this.prefsKey = prefsKey;
        }

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
            EnsureLoaded();
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

        /// <summary>다음 무기 하나를 해금하고 저장한다. 해금한 무기 (모두 해금됐으면 null)</summary>
        public WeaponDef UnlockNext()
        {
            var next = PeekNext();
            if (next == null) return null;

            unlocked.Add(next.Id);
            Save();
            EventBus<WeaponUnlockedEvent>.Publish(new WeaponUnlockedEvent(next));
            return next;
        }

        /// <summary>저장된 해금 기록을 지운다 (기본 무기만 남음). 테스트·디버그용</summary>
        public void ResetAll()
        {
            unlocked.Clear();
            loaded = true;
            PlayerPrefs.DeleteKey(prefsKey);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 라운드를 클리어하면 UnlockOrder 가 그 라운드 번호 이하인 잠긴 무기를 모두 해금하고 저장한다.
        /// 앞 라운드 해금을 놓친 기록(옛 저장)도 함께 채운다. StageClearedEvent 가 부른다.
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
            Save();
            foreach (var weapon in newlyUnlocked) EventBus<WeaponUnlockedEvent>.Publish(new WeaponUnlockedEvent(weapon));
            return newlyUnlocked;
        }

        /// <summary>지금 ServiceLocator 가 돌려주는 구현만 해금한다 (테스트가 다른 키의 Ledger 를 등록하면 실제 진행은 건드리지 않음)</summary>
        private void OnStageCleared(StageClearedEvent e)
        {
            if (ServiceLocator.TryGet<IWeaponUnlocks>(out var active) && ReferenceEquals(active, this) == false) return;
            UnlockForRound(e.Round);
        }

        private void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            unlocked.Clear();
            string raw = PlayerPrefs.GetString(prefsKey, "");
            foreach (string token in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(token.Trim(), out int id)) unlocked.Add(id);
        }

        private void Save()
        {
            PlayerPrefs.SetString(prefsKey, string.Join(",", unlocked.OrderBy(id => id)));
            PlayerPrefs.Save();
        }
    }
}
