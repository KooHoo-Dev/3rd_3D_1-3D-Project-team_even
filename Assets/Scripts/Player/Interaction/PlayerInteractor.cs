using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Weapon;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridShooter.Player
{
    /// <summary>
    /// 에임으로 상호작용 대상(IInteractable: 보급 무기 상자 등)을 고르고, 상호작용 키(F)로 쓴다. 플레이어 루트에 붙는다
    /// (없으면 PlayerController 가 Awake 에서 붙인다).
    ///
    ///  - 매 프레임 조준선(GunSlot.AimRay)을 트리거 포함으로 쏴서 가까운 순으로 본다. 트리거가 아닌 콜라이더(벽 등)가 먼저 맞으면 중단
    ///  - 거리는 플레이어 몸 ↔ IInteractable.InteractTransform 으로 잰다 (3인칭 카메라 거리와 무관)
    ///  - 대상이나 프롬프트가 바뀌면 InteractFocusChangedEvent 발행 → InteractPromptHud
    ///  - 무기를 쓸 수 없는 상태(사망·부활·구르기·투척·외부 제어·입력 차단)나 커서가 풀려 있으면 대상 없음
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [Tooltip("상호작용 거리(m). 플레이어 몸 중심 ↔ 대상 기준점")]
        [SerializeField, Min(0.5f)] private float interactRange = 3f;
        [Tooltip("몸 중심 높이(m). 발밑 기준 위치에 더해 거리를 잰다")]
        [SerializeField, Min(0f)] private float bodyCenterHeight = 1f;
        [Tooltip("조준선 길이 = 상호작용 거리 + 이 값 (1인칭 카메라가 몸보다 앞·위에 있는 만큼 여유)")]
        [SerializeField, Min(0f)] private float rayPadding = 2f;
        [SerializeField] private LayerMask mask = ~0;

        private const int MaxHits = 16;
        private readonly RaycastHit[] hits = new RaycastHit[MaxHits];

        private PlayerController player;
        private GunSlot gunSlot;
        private IInteractable focus;
        private InteractPrompt focusPrompt;
        private bool hasFocus;

        /// <summary>지금 조준 중인 상호작용 대상 (없으면 null)</summary>
        public IInteractable Focus => hasFocus ? focus : null;
        public float InteractRange => interactRange;

        #region 유니티 메시지
        private void Awake()
        {
            player = GetComponent<PlayerController>();
            gunSlot = GetComponent<GunSlot>();
        }

        private void OnDisable() => SetFocus(null);

        // 카메라·시선이 이번 프레임 위치로 움직인 뒤에 판정
        private void LateUpdate() => SetFocus(CanInteractNow ? FindTarget() : null);
        #endregion

        #region 외부 API
        /// <summary>상호작용 키 입력 (PlayerController). 대상이 없거나 쓸 수 없는 상태면 false</summary>
        public bool TryInteract()
        {
            if (CanInteractNow == false || hasFocus == false || IsAlive(focus) == false) return false;
            if (focus.CanInteract(gameObject) == false) return false;

            bool done = focus.Interact(gameObject);
            SetFocus(CanInteractNow ? FindTarget() : null); // 대상이 사라졌거나 프롬프트가 바뀌었을 수 있다
            return done;
        }
        #endregion

        #region 내부
        private bool CanInteractNow =>
            player != null && player.IsAlive && player.IsReviving == false && player.IsRolling == false
            && player.IsExternallyControlled == false && player.IsThrowingGrenade == false
            && player.IsGameplayInputBlocked == false && Cursor.lockState == CursorLockMode.Locked;

        private IInteractable FindTarget()
        {
            if (gunSlot == null) return null;
            Ray ray = gunSlot.AimRay;
            int count = Physics.RaycastNonAlloc(ray, hits, interactRange + rayPadding, mask, QueryTriggerInteraction.Collide);
            if (count == 0) return null;
            Array.Sort(hits, 0, count, Comparer.Instance);

            Vector3 body = transform.position + Vector3.up * bodyCenterHeight;
            for (int i = 0; i < count; i++)
            {
                Collider col = hits[i].collider;
                if (col == null || col.transform.IsChildOf(transform)) continue;

                var target = col.GetComponentInParent<IInteractable>();
                if (target != null && IsAlive(target))
                {
                    Transform point = target.InteractTransform != null ? target.InteractTransform : col.transform;
                    bool inRange = (point.position - body).sqrMagnitude <= interactRange * interactRange;
                    if (inRange && target.CanInteract(gameObject)) return target;
                }
                if (col.isTrigger == false) return null; // 벽·바닥 등에 막힘
            }
            return null;
        }

        private void SetFocus(IInteractable target)
        {
            if (target == null)
            {
                if (hasFocus == false) return;
                hasFocus = false;
                focus = null;
                focusPrompt = default;
                EventBus<InteractFocusChangedEvent>.Publish(new InteractFocusChangedEvent(false, default));
                return;
            }

            InteractPrompt prompt = target.GetPrompt(gameObject);
            if (hasFocus && ReferenceEquals(target, focus) && prompt.Equals(focusPrompt)) return;
            hasFocus = true;
            focus = target;
            focusPrompt = prompt;
            EventBus<InteractFocusChangedEvent>.Publish(new InteractFocusChangedEvent(true, prompt));
        }

        /// <summary>파괴된 MonoBehaviour 도 인터페이스로는 null 이 아니라서 따로 확인</summary>
        private static bool IsAlive(IInteractable target) => target is Object obj ? obj != null : target != null;

        private sealed class Comparer : IComparer<RaycastHit>
        {
            public static readonly Comparer Instance = new Comparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }
        #endregion
    }
}
