using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 반복 클립 이음새 고치기. 원본(FBX) 클립을 복사한 .anim 을 만들어 끝 → 처음이 매끄럽게 이어지도록 커브를 고친다 (원본 FBX·임포트 설정은 건드리지 않는다)
    ///  - 반복 구간: 원본 끝 프레임은 걸음 주기와 맞지 않았다 (이음새 직전 근육이 올라가다 직후 바로 내려가는 꺾임 → 측정: 튐의 41% 가 루프 순간).
    ///    처음(0 프레임)과 자세·속도가 가장 잘 맞는 끝 프레임을 찾아 그 구간만 쓴다 (Unity Loop Match 와 같은 생각, 시작은 0 고정)
    ///  - 자세(C0): 남은 (끝 − 처음) 차이를 시간 비율로 나눠 뺀다 → 끝 값 = 처음 값 (Unity Loop Pose 와 같은 방식)
    ///  - 이음새 다시 잇기: 원본 0 프레임 자체가 뾰족한 꼭짓점이라 위 둘로는 꺾임이 남았다 (측정으로 확인).
    ///    이음새 앞뒤 1~3 프레임(주기의 약 10%)을 바깥 프레임의 값·기울기를 잇는 Hermite 곡선으로 바꾼다
    ///  - 전력질주만: 상체 근육·몸 회전은 고르게 하고 진폭은 되돌린다, 몸 중심 위치는 고르게만 (UpperBodySmoothPasses, 12 프레임 클립의 거친 꺾임)
    ///  - 속도(C1): 매 프레임 키 기울기를 앞뒤 키 중앙 차분으로, 이음새는 끝 바로 앞·처음 바로 뒤 키로 → 처음·끝 기울기가 같다
    ///  - 클립 설정(루프·Bake Into Pose·높이 기준)은 원본 그대로, Loop Pose 는 끈다 (이미 맞췄으므로). 길이만 고른 구간으로
    /// 결과: Assets/Scenes/MS/Animations/LoopClips/&lt;원본 이름&gt;_Loop.anim (다시 만들어도 같은 에셋·GUID 를 덮어쓴다)
    /// 빌더(Build Player Animator)는 이동·전력질주·대기 클립을 이 판으로 쓴다. 컨트롤러를 다시 만들지 않고 바꾸려면 Tools/HybridShooter/Fix Loop Clips
    /// </summary>
    public static class PlayerLoopClips
    {
        public const string Folder = "Assets/Scenes/MS/Animations/LoopClips";
        private const string ParentFolder = "Assets/Scenes/MS/Animations";
        private const string ControllerPath = "Assets/Scenes/MS/Animations/Player_AC.controller";
        public const string LoopSuffix = "_Loop";
        /// <summary>원본 FBX 가 있는 곳 (빌더의 ClipFolder 아래 FBX motions)</summary>
        private const string SourceFolder = "Assets/ExternalAssets/PROTOFACTOR/Ultimate Animation Collection/Animations/Assault Rifle Animset/FBX motions";

        /// <summary>근육이 아닌 휴머노이드 커브 (루트·IK 목표). 반복 구간을 고를 때는 근육만 본다</summary>
        private static readonly System.Text.RegularExpressions.Regex NonMuscle =
            new System.Text.RegularExpressions.Regex(@"^(Root|Motion|LeftHand|RightHand|LeftFoot|RightFoot)[TQ]\.");
        /// <summary>반복 구간 끝을 찾는 범위: 원본 길이의 이 비율 ~ 원본 끝</summary>
        private const float MinCycleRatio = 0.6f;

        /// <summary>source 의 이음새를 고친 판 (없으면 만들고, 있으면 다시 쓴다). 반복 클립이 아니면 source 그대로</summary>
        public static AnimationClip MakeSeamless(AnimationClip source) => MakeSeamless(source, out _);

        /// <summary>
        /// report = "원본 N → M 프레임, 이음새 비용 a → b" (비용 = 근육별 |끝 자세 − 처음 자세| + |끝 한 프레임 변화 − 처음 한 프레임 변화| 합)
        /// </summary>
        public static AnimationClip MakeSeamless(AnimationClip source, out string report)
        {
            report = null;
            if (source == null) return null;
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            if (settings.loopTime == false) return source;

            float rate = source.frameRate > 0f ? source.frameRate : 30f;
            float dt = 1f / rate;
            int frames = Mathf.RoundToInt(source.length * rate);
            if (frames < 4) return source;

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(source);
            var samples = new float[bindings.Length][];
            var muscles = new List<int>();
            for (int i = 0; i < bindings.Length; i++)
            {
                AnimationCurve curve = AnimationUtility.GetEditorCurve(source, bindings[i]);
                samples[i] = new float[frames + 1];
                for (int f = 0; f <= frames; f++) samples[i][f] = curve != null ? curve.Evaluate(f * dt) : 0f;
                if (bindings[i].type == typeof(Animator) && NonMuscle.IsMatch(bindings[i].propertyName) == false) muscles.Add(i);
            }

            int end = FindLoopEnd(samples, muscles, frames, out float costBefore, out float costAfter);

            EnsureFolder();
            string path = $"{Folder}/{source.name}{LoopSuffix}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = rate;

            int smoothPasses = UpperBodySmoothPasses(source);
            var values = new float[bindings.Length][];
            for (int i = 0; i < bindings.Length; i++)
            {
                string property = bindings[i].propertyName;
                bool animator = bindings[i].type == typeof(Animator);
                int passes = 0;
                bool keepRange = true;
                if (animator && UpperBodyMuscle.IsMatch(property)) passes = smoothPasses;
                else if (animator && property.StartsWith("RootT.")) { passes = smoothPasses; keepRange = false; } // 튀던 몸 중심 흔들림 자체를 줄인다
                else if (animator && property.StartsWith("RootQ.")) passes = smoothPasses;                       // 몸통 비틀림 크기는 유지
                values[i] = PeriodicValues(samples[i], end, dt, passes, keepRange);
            }
            if (smoothPasses > 0) NormalizeRootRotation(bindings, values, end);

            var curves = new AnimationCurve[bindings.Length];
            for (int i = 0; i < bindings.Length; i++) curves[i] = PeriodicCurve(values[i], end, dt);
            AnimationUtility.SetEditorCurves(clip, bindings, curves);

            settings.loopTime = true;
            settings.loopBlend = false;
            settings.startTime = 0f;
            settings.stopTime = end * dt;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            report = $"{source.name}: {frames} → {end} 프레임, 이음새 비용 {costBefore:0.00} → {costAfter:0.00}, 이음새 앞뒤 {SeamHalfWidth(end)}프레임 다시 이음" +
                     (smoothPasses > 0 ? $", 상체 근육·몸 회전 평활화 {smoothPasses}회(진폭 유지), 몸 중심 위치 평활화 {smoothPasses}회" : "");
            return clip;
        }

        /// <summary>
        /// 처음(0 프레임)과 자세·속도가 가장 잘 맞는 끝 프레임. 시작은 0 으로 둔다 (블렌드트리에서 걷기·달리기 클립의 발 위상이 맞아 있어야 하므로).
        /// 원본 끝 프레임은 걸음 주기와 맞지 않아 이음새에서 근육이 올라가다 바로 내려가는 꺾임이 생겼다
        /// </summary>
        private static int FindLoopEnd(float[][] samples, List<int> muscles, int frames, out float costBefore, out float costAfter)
        {
            costBefore = LoopCost(samples, muscles, frames);
            int best = frames;
            float bestCost = costBefore;
            int min = Mathf.Max(3, Mathf.CeilToInt(frames * MinCycleRatio));
            for (int e = frames - 1; e >= min; e--)
            {
                float cost = LoopCost(samples, muscles, e);
                if (cost < bestCost - 1e-5f)
                {
                    bestCost = cost;
                    best = e;
                }
            }
            costAfter = bestCost;
            return best;
        }

        private static float LoopCost(float[][] samples, List<int> muscles, int end)
        {
            float cost = 0f;
            foreach (int m in muscles)
            {
                float[] s = samples[m];
                float pose = Mathf.Abs(s[end] - s[0]);
                float velocity = Mathf.Abs((s[end] - s[end - 1]) - (s[1] - s[0]));
                cost += pose + velocity;
            }
            return cost;
        }

        /// <summary>
        /// 0 ~ end 프레임으로 반복하는 값. 남은 자세 차이는 시간 비율로 나눠 빼고(끝 = 처음), 이음새를 다시 잇고, 필요하면 고르게 한다
        /// </summary>
        private static float[] PeriodicValues(float[] source, int end, float dt, int smoothPasses, bool keepRange)
        {
            var v = new float[end + 1];
            float drift = source[end] - source[0];
            for (int f = 0; f <= end; f++) v[f] = source[f] - drift * f / end;
            v[end] = v[0];
            SmoothSeam(v, end, SeamHalfWidth(end), dt);
            SmoothPeriodic(v, end, smoothPasses, keepRange);
            return v;
        }

        /// <summary>RootQ 네 성분을 따로 고르게 하면 길이가 1 에서 조금 벗어난다 → 프레임마다 다시 단위 쿼터니언으로</summary>
        private static void NormalizeRootRotation(EditorCurveBinding[] bindings, float[][] values, int end)
        {
            int[] q = new int[4];
            for (int c = 0; c < 4; c++)
            {
                string name = "RootQ." + "xyzw"[c];
                q[c] = System.Array.FindIndex(bindings, b => b.type == typeof(Animator) && b.propertyName == name);
                if (q[c] < 0) return;
            }
            for (int f = 0; f <= end; f++)
            {
                float x = values[q[0]][f], y = values[q[1]][f], z = values[q[2]][f], w = values[q[3]][f];
                float length = Mathf.Sqrt(x * x + y * y + z * z + w * w);
                if (length < 1e-6f) continue;
                for (int c = 0; c < 4; c++) values[q[c]][f] /= length;
            }
        }

        /// <summary>반복 값으로 커브를 만든다. 모든 키 기울기를 앞뒤 키 중앙 차분으로 (이음새는 끝 바로 앞 · 처음 바로 뒤 키를 써서 처음·끝이 같은 기울기)</summary>
        private static AnimationCurve PeriodicCurve(float[] v, int end, float dt)
        {
            var keys = new Keyframe[end + 1];
            for (int f = 0; f <= end; f++)
            {
                float prev = f == 0 ? v[end - 1] : v[f - 1];
                float next = f == end ? v[1] : v[f + 1];
                float slope = (next - prev) / (2f * dt);
                keys[f] = new Keyframe(f * dt, v[f], slope, slope);
            }
            return new AnimationCurve(keys);
        }

        /// <summary>상체 근육 (척추·가슴·목·머리·어깨·팔·손목). 다리는 발 디딤이 무뎌지지 않게 평활화하지 않는다</summary>
        private static readonly System.Text.RegularExpressions.Regex UpperBodyMuscle = new System.Text.RegularExpressions.Regex(
            @"^(Spine|Chest|UpperChest|Neck|Head|Left Shoulder|Right Shoulder|Left Arm|Right Arm|Left Forearm|Right Forearm|Left Hand|Right Hand) ");

        /// <summary>
        /// 상체 근육·몸 중심(RootT·RootQ) 평활화 횟수. 전력질주 클립은 한 바퀴가 12 프레임(0.4초)뿐이라 키 사이가 거칠다
        /// (측정: 남은 튐의 약 80% 가 전력질주, 144fps 에서 더 심함).
        ///  - 상체 근육: 2·4 프레임에서 30여 개가 한꺼번에 꺾임 → 2회로 꺾임 약 −40%, 진폭 유지. 이것만으로는 측정 튐이 그대로였다
        ///  - 몸 중심: 3→4 프레임에 높이 2.7cm 뚝 떨어지고 앞뒤로 5~8cm 튀었다 (골반도 가슴만큼 튐 → 튐의 실제 원인).
        ///    위치는 진폭을 되돌리지 않는다 (흔들림 자체가 결함, 꺾임 약 −70~77%), 회전은 비틀림 크기 유지 후 단위 쿼터니언으로
        /// </summary>
        public static int UpperBodySmoothPasses(AnimationClip source) =>
            source != null && source.name.StartsWith("Sprint") ? 2 : 0;

        /// <summary>
        /// 반복 커브를 [1, 2, 1]/4 로 passes 번 고르게 한 뒤, 평균 둘레로 늘려 원래 진폭(최대 − 최소)을 되돌린다.
        /// 날카로운 꺾임(높은 주파수)만 줄고 팔 흔들림 크기는 유지된다. v[end] = v[0] (반복)
        /// </summary>
        private static void SmoothPeriodic(float[] v, int end, int passes, bool keepRange = true)
        {
            if (passes <= 0 || end < 4) return;
            float max0 = float.MinValue, min0 = float.MaxValue;
            for (int f = 0; f < end; f++) { max0 = Mathf.Max(max0, v[f]); min0 = Mathf.Min(min0, v[f]); }

            var next = new float[end];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int f = 0; f < end; f++)
                {
                    float prev = v[(f - 1 + end) % end];
                    float after = v[(f + 1) % end];
                    next[f] = (prev + 2f * v[f] + after) * 0.25f;
                }
                for (int f = 0; f < end; f++) v[f] = next[f];
            }
            if (keepRange == false)
            {
                v[end] = v[0];
                return;
            }

            float mean = 0f, max1 = float.MinValue, min1 = float.MaxValue;
            for (int f = 0; f < end; f++) { mean += v[f]; max1 = Mathf.Max(max1, v[f]); min1 = Mathf.Min(min1, v[f]); }
            mean /= end;
            float gain = max1 - min1 > 1e-6f ? (max0 - min0) / (max1 - min1) : 1f;
            for (int f = 0; f < end; f++) v[f] = mean + (v[f] - mean) * gain;
            v[end] = v[0];
        }

        /// <summary>이음새 앞뒤로 다시 잇는 프레임 수 (한쪽). 짧은 클립일수록 좁게 (주기의 약 10%)</summary>
        public static int SeamHalfWidth(int end) => end >= 30 ? 3 : end >= 16 ? 2 : 1;

        /// <summary>
        /// 이음새(0 프레임 = end 프레임) 앞뒤 half 프레임 안쪽을, 바깥 두 프레임(-half, +half)의 값·기울기를 잇는 Hermite 곡선으로 바꾼다.
        /// 원본 0 프레임이 뾰족한 꼭짓점이라(직전까지 올라가다 직후 바로 내려감) 끝 프레임을 어디로 잡아도 꺾임이 남았다 (측정: 반복 구간을 다시 골라도 튐 그대로)
        /// </summary>
        private static void SmoothSeam(float[] v, int end, int half, float dt)
        {
            if (half < 1 || end < half * 2 + 3) return;
            float P(int f) => v[((f % end) + end) % end];
            float va = P(-half), vb = P(half);
            float sa = (P(-half) - P(-half - 1)) / dt; // 바깥쪽 한 프레임 기울기 (안쪽은 바꿀 값이라 쓰지 않는다)
            float sb = (P(half + 1) - P(half)) / dt;
            float span = 2 * half * dt;
            for (int f = -half + 1; f <= half - 1; f++)
            {
                float t = (f + half) / (2f * half);
                float t2 = t * t, t3 = t2 * t;
                float value = (2f * t3 - 3f * t2 + 1f) * va + (t3 - 2f * t2 + t) * span * sa
                            + (-2f * t3 + 3f * t2) * vb + (t3 - t2) * span * sb;
                v[((f % end) + end) % end] = value;
            }
            v[end] = v[0];
        }

        /// <summary>clips(키 → 클립) 중 keys 에 해당하는 반복 클립을 이음새 고친 판으로 바꾼다 (빌더용). 바꾼 개수</summary>
        public static int ReplaceInDictionary(Dictionary<string, AnimationClip> clips, IEnumerable<string> keys, List<string> reports = null)
        {
            int count = 0;
            foreach (string key in keys)
            {
                if (clips.TryGetValue(key, out AnimationClip source) == false || source == null) continue;
                if (IsLoopVersion(source)) continue;
                AnimationClip fixedClip = MakeSeamless(source, out string report);
                if (fixedClip == source) continue;
                clips[key] = fixedClip;
                reports?.Add(report);
                count++;
            }
            AssetDatabase.SaveAssets();
            return count;
        }

        public static bool IsLoopVersion(AnimationClip clip) =>
            clip != null && AssetDatabase.GetAssetPath(clip).StartsWith(Folder + "/");

        /// <summary>
        /// 지금 Player_AC 에서 이동·전력질주·대기 상태(Base Layer 의 Locomotion·Sprint, Upper Pose 의 Pose)가 쓰는 반복 클립을 이음새 고친 판으로 바꾼다.
        /// 컨트롤러를 다시 만들지 않으므로 씬·프리팹 연결과 다른 설정은 그대로
        /// </summary>
        [MenuItem("Tools/HybridShooter/Fix Loop Clips (이음새)")]
        private static void FixControllerMenu()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("반복 클립 이음새", "플레이 모드를 끄고 실행하세요.", "확인");
                return;
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                EditorUtility.DisplayDialog("반복 클립 이음새", $"애니메이터 컨트롤러가 없습니다:\n{ControllerPath}", "확인");
                return;
            }

            var replaced = new Dictionary<string, string>();
            var notFound = new List<string>();
            foreach (AnimatorControllerLayer layer in controller.layers)
            {
                foreach (ChildAnimatorState child in layer.stateMachine.states)
                {
                    string state = child.state.name;
                    bool target = state == PlayerAnimNames.States.Locomotion || state == PlayerAnimNames.States.Sprint || state == PlayerAnimNames.States.Pose;
                    if (target == false) continue;
                    child.state.motion = Replace(child.state.motion, replaced, notFound);
                    EditorUtility.SetDirty(child.state);
                }
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PlayerLoopClips] 이음새 고친 클립으로 교체 {replaced.Count}개 ({Folder})\n - " + string.Join("\n - ", replaced.Values), controller);
            if (notFound.Count > 0)
                Debug.LogWarning("[PlayerLoopClips] 원본 FBX 클립을 찾지 못해 이전 판을 그대로 둔 클립:\n - " + string.Join("\n - ", notFound.Distinct()), controller);
        }

        private static Motion Replace(Motion motion, Dictionary<string, string> replaced, List<string> notFound)
        {
            if (motion is AnimationClip clip)
            {
                // 이미 바꾼 판이면 원본을 찾아 다시 만든다 (만드는 방식이 바뀌었을 때도 메뉴 한 번으로 갱신)
                AnimationClip source = IsLoopVersion(clip) ? FindSource(clip) : clip;
                if (source == null)
                {
                    notFound.Add(clip.name);
                    return clip;
                }
                if (replaced.ContainsKey(source.name)) return AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Folder}/{source.name}{LoopSuffix}.anim");
                AnimationClip fixedClip = MakeSeamless(source, out string report);
                if (fixedClip != source) replaced[source.name] = report;
                return fixedClip;
            }
            if (motion is BlendTree tree)
            {
                ChildMotion[] children = tree.children;
                for (int i = 0; i < children.Length; i++) children[i].motion = Replace(children[i].motion, replaced, notFound);
                tree.children = children;
                EditorUtility.SetDirty(tree);
            }
            return motion;
        }

        /// <summary>_Loop 판의 원본 FBX 클립. 라이플 동작 폴더의 Humanoid@이름.fbx 를 먼저 보고, 없으면 프로젝트에서 같은 이름의 클립을 찾는다</summary>
        private static AnimationClip FindSource(AnimationClip loopClip)
        {
            string name = loopClip.name.EndsWith(LoopSuffix) ? loopClip.name.Substring(0, loopClip.name.Length - LoopSuffix.Length) : loopClip.name;
            foreach (string ext in new[] { "fbx", "FBX" })
            {
                AnimationClip direct = AssetDatabase.LoadAllAssetsAtPath($"{SourceFolder}/Humanoid@{name}.{ext}").OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == name);
                if (direct != null) return direct;
            }
            foreach (string guid in AssetDatabase.FindAssets(name))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(Folder + "/")) continue;
                AnimationClip found = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == name);
                if (found != null) return found;
            }
            return null;
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(Folder)) return;
            AssetDatabase.CreateFolder(ParentFolder, Folder.Substring(ParentFolder.Length + 1));
        }
    }
}
