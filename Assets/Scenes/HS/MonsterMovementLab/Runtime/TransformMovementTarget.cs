using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>
    /// 이동 실험실의 간단한 키보드 플레이어.
    /// 이동용(IMovementTarget)이자 몬스터 브레인용(IPlayerTarget)·피격 대상(IDamageable)이다.
    /// 실제 플레이어가 구현되기 전까지의 임시 대역이며, 이식할 때는 Register/Unregister 만 걷어내면 된다.
    /// </summary>
    public sealed class TransformMovementTarget : MonoBehaviour, IPlayerTarget, IDamageable
    {
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField] private float turnSpeed = 540f;
        private PlayerTrackingInfo playerTrackingInfo;

        // ── IPlayerTarget 공통 ──
        public bool IsValid => isActiveAndEnabled;

        public Transform Root => transform;
        public Transform ViewPoint => transform;

        public PlayerTrackingInfo PlayerTrackingInfo => playerTrackingInfo;

        // IPlayerTarget 과 IDamageable 의 IsAlive 를 하나로 충족한다 (IsValid 와 같은 조건)
        public bool IsAlive => isActiveAndEnabled;

        // ── IDamageable: 몬스터 공격이 피해를 줄 때 쓰는 값 (공격 시 IDamageable 이 없으면 예외) ──
        public Faction Faction => Faction.Player;
        public void TakeDamage(in DamageInfo info) =>
            Debug.Log($"[Lab 플레이어] 피해 {info.Amount} ({info.Kind})", this);

        /// <summary>실제 게임과 같은 경로: 몬스터의 MonsterController.AcquireTarget 이 ServiceLocator 에서 꺼내 간다</summary>

        #region unity method

        private void OnEnable() => ServiceLocator.Register<IPlayerTarget>(this);

        private void OnDisable() => ServiceLocator.Unregister<IPlayerTarget>(this);

        private void Awake()
        {
            playerTrackingInfo = new PlayerTrackingInfo(transform.position, transform.forward, transform.rotation);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector2 input = Vector2.zero;
            if (keyboard.wKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed) input.y -= 1f;
            if (keyboard.dKey.isPressed) input.x += 1f;
            if (keyboard.aKey.isPressed) input.x -= 1f;
            if (input.sqrMagnitude < 0.001f) return;

            Vector3 direction = new Vector3(input.x, 0f, input.y).normalized;
            transform.position += direction * (moveSpeed * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction, Vector3.up), turnSpeed * Time.deltaTime);
        }

        #endregion
    }
}
