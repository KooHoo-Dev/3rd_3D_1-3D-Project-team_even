using System.Collections.Generic;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 총별 손 위치 처음 값 자동 계산 (IK Pose Lab 의 "자동 맞춤"). Play 중 조준 자세에서 부른다.
    ///  1. 총 방향 = 오른손 아래 AimTransform 방향 (빌더가 조준 자세의 몸 정면으로 맞춰 둠)
    ///  2. 손잡이 = 총 메쉬를 앞뒤로 잘라 아래로 가장 깊게 튀어나온 부분 중 맨 뒤 (권총 손잡이. 그 앞은 보통 탄창)
    ///     → 손잡이가 오른손 바닥에 오도록 총을 옮긴다
    ///  3. 왼손 = 애니메이션의 왼손 바닥을 총 앞쪽 표면에 붙인 자리 (손 방향은 애니메이션 그대로)
    ///  4. 조준기 = 손잡이 위 총 윗면
    /// 대충 맞추는 값이라, 어긋난 총은 Lab 에서 씬 뷰로 옮겨 다듬는다
    /// </summary>
    internal static class WeaponGripAutoFit
    {
        private const float PalmToGrip = 0.035f;    // 오른손 바닥 중심(뼈 사이) → 손잡이 중심
        private const float PalmThickness = 0.025f; // 왼손 바닥 중심 → 총 표면
        private const int Slices = 40;

        /// <summary>조준 자세의 손 정보 (모든 총 공통)</summary>
        public struct Hands
        {
            public Quaternion gunRotation;    // 총 회전 (월드)
            public Vector3 rightPalm;         // 오른손 바닥 중심 (월드)
            public Vector3 leftWrist;         // IK 전 애니메이션 왼손 손목 (월드)
            public Quaternion leftRotation;
            public Vector3 leftPalmOffset;    // 왼손 손목 → 바닥 중심 (왼손 기준)
        }

        public static bool Measure(Animator animator, PlayerBodyIK bodyIK, out Hands hands, out string error)
        {
            hands = default;
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightMiddle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            Transform leftMiddle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            Transform aim = right != null ? right.Find("AimTransform") : null;
            if (right == null || left == null || rightMiddle == null || leftMiddle == null) { error = "손·손가락 뼈를 찾지 못했습니다"; return false; }
            if (aim == null) { error = "오른손 아래 AimTransform 이 없습니다 (Build Player Animator 실행)"; return false; }

            hands.gunRotation = aim.rotation;
            hands.rightPalm = (right.position + rightMiddle.position) * 0.5f;
            hands.leftWrist = right.TransformPoint(bodyIK.AnimLeftPosInRight);
            hands.leftRotation = right.rotation * bodyIK.AnimLeftRotInRight;
            hands.leftPalmOffset = left.InverseTransformPoint((left.position + leftMiddle.position) * 0.5f);
            error = null;
            return true;
        }

        /// <summary>총을 손에 맞춰 옮기고 그 결과를 entry 에 기록한다</summary>
        public static void Fit(Gun gun, Hands hands, WeaponGripTable.Entry entry)
        {
            Transform t = gun.transform;
            t.rotation = hands.gunRotation;

            // 총 기준(크기 무시, m) 꼭짓점
            List<Vector3> points = CollectPoints(t);
            if (points.Count == 0) return;
            var shape = new Shape(points);

            // 손잡이를 오른손 바닥 (조금 왼쪽 = 총 안쪽)에
            Vector3 gripWorld = ToWorld(t, shape.Grip);
            Vector3 target = hands.rightPalm - t.right * PalmToGrip;
            t.position += target - gripWorld;

            // 왼손: 애니메이션 왼손 바닥 → 총 표면
            Vector3 palm = hands.leftWrist + hands.leftRotation * hands.leftPalmOffset;
            Vector3 palmTarget = ToWorld(t, shape.LeftPalm(ToMetric(t, palm), PalmThickness));
            Vector3 wrist = palmTarget - hands.leftRotation * hands.leftPalmOffset;

            entry.gunPosition = t.localPosition;
            entry.gunRotation = t.localRotation;
            entry.gripPoint = t.InverseTransformPoint(ToWorld(t, shape.Grip));
            entry.leftHandPosition = t.InverseTransformPoint(wrist);
            entry.leftHandRotation = Quaternion.Inverse(t.rotation) * hands.leftRotation;
            entry.sightPosition = t.InverseTransformPoint(ToWorld(t, shape.Sight));
        }

        /// <summary>손잡이 지점이 와야 할 자리 (월드): 오른손 바닥 중심에서 조금 왼쪽(= 총 안쪽)</summary>
        public static Vector3 RightGripTarget(Animator animator, Transform gun)
        {
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (right == null || middle == null) return gun.position;
            return (right.position + middle.position) * 0.5f - gun.right * PalmToGrip;
        }

        /// <summary>총 메쉬에서 찾은 손잡이 지점 (총 기준 로컬)</summary>
        public static Vector3 FindGrip(Gun gun)
        {
            Transform t = gun.transform;
            List<Vector3> points = CollectPoints(t);
            return points.Count == 0 ? Vector3.forward * 0.01f : t.InverseTransformPoint(ToWorld(t, new Shape(points).Grip));
        }

        internal static Vector3 ToWorld(Transform t, Vector3 metric) => t.position + t.rotation * metric;
        internal static Vector3 ToMetric(Transform t, Vector3 world) => Quaternion.Inverse(t.rotation) * (world - t.position);

        private static List<Vector3> CollectPoints(Transform gun)
        {
            var points = new List<Vector3>();
            foreach (MeshFilter filter in gun.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh != null) AddPoints(points, gun, filter.transform, filter.sharedMesh);
            foreach (SkinnedMeshRenderer skinned in gun.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skinned.sharedMesh != null) AddPoints(points, gun, skinned.transform, skinned.sharedMesh);
            return points;
        }

        private static void AddPoints(List<Vector3> points, Transform gun, Transform owner, Mesh mesh)
        {
            Quaternion inv = Quaternion.Inverse(gun.rotation);
            foreach (Vector3 v in mesh.vertices) points.Add(inv * (owner.TransformPoint(v) - gun.position));
        }

        /// <summary>총 모양 분석 (총 기준 m, 정면 = +z)</summary>
        private class Shape
        {
            private readonly List<Vector3> points;
            private readonly Bounds bounds;
            private readonly float[] minY = new float[Slices];
            private readonly float[] maxY = new float[Slices];
            private readonly float barrelBottom;

            public Vector3 Grip { get; }
            public Vector3 Sight { get; }

            public Shape(List<Vector3> points)
            {
                this.points = points;
                bounds = new Bounds(points[0], Vector3.zero);
                foreach (Vector3 p in points) bounds.Encapsulate(p);
                for (int i = 0; i < Slices; i++) { minY[i] = float.PositiveInfinity; maxY[i] = float.NegativeInfinity; }
                foreach (Vector3 p in points)
                {
                    int i = Slice(p.z);
                    minY[i] = Mathf.Min(minY[i], p.y);
                    maxY[i] = Mathf.Max(maxY[i], p.y);
                }

                // 총열 아랫면 = 앞쪽에서 가장 높은 아랫면
                barrelBottom = float.NegativeInfinity;
                for (int i = Slices * 35 / 100; i < Slices * 90 / 100; i++)
                    if (Has(i)) barrelBottom = Mathf.Max(barrelBottom, minY[i]);
                if (float.IsNegativeInfinity(barrelBottom)) barrelBottom = bounds.center.y;

                Grip = FindGrip();
                float sightTop = float.NegativeInfinity;
                foreach (Vector3 p in points)
                    if (Mathf.Abs(p.z - Grip.z) < 0.03f) sightTop = Mathf.Max(sightTop, p.y);
                if (float.IsNegativeInfinity(sightTop)) sightTop = bounds.max.y;
                Sight = new Vector3(bounds.center.x, sightTop + 0.01f, Grip.z);
            }

            /// <summary>뒤 10%~75% 중 가장 깊은 튀어나옴의 60% 이상인 구간들 → 맨 뒤 구간이 손잡이</summary>
            private Vector3 FindGrip()
            {
                int from = Slices * 10 / 100, to = Slices * 75 / 100;
                float deepest = 0f;
                for (int i = from; i < to; i++)
                    if (Has(i)) deepest = Mathf.Max(deepest, barrelBottom - minY[i]);

                if (deepest > 0.03f)
                {
                    for (int i = from; i < to; i++)
                    {
                        if (Depth(i) < deepest * 0.6f) continue;
                        int end = i;
                        float runMin = minY[i];
                        while (end + 1 < to && Depth(end + 1) >= deepest * 0.6f) { end++; runMin = Mathf.Min(runMin, minY[end]); }
                        float z = Mathf.Lerp(SliceZ(i), SliceZ(end + 1), 0.5f);
                        float y = barrelBottom - Mathf.Min(0.06f, (barrelBottom - runMin) * 0.4f);
                        return new Vector3(bounds.center.x, y, z);
                    }
                }
                // 튀어나온 손잡이가 없음 (개머리판 손잡이 등): 뒤에서 30%, 아랫면 근처
                return new Vector3(bounds.center.x, barrelBottom - 0.03f, bounds.min.z + bounds.size.z * 0.3f);
            }

            /// <summary>왼손 바닥(총 기준)을 손잡이 앞쪽 총 표면으로 옮긴 자리</summary>
            public Vector3 LeftPalm(Vector3 palm, float thickness)
            {
                float front = bounds.max.z - 0.03f;
                // 짧은 총(권총): 오른손 아래·왼쪽에서 감싸 쥔다
                if (front - Grip.z < 0.2f) return Grip + new Vector3(-0.03f, -0.04f, 0f);

                palm.z = Mathf.Clamp(palm.z, Grip.z + 0.1f, front);
                int s = Slice(palm.z);
                var axis = new Vector3(bounds.center.x, Has(s) ? (minY[s] + maxY[s]) * 0.5f : bounds.center.y, palm.z);
                Vector3 outward = palm - axis;
                outward.z = 0f;
                if (outward.sqrMagnitude < 1e-6f) outward = Vector3.down;
                outward.Normalize();

                // 그 단면에서 outward 방향으로 가장 바깥 표면까지 거리 (방향이 비슷한 꼭짓점 우선)
                float radius = 0f, anyRadius = 0f;
                foreach (Vector3 p in points)
                {
                    if (Mathf.Abs(p.z - palm.z) > 0.015f) continue;
                    Vector3 d = p - axis;
                    d.z = 0f;
                    float along = Vector3.Dot(d, outward);
                    anyRadius = Mathf.Max(anyRadius, along);
                    if (d.sqrMagnitude > 1e-8f && along / d.magnitude > 0.9f) radius = Mathf.Max(radius, along);
                }
                if (radius <= 0f) radius = anyRadius;
                return axis + outward * (radius + thickness);
            }

            private int Slice(float z) => Mathf.Clamp((int)((z - bounds.min.z) / Mathf.Max(bounds.size.z, 1e-4f) * Slices), 0, Slices - 1);
            private float SliceZ(int i) => bounds.min.z + bounds.size.z * i / Slices;
            private bool Has(int i) => minY[i] <= maxY[i];
            private float Depth(int i) => Has(i) ? barrelBottom - minY[i] : 0f;
        }
    }
}
