using System;
using System.Collections.Generic;
using HybridShooter.Core.Data.Dtos;
using HybridShooter.Core.Data.Loading;
using UnityEngine;

namespace HybridShooter.Core.Data.Defs
{
    /// <summary>
    /// 사운드 하나의 정의. 재생 규칙(동시 재생 수, 쿨다운, 우선순위, 거리)까지 데이터로 들고 있어서
    /// 호출하는 쪽은 키만 넘기면 된다. 실제 재생·제한은 ISoundService 구현(SoundService)이 한다.
    /// </summary>
    public sealed class SoundDef
    {
        public const string Table = "SoundTable";

        public string Key { get; }
        public SoundCategory Category { get; }
        /// <summary>Resources 에서 로드된 오디오 클립들. 여러 개면 재생할 때마다 하나를 랜덤으로 고른다</summary>
        public IReadOnlyList<AudioClip> Clips { get; }
        /// <summary>첫 번째 클립 (없으면 null)</summary>
        public AudioClip Clip => Clips.Count > 0 ? Clips[0] : null;
        public float Volume { get; }
        public bool Loop { get; }
        /// <summary>true = 위치가 있는 3D 소리, false = 2D (UI·BGM·플레이어 자신의 피격 등)</summary>
        public bool Spatial { get; }
        /// <summary>같은 키 동시 재생 수. 0 = 제한 없음</summary>
        public int MaxInstances { get; }
        /// <summary>같은 키 최소 재생 간격(초). 샷건 펠릿처럼 한 프레임에 여러 번 불려도 한 번만 난다</summary>
        public float Cooldown { get; }
        /// <summary>피치 랜덤 폭 (0.05 = ±5%). 같은 소리 반복이 덜 거슬리게</summary>
        public float PitchJitter { get; }
        /// <summary>0~100, 클수록 중요. 재생 자리가 모자라면 낮은 것부터 끊긴다</summary>
        public int Priority { get; }
        /// <summary>3D 최대 들리는 거리(m). 0 = 제한 없음</summary>
        public float MaxDistance { get; }

        internal SoundDef(SoundDto d, DataLoadReport r)
        {
            Key = d.Key;
            Category = ParseUtil.Enum<SoundCategory>(d.Category, r, Table, Key, nameof(d.Category));
            Clips = LoadClips(d.Path, r, Key);
            Volume = d.Volume;
            Loop = d.Loop;
            Spatial = d.Spatial;
            MaxInstances = d.MaxInstances;
            Cooldown = d.Cooldown;
            PitchJitter = d.PitchJitter;
            Priority = d.Priority;
            MaxDistance = d.MaxDistance;

            r.Check(Volume >= 0f && Volume <= 1f, Table, Key, "Volume 은 0~1");
            r.Check(MaxInstances >= 0, Table, Key, "MaxInstances 는 0 이상 (0 = 제한 없음)");
            r.Check(Cooldown >= 0f, Table, Key, "Cooldown 은 0 이상");
            r.Check(PitchJitter >= 0f && PitchJitter <= 0.5f, Table, Key, "PitchJitter 는 0~0.5");
            r.Check(Priority >= 0 && Priority <= 100, Table, Key, "Priority 는 0~100");
            r.Check(MaxDistance >= 0f, Table, Key, "MaxDistance 는 0 이상 (0 = 제한 없음)");
            if (Category == SoundCategory.Bgm)
                r.Check(Spatial == false, Table, Key, "Bgm 은 Spatial 을 쓸 수 없습니다");
        }

        /// <summary>
        /// "A;B" → 클립 2개. 사운드는 없어도 게임이 돌아가야 하므로 빈 경로·없는 파일은 오류가 아니라 경고로 남긴다.
        /// (리소스가 아직 안 들어온 키를 테이블에 먼저 적어둘 수 있게)
        /// </summary>
        private static IReadOnlyList<AudioClip> LoadClips(string raw, DataLoadReport r, string key)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                r.Warn(Table, key, "Path 가 비었습니다 (소리 없음)");
                return Array.Empty<AudioClip>();
            }

            var clips = new List<AudioClip>();
            foreach (var part in raw.Split(ParseUtil.ListSeparator))
            {
                string path = part.Trim();
                if (path.Length == 0) continue;
                var clip = Resources.Load<AudioClip>(path);
                if (clip != null) clips.Add(clip);
                else r.Warn(Table, key, $"Path 'Resources/{path}' 에서 AudioClip 을 찾을 수 없습니다 (소리 없음)");
            }
            return clips;
        }
    }
}
