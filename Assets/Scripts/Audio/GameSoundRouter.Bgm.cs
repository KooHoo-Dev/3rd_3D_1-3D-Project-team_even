using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 배경음 결정. 이벤트 핸들러는 상태(기본 곡·강화 화면 여부)만 바꾸고, 실제 재생 요청은 LateUpdate 에서 한 프레임에 한 번만 한다
    /// (같은 프레임에 이벤트가 여러 개 와도 BgmPlayer 에는 마지막 결정 하나만 간다).
    ///   기본 곡  씬 시작 = sceneBgmKey · 라운드 시작 = RoundTable.CrowdBgm · 보스 등장 = RoundTable.BossBgm · 보스 처치·게임오버 = 없음
    ///   강화 화면 동안 upgradeBgmKey (비어 있으면 기본 곡 유지 = 튜토리얼). 보스 처치 뒤라면 클리어 효과음이 끝나도록 늦게 시작
    /// </summary>
    public sealed partial class GameSoundRouter
    {
        private RoundDef currentRound;
        private string contextBgmKey;
        private bool rewardBgmActive;
        private float upgradeBgmStartTime;
        private bool bossDefeatedThisRound;
        private string requestedBgmKey;

        /// <summary>강화 화면이면 업그레이드 곡(지연 중엔 무음), 아니면 기본 곡</summary>
        private string DesiredBgmKey
        {
            get
            {
                if (rewardBgmActive == false || string.IsNullOrEmpty(upgradeBgmKey)) return contextBgmKey;
                return Time.unscaledTime >= upgradeBgmStartTime ? upgradeBgmKey : null;
            }
        }

        private void StartSceneBgm() => contextBgmKey = NullIfEmpty(sceneBgmKey);

        private void LateUpdate()
        {
            string key = DesiredBgmKey;
            if (key == requestedBgmKey) return;
            requestedBgmKey = key;
            if (key == null) service.StopBgm();
            else service.PlayBgm(key);
        }

        private void SetRoundBgm(RoundDef round)
        {
            currentRound = round;
            bossDefeatedThisRound = false;
            contextBgmKey = round?.CrowdBgmKey ?? NullIfEmpty(crowdBgmKey);
        }

        private void SetBossBgm() => contextBgmKey = currentRound?.BossBgmKey ?? NullIfEmpty(bossBgmKey);

        private void ClearBgmForBossDefeated()
        {
            bossDefeatedThisRound = true;
            contextBgmKey = null;
        }

        private void SetRewardBgm(bool active)
        {
            if (active && rewardBgmActive == false)
                upgradeBgmStartTime = Time.unscaledTime + (bossDefeatedThisRound ? upgradeBgmDelayAfterBoss : 0f);
            rewardBgmActive = active;
        }

        /// <summary>게임오버 = 무음. 재시작(전체 정리)은 다음 라운드 시작 전까지 씬 기본 곡</summary>
        private void ClearBgmForGameOver() => contextBgmKey = null;

        private void ResetBgmForRestart()
        {
            currentRound = null;
            bossDefeatedThisRound = false;
            contextBgmKey = NullIfEmpty(sceneBgmKey);
        }

        private static string NullIfEmpty(string key) => string.IsNullOrWhiteSpace(key) ? null : key;
    }
}
