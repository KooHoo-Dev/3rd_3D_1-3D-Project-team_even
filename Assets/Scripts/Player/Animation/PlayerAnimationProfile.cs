using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 플레이어 애니메이션 설정값. 무기 세트별 이동 클립 실제 속도(_RM 판으로 잰 m/s)와 공통 조정값.
    /// 속도는 빌더 메뉴(Tools/HybridShooter/Build Player Animator)가 재서 채운다
    /// </summary>
    [CreateAssetMenu(menuName = "HybridShooter/Player Animation Profile", fileName = "PlayerAnimationProfile")]
    public class PlayerAnimationProfile : ScriptableObject
    {
        [Serializable]
        public class WeaponSet
        {
            [Tooltip("세트 이름 (Rifle, Pistol ...)")]
            public string name = "Rifle";
            [Tooltip("이 세트의 클립으로 바꾼 Override Controller. 비워 두면 Player_AC 그대로 (라이플)")]
            public AnimatorOverrideController overrideController;
            [Tooltip("정면 걷기 클립 속도(m/s)")]
            public float walkSpeed = 0.77f;
            [Tooltip("정면 달리기 클립 속도(m/s). 이보다 빠르면 재생 속도를 올린다")]
            public float runSpeed = 2.5f;
            [Tooltip("전력질주 클립 속도(m/s)")]
            public float sprintSpeed = 4.8f;
            [Tooltip("재장전 클립 길이(초). 실제 재장전 시간에 맞춰 재생 속도를 바꾼다")]
            public float reloadClipLength = 5f;
            [Tooltip("꺼내기 클립 길이(초)")]
            public float drawClipLength = 2.17f;
            [Tooltip("구르기 클립 4방향 평균 길이(초)")]
            public float dodgeClipLength = 1.5f;
        }

        [Tooltip("무기 세트 목록. 첫 번째가 기본(라이플)")]
        public List<WeaponSet> sets = new List<WeaponSet> { new WeaponSet() };

        [Header("이동")]
        [Tooltip("이동 클립 재생 속도 상한. 업계 관례상 15~20% 이상 올리면 종종걸음이 된다")]
        [Range(1f, 1.5f)] public float maxPlaybackScale = 1.25f;
        [Tooltip("MoveX / MoveY 가 실제 속도를 따라가는 부드러움(초)")]
        public float moveDampTime = 0.1f;
        [Tooltip("이동 방향이 몸 정면에서 이 값(코사인) 이상일 때만 전력질주 동작 (옆·뒤로는 달리기)")]
        [Range(0f, 1f)] public float sprintForwardDot = 0.7f;
        [Tooltip("이 속도(m/s)보다 느리면 멈춘 것으로 본다 (착지 동작을 끊고 이동으로 넘어가는 기준)")]
        public float movingSpeed = 0.3f;

        [Header("상체 자세 (Upper Pose)")]
        [Tooltip("들고 다니기 ↔ 조준 자세가 바뀌는 데 걸리는 시간(초)")]
        public float aimBlendTime = 0.15f;
        [Tooltip("조준 없이 쏜 뒤 조준 자세를 유지하는 시간(초)")]
        public float aimPoseAfterFire = 0.5f;
        [Tooltip("상체 자세 레이어를 켜고 끄는 데 걸리는 시간(초). 총이 없거나 사망·구르기·벽넘기 중에는 끈다")]
        public float upperPoseBlendTime = 0.15f;

        [Header("재장전 · 꺼내기 · 사격 반동")]
        [Tooltip("무기를 바꿀 때 꺼내기 동작을 재생할지")]
        public bool playDraw = true;
        [Tooltip("꺼내기 동작을 이 시간(초) 안에 끝낸다. 조준·사격하면 바로 끊긴다")]
        public float drawDuration = 0.6f;
        [Tooltip("재장전·꺼내기 재생 속도 범위 (너무 느리거나 빠르면 어색하다)")]
        public Vector2 actionSpeedRange = new Vector2(0.5f, 3f);
        [Tooltip("재장전·꺼내기 동작이 들어오고 나갈 때 조준 IK 를 풀고 다시 거는 시간(초)")]
        public float actionBlendTime = 0.15f;
        [Tooltip("사격 한 발마다 Final IK Recoil 세기 (1 = 기본)")]
        public float recoilMagnitude = 1f;

        [Header("구르기 · 피격 · 사망")]
        [Tooltip("물리 구르기(PlayerController.rollDuration)가 끝난 뒤 일어나는 동작에 줄 시간(초). 구르기 동작 = 구르기 + 이 시간")]
        public float dodgeRecoveryTime = 0.35f;
        [Tooltip("구르기 재생 속도 범위")]
        public Vector2 dodgeSpeedRange = new Vector2(0.8f, 2.5f);
        [Tooltip("피격 반응 세기 (Hit Reaction 레이어 가중치, 0 = 끔)")]
        [Range(0f, 1f)] public float hitReactionWeight = 1f;

        [Header("점프 · 낙하 · 착지")]
        [Tooltip("떨어지는 속도가 최대 하강 속도(PlayerController.maxFallSpeed)의 이 비율에 닿으면 낙하 동작. " +
                 "보통 점프는 여기까지 빨라지지 않으므로 점프 동작 그대로 착지한다")]
        [Range(0.5f, 1f)] public float fallTerminalRatio = 0.95f;
        [Tooltip("공중에서 가장 빨리 떨어진 속도(m/s)가 이 이상이면 중간 착지. 낙하 동작까지 갔으면(최대 하강 속도) 무거운 착지")]
        public float landMediumSpeed = 7f;

        public WeaponSet DefaultSet => sets != null && sets.Count > 0 ? sets[0] : null;
    }
}
