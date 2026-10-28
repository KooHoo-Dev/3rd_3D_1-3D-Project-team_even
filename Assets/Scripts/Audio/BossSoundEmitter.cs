using System;
using HybridShooter.Boss;
using HybridShooter.Boss.Clypeosaurus;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 보스 몸 소리. 보스 프리팹에 붙인다 (Tools > Audio > Add Boss Sound Emitter).
    /// 같은 프리팹의 패턴(BossAttack)·BossController 지역 이벤트만 받는다 — 전역 EventBus 를 쓰지 않는다.
    ///
    ///   예고     BossAttack.PhaseChanged(Windup) → WindupKey (회피 신호. 바로 취소된 공격은 알림이 오지 않는다)
    ///   루프     Active 동안 ActiveLoopKey (흡입), Recover·Idle(완료·취소) 에서 정지
    ///   타격     BossAttack.Impacted → ImpactKey (짓밟기 착지 지점)
    ///   사망     BossController.Died → 루프 전부 정지 + deathKey
    /// 마그마 착탄음은 투사체 자기 효과라 MagmaProjectile 이 직접 낸다. 보스 BGM·경고·처치 연출음은 GameSoundRouter 담당.
    /// 키는 보스가 1종이라 테이블 대신 여기 둔다 (보스가 늘면 BossTable 로 옮긴다).
    /// </summary>
    [RequireComponent(typeof(BossController))]
    public sealed class BossSoundEmitter : MonoBehaviour
    {
        [Serializable]
        private struct AttackSound
        {
            [Tooltip("같은 보스의 패턴 컴포넌트")]
            public BossAttack Attack;
            [Tooltip("예고(Windup) 시작 (SoundTable.Key, 비우면 없음)")]
            public string WindupKey;
            [Tooltip("발동(Active) 동안 루프. 회복·완료·취소에서 정지")]
            public string ActiveLoopKey;
            [Tooltip("루프가 따라갈 위치 (흡입 = 입). 비우면 voicePoint")]
            public Transform LoopPoint;
            [Tooltip("타격 지점에서 (BossAttack.Impacted)")]
            public string ImpactKey;
        }

        [SerializeField] private AttackSound[] attacks = Array.Empty<AttackSound>();
        [SerializeField] private string deathKey = "mon_clypeosaurus_hurt";
        [Tooltip("예고음·사망음 위치 (머리 등). 비우면 이 오브젝트")]
        [SerializeField] private Transform voicePoint;

        private BossController boss;
        private SoundHandle[] loops = Array.Empty<SoundHandle>();

        private Transform Point => voicePoint != null ? voicePoint : transform;

        private void Awake()
        {
            boss = GetComponent<BossController>();
            loops = new SoundHandle[attacks.Length];
            WarnMissingKeys();
        }

        /// <summary>패턴·보스 알림 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            foreach (var a in attacks)
            {
                if (a.Attack == null) continue;
                a.Attack.PhaseChanged += OnPhaseChanged;
                a.Attack.Impacted += OnImpacted;
            }
            boss.Died += OnDied;
        }

        /// <summary>구독 해제 + 루프 정지</summary>
        private void OnDisable()
        {
            foreach (var a in attacks)
            {
                if (a.Attack == null) continue;
                a.Attack.PhaseChanged -= OnPhaseChanged;
                a.Attack.Impacted -= OnImpacted;
            }
            boss.Died -= OnDied;
            StopAllLoops();
        }

        private void OnPhaseChanged(BossAttack attack, SkillPhase phase)
        {
            int i = IndexOf(attack);
            if (i < 0) return;
            switch (phase)
            {
                case SkillPhase.Windup:
                    Play(attacks[i].WindupKey, Point.position);
                    break;
                case SkillPhase.Active:
                    StartLoop(i);
                    break;
                default: // Recover·Idle = 발동 끝 (완료·취소)
                    StopLoop(i);
                    break;
            }
        }

        private void OnImpacted(BossAttack attack, Vector3 point)
        {
            int i = IndexOf(attack);
            if (i >= 0) Play(attacks[i].ImpactKey, point);
        }

        private void OnDied()
        {
            StopAllLoops();
            Play(deathKey, Point.position);
        }

        // ───────────── 내부 ─────────────

        private int IndexOf(BossAttack attack)
        {
            for (int i = 0; i < attacks.Length; i++)
                if (attacks[i].Attack == attack) return i;
            return -1;
        }

        private void StartLoop(int i)
        {
            string key = attacks[i].ActiveLoopKey;
            if (string.IsNullOrEmpty(key) || loops[i].IsValid || ServiceLocator.TryGet<ISoundService>(out var service) == false) return;
            loops[i] = service.PlayLoop(key, attacks[i].LoopPoint != null ? attacks[i].LoopPoint : Point);
        }

        private void StopLoop(int i)
        {
            if (loops[i].IsValid && ServiceLocator.TryGet<ISoundService>(out var service)) service.StopLoop(loops[i]);
            loops[i] = default;
        }

        private void StopAllLoops()
        {
            for (int i = 0; i < loops.Length; i++) StopLoop(i);
        }

        private static void Play(string key, Vector3 position)
        {
            if (string.IsNullOrEmpty(key) == false && ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(key, position);
        }

        /// <summary>테이블 키와 달리 로드 시 검증되지 않으므로, 시작할 때 SoundTable 에 없는 키를 알린다</summary>
        private void WarnMissingKeys()
        {
            Check(deathKey, "deathKey");
            foreach (var a in attacks)
            {
                string label = a.Attack != null ? a.Attack.GetType().Name : "(패턴 없음)";
                Check(a.WindupKey, label + ".WindupKey");
                Check(a.ActiveLoopKey, label + ".ActiveLoopKey");
                Check(a.ImpactKey, label + ".ImpactKey");
            }
        }

        private void Check(string key, string field)
        {
            if (string.IsNullOrEmpty(key) == false && DataManager.Sounds.Contains(key) == false)
                Debug.LogWarning($"[BossSoundEmitter] {field} '{key}' 가 SoundTable 에 없습니다", this);
        }

#if UNITY_EDITOR
        /// <summary>컴포넌트를 처음 붙일 때 같은 오브젝트의 패턴으로 목록을 채운다</summary>
        private void Reset() => FillDefaults();

        /// <summary>
        /// 같은 오브젝트의 BossAttack 마다 기본 키를 채운다 (클리페오사우루스 기준. 다른 패턴은 키를 비워 둔다).
        /// 흡입 루프 위치는 SuctionAttack 의 inhaleCenter 를 쓴다
        /// </summary>
        public void FillDefaults()
        {
            var found = GetComponents<BossAttack>();
            attacks = new AttackSound[found.Length];
            for (int i = 0; i < found.Length; i++)
            {
                var entry = new AttackSound { Attack = found[i] };
                switch (found[i])
                {
                    case StompAttack _:
                        entry.WindupKey = "mon_clypeosaurus_attack";
                        entry.ImpactKey = "mon_clypeosaurus_stomp_impact";
                        break;
                    case MagmaBarrageAttack _:
                        entry.WindupKey = "mon_clypeosaurus_skill_attack";
                        break;
                    case SuctionAttack suction:
                        entry.WindupKey = "mon_clypeosaurus_shout";
                        entry.ActiveLoopKey = "mon_clypeosaurus_suction_loop";
                        entry.LoopPoint = new UnityEditor.SerializedObject(suction).FindProperty("inhaleCenter")?.objectReferenceValue as Transform;
                        break;
                }
                attacks[i] = entry;
            }
            deathKey = "mon_clypeosaurus_hurt";
        }
#endif
    }
}
