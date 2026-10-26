namespace HybridShooter.Player
{
    /// <summary>몸체 회전 방식. LockYaw = 카메라 방향에 고정(현재 1·3인칭 공통), FollowMove = 이동 방향을 바라봄(현재 미사용)</summary>
    public enum FacingMode { FollowMove, LockYaw }

    /// <summary>시점 모드</summary>
    public enum ViewMode { FirstPerson, ThirdPerson }
}
