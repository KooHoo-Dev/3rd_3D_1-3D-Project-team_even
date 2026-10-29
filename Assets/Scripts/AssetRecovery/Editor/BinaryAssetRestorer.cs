using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HybridShooter.AssetRecovery.Editor
{
    /// <summary>
    /// git 줄바꿈 변환(LF→CRLF)으로 깨진 바이너리 직렬화 에셋(.prefab/.mat 등)을 git 에 저장된 원본으로 되돌린다.
    /// 옛 .gitattributes(*.prefab text)가 남아 있는 브랜치를 체크아웃/머지하면 Creepy_Cat 같은 바이너리 프리펩이 깨져
    /// "Invalid serialized file header" → Missing Prefab 이 된다. git 에 저장된 내용(index)은 멀쩡하므로 그대로 꺼내 덮어쓴다.
    ///
    ///   자동 : 에디터 시작 / 포커스 복귀 시 git index 가 바뀌었으면(체크아웃·머지·풀) 검사 후 복구
    ///   수동 : Tools/Git/Restore Broken Binary Assets
    ///
    /// 줄바꿈만 다른 파일만 되돌리고, 내용이 실제로 바뀐 파일(로컬 수정)은 건드리지 않는다.
    /// </summary>
    internal static class BinaryAssetRestorer
    {
        private const string RestoreMenu = "Tools/Git/Restore Broken Binary Assets";
        private const string AutoMenu = "Tools/Git/Auto Restore Broken Binary Assets";
        private const string AutoPrefKey = "HybridShooter.BinaryAssetRestorer.Auto";
        private const string IndexStampKey = "HybridShooter.BinaryAssetRestorer.IndexStamp";

        private static readonly HashSet<string> SerializedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".prefab", ".unity", ".mat", ".asset", ".anim", ".controller", ".overrideController", ".mask",
            ".physicMaterial", ".physicsMaterial2D", ".flare", ".guiskin", ".renderTexture", ".mixer",
            ".terrainlayer", ".playable", ".spriteatlas",
        };

        private static readonly byte[] YamlHeader = Encoding.ASCII.GetBytes("%YAML");

        private static bool scheduled;
        private static string indexPath; // .git/index (worktree 면 다른 위치)

        private static bool AutoEnabled
        {
            get => EditorPrefs.GetBool(AutoPrefKey, true);
            set => EditorPrefs.SetBool(AutoPrefKey, value);
        }

        private static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        [InitializeOnLoadMethod]
        private static void Init()
        {
            EditorApplication.focusChanged += focused =>
            {
                if (focused) ScheduleAutoCheck();
            };
            ScheduleAutoCheck();
        }

        [MenuItem(RestoreMenu)]
        private static void RestoreFromMenu() => Restore(interactive: true);

        [MenuItem(AutoMenu)]
        private static void ToggleAuto() => AutoEnabled = !AutoEnabled;

        [MenuItem(AutoMenu, true)]
        private static bool ToggleAutoValidate()
        {
            Menu.SetChecked(AutoMenu, AutoEnabled);
            return true;
        }

        private static void ScheduleAutoCheck()
        {
            if (!AutoEnabled || scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += () =>
            {
                scheduled = false;
                if (ConsumeIndexChange()) Restore(interactive: false);
            };
        }

        /// <summary>git index 수정 시각이 마지막 검사 이후 바뀌었으면 true (체크아웃·머지·풀 감지)</summary>
        private static bool ConsumeIndexChange()
        {
            try
            {
                if (indexPath == null)
                    indexPath = Path.Combine(ProjectRoot, RunGit("rev-parse --git-path index").Trim());

                string stamp = File.GetLastWriteTimeUtc(indexPath).Ticks.ToString();
                if (SessionState.GetString(IndexStampKey, "") == stamp) return false;
                SessionState.SetString(IndexStampKey, stamp);
                return true;
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                // git 이 없거나 저장소가 아니면 자동 검사는 조용히 건너뛴다 (메뉴로 실행하면 오류를 보여준다)
                return false;
            }
        }

        private static void Restore(bool interactive)
        {
            var restored = new List<string>();
            var skipped = new List<string>();
            try
            {
                Scan(restored, skipped);
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                Debug.LogError($"[BinaryAssetRestorer] git 실행 실패: {e.Message}");
                if (interactive) EditorUtility.DisplayDialog("Restore Broken Binary Assets", $"git 실행에 실패했습니다.\n{e.Message}", "확인");
                return;
            }

            if (restored.Count > 0)
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    foreach (string path in restored)
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                Debug.Log($"[BinaryAssetRestorer] 줄바꿈 변환으로 깨진 바이너리 에셋 {restored.Count}개를 git 원본으로 복구했습니다.\n{string.Join("\n", restored)}");
            }

            if (skipped.Count > 0)
                Debug.LogWarning($"[BinaryAssetRestorer] git 과 내용이 달라(로컬 수정) 복구하지 않은 바이너리 에셋 {skipped.Count}개:\n{string.Join("\n", skipped)}");

            if (interactive)
            {
                string message = restored.Count == 0 && skipped.Count == 0
                    ? "깨진 바이너리 에셋이 없습니다."
                    : $"복구: {restored.Count}개\n로컬 수정이라 건너뜀: {skipped.Count}개\n\n자세한 목록은 Console 을 확인하세요.";
                EditorUtility.DisplayDialog("Restore Broken Binary Assets", message, "확인");
            }
        }

        /// <summary>
        /// 디스크에서 바이너리인 직렬화 에셋 중 git index 와 해시가 다른 파일을 찾아,
        /// CR 만 다른 경우(줄바꿈 변환 손상) git 원본으로 덮어쓴다.
        /// </summary>
        private static void Scan(List<string> restored, List<string> skipped)
        {
            string root = ProjectRoot;
            string listing = RunGit("ls-files -s -z -- Assets");

            using (var blobs = new BlobReader())
            {
                foreach (string entry in listing.Split('\0'))
                {
                    // "<mode> <sha> <stage>\t<path>"
                    int tab = entry.IndexOf('\t');
                    if (tab < 0) continue;
                    string[] meta = entry.Substring(0, tab).Split(' ');
                    string path = entry.Substring(tab + 1);
                    if (meta.Length < 3 || meta[2] != "0") continue; // 머지 충돌 중인 항목 제외
                    if (!SerializedExtensions.Contains(Path.GetExtension(path))) continue;

                    string fullPath = Path.Combine(root, path);
                    if (!File.Exists(fullPath)) continue;

                    byte[] disk = File.ReadAllBytes(fullPath);
                    if (StartsWith(disk, YamlHeader)) continue; // 텍스트 에셋은 줄바꿈이 바뀌어도 무해
                    string sha = meta[1];
                    if (GitBlobSha(disk) == sha) continue;

                    byte[] original = blobs.Read(sha);
                    if (original == null) continue;

                    if (EqualsIgnoringCr(disk, original))
                    {
                        File.WriteAllBytes(fullPath, original);
                        restored.Add(path);
                    }
                    else
                    {
                        skipped.Add(path);
                    }
                }
            }
        }

        private static bool StartsWith(byte[] data, byte[] prefix)
        {
            if (data.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (data[i] != prefix[i]) return false;
            return true;
        }

        /// <summary>LF 앞의 CR 을 무시하고 비교 (git 의 LF→CRLF 변환 결과인지 판정)</summary>
        private static bool EqualsIgnoringCr(byte[] a, byte[] b)
        {
            int i = 0, j = 0;
            while (true)
            {
                if (i < a.Length && a[i] == '\r' && i + 1 < a.Length && a[i + 1] == '\n') i++;
                if (j < b.Length && b[j] == '\r' && j + 1 < b.Length && b[j + 1] == '\n') j++;
                if (i == a.Length || j == b.Length) return i == a.Length && j == b.Length;
                if (a[i] != b[j]) return false;
                i++;
                j++;
            }
        }

        /// <summary>git hash-object --no-filters 와 같은 값 (SHA-1 of "blob {len}\0" + data)</summary>
        private static string GitBlobSha(byte[] data)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] header = Encoding.ASCII.GetBytes($"blob {data.Length}\0");
                sha1.TransformBlock(header, 0, header.Length, null, 0);
                sha1.TransformFinalBlock(data, 0, data.Length);
                var sb = new StringBuilder(40);
                foreach (byte b in sha1.Hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static ProcessStartInfo GitStartInfo(string args) => new ProcessStartInfo("git", "-c core.quotepath=off " + args)
        {
            WorkingDirectory = ProjectRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };

        private static string RunGit(string args)
        {
            ProcessStartInfo info = GitStartInfo(args);
            info.RedirectStandardError = true;
            using (Process process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"git {args} (exit {process.ExitCode}): {error.Trim()}");
                return output;
            }
        }

        /// <summary>git cat-file --batch 프로세스 하나로 blob 원본 바이트를 연속으로 읽는다.</summary>
        private sealed class BlobReader : IDisposable
        {
            private Process process;
            private Stream input;
            private Stream output;

            public byte[] Read(string sha)
            {
                if (process == null)
                {
                    ProcessStartInfo info = GitStartInfo("cat-file --batch");
                    info.RedirectStandardInput = true;
                    process = Process.Start(info);
                    input = process.StandardInput.BaseStream;
                    output = process.StandardOutput.BaseStream;
                }

                byte[] request = Encoding.ASCII.GetBytes(sha + "\n");
                input.Write(request, 0, request.Length);
                input.Flush();

                // "<sha> blob <size>" 또는 "<sha> missing"
                string[] header = ReadLine().Split(' ');
                if (header.Length < 3) return null;

                int size = int.Parse(header[2]);
                var data = new byte[size];
                int read = 0;
                while (read < size)
                {
                    int n = output.Read(data, read, size - read);
                    if (n <= 0) throw new IOException("git cat-file 출력이 예상보다 일찍 끝났습니다.");
                    read += n;
                }
                output.ReadByte(); // 내용 뒤의 LF
                return data;
            }

            private string ReadLine()
            {
                var bytes = new List<byte>(64);
                int b;
                while ((b = output.ReadByte()) != -1 && b != '\n') bytes.Add((byte)b);
                return Encoding.ASCII.GetString(bytes.ToArray());
            }

            public void Dispose()
            {
                if (process == null) return;
                input.Close();
                process.WaitForExit(5000);
                process.Dispose();
            }
        }
    }
}
