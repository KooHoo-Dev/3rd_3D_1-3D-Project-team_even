using System;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using UnityEngine;

namespace HybridShooter.Boss.Clypeosaurus
{
    [RequireComponent(typeof(Rigidbody))]
    public class MagmaProjectile : MonoBehaviour
    {
        [SerializeField] private LayerMask damageMask;
        [SerializeField] private LayerMask coverMask;
        [SerializeField, Min(0.1f)] private float maxLifetime = 10f;
        
        private Rigidbody body;
        private SphereCollider sphere;
        private BossController owner;
        private ClypeosaurusDef data;
        private float age;
        
        public bool IsFinished { get; private set; } = true;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            sphere = GetComponentInChildren<SphereCollider>(true);
        }

        public void Launch(BossController owner, ClypeosaurusDef def, Vector3 destination)
        {
            this.owner = owner;
            data = def;
            age = 0f;

            foreach (var ownCollider in owner.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(sphere, ownCollider);
            }

            Vector3 scale = sphere.transform.lossyScale;

            float worldRadius = sphere.radius * Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z));

            Vector3 arrival = destination + Vector3.up * worldRadius;
            
            float time = data.BarrageFlightTime;
            // arrival - body.position : 거리, 방향
            // - (0.5f * Physics.gravity * time * time) : 중력이 아래로 잡아당기는 양을 빼서 위쪽으로 다시 보정
            // / time : 이동량을 초당 이동속도로 변환
            body.linearVelocity = (arrival - body.position - (0.5f * Physics.gravity * time * time)) / time;
            
            IsFinished = false;
        }

        private void Update()
        {
            if (StopIfInvalid()) return;

            age += Time.deltaTime;

            if (age >= maxLifetime) Cancel();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (StopIfInvalid()) return;

            ContactPoint contact = collision.GetContact(0);
            // 0.03f : 중심이 표면에 박히지 않도록 하기 위한 임시 보정 값.
            Vector3 center = contact.point + contact.normal * 0.03f;

            StopFlight();

            try
            {
                ApplyExplosion(center);
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        private void ApplyExplosion(Vector3 center)
        {
            var damaged = new HashSet<IDamageable>();

            Collider[] overlaps = Physics.OverlapSphere(center, data.BarrageRadius, damageMask, QueryTriggerInteraction.Ignore);

            // 폭발 범위 내의 모든 대상에게 피해 적용
            foreach (var col in overlaps)
            {
                if (owner == null || owner.IsAlive == false || owner.isActiveAndEnabled == false) break;
                if (col.TryGetDamageable(out var target) == false 
                    || target.CanBeHitBy(owner.Faction) == false 
                    || damaged.Contains(target)) continue;

                Vector3 hitPoint = col.bounds.center;
                
                if (HasCover(center, hitPoint, target)) continue;

                damaged.Add(target);

                Vector3 direction = hitPoint - center;
                direction = direction.sqrMagnitude > 0.001f
                    ? direction.normalized
                    : Vector3.up;

                var damage = new DamageInfo(data.BarrageDamage, DamageKind.MonsterSkill, owner.Faction, owner.gameObject, hitPoint, direction);
                
                target.TakeDamage(damage);
            }
        }

        /// <summary>
        /// 폭발 지점과 피해 대상 사이의 엄폐물 검사
        /// </summary>
        private bool HasCover(Vector3 origin, Vector3 destination, IDamageable target)
        {
            Vector3 difference = destination - origin;
            float distance = difference.magnitude;

            if (distance < 0.001f) return false;
            
            foreach(var hit in Physics.RaycastAll(origin, (difference / distance), distance, coverMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(owner.transform)) continue;
                if (hit.collider.TryGetDamageable(out var blocker) && object.ReferenceEquals(blocker, target)) continue;

                return true;
            }

            return false;
        }

        private bool StopIfInvalid()
        {
            if (IsFinished)
                return true;

            if (owner == null ||
                owner.IsAlive == false ||
                owner.isActiveAndEnabled == false)
            {
                Cancel();
                return true;
            }

            return false;
        }

        public void Cancel()
        {
            if (IsFinished) return;

            StopFlight();
            Destroy(gameObject);
        }
        
        private void StopFlight()
        {
            IsFinished = true;
            body.linearVelocity = Vector3.zero;

            // Destroy 처리 전 추가 충돌 차단.
            sphere.enabled = false;
        }
    }
}