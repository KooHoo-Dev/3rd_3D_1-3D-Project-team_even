using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 스포너 한 줄(WaveSpawnerDef)의 실행 1회. "StartDelay 초 뒤부터 SpawnInterval 간격으로 Monster 를 Count 마리, SpawnPattern 위치에" 소환한다.
    /// 웨이브(B4)가 만들어 매 프레임 Tick 으로 시간을 넘기고, IsFinished 가 되면 버린다.
    /// </summary>
    /// <remarks>
    /// 스스로 시간을 재지 않는다. Tick 에 Time.deltaTime 을 넘기면 강화 화면(timeScale 0)에서 저절로 멈춘다.
    /// 소환한 몬스터는 기억하지 않는다. 제거는 MonsterPool.WipeAll·RoundCleanupEvent 몫이다.
    /// 첫 스폰 개체는 소환한 프레임에 StateMachine 이 null 이다 (MonsterPool.Spawn 참고). 그래서 반환값을 쓰지 않는다.
    /// </remarks>
    public sealed class MonsterSpawner
    {
        private const int MaxPoseAttempts = 5;  // 1마리 소환에 위치를 다시 뽑는 최대 횟수

        private readonly WaveSpawnerDef spawner;
        private readonly IMonsterFactory monsterFactory;
        private readonly ISpawnZoneProvider spawnZones;
        private readonly int groupSeed;  // Herd 방향. 실행마다 무작위라 라운드마다 방향이 바뀐다

        // ── 진행 상태 ──
        private float elapsedTime;  // 초, 이 실행이 시작된 뒤 지난 시간
        private int spawnedCount;   // 소환 차례를 처리한 수 (위치를 못 구해 건너뛴 것 포함)
        private bool isStopped;

        /// <summary>Count 마리를 다 처리했거나 Stop 됐으면 true. 웨이브가 이 스포너를 버려도 된다는 뜻</summary>
        public bool IsFinished => isStopped || spawnedCount >= spawner.Count;

        /// <summary>
        /// 스포너 한 줄의 실행을 준비한다. 첫 소환은 다음 Tick 부터다.
        /// </summary>
        /// <param name="spawner">실행할 스포너 행 (Monster 가 Link 된 것)</param>
        /// <param name="monsterFactory">소환에 쓸 팩토리 (보통 ServiceLocator 의 MonsterPool)</param>
        /// <param name="spawnZones">소환 위치 계산 (보통 ServiceLocator 의 RingSpawnZoneProvider)</param>
        public MonsterSpawner(WaveSpawnerDef spawner, IMonsterFactory monsterFactory, ISpawnZoneProvider spawnZones)
        {
            this.spawner = spawner;
            this.monsterFactory = monsterFactory;
            this.spawnZones = spawnZones;
            groupSeed = Random.Range(int.MinValue, int.MaxValue);
        }

        /// <summary>
        /// 시간을 진행하고, 소환 시각이 지난 몬스터를 소환한다. 웨이브가 매 프레임 부른다.
        /// </summary>
        /// <param name="deltaTime">지난 시간 (초). 보통 Time.deltaTime</param>
        public void Tick(float deltaTime)
        {
            if (IsFinished) return;

            elapsedTime += deltaTime;
            SpawnDueMonsters();
        }

        /// <summary>
        /// 남은 소환을 취소한다. 이미 소환된 몬스터는 그대로 둔다. 스테이지 중단·라운드 종료 때 소유자가 부른다.
        /// </summary>
        public void Stop() => isStopped = true;

        /// <summary>
        /// 소환 시각이 지난 차례를 모두 처리한다. 한 프레임에 여러 차례가 지났으면 몰아서 소환한다 (SpawnInterval 0 = 전부 한 번에).
        /// </summary>
        private void SpawnDueMonsters()
        {
            while (spawnedCount < spawner.Count && elapsedTime >= GetSpawnTime(spawnedCount))
            {
                SpawnOne();
                spawnedCount++;
            }
        }

        /// <summary>
        /// index 번째 몬스터의 소환 시각.
        /// </summary>
        /// <param name="index">소환 차례 (0부터)</param>
        /// <returns>실행 시작 기준 초</returns>
        private float GetSpawnTime(int index) => spawner.StartDelay + index * spawner.SpawnInterval;

        /// <summary>
        /// 1마리 소환. 위치를 MaxPoseAttempts 번까지 다시 뽑고, 다 실패하면 경고를 남기고 이 차례를 건너뛴다.
        /// </summary>
        private void SpawnOne()
        {
            for (int attempt = 0; attempt < MaxPoseAttempts; attempt++)
            {
                bool hasPose = spawnZones.TryGetSpawnPose(spawner.SpawnPattern, groupSeed, out Vector3 position, out Quaternion rotation);
                if (hasPose == false) continue;

                monsterFactory.Spawn(spawner.Monster, position, rotation);
                return;
            }

            Debug.LogWarning($"[MonsterSpawner] 스포너 {spawner.Id}: 위치를 {MaxPoseAttempts}번 못 구해 {spawnedCount + 1}번째 몬스터를 건너뜀");
        }
    }
}
