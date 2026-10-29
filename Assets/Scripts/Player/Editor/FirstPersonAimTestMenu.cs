using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 1인칭 조준 자동 측정 메뉴 (Tools/HybridShooter/First Person Aim Test).
    ///  - Run Quick / Run Full: PlayerTest 씬을 Play 해서 Player 에 FirstPersonAimScenarioRunner 를 붙인다.
    ///    끝나면 Play 를 멈춘다 (이미 Play 중이었으면 지금 씬의 Player 로 재고 Play 는 그대로 둔다)
    ///  - 결과: <프로젝트>/Logs/FirstPersonAim/<시각>/summary.md · samples.csv
    /// </summary>
    [InitializeOnLoad]
    internal static class FirstPersonAimTestMenu
    {
        private const string ScenePath = "Assets/Scenes/MS/Scenes/PlayerTest.unity";
        private const string FolderKey = "HybridShooter.FirstPersonAim.Folder";
        private const string QuickKey = "HybridShooter.FirstPersonAim.Quick";
        private const string AutoExitKey = "HybridShooter.FirstPersonAim.AutoExit";

        private static string ResultsRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "FirstPersonAim"));

        static FirstPersonAimTestMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static string NewResultFolder()
        {
            string folder = Path.Combine(ResultsRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        [MenuItem("Tools/HybridShooter/First Person Aim Test/Run Quick (지금 총, 약 3분)", false, 110)]
        private static void RunQuick() => Run(true);

        [MenuItem("Tools/HybridShooter/First Person Aim Test/Run Full (모든 총, 총마다 약 3분)", false, 111)]
        private static void RunFull() => Run(false);

        [MenuItem("Tools/HybridShooter/First Person Aim Test/Open Results Folder", false, 130)]
        private static void OpenResults()
        {
            Directory.CreateDirectory(ResultsRoot);
            EditorUtility.RevealInFinder(ResultsRoot);
        }

        private static void Run(bool quick)
        {
            if (EditorApplication.isPlaying)
            {
                Attach(NewResultFolder(), quick, autoExit: false);
                return;
            }

            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
                EditorSceneManager.OpenScene(ScenePath);
            }
            SessionState.SetString(FolderKey, NewResultFolder());
            SessionState.SetBool(QuickKey, quick);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode) return;
            string folder = SessionState.GetString(FolderKey, "");
            if (string.IsNullOrEmpty(folder)) return;
            SessionState.EraseString(FolderKey);
            Attach(folder, SessionState.GetBool(QuickKey, true), autoExit: true);
        }

        private static void Attach(string folder, bool quick, bool autoExit)
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null)
            {
                Debug.LogError("[FirstPersonAim] 씬에 PlayerController 가 없습니다");
                if (autoExit) EditorApplication.ExitPlaymode();
                return;
            }
            if (player.GetComponent<FirstPersonAimScenarioRunner>() != null)
            {
                Debug.LogWarning("[FirstPersonAim] 이미 측정 중입니다");
                return;
            }

            SessionState.SetBool(AutoExitKey, autoExit);
            FirstPersonAimScenarioRunner.Completed -= OnCompleted;
            FirstPersonAimScenarioRunner.Completed += OnCompleted;
            var runner = player.gameObject.AddComponent<FirstPersonAimScenarioRunner>();
            runner.quick = quick;
            runner.outputFolder = folder;
        }

        private static void OnCompleted(string folder)
        {
            FirstPersonAimScenarioRunner.Completed -= OnCompleted;
            Debug.Log($"[FirstPersonAim] 결과: {Path.Combine(folder, "summary.md")}");
            if (SessionState.GetBool(AutoExitKey, false)) EditorApplication.ExitPlaymode();
        }
    }
}
