using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 연료 셀 테스트 씬(Test_FuelCell) 전용 도우미. FuelCellSceneTool 이 테스트 씬에만 놓는다 (통합 씬에는 없음).
    ///  - restoreKey: 씬의 모든 연료 셀을 처음 상태로 되살린다
    ///  - 보스 바로 부르기는 RoundSequencer 의 F12(보스로 건너뛰기)를 쓴다
    /// </summary>
    public sealed class ExplosivePropTester : MonoBehaviour
    {
        [SerializeField] private Key restoreKey = Key.F7;

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || keyboard[restoreKey].wasPressedThisFrame == false) return;

            var props = FindObjectsByType<ExplosiveProp>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var prop in props) prop.Restore();
            Debug.Log($"[ExplosivePropTester] 연료 셀 {props.Length}개를 되살렸습니다.", this);
        }
    }
}
