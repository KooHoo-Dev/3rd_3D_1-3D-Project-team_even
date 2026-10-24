using UnityEngine;
using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>
    /// 실험실이 만든 몬스터 표식 + 이동 정책 교체 담당.
    /// 코어 MonsterMovement 는 정책·수치를 활성화 시점에 복사해 두므로, 정책을 바꾸려면 비활성화 → Configure → 활성화로 다시 켠다.
    /// Inspector 에서 profile 의 policyKind/수치를 바꿔도 감지해 같은 방식으로 반영한다.
    /// </summary>
    public sealed class MovementLabOwnedMonster : MonoBehaviour
    {
        private const float PollInterval = 0.25f;

        [SerializeField] private MovementProfile ownedProfile;

        private MonsterMovement movement;
        private CrowdCoordinator coordinator;   // null 이면 직접 추적 모드
        private IPlayerTarget directTarget;
        private MovementPolicyKind appliedKind;
        private MovementTuning appliedTuning;
        private float nextPoll;

        public MovementProfile Profile => ownedProfile;

        /// <summary>비활성 상태에서 MonsterMovement 를 붙인 직후, 활성화 전에 호출</summary>
        public void Initialize(MovementProfile profile, CrowdCoordinator crowd, IPlayerTarget target)
        {
            movement = GetComponent<MonsterMovement>();
            ownedProfile = profile;
            coordinator = crowd;
            directTarget = target;
            Configure();
        }

        /// <summary>새 기본 profile 로 정책을 바꾼다 (이전 profile 은 파괴)</summary>
        public void SetPolicy(MovementPolicyKind kind)
        {
            var old = ownedProfile;
            ownedProfile = MovementLabProfiles.CreateRuntime(kind);
            Reapply();
            Destroy(old);
        }

        private void Update()
        {
            if (ownedProfile == null) return;
            if (Time.time < nextPoll) return;
            nextPoll = Time.time + PollInterval;
            if (ownedProfile.PolicyKind != appliedKind || !ownedProfile.ToTuning().Equals(appliedTuning))
                Reapply();
        }

        private void Reapply()
        {
            movement.enabled = false;   // OnDisable: 군중 관리자 등록 해제 + 슬롯 반납
            Configure();
            movement.enabled = true;    // OnEnable: 새 정책 생성 + 재등록
        }

        private void Configure()
        {
            if (coordinator != null) movement.Configure(ownedProfile, coordinator);
            else movement.ConfigureDirect(ownedProfile, directTarget);
            appliedKind = ownedProfile.PolicyKind;
            appliedTuning = ownedProfile.ToTuning();
        }

        private void OnDestroy()
        {
            if (ownedProfile != null) Destroy(ownedProfile);
        }
    }
}
