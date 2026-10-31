using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;

namespace HybridShooter.Player
{
    /// <summary>연출 중 입력 차단·무적 (IPlayerCinematic, 보스 최후 연출)</summary>
    public partial class PlayerController : IPlayerCinematic
    {
        private bool cinematicActive;

        public bool IsInCinematic => cinematicActive;

        public void SetCinematic(bool active)
        {
            if (cinematicActive == active) return;
            cinematicActive = active;
            ApplyInputBlock();
        }

        private void SubscribeCinematic() => ServiceLocator.Register<IPlayerCinematic>(this);

        private void UnsubscribeCinematic()
        {
            ServiceLocator.Unregister<IPlayerCinematic>(this);
            cinematicActive = false;
        }
    }
}
