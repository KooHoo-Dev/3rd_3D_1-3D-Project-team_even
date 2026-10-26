using System.Collections.Generic;
using HybridShooter.Core.Events;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HybridShooter.Monster.Tester
{
    /// <summary>
    /// 피격 확인용 디버그 표시. 씬 아무 GameObject 에 붙인다.
    /// MonsterController.TakeDamage 가 발행하는 DamageDealtEvent 를 받아,
    /// 맞은 자리에 피해량 숫자를 잠깐 띄워 Game 뷰(OnGUI)와 Scene 뷰(Handles.Label)에 그린다.
    /// </summary>
    public sealed class DamageFloatingTextTester : MonoBehaviour
    {
        [Header("표시")]
        [Tooltip("숫자가 떠 있는 시간 (s)")]
        [SerializeField, Min(0.1f)] private float lifetimeSeconds = 1f;
        [Tooltip("숫자가 위로 떠오르는 속도 (m/s)")]
        [SerializeField, Min(0f)] private float riseSpeed = 1f;
        [Tooltip("Game 뷰 글자 크기 (px)")]
        [SerializeField, Min(1)] private int fontSize = 20;
        [SerializeField] private Color normalColor = Color.white;
        [Tooltip("이 타격으로 체력이 0 이 됐을 때 색")]
        [SerializeField] private Color killColor = Color.red;

        /// <summary>떠 있는 숫자 하나</summary>
        private struct FloatingDamage
        {
            public Vector3 HitPoint;
            public int Amount;
            public bool IsKill;
            public float SpawnTime;   // Time.time 기준 (s)
        }

        // ── 표시 중인 숫자 ──
        private readonly List<FloatingDamage> floatingDamages = new List<FloatingDamage>();
        private GUIStyle labelStyle;

        private void OnEnable() => EventBus<DamageDealtEvent>.Subscribe(OnDamageDealt);
        private void OnDisable() => EventBus<DamageDealtEvent>.Unsubscribe(OnDamageDealt);

        /// <summary>
        /// 피해 1번마다 맞은 자리·피해량을 목록에 넣는다.
        /// </summary>
        /// <param name="e">HitPoint, Amount, IsKill 을 읽는다</param>
        private void OnDamageDealt(DamageDealtEvent e)
        {
            floatingDamages.Add(new FloatingDamage
            {
                HitPoint = e.Info.HitPoint,
                Amount = e.Info.Amount,
                IsKill = e.IsKill,
                SpawnTime = Time.time,
            });
        }

        /// <summary>수명이 지난 숫자를 지운다.</summary>
        private void Update()
        {
            floatingDamages.RemoveAll(IsExpired);
        }

        /// <summary>
        /// Game 뷰: 각 숫자의 현재 위치를 Camera.main 화면 좌표로 바꿔 그린다. 카메라 뒤에 있으면 건너뛴다.
        /// </summary>
        private void OnGUI()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            labelStyle.fontSize = fontSize;

            foreach (FloatingDamage damage in floatingDamages)
            {
                Vector3 screenPoint = mainCamera.WorldToScreenPoint(CurrentPosition(damage));
                bool isBehindCamera = screenPoint.z < 0f;
                if (isBehindCamera) continue;

                labelStyle.normal.textColor = ColorOf(damage);
                // GUI 좌표는 위가 0 이라 y 를 뒤집는다
                var rect = new Rect(screenPoint.x, Screen.height - screenPoint.y, 200f, fontSize * 2f);
                GUI.Label(rect, damage.Amount.ToString(), labelStyle);
            }
        }

#if UNITY_EDITOR
        /// <summary>Scene 뷰: 각 숫자를 현재 위치에 라벨로 그린다.</summary>
        private void OnDrawGizmos()
        {
            foreach (FloatingDamage damage in floatingDamages)
            {
                var style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = ColorOf(damage);
                Handles.Label(CurrentPosition(damage), damage.Amount.ToString(), style);
            }
        }
#endif

        /// <summary>생성 위치에서 경과 시간만큼 위로 떠오른 위치</summary>
        private Vector3 CurrentPosition(FloatingDamage damage)
        {
            float elapsedSeconds = Time.time - damage.SpawnTime;
            return damage.HitPoint + Vector3.up * (riseSpeed * elapsedSeconds);
        }

        private Color ColorOf(FloatingDamage damage)
        {
            if (damage.IsKill) return killColor;
            return normalColor;
        }

        private bool IsExpired(FloatingDamage damage) => Time.time - damage.SpawnTime > lifetimeSeconds;
    }
}
