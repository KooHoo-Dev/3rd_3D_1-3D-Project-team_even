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
    /// 몬스터가 추적하는 대상(IPlayerTarget)이자 피격 대상(IDamageable)이다.
    /// 실제 플레이어가 구현되기 전까지의 임시 대역이며, 이식할 때는 Register/Unregister 만 걷어내면 된다.
    /// </summary>
    public sealed class TransformMovementTarget : MonoBehaviour, IPlayerTarget, IDamageable
    {
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField] private float turnSpeed = 540f;
        // 선언 시 생성: Awake 전(편집 모드 Gizmos 등)에도 null 이 아니다. PlayerController 와 같은 방식
        private readonly PlayerTrackingInfo playerTrackingInfo = new PlayerTrackingInfo();

        // ── IPlayerTarget ──
        public Transform Root => transform;
        public Transform ViewPoint => transform;

        public PlayerTrackingInfo PlayerTrackingInfo => playerTrackingInfo;

        // IDamageable 의 IsAlive
        public bool IsAlive => isActiveAndEnabled;

        // ── IDamageable: 몬스터 공격이 피해를 줄 때 쓰는 값 (공격 시 IDamageable 이 없으면 예외) ──
        public Faction Faction => Faction.Player;
        public void TakeDamage(in DamageInfo info) =>
            Debug.Log($"[Lab 플레이어] 피해 {info.Amount} ({info.Kind})", this);

        /// <summary>실제 게임과 같은 경로: 몬스터의 MonsterController.AcquireTarget 이 ServiceLocator 에서 꺼내 간다</summary>

        #region unity method

        private void OnEnable() => ServiceLocator.Register<IPlayerTarget>(this);

        private void OnDisable() => ServiceLocator.Unregister<IPlayerTarget>(this);

        /// <summary>첫 LateUpdate 전에 몬스터가 읽어도 원점이 아닌 현재 위치를 보도록 값을 채운다</summary>
        private void Awake()
        {
            playerTrackingInfo.Position = transform.position;
            playerTrackingInfo.Forward = transform.forward;
            playerTrackingInfo.Rotation = transform.rotation;
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

        /// <summary>몬스터가 읽는 위치·방향·회전을 현재 transform 으로 맞춘다 (PlayerController.SetPlayerTrackingInfo 와 같은 역할)</summary>
        private void LateUpdate()
        {
            playerTrackingInfo.Position = transform.position;
            playerTrackingInfo.Forward = transform.forward;
            playerTrackingInfo.Rotation = transform.rotation;
        }

        #endregion
    }
}
