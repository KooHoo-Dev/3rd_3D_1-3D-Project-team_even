using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 일반·중간체 몬스터 소리 담당. 프리팹에 직접 붙이지 않아도 GameSoundRouter 가 생성 시 자동으로 붙인다.
    /// 소리 시점은 몬스터 쪽 IMonsterSoundSource(MonsterController) 이벤트로 받는다 — 애니메이션 이벤트 불필요.
    ///
    ///  - MonsterDef : IMonsterSoundSource.Def 우선. 없으면 GameSoundRouter 의 Bind(MonsterSpawnedEvent) 또는 monsterId
    ///  - 울음(IdleSfx) : idleInterval 마다 요청. 군중이 많아도 SoundTable 의 MaxInstances·Cooldown 으로 몇 개만 들린다
    ///  - 루프(LoopSfx) : 켜져 있는 동안 계속 (이동·날갯짓). 가까운 몇 마리만 들린다
    ///  - 공격 예고   : AttackPhaseChanged(Windup) → MonsterAttackDef.Sfx (돌진처럼 Attack 클립이 없는 공격도 동작)
    ///  - 기절       : Stunned → 울음·루프 일시 정지 + 공격 ImpactSfx(벽 충돌) + MonsterDef.StunSfx, 기절이 끝나면 다시 시작
    ///  - 도약 착지   : AttackLanded → MonsterAttackDef.ImpactSfx (착지 지점)
    ///  - 사망       : GameSoundRouter 가 MonsterDiedEvent 에서 Silence() 호출
    ///  - PlaySfxKey : 상태 머신 밖 동작(은신 등)용 SoundTable 키 직접 재생 (애니메이션 이벤트에서도 호출 가능)
    /// </summary>
    public sealed class MonsterSoundEmitter : MonoBehaviour
    {
        [Tooltip("소리 출처(IMonsterSoundSource)가 없는 몬스터용 MonsterTable Id. 0 = 생성 시 MonsterSpawnedEvent 로 받음")]
        [SerializeField] private int monsterId;
        [Tooltip("소리가 나는 위치 (머리 등). 비우면 이 오브젝트")]
        [SerializeField] private Transform voicePoint;
        [Tooltip("평상시 울음 간격(초) 최소 ~ 최대")]
        [SerializeField] private Vector2 idleInterval = new Vector2(4f, 10f);

        public MonsterDef Def { get; private set; }

        private IMonsterSoundSource source;
        private MonsterAttackDef lastAttack;
        private SoundHandle loopHandle;
        private float nextIdleTime;
        private bool silenced;
        private float stunEndTime = -1f; // -1 = 기절 아님

        private Transform Point => voicePoint != null ? voicePoint : transform;

        /// <summary>풀에서 다시 꺼낼 때도 불린다 → 출처 구독, 침묵 해제, 루프·울음 다시 시작</summary>
        private void OnEnable()
        {
            silenced = false;
            stunEndTime = -1f;
            source ??= GetComponentInParent<IMonsterSoundSource>();
            if (source != null)
            {
                source.AttackPhaseChanged += OnAttackPhaseChanged;
                source.Stunned += OnStunned;
                source.AttackLanded += OnAttackLanded;
                if (source.Def != null) Def = source.Def;
            }
            if (Def == null && monsterId != 0 && DataManager.Monsters.TryGet(monsterId, out var def)) Def = def;
            StartSounds(true);
        }

        /// <summary>풀 반환·파괴 시 구독 해제 + 루프 정지</summary>
        private void OnDisable()
        {
            if (source != null)
            {
                source.AttackPhaseChanged -= OnAttackPhaseChanged;
                source.Stunned -= OnStunned;
                source.AttackLanded -= OnAttackLanded;
            }
            StopLoop();
        }

        /// <summary>기절이 끝나면 소리 재개, 울음 시간이 되면 재생</summary>
        private void Update()
        {
            if (stunEndTime >= 0f)
            {
                if (Time.time < stunEndTime) return;
                stunEndTime = -1f;
                StartSounds(false);
            }
            if (silenced || Def == null || Def.IdleSfx == null || Time.time < nextIdleTime) return;
            ScheduleIdle(false);
            Play(Def.IdleSfx, Point.position);
        }

        /// <summary>몬스터 종류 지정 (생성 시 GameSoundRouter). 출처(IMonsterSoundSource)가 있으면 그 Def 가 우선</summary>
        public void Bind(MonsterDef def)
        {
            if (source != null && source.Def != null) def = source.Def;
            if (def == Def) return;
            StopLoop();
            Def = def;
            if (isActiveAndEnabled) StartSounds(true);
        }

        /// <summary>SoundTable 키를 그대로 재생 (상태 머신 밖 동작: 은신 등)</summary>
        public void PlaySfxKey(string soundKey)
        {
            if (silenced || string.IsNullOrEmpty(soundKey)) return;
            if (ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(soundKey, Point.position);
        }

        /// <summary>울음·루프를 멈춘다 (사망 연출 중). 다시 켜질 때(OnEnable) 해제</summary>
        public void Silence()
        {
            silenced = true;
            stunEndTime = -1f;
            StopLoop();
        }

        // ───────────── 출처 이벤트 ─────────────

        /// <summary>예고 시작에 공격 소리 → 플레이어가 소리로 피할 타이밍을 안다</summary>
        private void OnAttackPhaseChanged(MonsterAttackDef attack, SkillPhase phase)
        {
            if (attack != null) lastAttack = attack;
            if (phase != SkillPhase.Windup || silenced) return;
            Play(attack?.Sfx, Point.position);
        }

        /// <summary>기절: 울음·루프 정지 → 충돌음(마지막 공격의 ImpactSfx) + 기절음, seconds 뒤 재개</summary>
        private void OnStunned(float seconds)
        {
            if (silenced) return;
            StopLoop();
            stunEndTime = Time.time + Mathf.Max(0f, seconds);
            Play(lastAttack?.ImpactSfx, Point.position);
            Play(Def?.StunSfx, Point.position);
        }

        /// <summary>도약 착지 지점에서 착지음</summary>
        private void OnAttackLanded(MonsterAttackDef attack, Vector3 position)
        {
            if (silenced) return;
            Play(attack?.ImpactSfx, position);
        }

        // ───────────── 내부 ─────────────

        /// <summary>firstIdle = true 면 첫 울음을 0 ~ 최대 간격 사이로 흩뜨린다 (무리가 한꺼번에 생성돼도 동시에 울지 않게)</summary>
        private void StartSounds(bool firstIdle)
        {
            if (Def == null || silenced || stunEndTime >= 0f) return;
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

        private static void Play(SoundDef sound, Vector3 position)
        {
            if (sound != null && ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(sound, position);
        }
    }
}
