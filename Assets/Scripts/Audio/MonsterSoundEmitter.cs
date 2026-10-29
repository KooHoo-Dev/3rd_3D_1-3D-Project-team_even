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
    ///  - 울음(IdleSfx) : 상태와 관계없이 idleInterval 마다 요청. 군중이 많아도 SoundTable 의 MaxInstances·Cooldown 으로 몇 개만 들린다.
    ///                   플레이어 근처(SetNearIdle 거리 안)에서는 nearInterval 로 자주 울고, 근처에 들어온 순간 곧 한 번 운다
    ///                   → 공격 전에도 가까운 몬스터를 소리로 알 수 있게. 공격 예고 직후에는 울음을 잠시 미뤄 겹치지 않게 한다
    ///  - 기습형(HiddenSfx) : MonsterTable.HiddenSfxKey 가 있으면 근처에서는 울지 않고, 근처이면서 플레이어 시야 밖일 때
    ///                   공격 전 1번 HiddenSfx (파르다트록스 = Vanish). 근처를 벗어나거나 공격 예고가 나면 다시 낼 수 있다
    ///  - 루프(LoopSfx) : 켜져 있는 동안 계속 (이동·날갯짓). 가까운 몇 마리만 들린다
    ///  - 공격 예고   : AttackPhaseChanged(Windup) → MonsterAttackDef.Sfx (돌진처럼 Attack 클립이 없는 공격도 동작)
    ///  - 기절       : Stunned → 울음·루프 일시 정지 + 공격 ImpactSfx(벽 충돌) + MonsterDef.StunSfx, 기절이 끝나면 다시 시작
    ///  - 도약 착지   : AttackLanded → MonsterAttackDef.ImpactSfx (착지 지점)
    ///  - 돌진 명중   : AttackContacted → MonsterAttackDef.ImpactSfx (부딪힌 지점)
    ///  - 사망       : Died → 울음·루프 정지 + MonsterDef.DeathSfx (사망 연출 중에 울지 않게)
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

        /// <summary>공격 예고음 뒤 이 시간(초) 동안은 울지 않는다</summary>
        private const float IdleGapAfterAttack = 1.5f;
        /// <summary>기습형 신호의 최소 간격(초). 시야 경계에서 들락날락해도 연달아 나지 않게</summary>
        private const float HiddenMinGap = 5f;
        /// <summary>카메라를 못 찾을 때 쓰는 시야 수평 반각(도)</summary>
        private const float FallbackViewHalfAngle = 60f;

        public MonsterDef Def { get; private set; }

        // 근접 울음 — 값은 GameSoundRouter 가 붙일 때 넣는다 (씬의 Router 한 곳에서 조정). 0 = 끔
        private float nearDistance;
        private Vector2 nearInterval;
        private bool wasNear;
        private bool hiddenArmed = true; // 기습형 신호를 낼 수 있는 상태
        private float nextHiddenTime;

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
            wasNear = false;
            hiddenArmed = true;
            source ??= GetComponentInParent<IMonsterSoundSource>();
            if (source != null)
            {
                source.AttackPhaseChanged += OnAttackPhaseChanged;
                source.Stunned += OnStunned;
                source.AttackLanded += OnAttackLanded;
                source.AttackContacted += OnAttackLanded; // 명중도 착지와 같이 그 지점에서 ImpactSfx
                source.Died += OnDied;
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
                source.AttackContacted -= OnAttackLanded;
                source.Died -= OnDied;
            }
            StopLoop();
        }

        /// <summary>기절이 끝나면 소리 재개, 울음 시간이 되면 재생 (플레이어 근처면 더 자주)</summary>
        private void Update()
        {
            if (stunEndTime >= 0f)
            {
                if (Time.time < stunEndTime) return;
                stunEndTime = -1f;
                StartSounds(false);
            }
            if (silenced || Def == null) return;

            bool near = IsNearPlayer();
            bool entered = near && wasNear == false;
            wasNear = near;

            if (Def.HiddenSfx != null)
            {
                // 기습형: 근처에서는 울지 않는다. 근처·시야 밖이면 공격 전 신호 1번
                if (near == false) hiddenArmed = true;
                else
                {
                    if (hiddenArmed && Time.time >= nextHiddenTime && IsInPlayerView() == false)
                    {
                        hiddenArmed = false;
                        nextHiddenTime = Time.time + HiddenMinGap;
                        Play(Def.HiddenSfx, Point.position);
                    }
                    return;
                }
            }
            // 근처에 막 들어왔으면 남은 대기를 줄여 곧 한 번 운다 (접근 알림)
            else if (entered) nextIdleTime = Mathf.Min(nextIdleTime, Time.time + Random.Range(0.1f, 0.6f));

            if (Def.IdleSfx == null || Time.time < nextIdleTime) return;
            ScheduleIdle(false, near);
            Play(Def.IdleSfx, Point.position);
        }

        /// <summary>플레이어 근처 울음 설정 (생성 시 GameSoundRouter). distance 0 이하 = 끔</summary>
        public void SetNearIdle(float distance, Vector2 interval)
        {
            nearDistance = distance;
            nearInterval = interval;
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

        /// <summary>울음·루프를 멈춘다 (사망 연출 중·출처 없는 더미). 다시 켜질 때(OnEnable) 해제</summary>
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
            nextIdleTime = Mathf.Max(nextIdleTime, Time.time + IdleGapAfterAttack); // 예고음과 울음이 겹치지 않게
            // 기습형: 공격이 나왔으니 다음 기습 전에 다시 신호를 낼 수 있게 (도약 중에 바로 나지 않도록 간격은 둔다)
            hiddenArmed = true;
            nextHiddenTime = Mathf.Max(nextHiddenTime, Time.time + HiddenMinGap);
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

        /// <summary>도약 착지·돌진 명중 지점에서 ImpactSfx</summary>
        private void OnAttackLanded(MonsterAttackDef attack, Vector3 position)
        {
            if (silenced) return;
            Play(attack?.ImpactSfx, position);
        }

        /// <summary>사망: 울음·루프를 끄고 사망음 (위치 재생이라 시체가 풀로 돌아가도 끝까지 들린다)</summary>
        private void OnDied()
        {
            if (silenced) return;
            Silence();
            Play(Def?.DeathSfx, Point.position);
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

        /// <summary>near = 플레이어 근처면 nearInterval, 아니면 idleInterval</summary>
        private void ScheduleIdle(bool first, bool near = false)
        {
            var range = near ? nearInterval : idleInterval;
            float min = Mathf.Max(0.5f, range.x);
            float max = Mathf.Max(min, range.y);
            nextIdleTime = Time.time + (first ? Random.Range(0f, max) : Random.Range(min, max));
        }

        /// <summary>
        /// 플레이어 카메라(IPlayerTarget.ViewPoint) 수평 시야 안에 있는지. FlankPolicy 의 시야 판정과 같은 기준(수평 FOV 반각).
        /// 플레이어를 모르면 보이는 것으로 친다 (기습 신호를 내지 않는 쪽)
        /// </summary>
        private bool IsInPlayerView()
        {
            if (ServiceLocator.TryGet<IPlayerTarget>(out var player) == false) return true;
            var view = player.ViewPoint != null ? player.ViewPoint : player.Root;
            if (view == null) return true;

            Vector3 toMonster = Point.position - view.position;
            Vector3 forward = view.forward;
            toMonster.y = 0f;
            forward.y = 0f;
            if (toMonster.sqrMagnitude < 0.01f || forward.sqrMagnitude < 0.0001f) return true;

            var cam = view.GetComponent<Camera>();
            float halfAngle = cam != null
                ? Mathf.Atan(Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * cam.aspect) * Mathf.Rad2Deg
                : FallbackViewHalfAngle;
            return Vector3.Angle(forward, toMonster) <= halfAngle;
        }

        /// <summary>플레이어(IPlayerTarget)와의 거리가 nearDistance 이하 (공중 몬스터는 높이 차도 포함)</summary>
        private bool IsNearPlayer()
        {
            if (nearDistance <= 0f || ServiceLocator.TryGet<IPlayerTarget>(out var player) == false || player.Root == null) return false;
            return (player.Root.position - Point.position).sqrMagnitude <= nearDistance * nearDistance;
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
