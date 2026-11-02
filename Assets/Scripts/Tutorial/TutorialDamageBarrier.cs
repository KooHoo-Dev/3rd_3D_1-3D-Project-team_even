using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 피해 차단막 (① 구르기 확인). 길을 가로지르는 얇은 트리거이고, 진행 방향은 transform.forward.
    ///  - 구르기 무적(PlayerController.IsInvulnerable, 구르기 직후 rollInvulnerableTime) 상태로 들어오면 그대로 통과 → 반대편으로 나가면 Cleared
    ///  - 그냥 닿으면 피해(IDamageable.TakeDamage) 를 주고 불꽃(hitEffect)을 띄운 뒤 들어온 쪽으로 되돌려 보낸다 → Hit
    /// 통과하면 레이저(visual)를 끄고 더 이상 막지 않는다 (양옆 발사 기둥은 남는다). 겉모양: 붉은 레이저 그리드 (TutorialSceneBuilder). 피해량은 TutorialScript.BarrierDamage 를 Director 가 넣는다
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TutorialDamageBarrier : MonoBehaviour
    {
        [Tooltip("보이는 차단막. 통과하면 꺼진다")]
        [SerializeField] private GameObject visual;
        [Tooltip("되돌려 보낼 때 차단막 앞쪽으로 떨어뜨리는 거리(m)")]
        [SerializeField, Min(0.5f)] private float pushBackDistance = 1.5f;
        [Tooltip("그냥 닿았을 때 닿은 자리에 잠깐 띄우는 불꽃 (QFX PS_AreaProtector_LaserImpact). 비우면 없음")]
        [SerializeField] private GameObject hitEffect;
        [Tooltip("불꽃을 띄워 두는 시간(초). 반복 재생 파티클이라 이 시간 뒤 지운다")]
        [SerializeField, Min(0.1f)] private float hitEffectTime = 0.6f;

        /// <summary>그냥 닿아서 피해를 받음</summary>
        public event Action Hit;
        /// <summary>구르며 통과함</summary>
        public event Action Cleared;

        public bool IsCleared { get; private set; }
        public int Damage { get; set; } = 10;

        private Collider trigger;
        private bool rollingThrough;

        private void Awake()
        {
            trigger = GetComponent<Collider>();
            trigger.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsCleared) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null || player.IsAlive == false) return;

            if (player.IsRolling && player.IsInvulnerable)
            {
                rollingThrough = true;
                return;
            }

            rollingThrough = false;
            Vector3 forward = transform.forward;
            Vector3 toPlayer = player.transform.position - transform.position;
            // 들어온 쪽(진행 방향 뒤)으로 되돌린다. 반대편에서 들어왔으면 그쪽으로
            float side = Vector3.Dot(toPlayer, forward) > 0f ? 1f : -1f;
            float halfDepth = Mathf.Abs(Vector3.Dot(trigger.bounds.extents, forward)); // 축 정렬 경계 기준 (차단막은 길을 직각으로 가로지른다)
            Vector3 back = player.transform.position - forward * Vector3.Dot(toPlayer, forward) + forward * side * (halfDepth + pushBackDistance);

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            player.TakeDamage(new DamageInfo(Damage, DamageKind.Bullet, Faction.Neutral, gameObject, hitPoint, forward * side));
            player.Teleport(back);
            if (hitEffect != null) Destroy(Instantiate(hitEffect, hitPoint, Quaternion.LookRotation(forward * side)), hitEffectTime);
            Hit?.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsCleared || rollingThrough == false) return;
            var player = other.GetComponentInParent<PlayerController>();
            if (player == null) return;
            rollingThrough = false;
            if (Vector3.Dot(player.transform.position - transform.position, transform.forward) <= 0f) return; // 되돌아 나감

            IsCleared = true;
            trigger.enabled = false;
            if (visual != null) visual.SetActive(false);
            Cleared?.Invoke();
        }
    }
}
