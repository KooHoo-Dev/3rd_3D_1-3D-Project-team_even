namespace HybridShooter.Core.Contracts
{
    /// <summary>
    /// 땅울림 → 플레이어 (거대 보스 Tyranopode 등). ServiceLocator 에 등록된다. (ServiceLocator.Get&lt;IPlayerGroundTremor&gt;())
    ///  - 땅에 서 있는 동안만 받는다: 공중(점프)·구르기 무적 중이면 무시하고 false
    ///  - 받으면 화면이 흔들리고, 총의 퍼짐에 extraSpread(도)가 더해졌다가 duration 동안 0 으로 줄어든다 (사격 자체는 막지 않는다)
    ///  - 여러 번 받으면 큰 쪽을 따른다 (누적되지 않음)
    /// </summary>
    public interface IPlayerGroundTremor
    {
        /// <param name="extraSpread">더할 퍼짐(도). 0 이하면 흔들림만</param>
        /// <param name="duration">퍼짐이 0 으로 돌아오는 시간(초)</param>
        /// <param name="shake">같이 줄 화면 흔들림 (Duration &gt; 0)</param>
        /// <returns>받았으면 true. 공중·구르기 무적·쓰러짐이면 false</returns>
        bool ApplyGroundTremor(float extraSpread, float duration, in CameraShakeSettings shake);
    }
}
