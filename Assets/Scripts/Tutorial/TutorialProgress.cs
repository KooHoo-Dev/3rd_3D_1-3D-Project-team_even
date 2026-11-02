using UnityEngine;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 튜토리얼 완료 기록과 씬 이름. 빌드 흐름: 메인 메뉴 → 인트로 → (완료 기록이 없으면) 튜토리얼 → 게임.
    /// 기록은 PlayerPrefs 하나뿐이다 (씬을 넘는 런타임 값). 게이트 통과·건너뛰기 모두 완료로 기록한다
    /// </summary>
    public static class TutorialProgress
    {
        public const string PrefsKey = "Tutorial.Done";
        /// <summary>Build Settings 에 등록된 튜토리얼 씬 이름</summary>
        public const string SceneName = "TutorialMap";

        public static bool IsDone => PlayerPrefs.GetInt(PrefsKey, 0) == 1;

        public static void MarkDone()
        {
            PlayerPrefs.SetInt(PrefsKey, 1);
            PlayerPrefs.Save();
        }

        /// <summary>인트로 다음 씬: 처음이면 튜토리얼, 이미 끝냈으면 gameSceneName</summary>
        public static string NextSceneAfterIntro(string gameSceneName) => IsDone ? gameSceneName : SceneName;
    }
}
