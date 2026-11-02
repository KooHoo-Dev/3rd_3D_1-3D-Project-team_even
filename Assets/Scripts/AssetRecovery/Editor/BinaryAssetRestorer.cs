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
            var restored = new Dictionary<string, string>(); // path → index sha
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
                    foreach (string path in restored.Keys)
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                Debug.Log($"[BinaryAssetRestorer] 줄바꿈 변환으로 깨진 바이너리 에셋 {restored.Count}개를 git 원본으로 복구했습니다.\n{string.Join("\n", restored.Keys)}");

                RefreshGitIndex(restored);
                ConsumeIndexChange(); // 방금 갱신한 index 때문에 다시 검사하지 않도록
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
        private static void Scan(Dictionary<string, string> restored, List<string> skipped)
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
                        restored[path] = sha;
                    }
                    else
                    {
                        skipped.Add(path);
                    }
                }
            }
        }

        /// <summary>
        /// 복구한 파일의 git index 정보(크기·시각)를 갱신해 git status 에 수정됨으로 남지 않게 한다.
        /// git 이 이 경로에 적용할 필터(.gitattributes)를 거친 해시가 index 와 같은 파일만 add 하므로 스테이징 내용은 바뀌지 않는다.
        /// (옛 .gitattributes 가 남은 브랜치에서는 add 하면 CRLF→LF 로 깨진 내용이 스테이징되므로 건너뛴다)
        /// </summary>
        private static void RefreshGitIndex(Dictionary<string, string> restored)
        {
            try
            {
                var paths = new List<string>(restored.Keys);
                string[] hashes = RunGit("hash-object --stdin-paths", Encoding.UTF8.GetBytes(string.Join("\n", paths) + "\n"))
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                var same = new List<string>();
                var converted = new List<string>();
                for (int i = 0; i < paths.Count; i++)
                {
                    if (i < hashes.Length && hashes[i].Trim() == restored[paths[i]]) same.Add(paths[i]);
                    else converted.Add(paths[i]);
                }

                if (same.Count > 0)
                    RunGit("--literal-pathspecs add --pathspec-from-file=- --pathspec-file-nul", Encoding.UTF8.GetBytes(string.Join("\0", same)));

                if (converted.Count > 0)
                    Debug.LogWarning($"[BinaryAssetRestorer] 이 브랜치의 .gitattributes 가 바이너리 에셋을 텍스트로 변환해 git status 에 수정됨으로 보이는 파일 {converted.Count}개입니다. " +
                                     $"이 파일들은 커밋하지 마세요 (깨진 내용이 저장됩니다). .gitattributes 를 text=auto 로 고친 브랜치에서는 생기지 않습니다.\n{string.Join("\n", converted)}");
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
            {
                Debug.LogWarning($"[BinaryAssetRestorer] 파일은 복구했지만 git index 갱신에 실패했습니다. git status 에 수정됨으로 보일 수 있습니다 (내용은 git 원본과 같음).\n{e.Message}");
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

        private static string RunGit(string args, byte[] input = null)
        {
            ProcessStartInfo info = GitStartInfo(args);
            info.RedirectStandardError = true;
            info.RedirectStandardInput = input != null;
            using (Process process = Process.Start(info))
            {
                // 입력을 쓰는 동안 출력 파이프가 차서 멈추지 않도록 먼저 비동기로 읽기 시작
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (input != null)
                {
                    process.StandardInput.BaseStream.Write(input, 0, input.Length);
                    process.StandardInput.Close();
                }
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"git {args} (exit {process.ExitCode}): {error.Result.Trim()}");
                return output.Result;
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
