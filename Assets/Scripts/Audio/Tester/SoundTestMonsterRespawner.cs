using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Audio.Tester
{
    /// <summary>
    /// [테스트 씬 전용] 0 키(숫자·키패드)로 몬스터를 처음 배치대로 다시 만든다. 남아 있는 몬스터(살아 있거나 죽는 중)는 지운다.
    /// 실게임 생성처럼 MonsterSpawnedEvent 를 발행 → GameSoundRouter 가 이미터를 붙이고 Def 를 연결한다.
    /// HS 몬스터 프리팹에 Collider 가 없어 생성할 때마다 임시 히트박스를 붙인다 (AddTemporaryHitbox)
    /// </summary>
    public sealed class SoundTestMonsterRespawner : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public GameObject Prefab;
            public Vector3 Position;
        }

        [Tooltip("생성할 몬스터 프리팹과 위치. 플레이어 쪽(원점)을 보게 놓는다")]
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        [Tooltip("몬스터 부모. 다시 만들 때 이 아래를 전부 지운다")]
        [SerializeField] private Transform monsterRoot;

        /// <summary>메뉴가 씬을 만들 때 채운다</summary>
        public void Setup(Entry[] newEntries, Transform root)
        {
            entries = newEntries;
            monsterRoot = root;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.digit0Key.wasPressedThisFrame || keyboard.numpad0Key.wasPressedThisFrame) Respawn();
        }

        /// <summary>남은 몬스터를 지우고 entries 대로 새로 만든다</summary>
        public void Respawn()
        {
            if (monsterRoot == null) monsterRoot = transform;
            for (int i = monsterRoot.childCount - 1; i >= 0; i--) Destroy(monsterRoot.GetChild(i).gameObject);

            int count = 0;
            foreach (var entry in entries)
            {
                if (entry.Prefab == null) continue;
                var rotation = Quaternion.LookRotation(entry.Position.sqrMagnitude > 0.01f ? -entry.Position.normalized : Vector3.forward);
                var monster = Instantiate(entry.Prefab, entry.Position, rotation, monsterRoot); // Awake 에서 MonsterController.Def 가 채워진다
                AddTemporaryHitbox(monster);
                var def = monster.GetComponent<IMonsterSoundSource>()?.Def;
                EventBus<MonsterSpawnedEvent>.Publish(new MonsterSpawnedEvent(def, monster));
                count++;
            }
            Debug.Log($"[SoundTest] 몬스터 {count}마리 다시 생성 (0 키)", this);
        }

        /// <summary>
        /// [임시] Collider 가 하나도 없는 몬스터에 몸 크기(렌더러 경계)의 CapsuleCollider 를 루트에 붙인다.
        /// 루트(MonsterController)에 붙여 총알·폭발이 IDamageable 을 바로 찾게 한다. 이미 Collider 가 있으면 아무것도 하지 않는다
        /// </summary>
        /// <returns>붙였으면 true</returns>
        public static bool AddTemporaryHitbox(GameObject monster)
        {
            if (monster.GetComponentInChildren<Collider>(true) != null) return false;

            var renderers = monster.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return false;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            var root = monster.transform;
            float scale = Mathf.Max(0.0001f, root.lossyScale.y);
            var capsule = monster.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y
            capsule.height = bounds.size.y / scale;
            capsule.radius = Mathf.Min(bounds.size.x, bounds.size.z) * 0.5f / scale;
            capsule.center = root.InverseTransformPoint(bounds.center);
            return true;
        }
    }
}
