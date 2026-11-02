using System;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Tutorial
{
    /// <summary>
    /// 도착 트리거 (목표 지점 · 보급 구역 입구 · 강화 구역 · 전초기지 게이트).
    /// 플레이어(PlayerController 가 붙은 몸)가 들어오면 Entered 를 한 번 알리고 HasPlayer 를 켠다. 단계는 HasPlayer 를 읽거나 Entered 를 구독한다
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TutorialZone : MonoBehaviour
    {
        public event Action Entered;

        /// <summary>지금 플레이어가 안에 있다</summary>
        public bool HasPlayer => inside > 0;
        /// <summary>한 번이라도 들어왔다</summary>
        public bool Reached { get; private set; }

        private int inside;

        private void Reset() => GetComponent<Collider>().isTrigger = true;

        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayer(other) == false) return;
            inside++;
            if (Reached) return;
            Reached = true;
            Entered?.Invoke();
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsPlayer(other)) inside = Mathf.Max(0, inside - 1);
        }

        internal static bool IsPlayer(Collider other) => other.GetComponentInParent<PlayerController>() != null;
    }
}
