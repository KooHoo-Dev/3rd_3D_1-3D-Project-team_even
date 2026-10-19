using System;
using System.Collections.Generic;
using UnityEngine;

namespace HybridShooter.Core.Services
{
    /// <summary>
    /// 아주 작은 서비스 로케이터. "인터페이스 → 지금 씬에 있는 구현체"를 찾아준다.
    ///
    ///   구현 쪽: void OnEnable()  => ServiceLocator.Register&lt;ISoundService&gt;(this);
    ///            void OnDisable() => ServiceLocator.Unregister&lt;ISoundService&gt;(this);
    ///   사용 쪽: ServiceLocator.Get&lt;ISoundService&gt;().PlaySfx("sfx_reload");
    ///
    /// 등록된 구현이 없으면 Fallback(기본 구현)을 돌려준다.
    /// → 사운드 담당이 아직 작업 전이어도, 무기 담당은 PlaySfx 를 호출해 둘 수 있다.
    ///
    /// 싱글톤 대신 쓰는 이유: 사용하는 쪽이 "구체 클래스"가 아닌 "인터페이스"에만 의존하게 해서
    /// 담당자별 작업을 독립시키고, 테스트용 가짜 구현으로 쉽게 바꿔 끼우기 위해.
    /// </summary>
    public static class ServiceLocator
    {
        private static readonly Dictionary<Type, object> registered = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, object> fallbacks = new Dictionary<Type, object>();

        public static void Register<T>(T implementation) where T : class
        {
            if (implementation == null) return;
            if (registered.TryGetValue(typeof(T), out var existing) && existing != null && !ReferenceEquals(existing, implementation))
                Debug.LogWarning($"[ServiceLocator] {typeof(T).Name} 가 이미 등록되어 있어 교체합니다: {existing} → {implementation}");
            registered[typeof(T)] = implementation;
        }

        /// <summary>자신이 등록한 구현일 때만 해제 (다른 오브젝트가 덮어쓴 경우를 지우지 않도록)</summary>
        public static void Unregister<T>(T implementation) where T : class
        {
            if (registered.TryGetValue(typeof(T), out var existing) && ReferenceEquals(existing, implementation))
                registered.Remove(typeof(T));
        }

        /// <summary>등록된 구현이 없을 때 쓸 기본 구현. CoreBootstrap 에서 등록</summary>
        public static void SetFallback<T>(T fallback) where T : class => fallbacks[typeof(T)] = fallback;

        public static T Get<T>() where T : class
        {
            if (TryGet(out T service)) return service;
            Debug.LogError($"[ServiceLocator] {typeof(T).Name} 구현이 등록되지 않았습니다. (해당 컴포넌트가 씬에 있는지 확인)");
            return null;
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (registered.TryGetValue(typeof(T), out var obj) && IsAlive(obj)) { service = (T)obj; return true; }
            if (fallbacks.TryGetValue(typeof(T), out obj)) { service = (T)obj; return true; }
            service = null;
            return false;
        }

        /// <summary>파괴된 MonoBehaviour 가 남아있는 경우 걸러내기 (Unity 의 == null 오버로드)</summary>
        private static bool IsAlive(object obj) => obj is UnityEngine.Object uo ? uo != null : obj != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            registered.Clear();
            fallbacks.Clear();
        }
    }
}
