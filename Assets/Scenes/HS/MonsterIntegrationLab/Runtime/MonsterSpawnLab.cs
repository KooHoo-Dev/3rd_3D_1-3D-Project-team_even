using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Behaviour;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Scenes.HS.MonsterIntegrationLab.Runtime
{
    /// <summary>
    /// 테스트 전용. 통합 씬에서 지정한 몬스터 프리팹을 플레이어 주위 원에 소환하고, 죽은 몬스터를 같은 프리팹끼리 재사용한다.
    /// F5~F12 = monsterPrefabs 0~7번 소환. 오른쪽 위 조작 창에서도 소환·전체 제거를 한다.
    /// </summary>
    /// <remarks>
    /// 켜져 있는 동안 IMonsterFactory 로 등록된다. 그래서 MonsterController.Despawn 이 기본 팩토리(Destroy) 대신 이 클래스의 Despawn 으로 몬스터를 반납한다.
    /// 대상(플레이어)은 몬스터가 스스로 찾는다 (ServiceLocator 의 IPlayerTarget). 군중 묶음을 배정하지 않으므로 몬스터는 직접 추적한다.
    /// </remarks>
    public sealed class MonsterSpawnLab : MonoBehaviour, IMonsterFactory
    {
        [Header("소환 대상")]
        [Tooltip("소환할 몬스터 프리팹. 루트에 MonsterController 가 있어야 한다. 0~7번이 F5~F12 에 대응")]
        [SerializeField] private GameObject[] monsterPrefabs;

        [Header("소환 위치")]
        [Tooltip("한 번 소환할 때 만드는 몬스터 수. 조작 창에서 Play 중 바꾼다")]
        [SerializeField, Min(1)] private int spawnCount = 8;
        [Tooltip("소환 원의 반지름 (m). 원의 중심은 소환하는 순간의 플레이어 위치")]
        [SerializeField, Min(0f)] private float spawnRadius = 12f;
        [Tooltip("소환점에서 이 거리 (m) 안의 가장 가까운 NavMesh 위로 옮긴다. 못 찾으면 원래 점에 소환")]
        [SerializeField, Min(0.1f)] private float spawnSampleDistance = 3f;

        private static readonly Key[] SpawnKeys = { Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12 };

        // ── 풀 ──
        // 프리팹별로 반납돼 쉬고 있는 몬스터
        private readonly Dictionary<GameObject, Stack<MonsterController>> idleMonstersByPrefab = new Dictionary<GameObject, Stack<MonsterController>>();
        // 이 클래스가 만든 몬스터 → 원본 프리팹. 반납할 스택을 찾을 때 쓴다
        private readonly Dictionary<MonsterController, GameObject> prefabByMonster = new Dictionary<MonsterController, GameObject>();
        private readonly List<MonsterController> activeMonsters = new List<MonsterController>();

        // ── 조작 창 ──
        private const float WindowWidth = 320f;
        private Rect windowRect = new Rect(0f, 12f, WindowWidth, 0f);

        /// <summary>팩토리로 등록하고, 에디터에서는 Scene 뷰에도 조작 창을 그리도록 구독한다</summary>
        private void OnEnable()
        {
            ServiceLocator.Register<IMonsterFactory>(this);
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui += OnSceneGUI;
#endif
        }

        /// <summary>팩토리 등록과 Scene 뷰 구독을 해제한다</summary>
        private void OnDisable()
        {
            ServiceLocator.Unregister<IMonsterFactory>(this);
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
#endif
        }

        /// <summary>F5~F12 중 이번 프레임 눌린 키의 번호로 소환한다. 배열에 없는 번호의 키는 무시한다</summary>
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < SpawnKeys.Length; i++)
            {
                bool isPressed = keyboard[SpawnKeys[i]].wasPressedThisFrame;
                bool hasPrefab = i < monsterPrefabs.Length;
                if (isPressed && hasPrefab) SpawnAround(i);
            }
        }

        /// <summary>
        /// monsterPrefabs[prefabIndex] 를 spawnCount 마리, 플레이어 주위 원에 균등 간격으로 소환한다.
        /// F 키와 조작 창 버튼이 호출한다.
        /// </summary>
        /// <param name="prefabIndex">monsterPrefabs 배열 번호 (0부터)</param>
        public void SpawnAround(int prefabIndex)
        {
            GameObject prefab = monsterPrefabs[prefabIndex];
            for (int i = 0; i < spawnCount; i++)
            {
                float angle = i * Mathf.PI * 2f / spawnCount;
                Vector3 offsetFromPlayer = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnRadius;
                SpawnOne(prefab, GetSpawnPoint(offsetFromPlayer), Quaternion.identity);
            }
        }

        /// <summary>
        /// 살아 있는 소환 몬스터를 보상 없이 전부 죽인다. 사망 연출(시체 누움·침강) 뒤 각자 Despawn → 이 클래스의 풀로 반납된다.
        /// </summary>
        [ContextMenu("전체 제거")]
        public void KillAll()
        {
            foreach (MonsterController monster in activeMonsters)
            {
                bool isAssembled = monster.StateMachine != null;
                if (isAssembled) monster.StateMachine.Die(false);
            }
        }

        /// <summary>
        /// 소환 위치를 정한다. 중심은 소환하는 순간의 플레이어 위치다.
        /// 경사·계단 지형에서도 NavMeshAgent 가 NavMesh 위에 생기도록 가장 가까운 NavMesh 점으로 옮긴다.
        /// </summary>
        /// <param name="offsetFromPlayer">플레이어 위치에서의 수평 오프셋 (m)</param>
        /// <returns>소환할 월드 좌표. 반경 spawnSampleDistance 안에 NavMesh 가 없으면 옮기지 않은 원래 점</returns>
        private Vector3 GetSpawnPoint(Vector3 offsetFromPlayer)
        {
            Vector3 point = ServiceLocator.Get<IPlayerTarget>().Root.position + offsetFromPlayer;
            bool isOnNavMesh = NavMesh.SamplePosition(point, out NavMeshHit hit, spawnSampleDistance, NavMesh.AllAreas);
            if (isOnNavMesh == false) return point;
            return hit.position;
        }

        /// <summary>
        /// 프리팹의 쉬는 몬스터가 있으면 꺼내 되살리고, 없으면 새로 만든다. 소환 이벤트를 발행한다.
        /// </summary>
        /// <param name="prefab">소환할 몬스터 프리팹 (루트에 MonsterController)</param>
        /// <param name="position">소환 월드 위치</param>
        /// <param name="rotation">소환 회전</param>
        /// <returns>소환된 몬스터 GameObject</returns>
        private GameObject SpawnOne(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            MonsterController monster;
            Stack<MonsterController> idleMonsters = GetIdleMonsters(prefab);
            if (idleMonsters.Count > 0)
            {
                monster = idleMonsters.Pop();
                // 꺼진 상태에서 옮긴 뒤 켜야 NavMeshAgent 가 새 위치에서 시작한다
                monster.transform.SetPositionAndRotation(position, rotation);
                monster.gameObject.SetActive(true);
                monster.Respawn();
            }
            else
            {
                // 새 개체는 Awake 가 바로, Start(상태 머신 조립)가 다음 프레임에 실행된다
                monster = Instantiate(prefab, position, rotation).GetComponent<MonsterController>();
                prefabByMonster.Add(monster, prefab);
            }

            activeMonsters.Add(monster);
            EventBus<MonsterSpawnedEvent>.Publish(new MonsterSpawnedEvent(monster.Def, monster.gameObject));
            return monster.gameObject;
        }

        /// <summary>프리팹의 쉬는 몬스터 스택을 돌려준다. 처음 쓰는 프리팹이면 빈 스택을 만든다</summary>
        /// <param name="prefab">몬스터 프리팹</param>
        /// <returns>그 프리팹의 쉬는 몬스터 스택</returns>
        private Stack<MonsterController> GetIdleMonsters(GameObject prefab)
        {
            if (idleMonstersByPrefab.TryGetValue(prefab, out var idleMonsters)) return idleMonsters;
            idleMonsters = new Stack<MonsterController>();
            idleMonstersByPrefab.Add(prefab, idleMonsters);
            return idleMonsters;
        }

        // ════════ IMonsterFactory ════════

        /// <summary>테이블 Def 의 프리팹으로 1마리 소환한다. 게임 쪽 코드가 팩토리로 소환할 때 쓰인다</summary>
        /// <param name="def">소환할 몬스터 Def. Prefab 을 쓴다</param>
        /// <param name="position">소환 월드 위치</param>
        /// <param name="rotation">소환 회전</param>
        /// <returns>소환된 몬스터 GameObject</returns>
        public GameObject Spawn(MonsterDef def, Vector3 position, Quaternion rotation) => SpawnOne(def.Prefab, position, rotation);

        /// <summary>
        /// MonsterController.Despawn 이 호출한다. 이 클래스가 만든 몬스터는 끄고 프리팹별 풀에 반납한다.
        /// 씬에 미리 놓인 몬스터처럼 이 클래스가 만들지 않은 몬스터는 기본 팩토리처럼 파괴한다.
        /// </summary>
        /// <param name="monster">회수할 몬스터 GameObject</param>
        public void Despawn(GameObject monster)
        {
            var controller = monster.GetComponent<MonsterController>();
            if (prefabByMonster.TryGetValue(controller, out GameObject prefab) == false)
            {
                Destroy(monster);
                return;
            }

            monster.SetActive(false);
            activeMonsters.Remove(controller);
            GetIdleMonsters(prefab).Push(controller);
        }

        // ════════ IMGUI ════════

        /// <summary>Game 뷰 오른쪽 위에 조작 창을 그린다. 옛 MovementLabController 창(왼쪽 위)과 겹치지 않게 한다</summary>
        private void OnGUI() => DrawControlWindow(Screen.width);

        /// <summary>
        /// 조작 창을 뷰 오른쪽 위에 그린다. Game 뷰(OnGUI)와 Scene 뷰(OnSceneGUI)가 같이 쓴다.
        /// </summary>
        /// <param name="viewWidth">창을 그릴 뷰의 너비 (px). 창을 오른쪽 끝에 붙이는 데 쓴다</param>
        private void DrawControlWindow(float viewWidth)
        {
            windowRect.x = viewWidth - WindowWidth - 12f;
            windowRect.height = 0f; // 내용 길이에 맞게 늘고 줄게 매번 리셋
            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "Monster Spawn Lab");
        }

