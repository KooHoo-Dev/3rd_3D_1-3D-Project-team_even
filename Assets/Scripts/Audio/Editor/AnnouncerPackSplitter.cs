using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Audio.Editor
{
    /// <summary>
    /// Tools/Audio/Announcer Pack. 여러 대사가 이어 붙은 아나운서 팩(mp3)을 대사 단위 wav 로 나누고, 고른 대사를 보급 안내음으로 지정한다.
    ///
    ///   1) "1. Split Into Candidates" : 무음 구간을 기준으로 팩을 나눠 WorkFolder 에 Seg_번호_시작-끝.wav 로 저장한다 (Resources 밖이라 빌드에 안 들어감)
    ///   2) Project 창에서 후보를 들어 보고, 쓸 wav 를 선택한 채 "2. Use Selected As Supply Announce"
    ///      → Resources/Audio/Announce/Announce_SupplyDrop.wav 로 복사 (SoundTable sfx_supply_announce 가 이 경로를 쓴다)
    ///   3) 지정이 끝나면 WorkFolder 는 지워도 된다
    ///
    /// 원본 클립은 Decompress On Load 여야 AudioClip.GetData 로 샘플을 읽을 수 있다 (팩 meta 의 loadType 0 = Decompress On Load).
    /// </summary>
    internal static class AnnouncerPackSplitter
    {
        private const string SourcePath = "Assets/Resources/Audio/Announce/voicebosch-radio-soldier-announcer-pack-197724.mp3";
        private const string WorkFolder = "Assets/Audio_Work/AnnouncerPack";
        private const string TargetPath = "Assets/Resources/Audio/Announce/Announce_SupplyDrop.wav";

        private const float WindowSeconds = 0.02f;   // 음량(RMS) 측정 창
        private const float SilenceRatio = 0.06f;    // 가장 큰 창 음량 대비 이 비율 미만 = 무음
        private const float MinSilenceRatio = 0.01f; // 절대 하한 (거의 무음인 팩 대비)
        private const float MergeGapSeconds = 0.35f; // 이보다 짧은 무음은 같은 대사로 본다 (단어 사이 끊김)
        private const float MinLengthSeconds = 0.25f; // 이보다 짧은 소리는 잡음으로 버림
        private const float PadSeconds = 0.08f;      // 앞뒤 여유 (말 첫소리·끝 울림이 잘리지 않게)

        [MenuItem("Tools/Audio/Announcer Pack/1. Split Into Candidates")]
        private static void Split()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(SourcePath);
            if (clip == null)
            {
                Debug.LogError($"[AnnouncerPack] {SourcePath} 를 찾을 수 없습니다.");
                return;
            }
            if (clip.loadType != AudioClipLoadType.DecompressOnLoad)
            {
                Debug.LogError($"[AnnouncerPack] {SourcePath} 의 Load Type 을 Decompress On Load 로 바꾼 뒤 다시 실행하세요. (현재 {clip.loadType})");
                return;
            }

            float[] mono = ReadMono(clip);
            var segments = FindSegments(mono, clip.frequency);
            if (segments.Count == 0)
            {
                Debug.LogWarning("[AnnouncerPack] 소리가 있는 구간을 찾지 못했습니다.");
                return;
            }

            if (AssetDatabase.IsValidFolder(WorkFolder)) AssetDatabase.DeleteAsset(WorkFolder);
            Directory.CreateDirectory(WorkFolder);

            var log = new System.Text.StringBuilder($"[AnnouncerPack] 후보 {segments.Count}개 → {WorkFolder}\n");
            for (int i = 0; i < segments.Count; i++)
            {
                (int start, int end) = segments[i];
                float startSec = (float)start / clip.frequency;
                float endSec = (float)end / clip.frequency;
                string file = $"{WorkFolder}/Seg_{i + 1:00}_{startSec:0.00}s-{endSec:0.00}s.wav";
                WriteWav(file, mono, start, end - start, clip.frequency);
                log.AppendLine($"  {i + 1:00}: {startSec:0.00}s ~ {endSec:0.00}s ({endSec - startSec:0.00}s)");
            }
            AssetDatabase.Refresh();
            Debug.Log(log.ToString());
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(WorkFolder));
        }

        [MenuItem("Tools/Audio/Announcer Pack/2. Use Selected As Supply Announce")]
        private static void UseSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            File.Copy(path, TargetPath, true);
            AssetDatabase.ImportAsset(TargetPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[AnnouncerPack] {Path.GetFileName(path)} → {TargetPath} (SoundTable sfx_supply_announce)");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(TargetPath));
        }

        [MenuItem("Tools/Audio/Announcer Pack/2. Use Selected As Supply Announce", true)]
        private static bool CanUseSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return path.StartsWith(WorkFolder + "/") && path.EndsWith(".wav");
        }

        /// <summary>모든 채널을 평균해 모노 샘플로 읽는다</summary>
        private static float[] ReadMono(AudioClip clip)
        {
            var interleaved = new float[clip.samples * clip.channels];
            clip.GetData(interleaved, 0);
            if (clip.channels == 1) return interleaved;

            var mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                float sum = 0f;
                for (int c = 0; c < clip.channels; c++) sum += interleaved[i * clip.channels + c];
                mono[i] = sum / clip.channels;
            }
            return mono;
        }

        /// <summary>
        /// 소리가 있는 구간 (시작 샘플, 끝 샘플) 목록.
        /// 창마다 RMS 를 재서 기준 이상인 창을 이어 붙이고, 짧은 무음은 합치고, 짧은 소리는 버린 뒤 앞뒤로 여유를 준다.
        /// </summary>
        private static List<(int start, int end)> FindSegments(float[] samples, int frequency)
        {
            int window = Mathf.Max(1, Mathf.RoundToInt(frequency * WindowSeconds));
            int windowCount = samples.Length / window;
            var rms = new float[windowCount];
            float peak = 0f;
            for (int w = 0; w < windowCount; w++)
            {
                double sum = 0;
                for (int i = w * window; i < (w + 1) * window; i++) sum += samples[i] * samples[i];
                rms[w] = Mathf.Sqrt((float)(sum / window));
                peak = Mathf.Max(peak, rms[w]);
            }

            float threshold = Mathf.Max(MinSilenceRatio, peak * SilenceRatio);
            int mergeGap = Mathf.CeilToInt(MergeGapSeconds / WindowSeconds);
            var raw = new List<(int start, int end)>(); // 창 단위
            int runStart = -1, lastLoud = -1;
            for (int w = 0; w < windowCount; w++)
            {
                if (rms[w] < threshold) continue;
                if (runStart >= 0 && w - lastLoud > mergeGap)
                {
                    raw.Add((runStart, lastLoud + 1));
                    runStart = -1;
                }
                if (runStart < 0) runStart = w;
                lastLoud = w;
            }
            if (runStart >= 0) raw.Add((runStart, lastLoud + 1));

            int pad = Mathf.RoundToInt(frequency * PadSeconds);
            int minLength = Mathf.RoundToInt(frequency * MinLengthSeconds);
            var result = new List<(int start, int end)>();
            foreach (var (startWindow, endWindow) in raw)
            {
                int start = Mathf.Max(0, startWindow * window - pad);
                int end = Mathf.Min(samples.Length, endWindow * window + pad);
                if (end - start - pad * 2 >= minLength) result.Add((start, end));
            }
            return result;
        }

        /// <summary>모노 16bit PCM WAV 저장</summary>
        private static void WriteWav(string path, float[] samples, int offset, int count, int frequency)
        {
            using var writer = new BinaryWriter(File.Create(path));
            int dataBytes = count * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);                // fmt 청크 크기
            writer.Write((short)1);          // PCM
            writer.Write((short)1);          // 모노
            writer.Write(frequency);
            writer.Write(frequency * 2);     // byteRate
            writer.Write((short)2);          // blockAlign
            writer.Write((short)16);         // bitsPerSample
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);
            for (int i = offset; i < offset + count; i++)
                writer.Write((short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * short.MaxValue), short.MinValue, short.MaxValue));
        }
    }
}
