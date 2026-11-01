namespace HybridShooter.Core.Contracts
{
    /// <summary>
    /// 연출(시네마틱) 동안 플레이어를 멈춘다 (보스 최후 연출 등). ServiceLocator 에 등록된다. (ServiceLocator.Get&lt;IPlayerCinematic&gt;())
    ///  - 켜면 이동·사격·시선 입력을 막고 피해를 받지 않는다. 메뉴·게임 흐름 입력 차단과는 별도 플래그
    /// </summary>
    public interface IPlayerCinematic
    {
        void SetCinematic(bool active);
    }
}
