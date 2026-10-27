using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Audio.Tester
{
    /// <summary>
    /// [테스트 씬 전용] 같은 오브젝트의 IPlayerTarget(PlayerController)을 ServiceLocator 에 등록해 몬스터가 플레이어를 찾게 한다.
    /// 지금 PlayerController 는 IPlayerTarget 을 구현만 하고 등록하지 않는다 (플레이어 담당에 전달). 등록이 생기면 이 컴포넌트는 필요 없다
    /// </summary>
    public sealed class SoundTestPlayerTarget : MonoBehaviour
    {
        private IPlayerTarget target;

        private void Awake() => target = GetComponent<IPlayerTarget>();

        private void OnEnable()
        {
            if (target != null && ServiceLocator.TryGet<IPlayerTarget>(out _) == false) ServiceLocator.Register(target);
        }

        private void OnDisable()
        {
            if (target != null) ServiceLocator.Unregister(target);
        }
    }
}
