namespace HybridShooter.Core.GameFlow
{
    /// <summary>
    /// 게임 진행 상태 (E9). 라운드 흐름:
    /// Ready → CrowdBattle(1인칭, 약 3분) → BossIntro(예고·3인칭 전환·등장 공격) → BossBattle → Reward(강화 선택) → 다음 라운드 CrowdBattle
    /// 어느 상태에서든 플레이어 사망 → GameOver → (재시작) Ready
    /// </summary>
    public enum GameState
    {
        Ready,
        CrowdBattle,
        BossIntro,
        BossBattle,
        Reward,
        GameOver,
        Paused,
    }

    /// <summary>플레이어 카메라 시점 (BL-002, BL-022)</summary>
    public enum CameraMode { FirstPerson, ThirdPerson }

    /// <summary>보스 패턴 진행 단계 (BL-027: 준비 → 공격 → 회복)</summary>
    public enum SkillPhase { Windup, Active, Recover, Idle }
}
