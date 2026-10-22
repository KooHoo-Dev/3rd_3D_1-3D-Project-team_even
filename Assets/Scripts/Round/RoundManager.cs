using System;
using System.Collections.Generic;
using HybridShooter.Supply;
using UnityEngine;

namespace HybridShooter.Round
{
    public sealed class RoundManager : MonoBehaviour
    {
        private enum TimedSituation
        {
            StartSupplyDropCycle
        }
        
        [Serializable]
        private sealed class TimelineEntry
        {
            [Min(0f)] public float timeSeconds;
            public TimedSituation situation;
        }
        
        [Header("References")]
        [SerializeField] private SupplyDropper supplyDropper;

        [Header("Round Timeline")]
        [SerializeField] private List<TimelineEntry> timeline = new List<TimelineEntry>();

        private float elapsedSeconds;
        private int nextTimelineIndex;
        private bool isRoundRunning;

        // 라운드 시작 담당 코드에서 호출
        public void StartRound()
        {
            if (isRoundRunning) return;

            if (timeline == null) timeline = new List<TimelineEntry>();

            timeline.RemoveAll(entry => entry == null);
            timeline.Sort((a, b) => a.timeSeconds.CompareTo(b.timeSeconds));

            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();

            elapsedSeconds = 0f;
            nextTimelineIndex = 0;
            isRoundRunning = true;

            ProcessTimeline();
        }

        public void StopRound()
        {
            isRoundRunning = false;

            if (supplyDropper != null) supplyDropper.StopAutoDropCycle();
        }

        private void Update()
        {
            if (isRoundRunning == false) return;

            elapsedSeconds += Time.deltaTime;
            ProcessTimeline();
        }

        private void ProcessTimeline()
        {
            while (nextTimelineIndex < timeline.Count &&
                   elapsedSeconds >= timeline[nextTimelineIndex].timeSeconds)
            {
                TimedSituation situation = timeline[nextTimelineIndex].situation;

                // 실행 전에 인덱스를 이동해 중복 실행을 막는다.
                nextTimelineIndex++;

                Execute(situation);
            }
        }

        private void Execute(TimedSituation situation)
        {
            switch (situation)
            {
                case TimedSituation.StartSupplyDropCycle:
                    if (supplyDropper == null)
                    {
                        Debug.LogError("[RoundManager] S per가 연결되지 않았습니다.",this);
                        return;
                    }

                    supplyDropper.StartAutoDropCycle();
                    break;
            }
        }

        private void OnDisable()
        {
            StopRound();
        }
    }
}