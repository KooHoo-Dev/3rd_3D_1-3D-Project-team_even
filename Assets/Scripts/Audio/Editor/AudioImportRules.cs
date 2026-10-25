using System.IO;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Audio.Editor
{
    /// <summary>
    /// Assets/Resources/Audio 아래 오디오의 임포트 설정 규칙. 새 파일이 들어오면 자동 적용, 기존 파일은 메뉴로 일괄 적용.
    /// (이미 있는 파일을 손으로 바꾼 설정은 메뉴를 다시 누르기 전까지 유지된다)
    ///
    ///   효과음 : Force To Mono (3D 방향감 + 메모리 절반), 3초 이하 Decompress On Load / 초과 Compressed In Memory
    ///   BGM    : 스테레오 유지, Streaming
    ///   공통   : Vorbis 70%, 48kHz 초과(96kHz 등)는 48kHz 로 낮춤
    /// </summary>
    internal sealed class AudioImportRules : AssetPostprocessor
    {
        private const string Root = "Assets/Resources/Audio/";
        private const string BgmRoot = Root + "Bgm/";
        private const float LongClipSeconds = 3f;
        private const int MaxSampleRate = 48000;

        /// <summary>메뉴로 재임포트할 때만 true → 기존 파일에도 규칙 적용</summary>
        private static bool forceApply;

        [MenuItem("Tools/Audio/Apply Import Rules")]
        private static void ApplyToAll()
        {
            var guids = AssetDatabase.FindAssets("t:AudioClip", new[] { Root.TrimEnd('/') });
            forceApply = true;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var guid in guids)
                    AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                forceApply = false;
            }
            Debug.Log($"[Audio] 임포트 규칙 적용: {guids.Length}개 ({Root})");
        }

        private void OnPreprocessAudio()
        {
            if (assetPath.StartsWith(Root) == false) return;
            var importer = (AudioImporter)assetImporter;
            if (forceApply == false && importer.importSettingsMissing == false) return; // 새 파일만 자동

            bool isBgm = assetPath.StartsWith(BgmRoot);
            TryReadWav(assetPath, out int sampleRate, out float seconds);

            importer.forceToMono = isBgm == false;
            importer.loadInBackground = isBgm;

            var settings = importer.defaultSampleSettings;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            settings.preloadAudioData = isBgm == false;
            settings.loadType = isBgm ? AudioClipLoadType.Streaming
                : seconds > LongClipSeconds ? AudioClipLoadType.CompressedInMemory
                : AudioClipLoadType.DecompressOnLoad;
            if (sampleRate > MaxSampleRate)
            {
                settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                settings.sampleRateOverride = MaxSampleRate;
            }
            else
            {
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            }
            importer.defaultSampleSettings = settings;
        }

        /// <summary>WAV 헤더에서 샘플레이트와 길이(초) 읽기. WAV 가 아니거나 실패하면 0</summary>
        private static bool TryReadWav(string path, out int sampleRate, out float seconds)
        {
            sampleRate = 0;
            seconds = 0f;
            if (path.EndsWith(".wav", System.StringComparison.OrdinalIgnoreCase) == false) return false;

            try
            {
                using var reader = new BinaryReader(File.OpenRead(path));
                if (new string(reader.ReadChars(4)) != "RIFF") return false;
                reader.ReadInt32();
                if (new string(reader.ReadChars(4)) != "WAVE") return false;

                int byteRate = 0;
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string id = new string(reader.ReadChars(4));
                    int size = reader.ReadInt32();
                    long next = reader.BaseStream.Position + size + (size & 1);
                    if (id == "fmt ")
                    {
                        reader.ReadInt16();               // format
                        reader.ReadInt16();               // channels
                        sampleRate = reader.ReadInt32();
                        byteRate = reader.ReadInt32();
                    }
                    else if (id == "data")
                    {
                        if (byteRate > 0) seconds = (float)size / byteRate;
                        return true;
                    }
                    reader.BaseStream.Position = next;
                }
            }
            catch (IOException) { }
            return false;
        }
    }
}
