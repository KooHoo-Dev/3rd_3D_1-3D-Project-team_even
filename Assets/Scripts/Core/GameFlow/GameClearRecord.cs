using UnityEngine;

namespace HybridShooter.Core.GameFlow
{
    /// <summary>
    /// 게임을 한 번이라도 끝까지 클리어했는지 (PlayerPrefs "Game.Cleared", 게임을 껐다 켜도 유지).
    ///  - 기록: 최종 보스 최후 연출(TyranopodeFinale)이 '작전 성공' 화면을 띄울 때 — 엔딩을 건너뛰거나 도중에 꺼도 클리어로 남는다
    ///  - 읽기: 메인 메뉴(MenuClearedVariant)가 새벽 버전으로 바뀐다
    /// 에디터에서는 Tools > Ending > 클리어 기록 초기화/설정 으로 바꿔 볼 수 있다
    /// </summary>
    public static class GameClearRecord
    {
        public const string PrefsKey = "Game.Cleared";

        public static bool HasCleared => PlayerPrefs.GetInt(PrefsKey, 0) == 1;

        public static void MarkCleared()
        {
            PlayerPrefs.SetInt(PrefsKey, 1);
            PlayerPrefs.Save();
        }

        public static void Reset()
        {
            PlayerPrefs.DeleteKey(PrefsKey);
            PlayerPrefs.Save();
        }
    }
}
