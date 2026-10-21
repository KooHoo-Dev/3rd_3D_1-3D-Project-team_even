using System.IO;
using HybridShooter.Core.Data;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Core.Data.Editor
{
    /// <summary>
    /// 상단 메뉴 Tools/Data.
    /// TSV 를 고친 뒤 Play 하지 않고도 오류를 확인할 수 있다. (PR 올리기 전에 한 번 돌리는 것을 권장)
    /// </summary>
    public static class DataTableMenu
    {
        [MenuItem("Tools/Data/Validate Tables")]
        public static void Validate()
        {
            var report = DataManager.Load(DataManager.DefaultTableFolder);
            if (report.HasError)
                EditorUtility.DisplayDialog("Data Validate", $"오류 {report.Errors.Count}건. Console 을 확인하세요.", "확인");
            else
                Debug.Log($"[Data] 검증 통과 (경고 {report.Warnings.Count}건)");
        }

        [MenuItem("Tools/Data/Open Table Folder")]
        public static void OpenFolder()
        {
            string folder = DataManager.DefaultTableFolder;
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(Path.Combine(folder, "ConfigTable.tsv"));
        }
    }
}
