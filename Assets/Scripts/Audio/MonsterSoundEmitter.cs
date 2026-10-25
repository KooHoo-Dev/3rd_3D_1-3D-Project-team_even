using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 몬스터 프리팹용 소리 담당. Animator 와 "같은 오브젝트"에 붙인다 (애니메이션 이벤트는 그 오브젝트의 컴포넌트만 호출).
    ///
    ///  - MonsterDef : 생성 시 GameSoundRouter 가 MonsterSpawnedEvent 를 받아 Bind 해준다. 씬에 직접 둔 몬스터는 monsterId 로 지정
    ///  - 울음(IdleSfx) : idleInterval 마다 요청. 군중이 많아도 SoundTable 의 MaxInstances·Cooldown 으로 몇 개만 들린다
    ///  - 루프(LoopSfx) : 켜져 있는 동안 계속 (이동·날갯짓). 가까운 몇 마리만 들린다
    ///  - 공격·특수 동작 : 애니메이션 이벤트에서 아래 public 함수를 호출
    ///      PlayAttackSfx("Bite")              MonsterAttackTable.AttackKey, 예고(Telegraph) 시작 프레임에
    ///      PlaySfxKey("mon_letalobrach_groggy") SoundTable 키 그대로 (Groggy·Vanish 처럼 한 동작에만 쓰는 소리)
    ///      Silence()                          사망 연출 시작 프레임에 (죽는 중에 울지 않게)
    ///  - 보스 공격은 GameSoundRouter 가 BossSkillPhaseChangedEvent 로 재생하므로 보스에는 PlayAttackSfx 를 쓰지 않는다
    ///  - 날개 파괴 등으로 몬스터 종류가 바뀌면 (1003 → 1002) 몬스터 쪽에서 Bind(새 Def) 를 호출한다 → 루프가 바뀐다
    /// </summary>
    public sealed class MonsterSoundEmitter : MonoBehaviour
    {
        [Tooltip("씬에 직접 둔 몬스터용 MonsterTable Id. 0 = 생성 시 MonsterSpawnedEvent 로 받음")]
        [SerializeField] private int monsterId;
        [Tooltip("소리가 나는 위치 (머리 등). 비우면 이 오브젝트")]
        [SerializeField] private Transform voicePoint;
        [Tooltip("평상시 울음 간격(초) 최소 ~ 최대")]
        [SerializeField] private Vector2 idleInterval = new Vector2(4f, 10f);

        public MonsterDef Def { get; private set; }

        private SoundHandle loopHandle;
        private float nextIdleTime;
        private bool silenced;

        private Transform Point => voicePoint != null ? voicePoint : transform;

        /// <summary>풀에서 다시 꺼낼 때도 불린다 → 침묵 해제, 루프·울음 다시 시작</summary>
        private void OnEnable()
        {
            silenced = false;
            if (Def == null && monsterId != 0 && DataManager.Monsters.TryGet(monsterId, out var def)) Def = def;
            StartSounds(true);
        }

        /// <summary>풀 반환·파괴 시 루프 정지</summary>
        private void OnDisable() => StopLoop();

        /// <summary>울음 시간이 되면 재생</summary>
        private void Update()
        {
            if (silenced || Def == null || Def.IdleSfx == null || Time.time < nextIdleTime) return;
            ScheduleIdle(false);
            Play(Def.IdleSfx);
        }

        /// <summary>몬스터 종류 지정 (생성 시 GameSoundRouter, 종류 전환 시 몬스터 쪽에서 호출)</summary>
        public void Bind(MonsterDef def)
        {
            if (def == Def) return;
            StopLoop();
            Def = def;
            if (isActiveAndEnabled) StartSounds(true);
        }

        // ───────────── 애니메이션 이벤트용 ─────────────

        /// <summary>공격 소리. attackKey 가 비었거나 없으면 첫 번째 기본 공격</summary>
        public void PlayAttackSfx(string attackKey)
        {
            if (silenced || Def == null) return;
            var attack = string.IsNullOrEmpty(attackKey) ? null : Def.FindAttack(attackKey);
            if (attack == null && Def.BasicAttacks.Count > 0) attack = Def.BasicAttacks[0];
            Play(attack?.Sfx);
        }

        /// <summary>SoundTable 키를 그대로 재생</summary>
        public void PlaySfxKey(string soundKey)
        {
            if (silenced || string.IsNullOrEmpty(soundKey)) return;
            if (ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(soundKey, Point.position);
        }

        /// <summary>울음·루프를 멈춘다 (사망 연출 중). 다시 켜질 때(OnEnable) 해제</summary>
        public void Silence()
        {
            silenced = true;
            StopLoop();
        }

        // ───────────── 내부 ─────────────

        /// <summary>firstIdle = true 면 첫 울음을 0 ~ 최대 간격 사이로 흩뜨린다 (무리가 한꺼번에 생성돼도 동시에 울지 않게)</summary>
        private void StartSounds(bool firstIdle)
        {
            if (Def == null || silenced) return;
            ScheduleIdle(firstIdle);
            if (Def.LoopSfx != null && loopHandle.IsValid == false && ServiceLocator.TryGet<ISoundService>(out var service))
                loopHandle = service.PlayLoop(Def.LoopSfx, Point);
        }

        private void ScheduleIdle(bool first)
        {
            float min = Mathf.Max(0.5f, idleInterval.x);
            float max = Mathf.Max(min, idleInterval.y);
            nextIdleTime = Time.time + (first ? Random.Range(0f, max) : Random.Range(min, max));
        }

        private void StopLoop()
        {
            if (loopHandle.IsValid && ServiceLocator.TryGet<ISoundService>(out var service)) service.StopLoop(loopHandle);
            loopHandle = default;
        }

        private void Play(SoundDef sound)
        {
            if (sound != null && ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(sound, Point.position);
        }
    }
}
