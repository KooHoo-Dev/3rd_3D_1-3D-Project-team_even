using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 라운드 군중전의 스테이지 진행 (씬에 1개). 라운드 경과 시간을 재고, 그 라운드 스테이지를 각 StartTime 에 시작한다.
    /// RoundSequencer가 StartRound/StopRound를 직접 호출하고, 실행 중 스테이지를 매 프레임 Tick한다.
    /// </summary>
    /// <remarks>
    /// 다음 스테이지가 시작돼도 이전 스테이지를 멈추지 않는다. 이전 스테이지는 스스로 Finished 를 낼 때까지 남은 소환을 이어 간다.
    /// 그래서 WaveStageDef.EndTime 은 쓰지 않는다. 보스는 다루지 않는다 (보스전 전이 때 군중 소환을 멈추기만 한다).
    /// Time.deltaTime 으로 시간을 재므로 강화 화면(timeScale 0)에서 라운드 시간과 소환이 함께 멈춘다.
    /// </remarks>
    public sealed class MonsterWaveDirector : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("스테이지 시작 직전 WarmupStage 와 소환 팩토리 둘 다 이 풀을 쓴다")]
        [SerializeField] private MonsterPool monsterPool;

        // ── 라운드 진행 ──
        private bool isRoundRunning;
        private int roundNo;              // 로그용
        private float roundElapsedTime;   // 초, 라운드 시작 기준 (WaveStageDef.StartTime 과 같은 기준)
        private List<WaveStageDef> roundStages = new List<WaveStageDef>();  // 이번 라운드 스테이지, StartTime 순
        private int nextStageIndex;       // 다음에 시작할 roundStages 인덱스. Count = 다 시작함
        private readonly List<MonsterWaveStage> runningStages = new List<MonsterWaveStage>();

        /// <summary>전체 라운드 정리를 중단 안전망으로 구독한다. 시작·보스 전이는 시퀀서가 직접 호출한다.</summary>
        private void OnEnable()
        {
            EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);
        }

        /// <summary>구독을 해제하고 남은 소환을 취소한다. 다시 켜도 자동으로 이어 가지 않는다</summary>
        private void OnDisable()
        {
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            StopRound();
        }

        private void Awake()
        {
            if (monsterPool == null)
                monsterPool = FindAnyObjectByType<MonsterPool>();
        }

        /// <summary>라운드 정리(다음 라운드·재시작) 때 남은 소환을 모두 취소한다. FullReset 은 구분하지 않는다</summary>
        private void OnRoundCleanup(RoundCleanupEvent e) => StopRound();

        /// <summary>
        /// 라운드의 군중전 스테이지 진행을 0초부터 새로 시작한다. 진행 중이던 라운드는 버린다.
        /// StartTime 0인 스테이지는 이 호출 안에서 바로 시작된다. RoundSequencer 또는 Lab이 직접 부른다.
        /// </summary>
        /// <param name="roundNumber">라운드 번호. 이 RoundNo 의 스테이지가 없으면 경고만 남기고 시작하지 않는다</param>
        public void StartRound(int roundNumber)
        {
            StopRound();

            List<WaveStageDef> stagesOfRound = FindStagesOfRound(roundNumber);
            if (stagesOfRound.Count == 0)
            {
                // 끝 신호를 줄 스테이지가 없어 진행 중으로 두면 영원히 끝나지 않는다
                Debug.LogWarning($"[MonsterWaveDirector] 라운드 {roundNumber} 스테이지 데이터 없음. 군중 소환 안 함", this);
                return;
            }

            roundNo = roundNumber;
            roundStages = stagesOfRound;
            roundElapsedTime = 0f;
            nextStageIndex = 0;
            isRoundRunning = true;

            StartDueStages();
        }

        /// <summary>
        /// 실행 중 스테이지의 남은 소환과 아직 시작 안 한 스테이지를 모두 취소한다. 이미 나온 몬스터는 그대로 둔다.
        /// 라운드 정리·보스전 전이·비활성화 때 불린다. 스테이지 Finished 는 오지 않는다.
        /// </summary>
        public void StopRound()
        {
            isRoundRunning = false;
            foreach (MonsterWaveStage stage in runningStages) stage.Stop();
            runningStages.Clear();
        }

        /// <summary>
        /// 라운드 시간을 진행하고, 실행 중 스테이지를 Tick 한 뒤 시작 시각이 된 스테이지를 시작한다.
        /// </summary>
        /// <remarks>
        /// Tick → 시작 순서라서 새 스테이지의 첫 Tick 은 다음 프레임이다.
        /// </remarks>
        private void Update()
        {
            if (isRoundRunning == false) return;

            roundElapsedTime += Time.deltaTime;
            TickRunningStages(Time.deltaTime);
            StartDueStages();
        }

        /// <summary>
        /// 실행 중 스테이지에 시간을 넘긴다. 끝난 스테이지는 Finished 신호로 OnStageFinished 가 뺀다.
        /// 뒤에서부터 돌아서, 도중에 목록에서 빠져도 아직 안 돈 앞쪽 인덱스는 그대로다.
        /// </summary>
        /// <param name="deltaTime">지난 시간 (초)</param>
        private void TickRunningStages(float deltaTime)
        {
            for (int i = runningStages.Count - 1; i >= 0; i--) runningStages[i].Tick(deltaTime);
        }

        /// <summary>
        /// StartTime 이 지난 스테이지를 순서대로 시작한다. 한 프레임에 여러 개가 지났으면 몰아서 시작한다.
        /// </summary>
        private void StartDueStages()
        {
            while (nextStageIndex < roundStages.Count && roundStages[nextStageIndex].StartTime <= roundElapsedTime)
            {
                StartStage(roundStages[nextStageIndex]);
                nextStageIndex++;
            }
        }

        /// <summary>
        /// 스테이지 몫의 몬스터를 미리 만든 뒤(같은 프레임) 스테이지 실행을 만들고 Finished 를 구독한다.
        /// </summary>
        /// <param name="stage">시작할 스테이지 행</param>
        private void StartStage(WaveStageDef stage)
        {
            monsterPool.WarmupStage(stage);

            var runningStage = new MonsterWaveStage(stage, monsterPool, ServiceLocator.Get<ISpawnZoneProvider>());
            runningStage.Finished += OnStageFinished;
            runningStages.Add(runningStage);
            Debug.Log($"[MonsterWaveDirector] 라운드 {roundNo} 스테이지 {stage.Id} 시작 (라운드 {roundElapsedTime:0.0}초)", this);
        }

        /// <summary>
        /// 스테이지가 스스로 끝나면 목록에서 빼고, 라운드 군중전이 끝났는지 본다. 스테이지 Tick 안에서 불린다.
        /// </summary>
        /// <param name="stage">방금 끝난 스테이지</param>
        private void OnStageFinished(MonsterWaveStage stage)
        {
            runningStages.Remove(stage);
            EndHordePhaseIfAllDone();
        }

        /// <summary>
        /// 스테이지를 다 시작했고 실행 중 스테이지가 0개면 라운드 진행을 끝낸다. 이후 Update 는 아무것도 하지 않는다.
        /// </summary>
        private void EndHordePhaseIfAllDone()
        {
            bool hasStartedAllStages = nextStageIndex >= roundStages.Count;
            bool hasNoRunningStage = runningStages.Count == 0;
            if (hasStartedAllStages == false) return;
            if (hasNoRunningStage == false) return;

            isRoundRunning = false;
            Debug.Log($"[MonsterWaveDirector] 라운드 {roundNo} 군중전 소환 끝", this);
        }

        /// <summary>
        /// 라운드 번호의 스테이지를 StartTime 순으로 모은다.
        /// </summary>
        /// <param name="roundNumber">WaveStageDef.RoundNo 와 비교할 라운드 번호</param>
        /// <returns>그 라운드 스테이지의 새 목록 (StartTime 순). 없으면 빈 목록</returns>
        private static List<WaveStageDef> FindStagesOfRound(int roundNumber)
        {
            var stagesOfRound = new List<WaveStageDef>();
            foreach (WaveStageDef stage in DataManager.WaveStages.All)
            {
                if (stage.RoundNo == roundNumber) stagesOfRound.Add(stage);
            }
            stagesOfRound.Sort((a, b) => a.StartTime.CompareTo(b.StartTime));
            return stagesOfRound;
        }
    }
}
