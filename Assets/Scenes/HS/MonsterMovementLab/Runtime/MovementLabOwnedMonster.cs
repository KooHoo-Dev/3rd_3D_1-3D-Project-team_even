using UnityEngine;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>실험실이 만든 몬스터만 일괄 정리하기 위한 소유 표식.</summary>
    public sealed class MovementLabOwnedMonster : MonoBehaviour
    {
        private MovementProfile ownedProfile;
        public void SetProfile(MovementProfile profile) => ownedProfile = profile;
        private void OnDestroy()
        {
            if (ownedProfile != null) Destroy(ownedProfile);
        }
    }
}
