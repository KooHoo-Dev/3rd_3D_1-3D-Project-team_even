using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 손에 잠깐 쥐어 주는 소품 (던지기 전 수류탄, 회복 주사기).
    /// 원본 프리팹을 복제해서 손 뼈 아래에 붙이고, 물리는 꺼서 플레이어와 부딪히지 않게 한다.
    /// </summary>
    public static class HandProp
    {
        /// <param name="offset">손 뼈 방향 기준 오프셋(m). 뼈 크기(스케일)와 무관하게 미터 단위</param>
        public static GameObject Attach(GameObject prefab, Transform hand, Vector3 offset, Vector3 localEuler)
        {
            if (prefab == null || hand == null) return null;
            var go = Object.Instantiate(prefab, hand);
            var t = go.transform;
            t.localRotation = Quaternion.Euler(localEuler);
            t.position = hand.position + hand.rotation * offset;
            KeepWorldScale(t, prefab.transform.localScale);
            foreach (var col in go.GetComponentsInChildren<Collider>()) col.enabled = false;
            foreach (var body in go.GetComponentsInChildren<Rigidbody>()) body.isKinematic = true;
            foreach (var mono in go.GetComponentsInChildren<MonoBehaviour>()) mono.enabled = false;
            return go;
        }

        public static void Release(ref GameObject prop)
        {
            if (prop != null) Object.Destroy(prop);
            prop = null;
        }

        /// <summary>
        /// 모델 뼈에 스케일이 걸려 있으면(FBX 단위 차이 등) 자식으로 붙인 물체도 같이 커지거나 작아진다.
        /// 부모 스케일을 나눠서 월드 기준 크기를 worldScale 로 유지한다.
        /// </summary>
        public static void KeepWorldScale(Transform t, Vector3 worldScale)
        {
            Vector3 parent = t.parent != null ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(
                parent.x != 0f ? worldScale.x / parent.x : worldScale.x,
                parent.y != 0f ? worldScale.y / parent.y : worldScale.y,
                parent.z != 0f ? worldScale.z / parent.z : worldScale.z);
        }
    }
}
