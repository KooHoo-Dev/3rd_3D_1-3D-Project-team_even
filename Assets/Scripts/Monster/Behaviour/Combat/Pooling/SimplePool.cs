using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 프리팹 1개의 간단한 재사용 풀. PoisonAreaSettings 가 투사체·장판·표식마다 1개씩 만든다.
    /// 꺼낸 물체는 씬 루트에 두므로 몬스터가 회수돼도 함께 사라지지 않는다.
    /// </summary>
    /// <typeparam name="T">프리팹 루트에 붙은 컴포넌트 타입</typeparam>
    public sealed class SimplePool<T> where T : Component
    {
        private readonly T prefab;
        private readonly Stack<T> inactiveItems = new Stack<T>();

        /// <summary>풀을 만든다. 물체는 처음 꺼낼 때 만든다</summary>
        /// <param name="prefab">복제할 프리팹</param>
        /// <exception cref="ArgumentNullException">prefab 이 null 일 때</exception>
        public SimplePool(T prefab)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            this.prefab = prefab;
        }

        /// <summary>
        /// 쉬고 있는 물체를 꺼내 position 에 놓고 켠다. 쉬는 물체가 없으면 새로 만든다.
        /// </summary>
        /// <param name="position">놓을 월드 위치</param>
        /// <returns>켜진 물체</returns>
        public T Get(Vector3 position)
        {
            T item;
            if (inactiveItems.Count > 0) item = inactiveItems.Pop();
            else item = UnityEngine.Object.Instantiate(prefab);

            item.transform.position = position;
            item.gameObject.SetActive(true);
            return item;
        }

        /// <summary>물체를 끄고 풀에 돌려놓는다. 꺼낸 쪽(투사체·장판·공격)이 수명이 끝날 때 부른다</summary>
        /// <param name="item">이 풀에서 꺼낸 물체</param>
        public void Return(T item)
        {
            item.gameObject.SetActive(false);
            inactiveItems.Push(item);
        }
    }
}
