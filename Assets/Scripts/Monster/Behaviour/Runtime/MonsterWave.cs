using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 웨이브 한 줄(WaveDef)의 실행 1회. 만들 때 자기 스포너를 전부 함께 시작하고, 스포너 사이 시차는 각 StartDelay 로만 생긴다.
    /// 스테이지(MonsterWaveStage)가 StartTime 에 만들어 매 프레임 Tick 으로 시간을 넘기고, Finished 를 받으면 목록에서 뺀다.
    /// </summary>
    /// <remarks>
    /// 시작 시각은 스테이지가 정한다. 웨이브는 "약 10초 안에 소환을 끝낸다"는 규칙을 코드로 강제하지 않는다 (데이터 작성 기준).
    /// </remarks>
    public sealed class MonsterWave
    {
        // ── 실행 중 ──
        private readonly List<MonsterSpawner> runningSpawners = new List<MonsterSpawner>();

        // 실행 중 스포너가 0개면 true. 상태 조회용 (부모는 Finished 로 안다)
        public bool IsFinished => runningSpawners.Count == 0;

        // 마지막 스포너가 끝난 Tick 에서 정확히 한 번 발행. Stop 때는 발행하지 않는다
        public event Action<MonsterWave> Finished;

        /// <summary>
        /// 웨이브의 스포너를 전부 만들어 함께 시작한다. 첫 소환은 다음 Tick 부터다.
        /// 같은 StartDelay 의 Nest 스포너들에는 둥지(SpawnNest) 하나를 나눠 줘서, 몬스터 종류가 달라도 한 지점 주위에서 나오게 한다.
        /// </summary>
        /// <param name="wave">실행할 웨이브 행 (Spawners 가 Link 된 것)</param>
        /// <param name="monsterFactory">스포너에 그대로 넘길 팩토리</param>
        /// <param name="spawnZones">스포너·둥지에 그대로 넘길 소환 위치 계산</param>
        public MonsterWave(WaveDef wave, IMonsterFactory monsterFactory, ISpawnZoneProvider spawnZones)
        {
            var nestsByStartDelay = new Dictionary<float, SpawnNest>();

            foreach (WaveSpawnerDef spawner in wave.Spawners)
            {
                SpawnNest nest = FindOrCreateNest(spawner, nestsByStartDelay, spawnZones);
                var runningSpawner = new MonsterSpawner(spawner, monsterFactory, spawnZones, nest);
                runningSpawner.Finished += OnSpawnerFinished;
                runningSpawners.Add(runningSpawner);
            }
        }

        /// <summary>
        /// Nest 스포너가 쓸 둥지를 찾는다. 같은 StartDelay 의 둥지가 이미 있으면 그것을, 없으면 새로 만들어 등록한다.
        /// </summary>
        /// <param name="spawner">스포너 행</param>
        /// <param name="nestsByStartDelay">이 웨이브에서 만든 둥지 (키 = StartDelay 초)</param>
        /// <param name="spawnZones">새 둥지에 넘길 소환 위치 계산</param>
        /// <returns>공유할 둥지. Nest 패턴이 아니면 null</returns>
        private static SpawnNest FindOrCreateNest(WaveSpawnerDef spawner, Dictionary<float, SpawnNest> nestsByStartDelay, ISpawnZoneProvider spawnZones)
        {
            if (spawner.SpawnPattern != SpawnPattern.Nest) return null;
            if (nestsByStartDelay.TryGetValue(spawner.StartDelay, out SpawnNest existingNest)) return existingNest;

            var nest = new SpawnNest(spawnZones);
            nestsByStartDelay.Add(spawner.StartDelay, nest);
            return nest;
        }

        /// <summary>
        /// 실행 중 스포너의 시간을 진행한다. 끝난 스포너는 Finished 신호로 OnSpawnerFinished 가 뺀다. 스테이지가 매 프레임 부른다.
        /// </summary>
        /// <param name="deltaTime">지난 시간 (초). 보통 Time.deltaTime</param>
        /// <remarks>
        /// 뒤에서부터 돈다. Tick 도중 OnSpawnerFinished 가 목록에서 빼도 아직 안 돈 앞쪽 인덱스는 그대로다.
        /// </remarks>
        public void Tick(float deltaTime)
        {
            for (int i = runningSpawners.Count - 1; i >= 0; i--) runningSpawners[i].Tick(deltaTime);
        }

        /// <summary>
        /// 실행 중 스포너의 남은 소환을 모두 취소한다. 이미 소환된 몬스터는 그대로 둔다. 스테이지 Stop 이 부른다.
        /// </summary>
        public void Stop()
        {
            foreach (MonsterSpawner spawner in runningSpawners) spawner.Stop();
            runningSpawners.Clear();
        }

        /// <summary>
        /// 스포너가 끝나면 목록에서 빼고, 마지막 스포너였으면 이 웨이브의 Finished 를 발행한다. 스포너 Tick 안에서 불린다.
        /// </summary>
        /// <param name="spawner">방금 끝난 스포너</param>
        private void OnSpawnerFinished(MonsterSpawner spawner)
        {
            runningSpawners.Remove(spawner);
            if (runningSpawners.Count > 0) return;

            Finished?.Invoke(this);
        }
    }
}
