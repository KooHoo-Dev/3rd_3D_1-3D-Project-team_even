using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.Audio;

namespace HybridShooter.Audio
{
    /// <summary>
    /// ISoundService 구현. 씬에 하나 두면 ServiceLocator 에 등록되어 NullSoundService 대신 쓰인다.
    /// (Tools > Audio > Create Sound Service 로 GameSoundRouter 와 함께 만들 수 있다)
    ///
    /// 몬스터가 100마리 가까이 동시에 있으므로 "부르는 대로 다 틀지 않는다". 재생 요청은 아래 순서로 거른다.
    ///  1) 거리   : 듣는 위치에서 SoundDef.MaxDistance 보다 멀면 재생하지 않음
    ///  2) 쿨다운 : 같은 키를 Cooldown 안에 다시 부르면 무시 (샷건 펠릿 10발 → 1번)
    ///  3) 같은 키 동시 재생 수 : MaxInstances 만큼 울리는 중이면 그중 가장 오래된 것을 새 소리로 교체
    ///  4) 전체 자리 : 빈 AudioSource 가 없으면 Priority 가 가장 낮은(같으면 오래된) 소리를 끊고 사용.
    ///                새 소리보다 중요한 소리만 남았으면 버린다
    /// 루프(PlayLoop)는 요청만 받아두고 loopRefreshInterval 마다 키별로 가까운 MaxInstances 개만 실제로 소리를 낸다.
    /// </summary>
    public sealed class SoundService : MonoBehaviour, ISoundService
    {
        [Header("재생 자리")]
        [Tooltip("효과음·루프가 함께 쓰는 AudioSource 수 (BGM 2개 별도). Project Settings > Audio > Max Real Voices 보다 작게")]
        [SerializeField] private int voiceCount = 40;
        [Tooltip("루프 소리를 가까운 순으로 다시 고르는 간격(초)")]
        [SerializeField] private float loopRefreshInterval = 0.25f;
        [Tooltip("루프 시작·정지 페이드(초). 루프 이음새·끊김을 가린다")]
        [SerializeField] private float loopFadeTime = 0.2f;

        [Header("3D 감쇠")]
        [Tooltip("이 거리까지는 원래 볼륨(m)")]
        [SerializeField] private float minDistance = 2f;
        [Tooltip("MaxDistance 0(무제한) 인 3D 소리의 감쇠 거리(m)")]
        [SerializeField] private float unlimitedDistance = 300f;

        [Header("BGM")]
        [Tooltip("BGM 교체 크로스페이드(초)")]
        [SerializeField] private float bgmFadeTime = 1.5f;
        [Tooltip("덕킹(볼륨 잠시 낮춤)이 내려가고 돌아오는 시간(초)")]
        [SerializeField] private float duckFadeTime = 0.3f;

        [Header("믹서 그룹 (비워도 동작)")]
        [SerializeField] private AudioMixerGroup bgmGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;
        [SerializeField] private AudioMixerGroup monsterGroup;
        [SerializeField] private AudioMixerGroup uiGroup;

        [Header("볼륨 (옵션 화면은 SetVolume 사용)")]
        [Range(0f, 1f)][SerializeField] private float masterVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float bgmVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float sfxVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float monsterVolume = 1f;
        [Range(0f, 1f)][SerializeField] private float uiVolume = 1f;

        /// <summary>AudioSource 하나 = 재생 자리 하나</summary>
        private sealed class Voice
        {
            public AudioSource Source;
            /// <summary>null = 빈 자리</summary>
            public SoundDef Def;
            public float StartTime;
            /// <summary>0~1. 루프 페이드용 (일회성 소리는 1)</summary>
            public float Fade;
            public float FadeTarget;
            /// <summary>따라다닐 대상 (루프). 페이드아웃 중에도 계속 따라간다</summary>
            public Transform Follow;
            /// <summary>이 자리를 쓰는 루프 요청. 일회성이면 null</summary>
            public LoopRequest Loop;
        }

        /// <summary>PlayLoop 요청. 소리가 날지는 RefreshLoops 가 거리순으로 정한다</summary>
        private sealed class LoopRequest
        {
            public int Id;
            public SoundDef Def;
            public Transform Follow;
            /// <summary>지금 소리를 내는 자리. null = 대기 (멀거나 같은 키가 이미 MaxInstances 개)</summary>
            public Voice Voice;
            public float SqrDistance;
        }

        private static readonly System.Comparison<LoopRequest> ByDistance = (a, b) => a.SqrDistance.CompareTo(b.SqrDistance);

