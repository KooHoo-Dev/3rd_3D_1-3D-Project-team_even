using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// Player_AC 가 쓰는 반복(Loop Time) 클립의 끝 → 처음 이음새 검사 (Play 없이).
    /// 휴머노이드 근육 커브의 첫 값과 마지막 값 차이(자세), 처음·끝 기울기 차이(속도), 루트 높이·회전 차이를 잰다.
    /// Loop Pose 가 꺼져 있으면 Unity 가 이음새를 맞춰 주지 않으므로 차이가 그대로 한 바퀴마다 튐이 된다
    /// </summary>
    internal static class AnimClipLoopCheck
    {
        private const string ControllerPath = "Assets/Scenes/MS/Animations/Player_AC.controller";
        private const float HighPose = 0.05f;   // 근육 값(대략 -1~1) 차이
        private const float MediumPose = 0.02f;
        private const float HighKink = 3f;      // 이음새 꺾임 / 클립 안쪽 90% 꺾임
        private const float MediumKink = 1.5f;
        private static readonly Regex NonMuscle = new Regex(@"^(Root|Motion|LeftHand|RightHand|LeftFoot|RightFoot)[TQ]\.");

        private class Result
        {
            public AnimationClip clip;
            public string usage;
            public bool loopPose;
            public float poseDiff, velocityDiff, rootYDiff, rootAngleDiff;
            public float seamKink;
            public string seamKinkMuscle = "-";
            public string topMuscles = "-";
            public int muscleCount;

            private bool PoseHigh => poseDiff >= HighPose && loopPose == false;
            private bool PoseMedium => poseDiff >= MediumPose && loopPose == false;
            public string Grade => muscleCount == 0 ? "커브 없음"
                : PoseHigh || seamKink >= HighKink ? "높음"
                : PoseMedium || seamKink >= MediumKink ? "보통"
                : "OK";
            public int Order => muscleCount == 0 ? -1 : PoseHigh || seamKink >= HighKink ? 3 : PoseMedium || seamKink >= MediumKink ? 2 : 1;
        }

        [MenuItem("Tools/HybridShooter/Animation Jitter Test/Clip Loop Check (Play 없이)", false, 102)]
        private static void RunMenu()
        {
            string folder = AnimJitterTestMenu.NewResultFolder();
            string path = Write(folder);
            if (path != null) EditorUtility.RevealInFinder(path);
        }

        /// <summary>folder/clip_loop.md 에 결과를 쓰고 경로를 돌려준다 (컨트롤러가 없으면 null)</summary>
        public static string Write(string folder)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[AnimJitter] {ControllerPath} 가 없습니다 (Tools/HybridShooter/Build Player Animator)");
                return null;
            }

            var usage = new Dictionary<AnimationClip, List<string>>();
            foreach (AnimatorControllerLayer layer in controller.layers) Walk(layer.stateMachine, layer.name, usage);

            var results = new List<Result>();
            var oneShots = new List<string>();
            foreach (var pair in usage)
            {
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(pair.Key);
                string where = string.Join(", ", pair.Value.Distinct());
                if (settings.loopTime == false) { oneShots.Add($"{pair.Key.name} ({where})"); continue; }
                Result r = Measure(pair.Key);
                r.usage = where;
                r.loopPose = settings.loopBlend;
                results.Add(r);
            }
            results = results.OrderByDescending(x => x.Order).ThenByDescending(x => Mathf.Max(x.poseDiff / HighPose, x.seamKink / HighKink)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine("# 반복 클립 이음새 검사");
            sb.AppendLine();
            sb.AppendLine($"- 대상: {ControllerPath} 가 쓰는 Loop Time 클립 {results.Count}개");
            sb.AppendLine($"- 자세 차이 = 근육 커브 |마지막 − 처음| 최대 (높음 ≥ {HighPose}, 보통 ≥ {MediumPose}). Loop Pose 가 켜져 있으면 Unity 가 이음새를 섞어 준다");
            sb.AppendLine("- 속도 차이 = 처음·끝 한 프레임 기울기 차이 최대 (근육/초, 참고용: 빠른 동작은 원래 크다)");
            sb.AppendLine($"- 이음새 꺾임 = 이음새의 반 프레임 간격 2차 차분 ÷ 그 근육의 클립 안쪽 90% 값 (높음 ≥ {HighKink}, 보통 ≥ {MediumKink}). 1 근처면 이음새가 클립 안쪽만큼 매끄럽다");
            sb.AppendLine("- 루트 Y·회전 차이: Bake Into Pose 가 켜져 있으면 몸 높이·방향이 한 바퀴마다 이만큼 튄다");
            sb.AppendLine($"- 클립 경로가 {PlayerLoopClips.Folder} 이면 이음새를 고친 판 (Tools/HybridShooter/Fix Loop Clips)");
            sb.AppendLine();
            sb.AppendLine("| 판정 | 클립 | 쓰는 곳 | Loop Pose | 프레임 | 자세 차이 | 차이 큰 근육 | 이음새 꺾임 | 꺾임 큰 근육 | 속도 차이 | 루트 Y (cm) | 루트 회전 (°) |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (Result r in results)
                sb.AppendLine($"| {r.Grade} | {r.clip.name} | {r.usage} | {(r.loopPose ? "켜짐" : "꺼짐")} | {Mathf.RoundToInt(r.clip.length * r.clip.frameRate)} | {r.poseDiff:0.000} | {r.topMuscles} | ×{r.seamKink:0.0} | {r.seamKinkMuscle} | {r.velocityDiff:0.00} | {r.rootYDiff * 100f:0.0} | {r.rootAngleDiff:0.0} |");
            sb.AppendLine();
            if (oneShots.Count > 0)
            {
                sb.AppendLine("반복하지 않는 클립 (검사 안 함): " + string.Join(", ", oneShots));
                sb.AppendLine();
            }

            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "clip_loop.md");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));

            int high = results.Count(x => x.Order == 3), medium = results.Count(x => x.Order == 2);
            string worst = results.Count > 0 ? $" 가장 심한 클립: {results[0].clip.name} 자세 {results[0].poseDiff:0.000}, 이음새 꺾임 ×{results[0].seamKink:0.0}" : "";
            Debug.Log($"[AnimJitter] 클립 이음새 검사: 높음 {high} · 보통 {medium} / {results.Count}개.{worst}\n{path}");
            return path;
        }

        private static void Walk(AnimatorStateMachine machine, string where, Dictionary<AnimationClip, List<string>> usage)
        {
            foreach (ChildAnimatorState child in machine.states) AddMotion(child.state.motion, $"{where}/{child.state.name}", usage);
            foreach (ChildAnimatorStateMachine sub in machine.stateMachines) Walk(sub.stateMachine, $"{where}/{sub.stateMachine.name}", usage);
        }

        private static void AddMotion(Motion motion, string where, Dictionary<AnimationClip, List<string>> usage)
        {
            if (motion is AnimationClip clip)
            {
                if (usage.TryGetValue(clip, out List<string> list) == false) usage[clip] = list = new List<string>();
                list.Add(where);
            }
            else if (motion is BlendTree tree)
            {
                foreach (ChildMotion child in tree.children) AddMotion(child.motion, where, usage);
            }
        }

        private static Result Measure(AnimationClip clip)
        {
            var r = new Result { clip = clip };
            float length = clip.length;
            float step = clip.frameRate > 0f ? 1f / clip.frameRate : 1f / 30f;
            if (length <= step) return r;

            var diffs = new List<(string name, float diff)>();
            var rootQ0 = new float[4];
            var rootQ1 = new float[4];
            foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type != typeof(Animator)) continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) continue;
                float first = curve.Evaluate(0f), last = curve.Evaluate(length);
                string property = binding.propertyName;

                if (property == "RootT.y") { r.rootYDiff = Mathf.Abs(last - first); continue; }
                int q = "xyzw".IndexOf(property.Length == 7 && property.StartsWith("RootQ.") ? property[6] : '-');
                if (q >= 0) { rootQ0[q] = first; rootQ1[q] = last; continue; }
                if (NonMuscle.IsMatch(property)) continue;

                r.muscleCount++;
                diffs.Add((property, Mathf.Abs(last - first)));
                float startSlope = (curve.Evaluate(step) - first) / step;
                float endSlope = (last - curve.Evaluate(length - step)) / step;
                r.velocityDiff = Mathf.Max(r.velocityDiff, Mathf.Abs(endSlope - startSlope));

                // 이음새 꺾임: 반 프레임 간격 2차 차분을 이음새(끝 → 처음)와 클립 안쪽(90% 값)으로 비교. 키 사이 기울기 꺾임까지 잡힌다
                float h = step * 0.5f;
                float seam = Mathf.Abs(curve.Evaluate(h) - 2f * first + curve.Evaluate(length - h));
                var inner = new List<float>();
                for (float t = h; t <= length - h; t += h)
                    inner.Add(Mathf.Abs(curve.Evaluate(t + h) - 2f * curve.Evaluate(t) + curve.Evaluate(t - h)));
                if (inner.Count == 0) continue;
                inner.Sort();
                float p90 = inner[Mathf.Min(inner.Count - 1, (int)(inner.Count * 0.9f))];
                if (p90 < 1e-4f) continue; // 거의 안 움직이는 근육은 비율이 의미 없다
                float ratio = seam / p90;
                if (ratio > r.seamKink)
                {
                    r.seamKink = ratio;
                    r.seamKinkMuscle = property;
                }
            }

            if (diffs.Count > 0)
            {
                diffs.Sort((a, b) => b.diff.CompareTo(a.diff));
                r.poseDiff = diffs[0].diff;
                r.topMuscles = string.Join(", ", diffs.Take(3).Select(d => $"{d.name} {d.diff:0.000}"));
            }
            var start = new Quaternion(rootQ0[0], rootQ0[1], rootQ0[2], rootQ0[3]);
            var end = new Quaternion(rootQ1[0], rootQ1[1], rootQ1[2], rootQ1[3]);
            if (start != new Quaternion(0f, 0f, 0f, 0f) && end != new Quaternion(0f, 0f, 0f, 0f))
                r.rootAngleDiff = Quaternion.Angle(start.normalized, end.normalized);
            return r;
        }
    }
}
