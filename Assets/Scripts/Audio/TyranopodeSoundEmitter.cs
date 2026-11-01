using System;
using HybridShooter.Boss;
using HybridShooter.Boss.Tyranopode;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 티라노포드 몸 소리. GameSoundRouter 가 보스 등장 때 자동으로 붙인다 (프리팹 수정 불필요).
    /// 같은 오브젝트의 지역 이벤트만 받는다 — 전역 EventBus 를 쓰지 않는다.
    ///
    ///   패턴     BossAttack.PhaseChanged — 물기·돌진 예고(Windup), 돌진 루프(Active 동안), 포효 소환 예고·포자 발사(Active 시작)
    ///   순간     TyranopodeController.SoundCue — 포효·벽 충돌·그로기·침·알·흡수·타격·보호막·포자 등 (패턴 단계로 알 수 없는 것, TyranopodeSoundCue 참고)
    ///   기어가기 이동 속도가 crawlMinSpeed 이상인 동안 crawlLoopKey 루프 (그로기·사망 때 정지)
    ///   사망     BossController.Died → 루프 전부 정지 + deathKey
    /// 키는 보스 1종 전용이라 테이블 대신 여기 둔다 (BossSoundEmitter 와 같은 방식).
    /// </summary>
    [RequireComponent(typeof(TyranopodeController))]
    public sealed class TyranopodeSoundEmitter : MonoBehaviour
    {
        [Header("패턴 (BossAttack.PhaseChanged)")]
        [SerializeField] private string biteWindupKey = "mon_tyranopode_bite";
        [SerializeField] private string chargeWindupKey = "mon_tyranopode_charge_start";
        [Tooltip("돌진 발동 동안 루프 (벽 충돌·완료·취소에서 정지)")]
        [SerializeField] private string chargeLoopKey = "mon_tyranopode_charge_loop";
        [Tooltip("포효 소환 예고")]
        [SerializeField] private string summonWindupKey = "mon_tyranopode_roar";
        [Tooltip("포자(소환 신호기)를 쏘아 올리는 순간 = 포효 소환 발동 시작")]
        [SerializeField] private string summonActiveKey = "mon_tyranopode_spore_launch";

        [Header("순간 (TyranopodeController.SoundCue)")]
        [SerializeField] private string roarKey = "mon_tyranopode_roar";
        [SerializeField] private string wallHitKey = "mon_tyranopode_wall_hit";
        [SerializeField] private string groggyKey = "mon_tyranopode_groggy";
        [SerializeField] private string spitKey = "mon_tyranopode_spit";
        [SerializeField] private string eggThrowKey = "mon_tyranopode_egg_throw";
        [SerializeField] private string eggHatchKey = "mon_tyranopode_egg_hatch";
        [SerializeField] private string absorbKey = "mon_tyranopode_absorb";
        [SerializeField] private string biteImpactKey = "mon_tyranopode_bite_impact";
        [SerializeField] private string swipeKey = "mon_tyranopode_swipe";
        [SerializeField] private string tremorKey = "mon_tyranopode_tremor";
        [SerializeField] private string stormSweepKey = "mon_tyranopode_storm_sweep";
        [SerializeField] private string spitImpactKey = "mon_tyranopode_spit_impact";
        [SerializeField] private string eggBreakKey = "mon_tyranopode_egg_break";
        [SerializeField] private string immuneHitKey = "mon_tyranopode_immune_hit";
        [SerializeField] private string shieldHitKey = "mon_tyranopode_shield_hit";
        [SerializeField] private string shieldBreakKey = "mon_tyranopode_shield_break";
        [SerializeField] private string sporePulseKey = "mon_tyranopode_spore_pulse";
        [SerializeField] private string sporeBreakKey = "mon_tyranopode_spore_break";
        [SerializeField] private string sporeFailKey = "mon_tyranopode_spore_fail";

        [Header("이동·사망")]
        [Tooltip("맴돌기 이동 루프 (SoundTable.Key, 비우면 없음)")]
        [SerializeField] private string crawlLoopKey = "mon_tyranopode_crawl_loop";
        [Tooltip("이 속도(m/s) 이상으로 움직이는 동안 기어가기 루프")]
        [SerializeField, Min(0f)] private float crawlMinSpeed = 0.5f;
        [Tooltip("사망 (BossController.Died = 최후 연출의 마지막 일격)")]
        [SerializeField] private string deathKey = "mon_tyranopode_death";

        private TyranopodeController controller;
        private TyranopodeLocomotion locomotion;
        private BossController boss;
        private BossAttack[] attacks = Array.Empty<BossAttack>();
        private SoundHandle chargeLoop;
        private SoundHandle crawlLoop;
        private bool dead;

        private void Awake()
        {
            controller = GetComponent<TyranopodeController>();
            locomotion = GetComponent<TyranopodeLocomotion>();
            boss = GetComponent<BossController>();
            attacks = GetComponents<BossAttack>();
            WarnMissingKeys();
        }

        /// <summary>이동 여부에 맞춰 기어가기 루프를 켜고 끈다</summary>
        private void Update()
        {
            bool crawling = dead == false && controller.IsAlive && controller.IsGroggy == false
                            && locomotion.Velocity.sqrMagnitude >= crawlMinSpeed * crawlMinSpeed;
            if (crawling == crawlLoop.IsValid) return;
            if (crawling) crawlLoop = StartLoop(crawlLoopKey);
            else StopLoop(ref crawlLoop);
        }

        /// <summary>패턴·보스 알림 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            foreach (var attack in attacks) attack.PhaseChanged += OnPhaseChanged;
            controller.SoundCue += OnSoundCue;
            boss.Died += OnDied;
            dead = false;
        }

        /// <summary>구독 해제 + 루프 정지</summary>
        private void OnDisable()
        {
            foreach (var attack in attacks) attack.PhaseChanged -= OnPhaseChanged;
            controller.SoundCue -= OnSoundCue;
            boss.Died -= OnDied;
            StopAllLoops();
        }

        private void OnPhaseChanged(BossAttack attack, SkillPhase phase)
        {
            Vector3 voice = controller.Mouth.position;
            switch (attack)
            {
                case BiteAttack _ when phase == SkillPhase.Windup:
                    Play(biteWindupKey, voice);
                    break;
                case ChargeAttack _:
                    if (phase == SkillPhase.Windup) Play(chargeWindupKey, voice);
                    else if (phase == SkillPhase.Active) { if (chargeLoop.IsValid == false) chargeLoop = StartLoop(chargeLoopKey); }
                    else StopLoop(ref chargeLoop); // Recover·Idle = 발동 끝 (완료·벽 충돌·취소)
                    break;
                case SummonRoarAttack _:
                    if (phase == SkillPhase.Windup) Play(summonWindupKey, voice);
                    else if (phase == SkillPhase.Active) Play(summonActiveKey, voice);
                    break;
            }
        }

        private void OnSoundCue(TyranopodeSoundCue cue, Vector3 position) => Play(KeyOf(cue), position);

        private string KeyOf(TyranopodeSoundCue cue)
        {
            switch (cue)
            {
                case TyranopodeSoundCue.Roar: return roarKey;
                case TyranopodeSoundCue.WallHit: return wallHitKey;
                case TyranopodeSoundCue.Groggy: return groggyKey;
                case TyranopodeSoundCue.Spit: return spitKey;
                case TyranopodeSoundCue.EggThrow: return eggThrowKey;
                case TyranopodeSoundCue.EggHatch: return eggHatchKey;
                case TyranopodeSoundCue.Absorb: return absorbKey;
                case TyranopodeSoundCue.BiteImpact: return biteImpactKey;
                case TyranopodeSoundCue.Swipe: return swipeKey;
                case TyranopodeSoundCue.Tremor: return tremorKey;
                case TyranopodeSoundCue.StormSweep: return stormSweepKey;
                case TyranopodeSoundCue.SpitImpact: return spitImpactKey;
                case TyranopodeSoundCue.EggBreak: return eggBreakKey;
                case TyranopodeSoundCue.ImmuneHit: return immuneHitKey;
                case TyranopodeSoundCue.ShieldHit: return shieldHitKey;
                case TyranopodeSoundCue.ShieldBreak: return shieldBreakKey;
                case TyranopodeSoundCue.SporePulse: return sporePulseKey;
                case TyranopodeSoundCue.SporeBreak: return sporeBreakKey;
                case TyranopodeSoundCue.SporeFail: return sporeFailKey;
                default: return null;
            }
        }

        private void OnDied()
        {
            dead = true;
            StopAllLoops();
            Play(deathKey, controller.Mouth.position);
        }

        // ───────────── 내부 ─────────────

        private SoundHandle StartLoop(string key)
        {
            if (string.IsNullOrEmpty(key) || ServiceLocator.TryGet<ISoundService>(out var service) == false) return default;
            return service.PlayLoop(key, transform);
        }

        private static void StopLoop(ref SoundHandle handle)
        {
            if (handle.IsValid && ServiceLocator.TryGet<ISoundService>(out var service)) service.StopLoop(handle);
            handle = default;
        }

        private void StopAllLoops()
        {
            StopLoop(ref chargeLoop);
            StopLoop(ref crawlLoop);
        }

        private static void Play(string key, Vector3 position)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(key, position);
        }

        /// <summary>테이블 키와 달리 로드 시 검증되지 않으므로, 시작할 때 SoundTable 에 없는 키를 알린다</summary>
        private void WarnMissingKeys()
        {
            Check(biteWindupKey, nameof(biteWindupKey));
            Check(chargeWindupKey, nameof(chargeWindupKey));
            Check(chargeLoopKey, nameof(chargeLoopKey));
            Check(summonWindupKey, nameof(summonWindupKey));
            Check(summonActiveKey, nameof(summonActiveKey));
            foreach (TyranopodeSoundCue cue in Enum.GetValues(typeof(TyranopodeSoundCue))) Check(KeyOf(cue), cue.ToString());
            Check(crawlLoopKey, nameof(crawlLoopKey));
            Check(deathKey, nameof(deathKey));
        }

        private void Check(string key, string field)
        {
            if (string.IsNullOrEmpty(key) == false && DataManager.Sounds.Contains(key) == false)
                Debug.LogWarning($"[TyranopodeSoundEmitter] {field} '{key}' 가 SoundTable 에 없습니다", this);
        }
    }
}
