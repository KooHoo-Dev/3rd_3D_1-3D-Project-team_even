using System.Linq;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using HybridShooter.Weapon;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.UI.EditorTools
{
    /// <summary>
    /// 게임 흐름(무기 해금·무기 선택·게임 오버) 설정·디버그 메뉴. Tools/게임 흐름/...
    ///  - 에디터가 열릴 때 메인 메뉴 씬이 Build Settings 에 없으면 맨 앞에 추가한다 (게임 오버 → [메인 메뉴] 가 씬을 불러올 수 있게). 지우지는 않는다
    ///  - 해금 초기화 / 모두 해금 / 다음 1개 해금, 저장된 무기 선택 지우기 (플레이 중이면 등록된 Ledger, 아니면 저장값을 바로 고친다)
    /// </summary>
    [InitializeOnLoad]
    internal static class GameFlowSetupMenu
    {
        private const string Menu = "Tools/게임 흐름/";
        private const string MainMenuScenePath = "Assets/Scenes/GU/" + GameOverPanel.DefaultMainMenuScene + ".unity";
        private const string SessionKey = "HybridShooter.GameFlowSetup.BuildScenesChecked";

        static GameFlowSetupMenu()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += () => EnsureMainMenuInBuild(false);
        }

        [MenuItem(Menu + "메인 메뉴 씬을 Build Settings 에 추가")]
        private static void AddMainMenuToBuild() => EnsureMainMenuInBuild(true);

        private static void EnsureMainMenuInBuild(bool verbose)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath) == null)
            {
                if (verbose) Debug.LogWarning($"[GameFlowSetup] 메인 메뉴 씬이 없습니다: {MainMenuScenePath}");
                return;
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            int index = scenes.FindIndex(s => s.path == MainMenuScenePath);
            if (index >= 0)
            {
                if (scenes[index].enabled == false)
                {
                    scenes[index] = new EditorBuildSettingsScene(MainMenuScenePath, true);
                    EditorBuildSettings.scenes = scenes.ToArray();
                    Debug.Log($"[GameFlowSetup] Build Settings 의 메인 메뉴 씬을 켰습니다: {MainMenuScenePath}");
                }
                else if (verbose) Debug.Log("[GameFlowSetup] 메인 메뉴 씬이 이미 Build Settings 에 있습니다");
                return;
            }

            // 빌드 시작 씬 = 메인 메뉴 (메뉴의 [게임 시작]이 게임 씬을 이름으로 부른다)
            scenes.Insert(0, new EditorBuildSettingsScene(MainMenuScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[GameFlowSetup] Build Settings 맨 앞에 메인 메뉴 씬을 추가했습니다: {MainMenuScenePath}");
        }

        // 해금은 저장하지 않고 실행 중인 Ledger 에만 있다 → 해금 메뉴는 플레이 중에만 켠다
        [MenuItem(Menu + "무기 해금 초기화 (기본 무기만)", true)]
        [MenuItem(Menu + "다음 무기 1개 해금", true)]
        [MenuItem(Menu + "무기 모두 해금", true)]
        private static bool CanEditUnlocks() => Application.isPlaying;

        [MenuItem(Menu + "무기 해금 초기화 (기본 무기만)")]
        private static void ResetUnlocks()
        {
            Ledger().ResetAll();
            Debug.Log("[GameFlowSetup] 무기 해금 기록을 지웠습니다");
        }

        [MenuItem(Menu + "다음 무기 1개 해금")]
        private static void UnlockNext()
        {
            var def = Ledger().UnlockNext();
            Debug.Log(def != null ? $"[GameFlowSetup] 해금: {def.Name}" : "[GameFlowSetup] 더 해금할 무기가 없습니다");
        }

        [MenuItem(Menu + "무기 모두 해금")]
        private static void UnlockAll()
        {
            var ledger = Ledger();
            int count = 0;
            while (ledger.UnlockNext() != null) count++;
            Debug.Log($"[GameFlowSetup] {count}개 해금");
        }

        [MenuItem(Menu + "저장된 무기 선택 지우기")]
        private static void ClearLoadoutSelection()
        {
            foreach (int slot in GunSlot.LoadoutSlots) PlayerPrefs.DeleteKey(LoadoutPanel.PrefsKeyPrefix + slot);
            PlayerPrefs.Save();
            Debug.Log("[GameFlowSetup] 저장된 무기 선택(Loadout.Slot*)을 지웠습니다");
        }

        /// <summary>플레이 중 등록된 Ledger (화면·이벤트와 맞물림). 해금 메뉴는 플레이 중에만 켜진다 (CanEditUnlocks)</summary>
        private static WeaponUnlockLedger Ledger() => (WeaponUnlockLedger)ServiceLocator.Get<IWeaponUnlocks>();
    }
}
