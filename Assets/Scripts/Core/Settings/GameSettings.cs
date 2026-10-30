using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HybridShooter.Core.Settings
{
    /// <summary>
    /// 옵션 설정값 (사운드·조작·그래픽) + PlayerPrefs 저장/로드. 키 접두사 "Settings."
    /// 저장된 값은 GameSettings.Current, 옵션 화면은 Clone() 한 사본을 고치다가 [적용] 때 Save 로 넘긴다.
    ///
    /// 적용 지점
    ///  - 사운드: SoundService 는 씬마다 새로 생기고 Start 에서 Inspector 볼륨을 믹서에 다시 쓴다.
    ///    그래서 SoundService 를 고치거나 씬마다 적용 컴포넌트를 두는 대신, SceneManager.sceneLoaded 에서 저장값을 넣는다.
    ///    sceneLoaded 는 씬 오브젝트의 OnEnable(서비스 등록) 뒤, Start 전에 불리므로 SoundService.Start 가 이 값을 믹서에 반영한다
    ///  - 그래픽: 게임 시작 시 한 번 (저장된 적이 있을 때만. 처음 실행은 프로젝트 기본값 그대로)
    ///  - 감도·Y 반전: TODO(Player 담당 협의) PlayerController.UpdateLook 이 mouseSensitivity × aimSensitivity 에
    ///    GetLookSensitivity(3인칭 여부) 를 곱하고, invertY 를 InvertY 로 읽도록 연결 필요 (Changed 구독 또는 매 프레임 Current 읽기)
    /// </summary>
    public sealed class GameSettings
    {
        // ── 사운드 (0~1) ──
        public float MasterVolume = 1f;
        public float BgmVolume = 1f;
        public float SfxVolume = 1f;
        public float MonsterVolume = 1f;
        public float UiVolume = 1f;

        // ── 조작 ──
        /// <summary>1인칭(군중전) 마우스 감도 배율. 0.1~3.0</summary>
        public float FirstPersonSensitivity = 1f;
        /// <summary>3인칭(보스전) 마우스 감도 배율. 0.1~3.0</summary>
        public float ThirdPersonSensitivity = 1f;
        public bool InvertY;

        // ── 그래픽 ──
        /// <summary>0 = 지금 해상도 유지</summary>
        public int ResolutionWidth;
        public int ResolutionHeight;
        public FullScreenMode ScreenMode = FullScreenMode.FullScreenWindow;
        public int QualityLevel;
        public bool VSync = true;
        /// <summary>0 = 무제한. VSync 가 켜져 있으면 무시된다</summary>
        public int FrameRateLimit;

        public const float MinSensitivity = 0.1f;
        public const float MaxSensitivity = 3f;

        private const string Prefix = "Settings.";
        private const int Version = 1;

        private static GameSettings current;
        /// <summary>게임 시작 시 프로젝트 설정값 (기본값 복원용)</summary>
        private static int bootQualityLevel;
        private static bool bootVSync = true;
        private static FullScreenMode bootScreenMode = FullScreenMode.FullScreenWindow;

        /// <summary>저장된 설정. 처음 읽을 때 PlayerPrefs 에서 불러온다</summary>
        public static GameSettings Current => current ??= Load();

        /// <summary>한 번이라도 저장한 적이 있는지 (없으면 기본값으로 동작 중)</summary>
        public static bool HasSaved => PlayerPrefs.HasKey(Prefix + "Version");

        /// <summary>Save 직후 (인자 = 새 Current)</summary>
        public static event Action<GameSettings> Changed;

        // ───────────────────────── 시작 ─────────────────────────

        /// <summary>Enter Play Mode 옵션으로 도메인 리로드를 끈 경우 대비</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            current = null;
            Changed = null;
        }

        /// <summary>프로젝트 기본값을 기억하고, 저장된 그래픽 설정 적용 + 씬 로드마다 볼륨 적용 구독</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            bootQualityLevel = QualitySettings.GetQualityLevel();
            bootVSync = QualitySettings.vSyncCount > 0;
            bootScreenMode = Screen.fullScreenMode;

            // 에디터에서는 품질 단계가 프로젝트 설정에 남으므로 시작 시 자동 적용은 빌드에서만 ([적용] 버튼은 에디터에서도 동작)
            if (HasSaved && Application.isEditor == false) Current.ApplyGraphics();

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>새 씬의 SoundService 에 저장된 볼륨을 넣는다 (SoundService.Start 보다 먼저 불림)</summary>
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (ServiceLocator.TryGet<ISoundService>(out var sound)) Current.ApplySound(sound);
        }

        // ───────────────────────── 값 ─────────────────────────

        /// <summary>프로젝트 기본값 (볼륨 100%, 감도 1배, 화면은 게임 시작 시 설정)</summary>
        public static GameSettings CreateDefault() => new GameSettings
        {
            QualityLevel = bootQualityLevel,
            VSync = bootVSync,
            ScreenMode = bootScreenMode,
        };

        public GameSettings Clone() => (GameSettings)MemberwiseClone();

        public float GetVolume(SoundCategory category)
        {
            switch (category)
            {
                case SoundCategory.Bgm: return BgmVolume;
                case SoundCategory.Monster: return MonsterVolume;
                case SoundCategory.Ui: return UiVolume;
                default: return SfxVolume;
            }
        }

        public void SetVolume(SoundCategory category, float volume)
        {
            volume = Mathf.Clamp01(volume);
            switch (category)
            {
                case SoundCategory.Bgm: BgmVolume = volume; break;
                case SoundCategory.Monster: MonsterVolume = volume; break;
                case SoundCategory.Ui: UiVolume = volume; break;
                default: SfxVolume = volume; break;
            }
        }

        /// <summary>시점별 감도 배율 (1인칭 군중전 / 3인칭 보스전)</summary>
        public float GetLookSensitivity(bool thirdPerson) => thirdPerson ? ThirdPersonSensitivity : FirstPersonSensitivity;

        // ───────────────────────── 적용 ─────────────────────────

        /// <summary>볼륨을 사운드 서비스에 넣는다 (옵션 화면 미리듣기에도 사용)</summary>
        public void ApplySound(ISoundService sound)
        {
            if (sound == null) return;
            sound.MasterVolume = MasterVolume;
            foreach (SoundCategory category in Enum.GetValues(typeof(SoundCategory)))
                sound.SetVolume(category, GetVolume(category));
        }

        /// <summary>해상도·화면 모드·품질·V-Sync·프레임 제한 적용</summary>
        public void ApplyGraphics()
        {
            if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length && QualityLevel != QualitySettings.GetQualityLevel())
                QualitySettings.SetQualityLevel(QualityLevel, true);
            // 품질 단계에 V-Sync 가 들어 있으므로 품질을 바꾼 뒤에 적용
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = FrameRateLimit > 0 ? FrameRateLimit : -1;

            int width = ResolutionWidth > 0 ? ResolutionWidth : Screen.width;
            int height = ResolutionHeight > 0 ? ResolutionHeight : Screen.height;
            if (width != Screen.width || height != Screen.height || ScreenMode != Screen.fullScreenMode)
                Screen.SetResolution(width, height, ScreenMode);
        }

        // ───────────────────────── 저장 ─────────────────────────

        /// <summary>settings 를 Current 로 삼고 PlayerPrefs 에 저장한 뒤 Changed 알림</summary>
        public static void Save(GameSettings settings)
        {
            if (settings == null) return;
            current = settings.Clone();

            PlayerPrefs.SetInt(Prefix + "Version", Version);
            PlayerPrefs.SetFloat(Prefix + "MasterVolume", current.MasterVolume);
            PlayerPrefs.SetFloat(Prefix + "BgmVolume", current.BgmVolume);
            PlayerPrefs.SetFloat(Prefix + "SfxVolume", current.SfxVolume);
            PlayerPrefs.SetFloat(Prefix + "MonsterVolume", current.MonsterVolume);
            PlayerPrefs.SetFloat(Prefix + "UiVolume", current.UiVolume);
            PlayerPrefs.SetFloat(Prefix + "FirstPersonSensitivity", current.FirstPersonSensitivity);
            PlayerPrefs.SetFloat(Prefix + "ThirdPersonSensitivity", current.ThirdPersonSensitivity);
            PlayerPrefs.SetInt(Prefix + "InvertY", current.InvertY ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "ResolutionWidth", current.ResolutionWidth);
            PlayerPrefs.SetInt(Prefix + "ResolutionHeight", current.ResolutionHeight);
            PlayerPrefs.SetInt(Prefix + "ScreenMode", (int)current.ScreenMode);
            PlayerPrefs.SetInt(Prefix + "QualityLevel", current.QualityLevel);
            PlayerPrefs.SetInt(Prefix + "VSync", current.VSync ? 1 : 0);
            PlayerPrefs.SetInt(Prefix + "FrameRateLimit", current.FrameRateLimit);
            PlayerPrefs.Save();

            Changed?.Invoke(current);
        }

        /// <summary>저장값을 읽는다. 없는 항목은 기본값</summary>
        private static GameSettings Load()
        {
            var d = CreateDefault();
            if (HasSaved == false) return d;

            return new GameSettings
            {
                MasterVolume = PlayerPrefs.GetFloat(Prefix + "MasterVolume", d.MasterVolume),
                BgmVolume = PlayerPrefs.GetFloat(Prefix + "BgmVolume", d.BgmVolume),
                SfxVolume = PlayerPrefs.GetFloat(Prefix + "SfxVolume", d.SfxVolume),
                MonsterVolume = PlayerPrefs.GetFloat(Prefix + "MonsterVolume", d.MonsterVolume),
                UiVolume = PlayerPrefs.GetFloat(Prefix + "UiVolume", d.UiVolume),
                FirstPersonSensitivity = PlayerPrefs.GetFloat(Prefix + "FirstPersonSensitivity", d.FirstPersonSensitivity),
                ThirdPersonSensitivity = PlayerPrefs.GetFloat(Prefix + "ThirdPersonSensitivity", d.ThirdPersonSensitivity),
                InvertY = PlayerPrefs.GetInt(Prefix + "InvertY", d.InvertY ? 1 : 0) != 0,
                ResolutionWidth = PlayerPrefs.GetInt(Prefix + "ResolutionWidth", d.ResolutionWidth),
                ResolutionHeight = PlayerPrefs.GetInt(Prefix + "ResolutionHeight", d.ResolutionHeight),
                ScreenMode = (FullScreenMode)PlayerPrefs.GetInt(Prefix + "ScreenMode", (int)d.ScreenMode),
                QualityLevel = PlayerPrefs.GetInt(Prefix + "QualityLevel", d.QualityLevel),
                VSync = PlayerPrefs.GetInt(Prefix + "VSync", d.VSync ? 1 : 0) != 0,
                FrameRateLimit = PlayerPrefs.GetInt(Prefix + "FrameRateLimit", d.FrameRateLimit),
            };
        }
    }
}
