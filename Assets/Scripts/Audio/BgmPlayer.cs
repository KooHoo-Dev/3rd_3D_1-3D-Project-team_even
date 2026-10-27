using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// BGM 두 트랙 크로스페이드. SoundService 가 만들고 매 프레임 Update 를 부른다.
    /// 새 곡은 비어 있는 쪽 트랙에서 0 → 1, 이전 곡은 1 → 0 으로 바뀐다. 같은 곡을 다시 요청하면 무시
    /// </summary>
    internal sealed class BgmPlayer
    {
        private readonly AudioSource[] sources;
        private readonly SoundDef[] defs = new SoundDef[2];
        private readonly float[] fade = new float[2];
        private readonly float[] fadeTarget = new float[2];
        private int active;

        /// <summary>sources = 2D AudioSource 2개 (BGM 그룹 연결은 만드는 쪽에서)</summary>
        public BgmPlayer(AudioSource first, AudioSource second) => sources = new[] { first, second };

        /// <summary>지금 재생 중인 BGM 키 (없으면 null)</summary>
        public string CurrentKey => fadeTarget[active] > 0f ? defs[active]?.Key : null;

        public void Play(SoundDef def, AudioClip clip)
        {
            if (defs[active] == def && fadeTarget[active] > 0f) return;

            fadeTarget[active] = 0f;
            active = 1 - active;

            var source = sources[active];
            defs[active] = def;
            fade[active] = 0f;
            fadeTarget[active] = 1f;
            source.clip = clip;
            source.loop = def.Loop;
            source.volume = 0f;
            source.Play();
        }

        public void Stop()
        {
            fadeTarget[0] = 0f;
            fadeTarget[1] = 0f;
        }

        /// <summary>페이드 진행 + 볼륨 적용. volumeScale = 옵션 볼륨 × 덕킹 (믹서가 있으면 덕킹만)</summary>
        public void Update(float dt, float fadeTime, float volumeScale)
        {
            float step = fadeTime > 0f ? dt / fadeTime : 1f;
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (source.isPlaying == false) continue;

                fade[i] = Mathf.MoveTowards(fade[i], fadeTarget[i], step);
                if (fade[i] <= 0f && fadeTarget[i] <= 0f)
                {
                    source.Stop();
                    continue;
                }
                float defVolume = defs[i] != null ? defs[i].Volume : 1f;
                source.volume = defVolume * volumeScale * fade[i];
            }
        }
    }
}