        private readonly List<Voice> voices = new List<Voice>();
        private readonly Dictionary<int, LoopRequest> loops = new Dictionary<int, LoopRequest>();
        private readonly Dictionary<SoundDef, List<LoopRequest>> loopGroups = new Dictionary<SoundDef, List<LoopRequest>>();
        private readonly List<LoopRequest> deadLoops = new List<LoopRequest>();
        private readonly Dictionary<SoundDef, float> lastPlayTime = new Dictionary<SoundDef, float>();
        private readonly Dictionary<SoundDef, int> lastClipIndex = new Dictionary<SoundDef, int>();
        private readonly HashSet<string> warnedKeys = new HashSet<string>();
        private int nextLoopId;
        private float loopTimer;

        private const int CategoryCount = 4;
        private readonly float[] duck = { 1f, 1f, 1f, 1f };
        private readonly float[] duckLevel = { 1f, 1f, 1f, 1f };
        private readonly float[] duckUntil = new float[CategoryCount];

        private readonly AudioSource[] bgmSources = new AudioSource[2];
        private readonly SoundDef[] bgmDefs = new SoundDef[2];
        private readonly float[] bgmFade = new float[2];
        private readonly float[] bgmFadeTarget = new float[2];
        private int bgmActive;

        private Transform listener;
        private float nextListenerSearch;

        /// <summary>지금 소리를 내고 있는 자리 수 (테스터 표시용)</summary>
        public int PlayingVoiceCount { get; private set; }
        /// <summary>등록된 루프 요청 수 / 그중 실제로 소리가 나는 수 (테스터 표시용)</summary>
        public int LoopRequestCount => loops.Count;
        public int AudibleLoopCount { get; private set; }
        public int VoiceCapacity => voices.Count;
        /// <summary>지금 재생 중인 BGM 키 (없으면 null)</summary>
        public string CurrentBgmKey => bgmFadeTarget[bgmActive] > 0f ? bgmDefs[bgmActive]?.Key : null;

        // ───────────────────────── Lifecycle ─────────────────────────

        /// <summary>재생 자리(AudioSource)를 미리 만들어 둔다. 재생 중 생성/파괴가 없도록</summary>
        private void Awake()
        {
            for (int i = 0; i < voiceCount; i++)
            {
                var go = new GameObject($"Voice {i:00}");
                go.transform.SetParent(transform, false);
                voices.Add(new Voice { Source = CreateSource(go) });
            }
            for (int i = 0; i < bgmSources.Length; i++)
            {
                var go = new GameObject($"Bgm {i}");
                go.transform.SetParent(transform, false);
                bgmSources[i] = CreateSource(go);
                bgmSources[i].spatialBlend = 0f;
                bgmSources[i].outputAudioMixerGroup = bgmGroup;
            }
        }

        /// <summary>서비스 등록 + 라운드 정리 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            ServiceLocator.Register<ISoundService>(this);
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
        }

        /// <summary>서비스 해제 + 구독 해제</summary>
        private void OnDisable()
        {
            ServiceLocator.Unregister<ISoundService>(this);
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
        }

        /// <summary>루프 선별 → 자리별 위치·페이드·볼륨 갱신 → BGM 페이드. 대상이 움직인 뒤에 따라가도록 LateUpdate</summary>
        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime; // 강화 화면(timeScale 0)에서도 페이드가 진행되도록
            UpdateDucks(dt);

            loopTimer -= dt;
            if (loopTimer <= 0f)
            {
                loopTimer = loopRefreshInterval;
                RefreshLoops();
            }

            int playing = 0;
            float fadeStep = loopFadeTime > 0f ? dt / loopFadeTime : 1f;
            foreach (var v in voices)
            {
                if (v.Def == null) continue;

                if (v.Follow != null) v.Source.transform.position = v.Follow.position;
                v.Fade = Mathf.MoveTowards(v.Fade, v.FadeTarget, fadeStep);

                bool fadedOut = v.FadeTarget <= 0f && v.Fade <= 0f;
                bool finished = v.Source.loop == false && v.Source.isPlaying == false;
                if (fadedOut || finished)
                {
                    Release(v);
                    continue;
                }

                ApplyVolume(v);
                playing++;
            }
            PlayingVoiceCount = playing;

