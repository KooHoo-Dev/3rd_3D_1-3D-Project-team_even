using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 플레이어 조작 입력. 컨트롤러는 이 인터페이스만 본다 → 실제 입력(Input System)·AI·리플레이·테스트용 가짜로 교체 가능.
    /// </summary>
    public interface IPlayerInputSource
    {
        /// <summary>x = 좌우, y = 앞뒤 (-1 ~ 1)</summary>
        Vector2 Move { get; }

        /// <summary>이번 프레임 시점 회전량. 마우스 픽셀 단위 (스틱은 프레임 시간 반영 후 같은 단위로 환산됨)</summary>
        Vector2 Look { get; }

        bool SprintHeld { get; }

        /// <summary>점프 누름을 소비. 눌린 프레임에 true 를 한 번만 돌려준다</summary>
        bool ConsumeJump();

        /// <summary>구르기 누름을 소비</summary>
        bool ConsumeRoll();
    }
}