#if UNITY_EDITOR
        /// <summary>Scene 뷰 위에 조작 창을 띄운다. Handles.BeginGUI 구간에서 DrawControlWindow 를 그린다</summary>
        private void OnSceneGUI(UnityEditor.SceneView sceneView)
        {
            UnityEditor.Handles.BeginGUI();
            DrawControlWindow(sceneView.position.width);
            UnityEditor.Handles.EndGUI();
            sceneView.Repaint(); // 활성/풀 대기 수가 실시간으로 갱신되도록 계속 다시 그린다
        }
#endif

        /// <summary>조작 창 내용: 소환 수, 프리팹별 소환 버튼(F 키 표시), 전체 제거, 활성/대기 수</summary>
        private void DrawWindow(int id)
        {
            DrawSpawnCountField();

            for (int i = 0; i < monsterPrefabs.Length; i++)
            {
                string keyLabel = i < SpawnKeys.Length ? SpawnKeys[i].ToString() : "-";
                if (GUILayout.Button($"{keyLabel}  {monsterPrefabs[i].name}")) SpawnAround(i);
            }

            GUILayout.Space(4f);
            if (GUILayout.Button("전체 제거 (보상 없이 사망)")) KillAll();
            GUILayout.Label($"활성 {activeMonsters.Count} | 풀 대기 {CountIdleMonsters()}");
        }

        /// <summary>
        /// 소환 수: 슬라이더(1~100) + 직접 입력 칸. 입력 칸은 숫자가 아니면 무시하고(타이핑 중 빈 칸 허용),
        /// 슬라이더 범위를 넘는 숫자도 그대로 받는다(최소 1).
        /// </summary>
        private void DrawSpawnCountField()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("소환 수", GUILayout.Width(60f));
            int sliderValue = Mathf.RoundToInt(GUILayout.HorizontalSlider(spawnCount, 1f, 100f));
            if (sliderValue != spawnCount) spawnCount = sliderValue;

            string typed = GUILayout.TextField(spawnCount.ToString(), GUILayout.Width(50f));
            if (int.TryParse(typed, out int typedValue)) spawnCount = Mathf.Max(1, typedValue);
            GUILayout.EndHorizontal();
        }

        /// <summary>모든 프리팹 풀에서 쉬고 있는 몬스터 수를 더한다. 조작 창 표시용</summary>
        /// <returns>쉬고 있는 몬스터 총 수</returns>
        private int CountIdleMonsters()
        {
            int total = 0;
            foreach (Stack<MonsterController> idleMonsters in idleMonstersByPrefab.Values) total += idleMonsters.Count;
            return total;
        }
    }
}
