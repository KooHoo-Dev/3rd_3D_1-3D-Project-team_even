using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 애니메이션 에셋 조사 창 (재구축 1단계). Tools/HybridShooter/Animation Survey
    ///
    ///  1. 실측 리포트: 무기 세트별 클립의 길이·루프·평균 수평/수직 속도·회전 속도를 재서 Markdown 표로 저장한다
    ///  2. 미리보기 검수: 씬의 플레이어 몸(Animator)에 클립을 편집 모드에서 그대로 재생해 보고(리타게팅 확인),
    ///     클립마다 '좋음 / 문제' 와 메모를 남긴다. 검수 결과는 리포트에 함께 들어간다
    /// </summary>
    public class AnimationSurveyWindow : EditorWindow
    {
        private const string ClipRoot = "Assets/ExternalAssets/PROTOFACTOR/Ultimate Animation Collection/Animations";
        private const string DocFolder = "Assets/Scenes/MS/Docs";
        private const string ReportPath = DocFolder + "/AnimationSurvey.md";
        private const string ReviewPath = DocFolder + "/AnimationReview.json";

        /// <summary>(표시 이름, 폴더, 클립 이름 끝말)</summary>
        private static readonly (string label, string folder, string suffix)[] Sets =
        {
            ("라이플 (Assault Rifle)", "Assault Rifle Animset", "AssaultRifle"),
            ("권총 (2Handed Gun)", "2Handed Gun Animset", "2HandedGun"),
            ("산탄총 (Shotgun)", "Shotgun Animset", "Shotgun"),
            ("로켓 (Bazooka)", "Bazooka Animset", "Bazooka"),
            ("미니건 (Minigun)", "Minigun Animset", "Minigun"),
            ("맨손 (Basic Locomotion)", "Basic Locomotion Animset", "Unarmed"),
        };

        [Serializable]
        private class ReviewEntry
        {
            public string clip;
            public int state;   // 0 = 미검수, 1 = 좋음, 2 = 문제
            public string note;
        }

        [Serializable]
        private class ReviewData
        {
            public List<ReviewEntry> entries = new List<ReviewEntry>();
        }

        private int setIndex;
        private bool hideRootMotion = true;
        private string filter = "";
        private Vector2 scroll;

        private List<AnimationClip> clips = new List<AnimationClip>();
        private int selected = -1;
        private Animator target;
        private float time;
        private bool playing;
        private double lastTick;

        private ReviewData review;

        [MenuItem("Tools/HybridShooter/Animation Survey")]
        private static void Open() => GetWindow<AnimationSurveyWindow>("Animation Survey");

        private void OnEnable()
        {
            review = LoadReview();
            LoadSet();
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            StopPreview();
        }

        #region GUI
        private void OnGUI()
        {
            EditorGUILayout.LabelField("1. 실측 리포트", EditorStyles.boldLabel);
            if (GUILayout.Button("모든 세트 측정 → " + ReportPath, GUILayout.Height(26))) WriteReport();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("2. 미리보기 검수", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            setIndex = EditorGUILayout.Popup("무기 세트", setIndex, Sets.Select(s => s.label).ToArray());
            hideRootMotion = EditorGUILayout.Toggle("_RM(루트모션) 클립 숨김", hideRootMotion);
            if (EditorGUI.EndChangeCheck()) LoadSet();
            filter = EditorGUILayout.TextField("이름 검색", filter);

            target = (Animator)EditorGUILayout.ObjectField("재생할 몸", target, typeof(Animator), true);
            if (target == null && GUILayout.Button("씬에서 플레이어 몸 찾기")) target = FindPlayerBody();

            DrawClipList();
            DrawPlayer();
        }

        private void DrawClipList()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(260));
            for (int i = 0; i < clips.Count; i++)
            {
                string key = Key(clips[i]);
                if (filter.Length > 0 && key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                ReviewEntry entry = FindReview(key);
                string mark = entry == null || entry.state == 0 ? "  " : entry.state == 1 ? "✔" : "✖";
                var style = i == selected ? EditorStyles.boldLabel : EditorStyles.label;
                if (GUILayout.Button($"{mark} {key}", style)) Select(i);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawPlayer()
        {
            if (selected < 0 || selected >= clips.Count) return;
            AnimationClip clip = clips[selected];
            string key = Key(clip);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(key, EditorStyles.boldLabel);
            Vector3 v = clip.averageSpeed;
            EditorGUILayout.LabelField($"길이 {clip.length:0.00}s · 루프 {(Settings(clip).loopTime ? "O" : "X")} · 수평 {Horizontal(v):0.00} m/s · 수직 {v.y:0.00} m/s · 회전 {clip.averageAngularSpeed:0} °/s");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ 이전")) Select(Mathf.Max(0, selected - 1));
                if (GUILayout.Button(playing ? "■ 정지" : "▶ 재생")) playing = !playing;
                if (GUILayout.Button("다음 ▶")) Select(Mathf.Min(clips.Count - 1, selected + 1));
            }
            EditorGUI.BeginChangeCheck();
            time = EditorGUILayout.Slider("시간", time, 0f, clip.length);
            if (EditorGUI.EndChangeCheck()) { playing = false; Sample(); }

            ReviewEntry entry = FindReview(key) ?? new ReviewEntry { clip = key };
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("✔ 좋음")) SetReview(entry, 1);
                if (GUILayout.Button("✖ 문제")) SetReview(entry, 2);
                if (GUILayout.Button("검수 지우기")) SetReview(entry, 0);
            }
            EditorGUI.BeginChangeCheck();
            string note = EditorGUILayout.TextField("메모", entry.note ?? "");
            if (EditorGUI.EndChangeCheck()) { entry.note = note; SetReview(entry, entry.state); }

            if (GUILayout.Button("미리보기 끝내기 (원래 자세로)")) StopPreview();
        }
        #endregion

        #region 미리보기
        private void Select(int index)
        {
            selected = index;
            time = 0f;
            Sample();
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastTick);
            lastTick = now;
            if (playing == false || selected < 0 || selected >= clips.Count) return;

            AnimationClip clip = clips[selected];
            time += dt;
            if (time > clip.length) time = Settings(clip).loopTime ? time % Mathf.Max(clip.length, 0.0001f) : 0f;
            Sample();
            Repaint();
        }

        /// <summary>편집 모드에서 몸에 클립의 지금 시간 자세를 입힌다 (씬은 바뀌지 않는다. 끝내면 원래 자세)</summary>
        private void Sample()
        {
            if (target == null || selected < 0 || selected >= clips.Count) return;
            if (EditorApplication.isPlaying) return;
            if (AnimationMode.InAnimationMode() == false) AnimationMode.StartAnimationMode();

            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(target.gameObject, clips[selected], time);
            AnimationMode.EndSampling();
            SceneView.RepaintAll();
        }

        private void StopPreview()
        {
            playing = false;
            if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            SceneView.RepaintAll();
        }

        private static Animator FindPlayerBody()
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            return player != null ? player.GetComponentInChildren<Animator>(true) : null;
        }
        #endregion

        #region 클립·검수 데이터
        private void LoadSet()
        {
            StopPreview();
            clips = LoadClips(Sets[setIndex].folder)
                .Where(c => hideRootMotion == false || Key(c).EndsWith("_RM") == false)
                .OrderBy(c => Category(Key(c))).ThenBy(Key)
                .ToList();
            selected = -1;
        }

        private static List<AnimationClip> LoadClips(string folder)
        {
            var result = new List<AnimationClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipRoot + "/" + folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileName(path).StartsWith("Humanoid@") == false) continue;
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
                if (clip != null && result.Contains(clip) == false) result.Add(clip);
            }
            return result;
        }

        /// <summary>"…/Humanoid@RunForwardAssaultRifle.FBX" → "RunForwardAssaultRifle" (클립 이름이 Take 001 인 파일도 있어서 파일 이름 기준)</summary>
        private static string Key(AnimationClip clip)
        {
            string file = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clip));
            int at = file.IndexOf('@');
            return at >= 0 ? file.Substring(at + 1) : file;
        }

        private static AnimationClipSettings Settings(AnimationClip clip) => AnimationUtility.GetAnimationClipSettings(clip);

        private static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>이름 앞부분으로 분류 (리포트 정렬·묶음용)</summary>
        private static string Category(string key)
        {
            (string prefix, string category)[] rules =
            {
                ("WalkJump", "07 점프"), ("RunJump", "07 점프"), ("WalkUTurn", "05 제자리 회전"), ("RunUTurn", "05 제자리 회전"),
                ("Idle", "01 대기"), ("Walk", "02 걷기"), ("Run", "03 달리기"), ("Sprint", "04 전력질주"),
                ("Turn", "05 제자리 회전"), ("Crouch", "06 앉기"), ("JumpToApex", "07 점프"),
                ("Falling", "07 점프"), ("Landing", "07 점프"), ("Dodge", "08 구르기"),
                ("Shoot", "09 사격"), ("Reload", "10 재장전"), ("Draw", "11 꺼내기·넣기"), ("PutBack", "11 꺼내기·넣기"),
                ("Throw", "12 투척"), ("GoToAimGrenade", "12 투척"), ("GetHit", "13 피격"), ("Death", "14 사망"),
                ("Dead", "14 사망"), ("GetBackUp", "14 사망"), ("Melee", "15 근접"), ("Pass", "16 벽넘기"),
                ("Climb", "16 벽넘기"), ("Strafe", "17 엄폐"), ("GoBack", "17 엄폐"), ("GoOut", "17 엄폐"),
                ("GoToTake", "17 엄폐"), ("GoToPeek", "17 엄폐"), ("Peek", "17 엄폐"), ("TakeCover", "17 엄폐"),
            };
            foreach (var (prefix, category) in rules)
                if (key.StartsWith(prefix)) return key.Contains("Cover") ? "17 엄폐" : category;
            return "99 기타";
        }

        private static ReviewData LoadReview()
        {
            try { return File.Exists(ReviewPath) ? JsonUtility.FromJson<ReviewData>(File.ReadAllText(ReviewPath)) ?? new ReviewData() : new ReviewData(); }
            catch (Exception e) { Debug.LogWarning($"[AnimationSurvey] 검수 파일을 읽지 못했습니다: {e.Message}"); return new ReviewData(); }
        }

        private ReviewEntry FindReview(string key) => review.entries.FirstOrDefault(e => e.clip == key);

        private void SetReview(ReviewEntry entry, int state)
        {
            entry.state = state;
            if (review.entries.Contains(entry) == false) review.entries.Add(entry);
            EnsureDocFolder();
            File.WriteAllText(ReviewPath, JsonUtility.ToJson(review, true));
            AssetDatabase.ImportAsset(ReviewPath);
        }

        private static void EnsureDocFolder()
        {
            if (AssetDatabase.IsValidFolder(DocFolder)) return;
            AssetDatabase.CreateFolder(Path.GetDirectoryName(DocFolder).Replace('\\', '/'), Path.GetFileName(DocFolder));
        }
        #endregion

        #region 리포트
        private void WriteReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 애니메이션 에셋 실측 리포트");
            sb.AppendLine();
            sb.AppendLine($"- 생성: {DateTime.Now:yyyy-MM-dd HH:mm} (Tools/HybridShooter/Animation Survey)");
            sb.AppendLine("- 속도는 유니티가 클립의 루트 이동으로 계산한 평균값 (`Motion.averageSpeed`). 수평 = 이동 블렌드트리 좌표로 쓸 값");
            sb.AppendLine("- 검수: ✔ 좋음 / ✖ 문제 / 빈칸 미검수 (미리보기로 이 캐릭터에 재생해 본 결과)");
            sb.AppendLine();

            foreach (var (label, folder, _) in Sets)
            {
                List<AnimationClip> setClips = LoadClips(folder).Where(c => Key(c).EndsWith("_RM") == false).ToList();
                sb.AppendLine($"## {label} — {setClips.Count}개 (루트모션 _RM 판 제외)");
                sb.AppendLine();
                sb.AppendLine("| 분류 | 클립 | 길이(s) | 루프 | 수평 m/s | 수직 m/s | 회전 °/s | 검수 | 메모 |");
                sb.AppendLine("|---|---|---:|:---:|---:|---:|---:|:---:|---|");
                foreach (AnimationClip clip in setClips.OrderBy(c => Category(Key(c))).ThenBy(Key))
                {
                    string key = Key(clip);
                    Vector3 v = clip.averageSpeed;
                    ReviewEntry entry = FindReview(key);
                    string mark = entry == null || entry.state == 0 ? "" : entry.state == 1 ? "✔" : "✖";
                    sb.AppendLine($"| {Category(key).Substring(3)} | {key} | {clip.length:0.00} | {(Settings(clip).loopTime ? "O" : "X")} | " +
                                  $"{Horizontal(v):0.00} | {v.y:0.00} | {clip.averageAngularSpeed:0} | {mark} | {entry?.note ?? ""} |");
                }
                sb.AppendLine();
            }

            EnsureDocFolder();
            File.WriteAllText(ReportPath, sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ReportPath);
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<TextAsset>(ReportPath));
            Debug.Log($"[AnimationSurvey] 리포트 저장: {ReportPath}");
        }
        #endregion
    }
}
