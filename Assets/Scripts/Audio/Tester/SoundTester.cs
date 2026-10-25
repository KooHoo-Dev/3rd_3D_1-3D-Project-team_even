using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Audio.Tester
{
    /// <summary>
    /// 사운드 인게임 확인용. 몬스터·보스·무기 피격 구현이 없어도, 그쪽이 발행할 이벤트를 키 입력으로 대신 발행한다.
    /// 씬 준비: Tools > Audio > Create Sound Service + 이 컴포넌트 + AudioListener 가 있는 카메라.
    ///
    ///   [ / ] : 테스트 몬스터 선택 (MonsterTable 순서)
    ///   E     : 선택 몬스터 더미 1마리 생성 (MonsterSpawnedEvent → MonsterSoundEmitter 연결, 울음·루프 확인). 주변을 돈다
    ///   M     : 선택 몬스터 더미 12마리 한 번에 생성 (Herd) → 루프는 가까운 MaxInstances 개만 들려야 함
    ///   A     : 더미 전부 공격음 (애니메이션 이벤트 PlayAttackSfx 대신). 더미가 없으면 선택 몬스터 공격음 1번
    ///   D     : 더미 1마리 처치 (MonsterDiedEvent) → 사망음, 그 더미의 루프 정지
    ///   C     : 군중 20마리 동시 사망 (이벤트만) → 사망음이 MaxInstances 개 이하로만 겹쳐야 함
    ///   H     : 총기 명중 (DamageDealtEvent Bullet)         → 타격음(3D) + 히트마커
    ///   K     : 총기 처치 (DamageDealtEvent IsKill)          → 타격음(3D) + 처치 확인음
    ///   G     : 샷건 10발 동시 명중                           → Cooldown 으로 한 번만 나야 함
    ///   X     : 수류탄 범위 8마리 명중 (Explosion)            → 타격음 없음, 히트마커 1번
    ///   P     : 플레이어 피격 (PlayerDamagedEvent)
    ///   B     : 보스 경고 (BossWarningEvent)                  → 경고음 + 몬스터 소리 덕킹
    ///   V     : 보스 등장 (BossAppearedEvent)                 → 보스 BGM
    ///   1~6   : 보스 공격 예고 (BossSkillPhaseChangedEvent Windup, 보스 공격 순서. 등장 공격이면 포효)
    ///   S     : 보스 페이즈 전환 (BossDamageStageChangedEvent) → 보스 Hurt
    ///   R     : 라운드 시작 (군중 BGM)   O : 게임 오버 (BGM 정지)   N : 라운드 정리 (효과음 정지, 더미 제거)
    /// </summary>
    public sealed class SoundTester : MonoBehaviour
    {
        [Tooltip("소리 위치 기준. 비우면 Camera.main")]
        [SerializeField] private Transform center;
        [Tooltip("일회성 소리를 낼 거리(m)")]
        [SerializeField] private float testDistance = 8f;
        [Tooltip("더미가 생성되는 거리 범위(m). 루프 거리 선별 확인용으로 넓게")]
        [SerializeField] private Vector2 dummyRadius = new Vector2(4f, 40f);
        [Tooltip("더미가 도는 속도(도/초)")]
        [SerializeField] private float dummyOrbitSpeed = 20f;
        [Tooltip("시작 시 선택할 몬스터 Id")]
        [SerializeField] private int startMonsterId = 1002;
        [Tooltip("보스 Id")]
        [SerializeField] private int bossMonsterId = 3001;
        [Tooltip("화면에 상태 표시")]
        [SerializeField] private bool showStatus = true;

        private sealed class Dummy
        {
            public GameObject Go;
            public MonsterDef Def;
            public float Radius;
            public float Angle;
        }

        private readonly List<Dummy> dummies = new List<Dummy>();
        private int monsterIndex;
        private int bossStage;
        private int testRound;
        private string lastAction = "-";
        private GUIStyle statusStyle;

        private MonsterDef Selected => DataManager.Monsters.All.Count > 0 ? DataManager.Monsters.All[monsterIndex] : null;
        private Vector3 Center => center != null ? center.position : Camera.main != null ? Camera.main.transform.position : transform.position;

        /// <summary>시작 몬스터 선택</summary>
        private void Start()
        {
            var all = DataManager.Monsters.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Id == startMonsterId) monsterIndex = i;
        }

        /// <summary>키 입력 → 테스트 이벤트, 더미 이동</summary>
        private void Update()
        {
            MoveDummies();

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.leftBracketKey.wasPressedThisFrame) SelectMonster(-1);
            if (keyboard.rightBracketKey.wasPressedThisFrame) SelectMonster(1);
            if (keyboard.eKey.wasPressedThisFrame) SpawnDummies(1);
            if (keyboard.mKey.wasPressedThisFrame) SpawnDummies(12);
            if (keyboard.aKey.wasPressedThisFrame) PlayAttacks();
            if (keyboard.dKey.wasPressedThisFrame) KillDummy();
            if (keyboard.cKey.wasPressedThisFrame) KillCrowd(20);
            if (keyboard.hKey.wasPressedThisFrame) PublishHits(1, DamageKind.Bullet, false, "총기 명중");
            if (keyboard.kKey.wasPressedThisFrame) PublishHits(1, DamageKind.Bullet, true, "총기 처치");
            if (keyboard.gKey.wasPressedThisFrame) PublishHits(10, DamageKind.Bullet, false, "샷건 10발");
            if (keyboard.xKey.wasPressedThisFrame) PublishHits(8, DamageKind.Explosion, false, "수류탄 8마리");
            if (keyboard.pKey.wasPressedThisFrame) PublishPlayerHit();
            if (keyboard.bKey.wasPressedThisFrame) PublishBossWarning();
            if (keyboard.vKey.wasPressedThisFrame) PublishBossAppeared();
            if (keyboard.sKey.wasPressedThisFrame) PublishBossStage();
            if (keyboard.rKey.wasPressedThisFrame) Publish("라운드 시작", () => EventBus<RoundStartedEvent>.Publish(new RoundStartedEvent(++testRound, DataManager.GetRound(testRound))));
            if (keyboard.oKey.wasPressedThisFrame) Publish("게임 오버", () => EventBus<GameOverEvent>.Publish(new GameOverEvent(testRound)));
            if (keyboard.nKey.wasPressedThisFrame) Cleanup();

            for (int i = 0; i < 6; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame) PublishBossSkill(i);
        }

        /// <summary>선택 몬스터·재생 자리·루프 수·키 안내 표시</summary>
        private void OnGUI()
        {
            if (showStatus == false) return;
            statusStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 18 };

            string serviceText = ServiceLocator.TryGet<ISoundService>(out var s) && s is SoundService service
                ? $"재생 중 {service.PlayingVoiceCount}/{service.VoiceCapacity}  루프 {service.AudibleLoopCount}/{service.LoopRequestCount} 들림  BGM {service.CurrentBgmKey ?? "-"}"
                : "SoundService 없음 (Tools > Audio > Create Sound Service)";
            var m = Selected;
            string text =
                $"선택 [{m?.Id}] {m?.Name}  Idle {m?.IdleSfx?.Key ?? "-"}  Loop {m?.LoopSfx?.Key ?? "-"}  Death {m?.DeathSfx?.Key ?? "-"}\n" +
                $"{serviceText}  더미 {dummies.Count}\n" +
                $"마지막: {lastAction}\n" +
                "[ ] 선택  E 더미1  M 더미12  A 공격  D 처치  C 군중20사망  H 명중  K 처치  G 샷건  X 수류탄  P 플레이어피격\n" +
                "B 보스경고  V 보스등장  1~6 보스공격  S 페이즈  R 라운드  O 게임오버  N 정리";
            GUI.Label(new Rect(20f, 20f, 1400f, 160f), text, statusStyle);
        }

        private void SelectMonster(int step)
        {
            int count = DataManager.Monsters.All.Count;
            if (count == 0) return;
            monsterIndex = (monsterIndex + step + count) % count;
            lastAction = $"선택: {Selected.Name}";
        }

        /// <summary>더미 생성 → MonsterSpawnedEvent 로 Emitter 연결 (팩토리가 할 일을 대신)</summary>
        private void SpawnDummies(int count)
        {
            var def = Selected;
            if (def == null) return;

            float baseAngle = Random.Range(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"SoundDummy {def.Key}";
                Destroy(go.GetComponent<Collider>());
                go.AddComponent<MonsterSoundEmitter>();

                // 무리(Herd)처럼 한 방향에 모으되 거리는 넓게 → 가까운 것만 루프가 들리는지 확인
                var dummy = new Dummy
                {
                    Go = go,
                    Def = def,
                    Radius = Random.Range(dummyRadius.x, dummyRadius.y),
                    Angle = count == 1 ? Random.Range(0f, 360f) : baseAngle + Random.Range(-20f, 20f),
                };
                dummies.Add(dummy);
                PlaceDummy(dummy);
                EventBus<MonsterSpawnedEvent>.Publish(new MonsterSpawnedEvent(def, go));
            }
            lastAction = $"{def.Name} 더미 {count}마리 생성 (Loop {def.LoopSfx?.Key ?? "없음"})";
        }

        private void MoveDummies()
        {
            foreach (var d in dummies)
            {
                d.Angle += dummyOrbitSpeed * Time.deltaTime;
                PlaceDummy(d);
            }
        }

        private void PlaceDummy(Dummy d)
        {
            float rad = d.Angle * Mathf.Deg2Rad;
            float height = d.Def.FlyHeight > 0f ? d.Def.FlyHeight : 0.5f;
            d.Go.transform.position = Center + new Vector3(Mathf.Sin(rad) * d.Radius, height - 1f, Mathf.Cos(rad) * d.Radius);
        }

        private void PlayAttacks()
        {
            if (dummies.Count == 0)
            {
                var def = Selected;
                var attack = def != null && def.BasicAttacks.Count > 0 ? def.BasicAttacks[0] : null;
                Sound()?.PlaySfx(attack?.Sfx, TestPoint());
                lastAction = $"공격음 {attack?.Sfx?.Key ?? "없음"}";
                return;
            }
            foreach (var d in dummies) d.Go.GetComponent<MonsterSoundEmitter>().PlayAttackSfx("");
            lastAction = $"더미 {dummies.Count}마리 공격음";
        }

        /// <summary>가장 가까운 더미 처치. 실제 몬스터처럼 Silence → MonsterDiedEvent → 제거</summary>
        private void KillDummy()
        {
            if (dummies.Count == 0)
            {
                Publish("사망 (더미 없음)", () => EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(Selected, TestPoint(), null)));
                return;
            }
            Vector3 c = Center;
            dummies.Sort((a, b) => (a.Go.transform.position - c).sqrMagnitude.CompareTo((b.Go.transform.position - c).sqrMagnitude));
            var d = dummies[0];
            dummies.RemoveAt(0);
            d.Go.GetComponent<MonsterSoundEmitter>().Silence();
            EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(d.Def, d.Go.transform.position, d.Go));
            Destroy(d.Go);
            lastAction = $"{d.Def.Name} 처치 (사망음 {d.Def.DeathSfx?.Key ?? "없음"})";
        }

        private void KillCrowd(int count)
        {
            var def = Selected;
            for (int i = 0; i < count; i++)
                EventBus<MonsterDiedEvent>.Publish(new MonsterDiedEvent(def, RandomPoint(), null));
            lastAction = $"{def?.Name} {count}마리 동시 사망";
        }

        private void PublishHits(int count, DamageKind kind, bool isKill, string label)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 point = TestPoint() + Random.insideUnitSphere * 0.5f;
                var info = new DamageInfo(10, kind, Faction.Player, gameObject, point, (point - Center).normalized);
                EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(null, info, isKill));
            }
            lastAction = label;
        }

        private void PublishPlayerHit()
        {
            var info = new DamageInfo(5, DamageKind.MonsterAttack, Faction.Monster, gameObject, Center, Vector3.forward);
            Publish("플레이어 피격", () => EventBus<PlayerDamagedEvent>.Publish(new PlayerDamagedEvent(info)));
        }

        private void PublishBossWarning()
        {
            if (DataManager.Monsters.TryGet(bossMonsterId, out var boss) == false) return;
            Publish("보스 경고", () => EventBus<BossWarningEvent>.Publish(new BossWarningEvent(boss)));
        }

        private void PublishBossAppeared()
        {
            if (DataManager.Monsters.TryGet(bossMonsterId, out var boss) == false) return;
            bossStage = 0;
            Publish("보스 등장", () => EventBus<BossAppearedEvent>.Publish(new BossAppearedEvent(boss, null)));
        }

        /// <summary>보스 공격 index 번째의 예고 단계 발행</summary>
        private void PublishBossSkill(int index)
        {
            if (DataManager.Monsters.TryGet(bossMonsterId, out var boss) == false || index >= boss.Attacks.Count) return;
            var attack = boss.Attacks[index];
            Publish($"보스 {attack.Name} 예고 ({attack.Sfx?.Key ?? "소리 없음"})",
                () => EventBus<BossSkillPhaseChangedEvent>.Publish(new BossSkillPhaseChangedEvent(attack, SkillPhase.Windup)));
        }

        private void PublishBossStage()
        {
            int stage = ++bossStage;
            Publish($"보스 페이즈 {stage}", () => EventBus<BossDamageStageChangedEvent>.Publish(new BossDamageStageChangedEvent(stage)));
        }

        private void Cleanup()
        {
            foreach (var d in dummies) Destroy(d.Go);
            dummies.Clear();
            Publish("라운드 정리", () => EventBus<RoundCleanupEvent>.Publish(new RoundCleanupEvent(false)));
        }

        private void Publish(string label, System.Action publish)
        {
            lastAction = label;
            publish();
        }

        private static ISoundService Sound() => ServiceLocator.TryGet<ISoundService>(out var s) ? s : null;

        /// <summary>시점 앞쪽 testDistance 지점</summary>
        private Vector3 TestPoint()
        {
            var forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            return Center + forward * testDistance;
        }

        private Vector3 RandomPoint()
        {
            var circle = Random.insideUnitCircle.normalized * Random.Range(dummyRadius.x, dummyRadius.y);
            return Center + new Vector3(circle.x, 0f, circle.y);
        }
    }
}
