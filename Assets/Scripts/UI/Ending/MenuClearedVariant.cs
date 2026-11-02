using System;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.UI.Ending
{
    /// <summary>
    /// 게임을 한 번이라도 클리어했으면(GameClearRecord) 메인 메뉴 대치 장면을 '새벽'으로 바꾼다.
    /// 시점은 엔딩보다 앞선 '구조선을 타고 떠나던 날 아침' (엔딩 크레딧에서 이 행성은 폭발한다 — 메뉴는 그 전의 장면)
    ///  - 몬스터 무리·보스·추락한 우주선·불씨·원경 섬광·달빛을 끄고, 새벽 해·볼륨·착륙한 구조선을 켠다
    ///  - 하늘·안개·환경광을 새벽으로 (하늘 셰이더의 붉은 눈 달 → 떠오르는 해)
    /// 메뉴 코드(MainMenuController·MenuBackdropDirector)는 건드리지 않는다. 그들보다 먼저 실행해(DefaultExecutionOrder)
    /// 무리를 끄므로 MenuBackdropDirector 가 몬스터를 모으지 않고 QUIT 포효도 생략된다.
    /// 장면은 Tools > Ending > Rebuild Menu Cleared Variant 가 디오라마와 별도 루트(ClearedVariant)에 만든다
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MenuClearedVariant : MonoBehaviour
    {
        [Tooltip("클리어 뒤 끄는 오브젝트 (디오라마의 무리·보스·추락선·달빛 등)")]
        [SerializeField] private GameObject[] hideWhenCleared;
        [Tooltip("클리어 뒤 켜는 루트 (새벽 해·볼륨·구조선). 평소에는 꺼 둔다")]
        [SerializeField] private GameObject dawnRoot;
        [SerializeField] private SceneEnvironment dawn;

        private void Awake()
        {
            Validate();
            bool cleared = GameClearRecord.HasCleared;
            dawnRoot.SetActive(cleared);
            if (cleared == false) return;

            foreach (var go in hideWhenCleared) go.SetActive(false);
            dawn.Apply();
        }

        /// <summary>디오라마를 다시 만들면 끌 오브젝트 참조가 끊긴다 → 클리어한 뒤에야 드러나지 않게 시작할 때 확인</summary>
        private void Validate()
        {
            if (dawnRoot == null || dawn.Skybox == null || dawn.Sun == null || Array.IndexOf(hideWhenCleared, null) >= 0)
                throw new InvalidOperationException("[MenuClearedVariant] 참조가 비어 있습니다 (메인 메뉴 디오라마를 다시 만들었다면). " +
                                                    "'Tools > Ending > Rebuild Menu Cleared Variant' 를 다시 실행해주세요.");
        }
    }
}
