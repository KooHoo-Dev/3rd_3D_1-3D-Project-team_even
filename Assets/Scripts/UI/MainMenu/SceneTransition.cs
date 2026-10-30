using System.Threading;
using Cysharp.Threading.Tasks;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>
    /// 씬 전환 (Boot → MainMenu, MainMenu → 게임). SoundService 는 씬마다 새로 생기므로 BGM 이 씬을 넘어 이어지지 않는다.
    /// 그대로 로드하면 BGM 이 툭 끊기므로, BGM 페이드아웃과 다음 씬 미리 로드를 동시에 하고 둘 다 끝나면 활성화한다
    /// </summary>
    public static class SceneTransition
    {
        /// <summary>
        /// sceneName 을 미리 로드하면서 BGM 을 끄고, minWait 초(= SoundService BGM 페이드 시간 이상 권장)와 로드가 모두 끝나면 활성화.
        /// 화면 연출(암전 등)은 부르는 쪽이 이 대기 시간 안에 맞춘다
        /// </summary>
        public static async UniTask LoadAsync(string sceneName, float minWait, CancellationToken token = default)
        {
            if (Application.CanStreamedLevelBeLoaded(sceneName) == false)
            {
                Debug.LogError($"[SceneTransition] '{sceneName}' 이 Build Settings 에 없습니다");
                return;
            }

            if (ServiceLocator.TryGet<ISoundService>(out var sound)) sound.StopBgm();

            var load = SceneManager.LoadSceneAsync(sceneName);
            load.allowSceneActivation = false;
            float until = Time.unscaledTime + minWait;
            // allowSceneActivation = false 면 progress 는 0.9 에서 멈춘다
            await UniTask.WaitUntil(() => load.progress >= 0.9f && Time.unscaledTime >= until, cancellationToken: token);
            load.allowSceneActivation = true;
        }
    }
}
