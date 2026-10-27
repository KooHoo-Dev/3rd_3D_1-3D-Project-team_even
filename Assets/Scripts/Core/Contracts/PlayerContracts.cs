using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    // 플레이어(담당: 플레이어·무기)가 "구현"하고 다른 파트가 "사용"하는 계약.
    // 다른 파트는 PlayerController 같은 구체 클래스를 직접 참조하지 않는다.

    /// <summary>
    /// 몬스터 AI·보스·스폰이 참조할 플레이어 정보. ServiceLocator 에 등록된다. (ServiceLocator.Get&lt;IPlayerTarget&gt;())
    /// </summary>
    public interface IPlayerTarget
    {
        Transform Root { get; }
        /// <summary>현재 시야(카메라) 트랜스폼. 침투형의 "시야 밖" 판정용 (BL-012)</summary>
        Transform ViewPoint { get; }
        // /// <summary>몬스터가 조준할 지점 (가슴 높이)</summary>
        // Transform AimPoint { get; }
        // Velocity: 일단 지금 필요하지 않아서 제거
        public PlayerTrackingInfo PlayerTrackingInfo { get; }
    }

    public class PlayerTrackingInfo
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Quaternion Rotation;
        public PlayerTrackingInfo(){}
        public PlayerTrackingInfo(Vector3 pos, Vector3 forward, Quaternion rot)
        {
            Position = pos;
            Forward = forward;
            Rotation = rot;
        }
    }
    /// <summary>
    /// 플레이어 목숨. 난이도 설정·스테이지 클리어 보상 등이 쓴다. ServiceLocator 에 등록된다. (ServiceLocator.Get&lt;IPlayerLives&gt;())
    /// 플레이어가 등록된 뒤(예: RoundStartedEvent 이후)에 부른다. 변화는 PlayerLivesChangedEvent 로 알린다.
    ///  - 목숨이 남아 있으면 사망 후 자동 부활하며 1 소모, 마지막 목숨이면 최종 사망(PlayerFinalDeathEvent)
    ///  - 처음부터 재시작(RoundCleanupEvent.FullReset) 때: SetMaxLives 값은 유지, AddMaxLives 로 늘린 몫은 사라지고 현재 = 최대
    /// </summary>
    public interface IPlayerLives
    {
        /// <summary>남은 목숨 (지금 목숨 포함). 1 이면 다음 사망이 최종 사망</summary>
        int Lives { get; }
        int MaxLives { get; }

        /// <summary>최대 목숨을 정한다 (난이도). refill = true 면 현재도 최대로. 1 미만은 1</summary>
        void SetMaxLives(int max, bool refill);

        /// <summary>최대 목숨을 늘린다 (스테이지 클리어 보상). addLives = true 면 늘어난 만큼 현재도 늘린다</summary>
        void AddMaxLives(int amount, bool addLives);

        /// <summary>현재 목숨을 늘린다 (최대까지). 이미 최대라 못 늘리면 false</summary>
        bool AddLives(int amount);
    }

    /// <summary>
    /// 플레이어에게 외부 속도를 더하는 슬롯 (보스 흡입 등). ServiceLocator 에 등록된다. (ServiceLocator.Get&lt;IPlayerExternalVelocity&gt;())
    ///  - 소스(IPlayerVelocitySource)를 붙이면 플레이어가 매 물리 프레임 그 소스를 불러 속도를 더한다. 여러 개면 모두 합친다
    ///  - 보스 흡입은 미리 만든 InhaleSource.Start(...) 를 쓰면 붙이기·끝내기·종료 이유까지 처리된다
    ///  - 플레이어가 쓰러짐·부활 연출·벽넘기 중에는 적용하지 않고(붙은 상태 유지), 처음부터 재시작하면 모두 뗀다
    /// </summary>
    public interface IPlayerExternalVelocity
    {
        /// <summary>소스를 붙인다. null 이거나 이미 붙어 있으면 false</summary>
        bool AddVelocitySource(IPlayerVelocitySource source);
        /// <summary>소스를 뗀다 (OnRemovedByPlayer 는 불리지 않음). 붙어 있지 않으면 아무 일 없음</summary>
        void RemoveVelocitySource(IPlayerVelocitySource source);
    }

    /// <summary>회복 팩 → 플레이어 (BL-006, BL-019). 가득 차서 못 먹으면 false</summary>
    public interface IHealable
    {
        bool Heal(int amount);
    }

    /// <summary>
    /// 아이템 보급 → 플레이어 (BL-019). 수류탄·회복 팩 "보유 개수"를 늘린다. 최대치라 못 먹으면 false
    /// </summary>
    public interface IItemUser
    {
        int Grenades { get; }
        int MaxGrenades { get; }
        bool AddGrenades(int amount);

        int HealPacks { get; }
        int MaxHealPacks { get; }
        bool AddHealPacks(int amount);
    }
}
