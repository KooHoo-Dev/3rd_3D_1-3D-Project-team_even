using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 발 IK 시험용 지형 (IK Pose Lab "발 IK" 칸). Play 중에만 만들고, Play 를 끝내면 사라진다 (씬은 건드리지 않는다).
    /// 플레이어 앞쪽에 왼쪽부터: 경사로 10·20·30·40° / 계단 높이 10·20·30cm / 40cm 턱 (Grounder 최대 높이 근처)
    /// 바닥 높이는 플레이어의 GroundMask 로 찾고, 레이어는 Default (바닥 판정에 들어간다)
    /// </summary>
    internal static class IKTestTerrain
    {
        private const string RootName = "IK_TestTerrain";
        private const float Lane = 1.6f;       // 지형 하나의 폭 + 간격
        private const float Width = 1.4f;
        private const float StartAhead = 2.5f; // 플레이어 앞 거리

        public static bool Exists => GameObject.Find(RootName) != null;

        public static void Create(PlayerController player)
        {
            Remove();
            Transform root = new GameObject(RootName).transform;
            Vector3 forward = Vector3.ProjectOnPlane(player.Forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);

            float[] ramps = { 10f, 20f, 30f, 40f };
            float[] steps = { 0.1f, 0.2f, 0.3f };
            int count = ramps.Length + steps.Length + 1;
            int lane = 0;
            Vector3 Start(int i) => player.transform.position + forward * StartAhead + right * ((i - (count - 1) * 0.5f) * Lane);

            foreach (float angle in ramps) Ramp(root, Ground(player, Start(lane++)), facing, angle);
            foreach (float height in steps) Stairs(root, Ground(player, Start(lane++)), facing, height);
            Box(root, "Ledge 0.40", Ground(player, Start(lane)) + forward * 0.75f + Vector3.up * 0.2f, facing, new Vector3(Width, 0.4f, 1.5f));
        }

        public static void Remove()
        {
            GameObject root = GameObject.Find(RootName);
            if (root != null) Object.DestroyImmediate(root); // 바로 지워야 새로 만들 때 바닥 검사에 걸리지 않는다
        }

        /// <summary>start 에서 앞으로 4m, angle 만큼 올라가는 판 (아래 끝이 바닥에 닿음)</summary>
        private static void Ramp(Transform root, Vector3 start, Quaternion facing, float angle)
        {
            const float Length = 4f, Thickness = 0.2f;
            Quaternion rotation = facing * Quaternion.Euler(-angle, 0f, 0f);
            Vector3 center = start + rotation * new Vector3(0f, -Thickness * 0.5f, Length * 0.5f);
            Box(root, $"Ramp {angle:0}°", center, rotation, new Vector3(Width, Thickness, Length));
        }

        /// <summary>단 높이 height, 깊이 0.35m 계단 5단 + 꼭대기 판</summary>
        private static void Stairs(Transform root, Vector3 start, Quaternion facing, float height)
        {
            const int Count = 5;
            const float Depth = 0.35f;
            Vector3 forward = facing * Vector3.forward;
            for (int i = 0; i < Count; i++)
            {
                float top = height * (i + 1);
                Vector3 center = start + forward * (Depth * (i + 0.5f)) + Vector3.up * (top * 0.5f);
                Box(root, $"Stairs {height * 100f:0}cm #{i + 1}", center, facing, new Vector3(Width, top, Depth));
            }
            float landing = height * Count;
            Box(root, $"Stairs {height * 100f:0}cm top", start + forward * (Depth * Count + 0.75f) + Vector3.up * (landing * 0.5f), facing, new Vector3(Width, landing, 1.5f));
        }

        private static void Box(Transform root, string name, Vector3 center, Quaternion rotation, Vector3 size)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.layer = 0;
            box.transform.SetParent(root, false);
            box.transform.SetPositionAndRotation(center, rotation);
            box.transform.localScale = size;
        }

        /// <summary>그 자리의 바닥 높이 (플레이어 GroundMask). 못 찾으면 플레이어 발 높이</summary>
        private static Vector3 Ground(PlayerController player, Vector3 point)
        {
            Vector3 from = new Vector3(point.x, player.transform.position.y + 5f, point.z);
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 20f, player.GroundMask, QueryTriggerInteraction.Ignore)
                && hit.collider.transform.IsChildOf(player.transform) == false)
                return hit.point;
            return new Vector3(point.x, player.transform.position.y, point.z);
        }
    }
}
