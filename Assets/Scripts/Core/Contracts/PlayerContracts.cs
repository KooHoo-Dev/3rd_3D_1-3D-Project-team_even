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
        // Behaviour 브랜치와의 계약 호환. 실제 플레이어 등록/연동은 후속 작업에서 연결한다.
        Vector3 Position { get; }
        /// <summary>몬스터가 조준할 지점 (가슴 높이)</summary>
        Transform AimPoint { get; }
        /// <summary>현재 시야(카메라) 트랜스폼. 침투형의 "시야 밖" 판정용 (BL-012)</summary>
        Transform ViewPoint { get; }
        bool IsAlive { get; }
    }

    /// <summary>회복 팩 → 플레이어 (BL-006, BL-019). 가득 차서 못 먹으면 false</summary>
    public interface IHealable
    {
        bool Heal(int amount);
    }

    /// <summary>수류탄 보급 → 플레이어 (BL-019). 최대치라 못 먹으면 false</summary>
    public interface IGrenadeReceiver
    {
        int Grenades { get; }
        int MaxGrenades { get; }
        bool AddGrenades(int amount);
    }
}
