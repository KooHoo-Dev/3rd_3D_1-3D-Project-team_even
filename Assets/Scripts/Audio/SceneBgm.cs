using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// GameSoundRouter 가 없는 씬(메인 메뉴)의 배경음. 씬 시작 때 한 번 재생한다.
    /// 라우터가 있는 씬은 GameSoundRouter.sceneBgmKey 를 쓴다 (강화 화면 곡과의 전환을 라우터가 함께 결정해야 하므로)
    /// </summary>
    public sealed class SceneBgm : MonoBehaviour
    {
        [Tooltip("SoundTable 키 (Category Bgm)")]
        [SerializeField] private string bgmKey = "bgm_menu";

        private void Start()
        {
            if (string.IsNullOrWhiteSpace(bgmKey)) return;
            if (ServiceLocator.TryGet<ISoundService>(out var sound)) sound.PlayBgm(bgmKey);
            else Debug.LogWarning("[SceneBgm] 씬에 SoundService 가 없어 배경음을 재생하지 않습니다", this);
        }
    }
}
