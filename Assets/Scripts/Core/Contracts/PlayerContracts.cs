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
    /// 플레이어를 한 점으로 끌어당긴다 (보스 패턴 등). ServiceLocator 에 등록된다. (ServiceLocator.Get&lt;IPlayerPullable&gt;())
    ///  - 핸들 방식: StartPull 의 반환값을 StopPull 에 넘긴다. 여러 개가 겹치면 합쳐서 적용 (플레이어 쪽 상한 있음)
    ///  - 패턴이 끝나면 반드시 StopPull. 보스가 도중에 사라질 때를 위해 OnDisable 에서도 부를 것
    ///    (Anchor 가 파괴되거나 Duration 이 지나면 저절로 끝난다)
    ///  - 플레이어가 쓰러짐·부활 연출·벽넘기 중에는 적용되지 않고(목록은 유지), 처음부터 재시작하면 모두 지워진다
    /// </summary>
    public interface IPlayerPullable
    {
        /// <summary>끌어당김 시작. 반환값 = StopPull 에 넘길 핸들 (0 = 거부: Speed 0 이하)</summary>
        int StartPull(in PlayerPullSettings settings);
        /// <summary>중심을 이 위치로 옮긴다 (Anchor 를 쓰던 중이면 Anchor 대신 이 위치)</summary>
        void SetPullCenter(int handle, Vector3 worldCenter);
        /// <summary>fadeOut 동안 약해지며 끝난다 (0 = 바로). 이미 끝난 핸들이면 아무 일 없음</summary>
        void StopPull(int handle, float fadeOut = 0.2f);
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