            UpdateBgm(dt);
        }

        // ───────────────────────── ISoundService ─────────────────────────

        public void PlaySfx(string key, Vector3? position = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (DataManager.Sounds.TryGet(key, out var def)) PlaySfx(def, position);
            else if (warnedKeys.Add(key)) Debug.LogWarning($"[SoundService] SoundTable 에 '{key}' 가 없습니다", this);
        }

        public void PlaySfx(SoundDef sound, Vector3? position = null)
        {
            if (sound == null || sound.Clips.Count == 0) return;
            if (sound.Category == SoundCategory.Bgm)
            {
                PlayBgm(sound.Key);
                return;
            }

            bool spatial = sound.Spatial && position.HasValue;
            if (spatial && IsOutOfRange(sound, position.Value)) return;

            float now = Time.unscaledTime;
            if (sound.Cooldown > 0f && lastPlayTime.TryGetValue(sound, out float last) && now - last < sound.Cooldown) return;

            var voice = FindVoice(sound, true);
            if (voice == null) return;

            lastPlayTime[sound] = now;
            StartVoice(voice, sound, spatial ? position : null, null);
        }

        public SoundHandle PlayLoop(SoundDef sound, Transform follow)
        {
            if (sound == null || sound.Clips.Count == 0 || follow == null) return default;

            var request = new LoopRequest { Id = ++nextLoopId, Def = sound, Follow = follow };
            loops.Add(request.Id, request);
            loopTimer = 0f; // 다음 프레임에 바로 선별
            return new SoundHandle(request.Id);
        }

        public void StopLoop(SoundHandle handle)
        {
            if (handle.IsValid == false || loops.TryGetValue(handle.Id, out var request) == false) return;
            loops.Remove(handle.Id);
            FadeOut(request);
        }

        public void PlayBgm(string key)
        {
            if (DataManager.Sounds.TryGet(key, out var def) == false || def.Clips.Count == 0)
            {
                if (warnedKeys.Add(key)) Debug.LogWarning($"[SoundService] BGM '{key}' 를 재생할 수 없습니다 (SoundTable 키 또는 클립 없음)", this);
                return;
            }
            if (bgmDefs[bgmActive] == def && bgmFadeTarget[bgmActive] > 0f) return;

            bgmFadeTarget[bgmActive] = 0f;
            bgmActive = 1 - bgmActive;

            var source = bgmSources[bgmActive];
            bgmDefs[bgmActive] = def;
            bgmFade[bgmActive] = 0f;
            bgmFadeTarget[bgmActive] = 1f;
            source.clip = PickClip(def);
            source.loop = def.Loop;
            source.volume = 0f;
            source.Play();
        }

        public void StopBgm()
        {
            bgmFadeTarget[0] = 0f;
            bgmFadeTarget[1] = 0f;
        }

        // ───────────────────────── 볼륨·덕킹 ─────────────────────────

        public float MasterVolume
        {
            get => masterVolume;
            set => masterVolume = Mathf.Clamp01(value);
        }

        public float GetVolume(SoundCategory category)
        {
            switch (category)
            {
                case SoundCategory.Bgm: return bgmVolume;
                case SoundCategory.Monster: return monsterVolume;
                case SoundCategory.Ui: return uiVolume;
                default: return sfxVolume;
            }
        }

        public void SetVolume(SoundCategory category, float volume)
        {
            volume = Mathf.Clamp01(volume);
            switch (category)
            {
                case SoundCategory.Bgm: bgmVolume = volume; break;
                case SoundCategory.Monster: monsterVolume = volume; break;
                case SoundCategory.Ui: uiVolume = volume; break;
                default: sfxVolume = volume; break;
            }
        }

        /// <summary>category 볼륨을 holdTime 초 동안 level 배로 낮춘다 (보스 포효 때 군중 소리 줄이기)</summary>
        public void Duck(SoundCategory category, float level, float holdTime)
        {
            int c = (int)category;
            duckLevel[c] = Mathf.Clamp01(level);
            duckUntil[c] = Mathf.Max(duckUntil[c], Time.unscaledTime + holdTime);
        }

        /// <summary>일회성 효과음을 전부 즉시 정지 (루프 요청과 BGM 은 유지)</summary>
        public void StopAllSfx()
        {
            foreach (var v in voices)
                if (v.Def != null && v.Loop == null) Release(v);
        }

        // ───────────────────────── 내부 ─────────────────────────

        /// <summary>라운드 정리: 남은 효과음 정지. 루프는 대상 몬스터가 정리되면 자동으로 멈춘다</summary>
        private void OnRoundCleanup(RoundCleanupEvent e) => StopAllSfx();

        private static AudioSource CreateSource(GameObject go)
        {
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.dopplerLevel = 0f;                         // 빠른 몬스터에 피치가 출렁이지 않게
            source.rolloffMode = AudioRolloffMode.Linear;     // MaxDistance 에서 정확히 0 → 거리 컷과 맞물려 툭 끊기지 않음
            return source;
        }

        /// <summary>
        /// 새 소리를 낼 자리 찾기. checkSameKey = true 면 같은 키가 MaxInstances 개 울리는 중일 때 그중 가장 오래된 것을 돌려준다.
        /// 없으면 null (새 소리를 버림)
        /// </summary>
        private Voice FindVoice(SoundDef sound, bool checkSameKey)
        {
            if (checkSameKey && sound.MaxInstances > 0)
            {
                int count = 0;
                Voice oldest = null;
                foreach (var v in voices)
                {
                    if (v.Def != sound || v.Loop != null) continue;
                    count++;
                    if (oldest == null || v.StartTime < oldest.StartTime) oldest = v;
                }
                if (count >= sound.MaxInstances) return oldest;
            }

            Voice victim = null;
            foreach (var v in voices)
            {
                if (v.Def == null) return v;
                if (v.Def.Priority > sound.Priority) continue;
                if (v.Loop != null && v.Def == sound) continue; // 같은 루프끼리 자리를 뺏고 뺏기는 반복 방지
                if (victim == null || v.Def.Priority < victim.Def.Priority
                    || (v.Def.Priority == victim.Def.Priority && v.StartTime < victim.StartTime))
                    victim = v;
            }
            return victim;
        }

        private void StartVoice(Voice voice, SoundDef sound, Vector3? position, LoopRequest loop)
        {
            if (voice.Loop != null) voice.Loop.Voice = null; // 빼앗긴 루프는 대기로 돌아감

            var source = voice.Source;
            source.Stop();
            source.clip = PickClip(sound);
            source.loop = loop != null;
            source.outputAudioMixerGroup = GroupOf(sound.Category);
            source.spatialBlend = position.HasValue ? 1f : 0f;
            source.minDistance = minDistance;
            source.maxDistance = sound.MaxDistance > 0f ? Mathf.Max(sound.MaxDistance, minDistance + 0.1f) : unlimitedDistance;
            source.pitch = 1f + Random.Range(-sound.PitchJitter, sound.PitchJitter);
            source.priority = Mathf.Clamp(Mathf.RoundToInt((100 - sound.Priority) * 2.55f), 0, 255); // Unity: 0 이 가장 중요
            source.transform.position = position ?? Vector3.zero;

            voice.Def = sound;
            voice.StartTime = Time.unscaledTime;
            voice.Loop = loop;
            voice.Follow = loop?.Follow;
            voice.FadeTarget = 1f;
            voice.Fade = loop != null ? 0f : 1f;
            if (loop != null)
            {
                loop.Voice = voice;
                // 같은 루프 여러 개가 똑같은 위상으로 겹치지 않도록 시작 지점을 흩뜨린다
                if (source.clip.length > 0.5f) source.time = Random.Range(0f, source.clip.length * 0.9f);
            }

            ApplyVolume(voice);
            source.Play();
        }

        private void Release(Voice voice)
        {
            if (voice.Loop != null && voice.Loop.Voice == voice) voice.Loop.Voice = null;
            voice.Source.Stop();
            voice.Source.clip = null;
            voice.Def = null;
            voice.Loop = null;
            voice.Follow = null;
        }

        /// <summary>루프 요청의 소리를 페이드아웃 (자리는 페이드가 끝나면 비워짐)</summary>
        private static void FadeOut(LoopRequest request)
        {
            var v = request.Voice;
            request.Voice = null;
            if (v == null) return;
            v.Loop = null;
            v.FadeTarget = 0f;
        }

        /// <summary>
        /// 대상이 사라진 요청 정리 → 키별로 듣는 위치에서 가까운 순 정렬 → 앞에서 MaxInstances 개(거리 안)만 소리를 낸다.
        /// 몬스터 24마리가 같은 걸음 루프를 요청해도 실제로는 가장 가까운 몇 마리만 들린다.
        /// </summary>
        private void RefreshLoops()
        {
            deadLoops.Clear();
            foreach (var request in loops.Values)
                if (request.Follow == null || request.Follow.gameObject.activeInHierarchy == false) deadLoops.Add(request);
            foreach (var request in deadLoops)
            {
                loops.Remove(request.Id);
                FadeOut(request);
            }

            foreach (var list in loopGroups.Values) list.Clear();
            Vector3 center = ListenerPosition;
            foreach (var request in loops.Values)
            {
                request.SqrDistance = (request.Follow.position - center).sqrMagnitude;
                if (loopGroups.TryGetValue(request.Def, out var list) == false) loopGroups.Add(request.Def, list = new List<LoopRequest>());
                list.Add(request);
            }

            int audible = 0;
            foreach (var pair in loopGroups)
            {
                var sound = pair.Key;
                var list = pair.Value;
                if (list.Count == 0) continue;
                list.Sort(ByDistance);

                int allowed = sound.MaxInstances > 0 ? sound.MaxInstances : int.MaxValue;
                float maxSqr = sound.MaxDistance > 0f ? sound.MaxDistance * sound.MaxDistance : float.MaxValue;
                for (int i = 0; i < list.Count; i++)
                {
                    var request = list[i];
                    bool shouldPlay = i < allowed && request.SqrDistance <= maxSqr;
                    if (shouldPlay && request.Voice == null)
                    {
                        var voice = FindVoice(sound, false);
                        if (voice != null) StartVoice(voice, sound, sound.Spatial ? request.Follow.position : (Vector3?)null, request);
                    }
                    else if (shouldPlay == false && request.Voice != null)
                    {
                        FadeOut(request);
                    }
                    if (request.Voice != null) audible++;
                }
            }
            AudibleLoopCount = audible;
        }

        private AudioClip PickClip(SoundDef sound)
        {
            int count = sound.Clips.Count;
            if (count == 1) return sound.Clips[0];

            // 직전과 다른 클립 (변형이 2개 이상일 때)
            int index = Random.Range(0, count);
            if (lastClipIndex.TryGetValue(sound, out int last) && index == last) index = (index + 1 + Random.Range(0, count - 1)) % count;
            lastClipIndex[sound] = index;
            return sound.Clips[index];
        }

        private void ApplyVolume(Voice voice)
        {
            int c = (int)voice.Def.Category;
            voice.Source.volume = voice.Def.Volume * masterVolume * GetVolume(voice.Def.Category) * duck[c] * voice.Fade;
        }

        private void UpdateDucks(float dt)
        {
            float step = duckFadeTime > 0f ? dt / duckFadeTime : 1f;
            float now = Time.unscaledTime;
            for (int c = 0; c < CategoryCount; c++)
                duck[c] = Mathf.MoveTowards(duck[c], now < duckUntil[c] ? duckLevel[c] : 1f, step);
        }

        private void UpdateBgm(float dt)
        {
            float step = bgmFadeTime > 0f ? dt / bgmFadeTime : 1f;
            for (int i = 0; i < bgmSources.Length; i++)
            {
                var source = bgmSources[i];
                if (source.isPlaying == false) continue;

                bgmFade[i] = Mathf.MoveTowards(bgmFade[i], bgmFadeTarget[i], step);
                if (bgmFade[i] <= 0f && bgmFadeTarget[i] <= 0f)
                {
                    source.Stop();
                    continue;
                }
                float defVolume = bgmDefs[i] != null ? bgmDefs[i].Volume : 1f;
                source.volume = defVolume * masterVolume * bgmVolume * duck[(int)SoundCategory.Bgm] * bgmFade[i];
            }
        }

        private bool IsOutOfRange(SoundDef sound, Vector3 position)
            => sound.MaxDistance > 0f && (position - ListenerPosition).sqrMagnitude > sound.MaxDistance * sound.MaxDistance;

        private AudioMixerGroup GroupOf(SoundCategory category)
        {
            switch (category)
            {
                case SoundCategory.Bgm: return bgmGroup;
                case SoundCategory.Monster: return monsterGroup != null ? monsterGroup : sfxGroup;
                case SoundCategory.Ui: return uiGroup;
                default: return sfxGroup;
            }
        }

        /// <summary>AudioListener 위치 (보통 플레이어 카메라). 없으면 이 오브젝트 위치. 카메라 전환에 대비해 사라지면 다시 찾는다</summary>
        private Vector3 ListenerPosition
        {
            get
            {
                if (listener == null && Time.unscaledTime >= nextListenerSearch)
                {
                    nextListenerSearch = Time.unscaledTime + 1f;
                    var found = FindAnyObjectByType<AudioListener>();
                    listener = found != null ? found.transform : null;
                }
                return listener != null ? listener.position : transform.position;
            }
        }
    }
}
