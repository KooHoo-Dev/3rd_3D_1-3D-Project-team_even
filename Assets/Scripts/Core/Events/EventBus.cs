using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Core.Events
{
    /// <summary>
    /// 타입별 정적 이벤트 버스. "누가 듣는지 모르는" 알림에 사용한다. (몬스터 처치 → 재화·스폰 카운트·UI·사운드)
    ///
    ///   발행: EventBus&lt;MonsterDiedEvent&gt;.Publish(new MonsterDiedEvent(def, pos, gameObject));
    ///   구독: void OnEnable()  => EventBus&lt;MonsterDiedEvent&gt;.Subscribe(OnMonsterDied);
    ///         void OnDisable() => EventBus&lt;MonsterDiedEvent&gt;.Unsubscribe(OnMonsterDied);
    ///
    /// 설계 포인트
    ///  - 이벤트마다 struct 하나 → 새 이벤트 추가 시 공용 파일을 고칠 필요가 적어 머지 충돌이 줄어든다.
    ///  - 구독자 하나가 예외를 던져도 나머지 구독자는 계속 호출된다. (한 파트 버그가 다른 파트를 멈추지 않음)
    ///  - 도메인 리로드를 꺼도 플레이 시작마다 구독자가 비워진다.
    /// </summary>
    public static class EventBus<T> where T : struct
    {
        private static readonly List<Action<T>> handlers = new List<Action<T>>();
        private static readonly List<Action<T>> snapshot = new List<Action<T>>();

        static EventBus() => EventBusRegistry.Register(Clear);

        public static void Subscribe(Action<T> handler)
        {
            if (handler != null && handlers.Contains(handler) == false) handlers.Add(handler);
        }

        public static void Unsubscribe(Action<T> handler) => handlers.Remove(handler);

        public static void Publish(T evt)
        {
            // 콜백 안에서 구독/해제해도 안전하도록 복사본으로 순회
            snapshot.Clear();
            snapshot.AddRange(handlers);
            foreach (var h in snapshot)
            {
                try { h(evt); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public static int SubscriberCount => handlers.Count;

        private static void Clear() => handlers.Clear();
    }

    internal static class EventBusRegistry
    {
        private static readonly List<Action> clearActions = new List<Action>();

        internal static void Register(Action clear) => clearActions.Add(clear);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearAll()
        {
            foreach (var clear in clearActions) clear();
        }
    }
}
