using System;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 총 한 자루를 쥘 때의 손 자세. 총 오브젝트(WeaponItem 과 같은 곳)에 붙는다. PlayerHandIK 가 매 프레임 읽어 적용한다.
    ///
    ///  - 손가락: 손가락마다 "얼마나 굽힐지" 슬라이더 (0 = 쫙 편 손, 1 = 꽉 쥔 주먹). 애니메이션의 손가락 위에 Weight 만큼 덮어쓴다
    ///  - 팔꿈치: ElbowHint 오브젝트를 놓으면 팔꿈치가 그쪽을 향해 굽는다 (손 위치는 그대로)
    ///  - 왼손 위치·방향은 WeaponItem 의 LeftHandGrip, 오른손 위치는 총 오브젝트 자체 위치로 맞춘다
    ///
    /// 맞추는 법: 플레이 중에 이 컴포넌트 값을 바꾸면 바로 보인다 → 인스펙터의 "플레이 중 값 저장" 을 누르면
    /// 플레이를 끈 뒤 Player 프리팹에 자동으로 반영된다. (Docs/Guides/PlayerAnimationGuide.md 5장)
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponHandPose : MonoBehaviour
    {
        [Serializable]
        public class HandSettings
        {
            [Tooltip("손가락 자세를 얼마나 덮어쓸지. 0 = 애니메이션 손가락 그대로, 1 = 아래 값 그대로")]
            [Range(0f, 1f)] public float fingerWeight = 1f;
            [Range(0f, 1f)] public float thumb = 0.5f;
            [Range(0f, 1f)] public float index = 0.4f;
            [Range(0f, 1f)] public float middle = 0.8f;
            [Range(0f, 1f)] public float ring = 0.85f;
            [Range(0f, 1f)] public float little = 0.85f;
            [Tooltip("손가락 사이 벌림 (-1 = 붙임, 0 = 기본, 1 = 벌림)")]
            [Range(-1f, 1f)] public float spread;

            [Tooltip("팔꿈치가 향할 위치. 비워 두면 애니메이션 팔꿈치 그대로")]
            public Transform elbowHint;
            [Range(0f, 1f)] public float elbowHintWeight = 1f;

            /// <summary>엄지 · 검지 · 중지 · 약지 · 새끼 순서</summary>
            public float Curl(int finger)
            {
                switch (finger)
                {
                    case 0: return thumb;
                    case 1: return index;
                    case 2: return middle;
                    case 3: return ring;
                    default: return little;
                }
            }

            public HandSettings Clone() => (HandSettings)MemberwiseClone();
        }

        [Header("총 드는 위치 (PlayerHandIK 가 '총 기준' 모드일 때)")]
        [Tooltip("아래 위치가 저장돼 있는지. 꺼져 있으면 처음 들 때 애니메이션 오른손 위치로 자동 계산한다")]
        public bool hasHoldOffset;
        [Tooltip("오른쪽 어깨 기준 총 위치 (x = 오른쪽, y = 위, z = 조준 방향). 플레이 중 'Hold' 핸들을 옮겨 맞춘 뒤 저장")]
        public Vector3 holdPosition;
        public Quaternion holdRotation = Quaternion.identity;
        [Tooltip("조준하지 않을 때 총을 내리는 각도(도). 0 = 계속 조준선 방향")]
        [Range(-10f, 70f)] public float loweredPitch = 25f;
        [Tooltip("조준하지 않을 때도 조준선 쪽을 얼마나 향할지 (0 = 완전히 내림, 1 = 조준 때와 같음)")]
        [Range(0f, 1f)] public float hipAim = 0.5f;

        [Header("오른손 (방아쇠 쪽)")]
        public HandSettings right = new HandSettings();

        [Header("왼손 (총열 받치는 쪽)")]
        public HandSettings left = new HandSettings { thumb = 0.35f, index = 0.55f, middle = 0.6f, ring = 0.65f, little = 0.65f };

        [Header("손가락 굽힘 최대 각도")]
        [Tooltip("굽힘 1 일 때 마디 하나가 꺾이는 각도(도)")]
        [Range(30f, 110f)] public float maxJointAngle = 85f;
        [Tooltip("엄지는 다른 손가락보다 덜 굽는다")]
        [Range(20f, 90f)] public float maxThumbAngle = 50f;
        [Tooltip("벌림 1 일 때 손가락이 옆으로 벌어지는 각도(도)")]
        [Range(0f, 30f)] public float maxSpreadAngle = 12f;

        public HandSettings Get(bool rightHand) => rightHand ? right : left;
    }
}
