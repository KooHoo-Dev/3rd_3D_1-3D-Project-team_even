using System.Collections.Generic;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 1인칭 조준 시야 검사 (IK Pose Lab "조준 시야 검사"). 총 메쉬 삼각형에 광선을 쏴서 조준할 때 화면 가운데가 총에 가리는지 본다.
    /// 모두 총 기준(크기 무시 m, 정면 = +z) 좌표. 조준 때 눈은 조준기 뒤 adsDistance, 시선 = 총 정면
    ///  - 눈이 총 안: 눈 · 카메라 잘림 면 중앙에서 위로 쏜 광선이 메쉬 면을 홀수 번 지나면 안쪽 (닫힌 메쉬 기준)
    ///    → 잘림 면이 메쉬를 자르며 안쪽 면이 보인다
    ///  - 가림: 눈에서 조준기 쪽으로 쏜 광선(중앙 + 둘레 4개)이 잘림 면 ~ 조준기 사이에서 먼저 닿으면 그 앞을 총이 가린다.
    ///    처음 닿은 면이 뒷면이면 총 안쪽이 보이는 것
    ///  - 자동 맞춤: 조준기(= 조준선)를 2mm 씩 올려 가며 가림이 없어지는 높이를 찾는다 (총이 화면 아래로 내려감, 볼을 개머리판에 붙이고 조준선 위로 보는 자세)
    /// </summary>
    internal static class WeaponSightCheck
    {
        public struct Result
        {
            public bool valid;
            public bool eyeInside;          // 눈 또는 잘림 면 중앙이 메쉬 안
            public float blockDistance;     // 눈에서 가리는 면까지 거리(m). 가림 없으면 -1
            public bool blockBackFace;      // 가장 먼저 닿은 면이 뒷면 (= 총 안쪽이 보임)
            public float sightDistance;     // 눈 ~ 조준기 거리(m)
            public Vector3 blockPoint;      // 가린 자리 (총 기준)
            public bool Clear => valid && eyeInside == false && blockDistance < 0f;
        }

        private const float ConeAngle = 1.5f;     // 둘레 광선 각도(도) ≈ 화면 가운데 작은 원
        private const float SightTolerance = 0.01f; // 조준기 자체(가늠자)는 가림으로 치지 않는 앞쪽 여유(m)
        private static readonly Dictionary<Gun, Vector3[]> Cache = new Dictionary<Gun, Vector3[]>();

        public static void ClearCache() => Cache.Clear();

        /// <summary>eye · sight: 총 기준 좌표. near: 카메라 잘림 거리</summary>
        public static Result Check(Gun gun, Vector3 eye, Vector3 sight, float near)
        {
            var result = new Result { valid = false, blockDistance = -1f };
            Vector3[] tris = Triangles(gun);
            if (tris.Length == 0) return result;
            result.valid = true;
            Vector3 forward = sight - eye;
            result.sightDistance = forward.magnitude;
            if (result.sightDistance < 1e-4f) forward = Vector3.forward;
            forward.Normalize();

            result.eyeInside = Inside(tris, eye) || Inside(tris, eye + forward * near);

            Quaternion look = Quaternion.LookRotation(forward, Vector3.up);
            float maxDistance = result.sightDistance - SightTolerance;
            float best = float.MaxValue;
            for (int i = 0; i < 5; i++)
            {
                Vector3 dir = i == 0 ? forward : look * (Quaternion.Euler(i == 1 ? ConeAngle : i == 2 ? -ConeAngle : 0f, i == 3 ? ConeAngle : i == 4 ? -ConeAngle : 0f, 0f) * Vector3.forward);
                if (Raycast(tris, eye, dir, near, maxDistance, out float t, out bool front) && t < best)
                {
                    best = t;
                    result.blockDistance = t;
                    result.blockBackFace = front == false;
                    result.blockPoint = eye + dir * t;
                }
            }
            return result;
        }

        /// <summary>
        /// 조준기를 위로 올려 가며 가림이 없어지는 높이(m)를 찾는다 (최대 maxRaise). margin 만큼 더 올린다.
        /// 못 찾으면 -1. 조준 거리(ads)는 그대로 → 눈 = 조준기 - (0,0,ads)
        /// </summary>
        public static float FindRaise(Gun gun, Vector3 sight, float ads, float near, float maxRaise = 0.08f, float margin = 0.003f)
        {
            const float Step = 0.002f;
            for (float raise = 0f; raise <= maxRaise + 1e-5f; raise += Step)
            {
                Vector3 s = sight + Vector3.up * raise;
                if (Check(gun, s - Vector3.forward * ads, s, near).Clear) return raise > 0f ? raise + margin : 0f;
            }
            return -1f;
        }

        /// <summary>위로 쏜 광선이 면을 홀수 번 지나면 안쪽</summary>
        private static bool Inside(Vector3[] tris, Vector3 point)
        {
            int crossings = 0;
            for (int i = 0; i < tris.Length; i += 3)
                if (Intersect(point, Vector3.up, tris[i], tris[i + 1], tris[i + 2], out float t, out _) && t > 0f) crossings++;
            return (crossings & 1) == 1;
        }

        private static bool Raycast(Vector3[] tris, Vector3 origin, Vector3 dir, float min, float max, out float distance, out bool front)
        {
            distance = float.MaxValue;
            front = true;
            bool hit = false;
            for (int i = 0; i < tris.Length; i += 3)
            {
                if (Intersect(origin, dir, tris[i], tris[i + 1], tris[i + 2], out float t, out bool f) == false) continue;
                if (t < min || t > max || t >= distance) continue;
                distance = t;
                front = f;
                hit = true;
            }
            return hit;
        }

        /// <summary>Möller–Trumbore. front = 앞면 (Unity: 시계 방향으로 보이는 면)</summary>
        private static bool Intersect(Vector3 origin, Vector3 dir, Vector3 a, Vector3 b, Vector3 c, out float t, out bool front)
        {
            t = 0f;
            front = true;
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -1e-9f && det < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = origin - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            t = Vector3.Dot(e2, q) * inv;
            front = det > 0f;
            return true;
        }

        /// <summary>총의 모든 메쉬 삼각형 (총 기준). 총마다 한 번 계산해 둔다</summary>
        private static Vector3[] Triangles(Gun gun)
        {
            if (Cache.TryGetValue(gun, out Vector3[] cached) && cached != null) return cached;
            var list = new List<Vector3>();
            Transform g = gun.transform;
            foreach (MeshFilter filter in gun.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) Add(list, g, filter.transform, filter.sharedMesh);
            foreach (SkinnedMeshRenderer skinned in gun.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skinned.sharedMesh != null) Add(list, g, skinned.transform, skinned.sharedMesh);
            Vector3[] tris = list.ToArray();
            Cache[gun] = tris;
            return tris;
        }

        private static void Add(List<Vector3> list, Transform gun, Transform owner, Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] indices = mesh.triangles;
            // 크기가 음수(거울)면 감김 방향이 뒤집힌다 → 앞뒷면 판정이 맞도록 두 점을 바꾼다
            Vector3 scale = owner.lossyScale;
            bool mirrored = scale.x * scale.y * scale.z < 0f;
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                list.Add(WeaponGripAutoFit.ToMetric(gun, owner.TransformPoint(vertices[indices[i]])));
                list.Add(WeaponGripAutoFit.ToMetric(gun, owner.TransformPoint(vertices[indices[mirrored ? i + 2 : i + 1]])));
                list.Add(WeaponGripAutoFit.ToMetric(gun, owner.TransformPoint(vertices[indices[mirrored ? i + 1 : i + 2]])));
            }
        }
    }
}
