using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 공격 설정 컴포넌트(PounceSettings, ChargeSettings 등)의 공통 상위 클래스.
    /// MonsterController 가 이 타입으로 몬스터에 붙은 설정을 모두 모아 MonsterAttackFactory 에 넘기고,
    /// 팩토리는 공격마다 필요한 설정 타입만 골라 꺼낸다.
    /// </summary>
    /// <remarks>공통 멤버는 없다. "공격 설정 컴포넌트"라는 분류로만 쓴다.</remarks>
    public abstract class MonsterAttackSettingsComponent : MonoBehaviour
    {
    }
}
