using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 씬에서 Tyranopode 경기장을 직접 정한다 (선택). 씬에 하나 놓으면 TyranopodeController 가 측정 대신 이 중심·반지름을 쓴다.
    /// 반지름 = 걸을 수 있는 경기장 끝까지 (보스 몸은 여기서 TyranopodeLocomotion.wallMargin 만큼 안쪽에 머문다).
    /// 없으면 보스 발밑 NavMesh 에서 잰다 (TyranopodeMinions.MeasureArena)
    /// </summary>
    public sealed class TyranopodeArena : MonoBehaviour
    {
        [SerializeField, Min(5f)] private float radius = 40f;

        public float Radius => radius;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            UnityEditor.Handles.color = new Color(1f, 0.55f, 0.1f);
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, radius);
        }
#endif
    }
}
