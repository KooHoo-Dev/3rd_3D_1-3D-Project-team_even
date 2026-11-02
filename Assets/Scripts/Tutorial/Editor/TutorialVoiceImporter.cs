using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Tutorial.Editor
{
    /// <summary>
    /// 튜토리얼 음성 가져오기. 메뉴: Tools/Tutorial/Import Voice Recording
    /// TTS 로 대사본(Tutorial_VoiceScript_TTS.txt, 한 줄 = 대사 하나)을 한 번에 읽힌 녹음 파일 하나를
    ///  1) 대사 수(대사본 TSV 줄 수)만큼 나눈다. 대사 사이 무음과 대사 안의 문장 사이 쉼이 비슷한 길이라 무음 길이만으로는 못 가른다 →
    ///     무음 후보 중에서 "조각 길이가 글자 수로 예상한 길이에 맞고, 경계 무음이 길수록 좋은" 경계 조합을 동적 계획법으로 고른다
    ///  2) 음높이는 그대로 두고 SpeedUp 배로 빠르게 한다 (WSOLA)
    ///  3) 순서대로 대사 Id 이름의 wav 로 Resources/Audio/Voice/Tutorial/ 에 저장 (임포트 규칙 AudioImportRules 가 자동 적용)
    ///  4) SoundTable.tsv 에 대사 Id = 키로 등록한다 (vo_tut_ 행을 새로 씀). TutorialHud 가 이 키로 재생한다
    /// 콘솔에 대사마다 녹음 속 위치와 길이(예상 대비 비율)를 남긴다 — 비율이 크게 벗어난 줄은 짝이 어긋났을 수 있다.
    /// 원본 녹음은 SourcePath 에 둔다 (Resources 밖 → 빌드에 들어가지 않음). 녹음을 다시 만들면 파일만 바꾸고 메뉴를 다시 실행
    /// </summary>
    public static class TutorialVoiceImporter
    {
        private const string SourcePath = "Assets/Audio/Voice/Tutorial/Source/Tutorial_Voice.mp3";
        private const string OutputFolder = "Assets/Resources/Audio/Voice/Tutorial";
        private const string ResourcesPrefix = "Audio/Voice/Tutorial/";
        private const string VoiceScriptTsv = "Docs/Design/Tutorial_VoiceScript.tsv";
        private const string SoundTablePath = "Assets/StreamingAssets/Tables/SoundTable.tsv";
        private const string KeyPrefix = "vo_tut_";

        /// <summary>재생 속도 배율 (음높이 유지). 1 = 원래 속도</summary>
        private const float SpeedUp = 1.15f;

        /// <summary>음량을 재는 창 (초)</summary>
        private const float WindowSeconds = 0.02f;
        /// <summary>무음 판정: 최대 음량 대비 이 dB 아래</summary>
        private const float SilenceDb = -38f;
        /// <summary>경계 후보로 보는 최소 무음 길이 (초)</summary>
        private const float MinSilenceSeconds = 0.12f;
        /// <summary>경계 무음이 길수록 주는 가산점 (길이 맞춤 오차와 저울질)</summary>
        private const float GapWeight = 0.35f;
        /// <summary>조각 앞뒤에 남기는 여유 (초). 경계 무음 가운데를 넘지 않는다</summary>
        private const float PadBefore = 0.06f, PadAfter = 0.2f;

        [MenuItem("Tools/Tutorial/Import Voice Recording")]
        private static void Import()
        {
            var lines = ReadLines();
            if (lines.Count == 0)
            {
                Debug.LogError($"[TutorialVoiceImporter] 대사본 {VoiceScriptTsv} 에서 대사를 읽지 못했습니다.");
                return;
            }
            if (TryLoadSamples(out var samples, out int sampleRate) == false) return;

            var weights = new float[lines.Count];
            for (int i = 0; i < lines.Count; i++) weights[i] = SpokenLength(lines[i].tts);
            if (TrySplit(samples, sampleRate, weights, out var segments, out var ratios) == false) return;

            Directory.CreateDirectory(OutputFolder);
            var log = new StringBuilder($"[TutorialVoiceImporter] {lines.Count}줄 저장 · {SpeedUp:0.00}배속 (비율 = 원래 길이 ÷ 글자 수 예상, 0.6 미만·1.6 초과는 ⚠)\n");
            for (int i = 0; i < lines.Count; i++)
            {
                var (start, end) = segments[i];
                var clip = Stretch(samples, start, end, sampleRate, SpeedUp);
                WriteWav($"{OutputFolder}/{lines[i].id}.wav", clip, sampleRate);
                string warn = ratios[i] < 0.6f || ratios[i] > 1.6f ? " ⚠" : "";
                log.AppendLine($"{i + 1:00}. {lines[i].id}  {start / (float)sampleRate:0.00}s~{end / (float)sampleRate:0.00}s → {clip.Length / (float)sampleRate:0.00}s  비율 {ratios[i]:0.00}{warn}  {lines[i].tts}");
            }
            WriteSoundTable(lines);
            AssetDatabase.Refresh();
            Debug.Log(log.ToString());
        }

        #region 대사본
        /// <summary>대사본 TSV (대사 Id · 단계 · 나오는 때 · 자막 · TTS 입력). TTS 입력이 비면 자막. 순서 = TTS 입력 순서</summary>
        private static List<(string id, string tts)> ReadLines()
        {
            var lines = new List<(string, string)>();
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, VoiceScriptTsv);
            if (File.Exists(path) == false) return lines;
            foreach (string row in File.ReadAllLines(path))
            {
                string[] cols = row.Split('\t');
                if (cols.Length < 4 || cols[0].Trim().StartsWith(KeyPrefix) == false) continue;
                string tts = cols.Length > 4 && string.IsNullOrWhiteSpace(cols[4]) == false ? cols[4].Trim() : cols[3].Trim();
                lines.Add((cols[0].Trim(), tts));
            }
            return lines;
        }

        /// <summary>말하는 길이의 대리값: 글자 수 (공백 제외) + 문장 부호마다 쉼 몇 글자 몫</summary>
        private static float SpokenLength(string text)
        {
            float length = 0f;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c)) continue;
                length += c == '.' || c == ',' || c == '?' || c == '!' ? 1.5f : 1f;
            }
            return Mathf.Max(1f, length);
        }
        #endregion

        #region 녹음 읽기
        /// <summary>원본 녹음을 모노 PCM 으로 읽는다 (GetData 가 되도록 임포트 설정을 바꿔 다시 임포트)</summary>
        private static bool TryLoadSamples(out float[] samples, out int sampleRate)
        {
            samples = null;
            sampleRate = 0;
            if (AssetImporter.GetAtPath(SourcePath) is not AudioImporter importer)
            {
                Debug.LogError($"[TutorialVoiceImporter] 녹음 파일이 없습니다: {SourcePath}");
                return false;
            }
            var settings = importer.defaultSampleSettings;
            if (importer.forceToMono == false || settings.loadType != AudioClipLoadType.DecompressOnLoad || settings.preloadAudioData == false)
            {
                importer.forceToMono = true;
                settings.loadType = AudioClipLoadType.DecompressOnLoad;
                settings.preloadAudioData = true;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SourcePath);
            if (clip == null || clip.LoadAudioData() == false)
            {
                Debug.LogError($"[TutorialVoiceImporter] 녹음을 읽지 못했습니다: {SourcePath}");
                return false;
            }
            samples = new float[clip.samples * clip.channels];
            if (clip.GetData(samples, 0) == false)
            {
                Debug.LogError("[TutorialVoiceImporter] 오디오 데이터를 꺼내지 못했습니다 (임포트 설정 확인).");
                return false;
            }
            if (clip.channels > 1) samples = DownMix(samples, clip.channels);
            sampleRate = clip.frequency;
            return true;
        }

        private static float[] DownMix(float[] interleaved, int channels)
        {
            var mono = new float[interleaved.Length / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }
        #endregion

        #region 나누기
        /// <summary>
        /// 무음 후보(창 단위 [시작, 끝)) 중 weights.Length - 1 개를 경계로 골라 조각을 만든다.
        /// 비용 = Σ (조각 길이 − 예상 길이)² / 예상² − GapWeight × Σ 경계 무음 길이(중앙값 기준, 3배까지).
        /// 예상 길이 = 글자 수 × 초당 글자 비율 — 첫 결과로 비율을 다시 재서 한 번 더 푼다
        /// </summary>
        private static bool TrySplit(float[] samples, int sampleRate, float[] weights, out List<(int start, int end)> segments, out float[] ratios)
        {
            segments = null;
            ratios = null;
            int window = Mathf.Max(1, Mathf.RoundToInt(sampleRate * WindowSeconds));
            var rms = new float[samples.Length / window];
            float peak = 0f;
            for (int w = 0; w < rms.Length; w++)
            {
                double sum = 0;
                for (int i = w * window; i < (w + 1) * window; i++) sum += samples[i] * samples[i];
                rms[w] = Mathf.Sqrt((float)(sum / window));
                peak = Mathf.Max(peak, rms[w]);
            }
            float threshold = peak * Mathf.Pow(10f, SilenceDb / 20f);

            int first = System.Array.FindIndex(rms, r => r >= threshold);
            int last = System.Array.FindLastIndex(rms, r => r >= threshold) + 1;
            var gaps = FindSilences(rms, threshold, first, last, Mathf.RoundToInt(MinSilenceSeconds / WindowSeconds));
            int n = weights.Length;
            if (first < 0 || gaps.Count < n - 1)
            {
                Debug.LogError($"[TutorialVoiceImporter] 무음 경계 후보({gaps.Count})가 대사 수 - 1({n - 1})보다 적습니다. 녹음 파일을 확인하세요.");
                return false;
            }

            float totalWeight = 0f;
            foreach (float w in weights) totalWeight += w;
            float perWeight = (last - first) / totalWeight; // 창 수 / 글자
            int[] cuts = null;
            for (int pass = 0; pass < 2; pass++)
            {
                cuts = SolveCuts(gaps, first, last, weights, perWeight);
                // 실제 고른 조각 길이로 비율을 다시 잰다 (경계 무음을 뺀 순수 길이)
                float spoken = 0f;
                for (int i = 0; i < n; i++) spoken += SegmentEnd(i) - SegmentStart(i);
                perWeight = spoken / totalWeight;
            }

            segments = new List<(int, int)>();
            ratios = new float[n];
            int padBefore = Mathf.RoundToInt(PadBefore * sampleRate), padAfter = Mathf.RoundToInt(PadAfter * sampleRate);
            for (int i = 0; i < n; i++)
            {
                int start = SegmentStart(i) * window, end = SegmentEnd(i) * window;
                int prevMid = i > 0 ? (gaps[cuts[i - 1]].start + gaps[cuts[i - 1]].end) * window / 2 : 0;
                int nextMid = i < n - 1 ? (gaps[cuts[i]].start + gaps[cuts[i]].end) * window / 2 : samples.Length;
                segments.Add((Mathf.Max(prevMid, start - padBefore), Mathf.Min(nextMid, end + padAfter)));
                ratios[i] = (SegmentEnd(i) - SegmentStart(i)) / (weights[i] * perWeight);
            }
            return true;

            int SegmentStart(int i) => i == 0 ? first : gaps[cuts[i - 1]].end;
            int SegmentEnd(int i) => i == n - 1 ? last : gaps[cuts[i]].start;
        }

        /// <summary>first ~ last 안의 무음 구간 (minLength 창 이상)</summary>
        private static List<(int start, int end)> FindSilences(float[] rms, float threshold, int first, int last, int minLength)
        {
            var gaps = new List<(int, int)>();
            int runStart = -1;
            for (int w = first; w < last; w++)
            {
                bool quiet = rms[w] < threshold;
                if (quiet && runStart < 0) runStart = w;
                else if (quiet == false && runStart >= 0)
                {
                    if (w - runStart >= minLength) gaps.Add((runStart, w));
                    runStart = -1;
                }
            }
            return gaps;
        }

        /// <summary>동적 계획법: cost[i, j] = 대사 0..i 를 넣고 i 번째 대사가 후보 j 에서 끝날 때 최소 비용. 반환 = 경계로 고른 후보 번호 (n - 1 개)</summary>
        private static int[] SolveCuts(List<(int start, int end)> gaps, int first, int last, float[] weights, float perWeight)
        {
            int n = weights.Length, m = gaps.Count;
            var lengths = new List<int>();
            foreach (var g in gaps) lengths.Add(g.end - g.start);
            lengths.Sort();
            float medianGap = Mathf.Max(1f, lengths[lengths.Count / 2]);

            float GapScore(int j) => GapWeight * Mathf.Min((gaps[j].end - gaps[j].start) / medianGap, 3f);
            float SegmentCost(int i, int from, int to)
            {
                float expected = weights[i] * perWeight;
                float error = (to - from - expected) / expected;
                return error * error;
            }

            var cost = new float[n - 1, m];
            var back = new int[n - 1, m];
            for (int i = 0; i < n - 1; i++)
                for (int j = 0; j < m; j++)
                {
                    cost[i, j] = float.PositiveInfinity;
                    if (j < i || j > m - (n - 1 - i)) continue; // 앞뒤 대사가 쓸 후보가 남아야 한다
                    if (i == 0)
                    {
                        cost[i, j] = SegmentCost(0, first, gaps[j].start) - GapScore(j);
                        continue;
                    }
                    for (int k = i - 1; k < j; k++)
                    {
                        if (float.IsPositiveInfinity(cost[i - 1, k])) continue;
                        float c = cost[i - 1, k] + SegmentCost(i, gaps[k].end, gaps[j].start) - GapScore(j);
                        if (c < cost[i, j])
                        {
                            cost[i, j] = c;
                            back[i, j] = k;
                        }
                    }
                }

            // 마지막 대사: 마지막 경계 뒤 ~ 녹음 끝
            int bestJ = -1;
            float best = float.PositiveInfinity;
            for (int j = n - 2; j < m; j++)
            {
                if (float.IsPositiveInfinity(cost[n - 2, j])) continue;
                float c = cost[n - 2, j] + SegmentCost(n - 1, gaps[j].end, last);
                if (c < best)
                {
                    best = c;
                    bestJ = j;
                }
            }

            var cuts = new int[n - 1];
            for (int i = n - 2, j = bestJ; i >= 0; i--)
            {
                cuts[i] = j;
                j = back[i, j];
            }
            return cuts;
        }
        #endregion

        #region 배속 · 저장
        /// <summary>
        /// 음높이를 유지한 채 speed 배로 빠르게 (WSOLA: 겹치는 짧은 조각을 비슷한 모양끼리 이어 붙인다).
        /// 조각 30ms · 겹침 절반 · 이어 붙일 자리를 ±10ms 안에서 상관도로 찾는다 (계산량 때문에 4 샘플 간격으로 비교)
        /// </summary>
        private static float[] Stretch(float[] samples, int start, int end, int sampleRate, float speed)
        {
            int length = end - start;
            if (speed <= 1.001f || length < sampleRate / 10)
            {
                var copy = new float[length];
                System.Array.Copy(samples, start, copy, 0, length);
                return copy;
            }

            int frame = Mathf.RoundToInt(sampleRate * 0.03f) & ~1;
            int synthHop = frame / 2;
            float analysisHop = synthHop * speed;
            int tolerance = Mathf.RoundToInt(sampleRate * 0.01f);
            int outLength = Mathf.CeilToInt(length / speed) + frame;
            var output = new float[outLength];
            var norm = new float[outLength];
            var hann = new float[frame];
            for (int i = 0; i < frame; i++) hann[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (frame - 1));

            float Sample(int i) => i >= 0 && i < length ? samples[start + i] : 0f;

            int prevPos = 0;
            for (int k = 0; ; k++)
            {
                int outPos = k * synthHop;
                if (outPos + frame > outLength) break;
                int ideal = Mathf.RoundToInt(k * analysisHop);
                if (ideal >= length) break;

                int pos = ideal;
                if (k > 0)
                {
                    // 앞 조각이 자연스럽게 이어졌을 자리(prevPos + synthHop)와 가장 닮은 자리
                    int natural = prevPos + synthHop;
                    float bestScore = float.NegativeInfinity;
                    for (int offset = -tolerance; offset <= tolerance; offset += 2)
                    {
                        int candidate = ideal + offset;
                        float score = 0f;
                        for (int i = 0; i < synthHop; i += 4) score += Sample(natural + i) * Sample(candidate + i);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            pos = candidate;
                        }
                    }
                }

                for (int i = 0; i < frame; i++)
                {
                    output[outPos + i] += Sample(pos + i) * hann[i];
                    norm[outPos + i] += hann[i];
                }
                prevPos = pos;
            }

            int resultLength = Mathf.Min(outLength, Mathf.CeilToInt(length / speed));
            var result = new float[resultLength];
            for (int i = 0; i < resultLength; i++) result[i] = norm[i] > 0.0001f ? output[i] / norm[i] : 0f;
            return result;
        }

        /// <summary>16비트 모노 PCM wav. 앞뒤 10ms 는 서서히 키우고 줄여 딸깍 소리를 막는다</summary>
        private static void WriteWav(string path, float[] samples, int sampleRate)
        {
            int count = samples.Length;
            int fade = Mathf.Min(count / 4, Mathf.RoundToInt(sampleRate * 0.01f));
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + count * 2);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);       // PCM
            writer.Write((short)1);       // 모노
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2); // 초당 바이트
            writer.Write((short)2);       // 블록 정렬
            writer.Write((short)16);      // 비트
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(count * 2);
            for (int i = 0; i < count; i++)
            {
                float gain = i < fade ? i / (float)fade : i >= count - fade ? (count - 1 - i) / (float)fade : 1f;
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * gain * 32767f), short.MinValue, short.MaxValue));
            }
        }

        /// <summary>SoundTable 의 vo_tut_ 행을 지우고 대사 순서대로 다시 쓴다 (Category Ui · 2D · 같은 키 1개)</summary>
        private static void WriteSoundTable(List<(string id, string tts)> lines)
        {
            var rows = new List<string>(File.ReadAllLines(SoundTablePath));
            rows.RemoveAll(r => r.StartsWith(KeyPrefix) || r.StartsWith("# 튜토리얼 음성"));
            rows.Add("# 튜토리얼 음성 (오퍼레이터) — Tools/Tutorial/Import Voice Recording 이 녹음 파일을 잘라 다시 쓴다. 키 = 대사본 대사 Id");
            foreach (var (id, _) in lines)
                rows.Add($"{id}\tUi\t{ResourcesPrefix}{id}\t1.0\tfalse\tfalse\t1\t0\t0\t90\t0");
            File.WriteAllLines(SoundTablePath, rows, new UTF8Encoding(false));
        }
        #endregion
    }
}
