using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 손가락을 "굽힘 정도(0~1)" 값으로 접는다. 한 손에 하나.
    ///
    /// 방법: 손가락 마디를 처음(쫙 편) 자세로 되돌린 뒤, 마디마다 손바닥 쪽으로 각도만큼 꺾는다.
    ///       손바닥 방향은 뼈 위치(손목·중지·엄지)로 계산하므로 모델마다 다른 뼈 축 방향과 상관없이 같은 결과가 나온다.
    ///       마지막에 애니메이션 손가락과 Weight 비율로 섞는다.
    ///
    /// 처음 자세는 만들 때(애니메이션이 아직 손대지 않은 T자세) 저장해 둔다.
    /// </summary>
    public sealed class HandFingerPoser
    {
        private const int Fingers = 5;
        private const int Joints = 3;

        private readonly bool right;
        private readonly Transform hand;
        private readonly Transform[,] bones = new Transform[Fingers, Joints];
        private readonly Quaternion[,] rest = new Quaternion[Fingers, Joints];
        private readonly Quaternion[,] animated = new Quaternion[Fingers, Joints];
        private readonly Quaternion[,] posed = new Quaternion[Fingers, Joints];

        public bool IsValid { get; }

        /// <summary>animator 가 지금 "손가락을 편 기본 자세" 일 때 만들어야 한다</summary>
        public HandFingerPoser(Animator animator, bool rightHand)
            : this(bone => Bone(animator, bone), rightHand) { }

        /// <param name="findBone">휴머노이드 뼈 찾기 (에디터에서는 아바타 이름표로 찾는 함수를 넘긴다)</param>
        public HandFingerPoser(System.Func<HumanBodyBones, Transform> findBone, bool rightHand)
        {
            right = rightHand;
            hand = findBone(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            // HumanBodyBones 는 손가락이 엄지→새끼, 마디가 Proximal→Intermediate→Distal 순서로 연속 번호다
            int first = (int)(rightHand ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            int found = 0;
            for (int f = 0; f < Fingers; f++)
                for (int j = 0; j < Joints; j++)
                {
                    Transform t = findBone((HumanBodyBones)(first + f * Joints + j));
                    bones[f, j] = t;
                    if (t == null) continue;
                    rest[f, j] = t.localRotation;
                    found++;
                }
            IsValid = hand != null && bones[2, 0] != null && found > 0;
        }

        private static Transform Bone(Animator animator, HumanBodyBones bone)
            => animator != null && animator.isHuman ? animator.GetBoneTransform(bone) : null;

        /// <summary>애니메이션이 끝난 손 위에 덮어쓴다. LateUpdate 의 IK 뒤에 호출</summary>
        public void Apply(WeaponHandPose.HandSettings settings, WeaponHandPose limits, float weight)
        {
            if (IsValid == false || settings == null || weight <= 0.001f) return;

            // 손바닥 기준 방향: length = 손목 → 중지, side = 엄지 쪽, palm = 손바닥이 향하는 쪽
            Vector3 length = (bones[2, 0].position - hand.position).normalized;
            Vector3 side = bones[0, 0] != null ? Vector3.ProjectOnPlane(bones[0, 0].position - hand.position, length).normalized : Vector3.zero;
            if (side.sqrMagnitude < 0.0001f) return;
            Vector3 palm = (right ? Vector3.Cross(length, side) : Vector3.Cross(side, length)).normalized;

            for (int f = 0; f < Fingers; f++)
                for (int j = 0; j < Joints; j++)
                    if (bones[f, j] != null) animated[f, j] = bones[f, j].localRotation;

            for (int f = 0; f < Fingers; f++)
            {
                if (bones[f, 0] == null) continue;
                for (int j = 0; j < Joints; j++)
                    if (bones[f, j] != null) bones[f, j].localRotation = rest[f, j];

                // 벌림: 검지는 엄지 쪽으로, 약지·새끼는 반대쪽으로 (중지·엄지는 그대로)
                float spreadSign = f == 1 ? 1f : f == 3 ? -0.6f : f == 4 ? -1.2f : 0f;
                if (spreadSign != 0f && Mathf.Abs(settings.spread) > 0.001f)
                    Bend(f, 0, side, settings.spread * spreadSign * limits.maxSpreadAngle);

                // 굽힘: 마디마다 손바닥 쪽으로. 다음 마디의 "손바닥 쪽"은 앞 마디가 꺾인 만큼 같이 돈다
                float angle = settings.Curl(f) * (f == 0 ? limits.maxThumbAngle : limits.maxJointAngle);
                Vector3 inward = palm;
                for (int j = 0; j < Joints; j++)
                {
                    if (bones[f, j] == null) break;
                    inward = Bend(f, j, inward, angle) * inward;
                }

                for (int j = 0; j < Joints; j++)
                    if (bones[f, j] != null) posed[f, j] = bones[f, j].localRotation;
            }

            for (int f = 0; f < Fingers; f++)
                for (int j = 0; j < Joints; j++)
                    if (bones[f, j] != null)
                        bones[f, j].localRotation = Quaternion.Slerp(animated[f, j], posed[f, j], weight);
        }

        /// <summary>마디 (f, j) 를 toward 방향으로 angle 도 꺾는다. 실제로 적용한 회전을 돌려준다</summary>
        private Quaternion Bend(int f, int j, Vector3 toward, float angle)
        {
            Transform bone = bones[f, j];
            Vector3 dir = SegmentDirection(f, j);
            Vector3 perpendicular = Vector3.ProjectOnPlane(toward, dir);
            if (dir.sqrMagnitude < 0.0001f || perpendicular.sqrMagnitude < 0.0001f) return Quaternion.identity;

            // "dir 에서 수직 방향으로 90도" 회전을 angle/90 비율로 (90도 넘게도 가능)
            Quaternion quarter = Quaternion.FromToRotation(dir, perpendicular.normalized);
            Quaternion r = Quaternion.SlerpUnclamped(Quaternion.identity, quarter, angle / 90f);
            bone.rotation = r * bone.rotation;
            return r;
        }

        /// <summary>마디가 뻗은 방향 (다음 마디 쪽). 마지막 마디는 자식(손끝)이 있으면 그쪽, 없으면 앞 마디 방향</summary>
        private Vector3 SegmentDirection(int f, int j)
        {
            Transform bone = bones[f, j];
            Transform next = j + 1 < Joints ? bones[f, j + 1] : (bone.childCount > 0 ? bone.GetChild(0) : null);
            if (next != null) return (next.position - bone.position).normalized;
            if (j > 0 && bones[f, j - 1] != null) return (bone.position - bones[f, j - 1].position).normalized;
            return Vector3.zero;
        }

        /// <summary>처음 자세로 되돌린다 (에디터 미리보기 종료용)</summary>
        public void RestoreRest()
        {
            for (int f = 0; f < Fingers; f++)
                for (int j = 0; j < Joints; j++)
                    if (bones[f, j] != null) bones[f, j].localRotation = rest[f, j];
        }
    }
}
