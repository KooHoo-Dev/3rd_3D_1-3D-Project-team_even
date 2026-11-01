using HybridShooter.Round;
using HybridShooter.Weapon;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HybridShooter.UI
{
    /// <summary>
    /// 게임 흐름 화면을 씬·프리팹 수정 없이 붙인다. 씬이 열릴 때마다(맵이 바뀌어도) 확인한다.
    ///  - 조건: 그 씬에 RunDirector(라운드 진행)와 GunSlot(플레이어 무기)이 있을 때만. 메인 메뉴·몬스터 실험 씬 등은 그대로
    ///  - 붙이는 것: LoadoutPanel(무기 선택) · UnlockToastHud(해금 알림) · RoundSupplyToastHud(다음 라운드 지급 알림) · SupplyWeaponPickupHud(보급 무기 획득 카드) · GameOverPanel(게임 오버)
    ///    · GameFlowDebugPanel(에디터·개발 빌드)
    ///  - 이미 씬에 같은 화면을 직접 둔 경우 그것을 쓴다 (예: openOnStart 를 끈 LoadoutPanel 로 이 씬만 끄기)
    /// 실행 시점: sceneLoaded = 씬 오브젝트 Awake·OnEnable 뒤, Start 앞 → RunDirector.Start 가 무기 선택 화면(IRoundStartGate)을 본다
    /// </summary>
    internal static class GameFlowUiBootstrap
    {
        private const string RootName = "GameFlowUI";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindAnyObjectByType<RunDirector>() == null || Object.FindAnyObjectByType<GunSlot>() == null) return;

            GameObject root = null;
            Ensure<LoadoutPanel>(scene, ref root);
            Ensure<UnlockToastHud>(scene, ref root);
            Ensure<RoundSupplyToastHud>(scene, ref root);
            Ensure<SupplyWeaponPickupHud>(scene, ref root);
            Ensure<GameOverPanel>(scene, ref root);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Ensure<GameFlowDebugPanel>(scene, ref root);
#endif
        }

        private static void Ensure<T>(Scene scene, ref GameObject root) where T : Component
        {
            if (Object.FindAnyObjectByType<T>(FindObjectsInactive.Include) != null) return;
            if (root == null)
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            root.AddComponent<T>();
        }
    }
}
