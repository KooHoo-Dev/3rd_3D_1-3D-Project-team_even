using System.Collections;
using System.Collections.Generic;
using HybridShooter.Core.Data.Loading;
using UnityEngine;

namespace HybridShooter.Core.Data
{
    /// <summary>
    /// Key → Def 조회용 읽기 전용 테이블.
    /// 외부(각 파트)는 Get/TryGet/All 만 쓸 수 있고, 추가는 DataManager(같은 어셈블리)만 가능하다.
    /// </summary>
    public sealed class DefTable<TKey, TDef> : IReadOnlyCollection<TDef> where TDef : class
    {
        private readonly Dictionary<TKey, TDef> map = new Dictionary<TKey, TDef>();
        private readonly List<TDef> ordered = new List<TDef>();

        public string Name { get; }
        public int Count => ordered.Count;

        /// <summary>테이블 파일에 적힌 순서 그대로의 목록 (상점 진열 순서 등에 사용)</summary>
        public IReadOnlyList<TDef> All => ordered;

        internal DefTable(string name) { Name = name; }

        internal bool Add(TKey key, TDef def, DataLoadReport report)
        {
            if (map.ContainsKey(key))
            {
                report.Error(Name, key, "Key 가 중복됩니다. 뒤에 나온 행은 무시합니다.");
                return false;
            }
            map.Add(key, def);
            ordered.Add(def);
            return true;
        }

        /// <summary>없으면 에러 로그 후 null. "반드시 있어야 하는" 데이터를 조회할 때 사용.</summary>
        public TDef Get(TKey key)
        {
            if (map.TryGetValue(key, out var def)) return def;
            Debug.LogError($"[DataManager] {Name} 에 Key '{key}' 가 없습니다.");
            return null;
        }

        /// <summary>없을 수도 있는 데이터를 조회할 때 사용. (로그 없음)</summary>
        public bool TryGet(TKey key, out TDef def) => map.TryGetValue(key, out def);

        public bool Contains(TKey key) => map.ContainsKey(key);

        public IEnumerator<TDef> GetEnumerator() => ordered.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
