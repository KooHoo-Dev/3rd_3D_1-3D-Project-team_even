using System.Collections.Generic;
using HybridShooter.Monster.Movement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>Core 이동 모듈을 실제 NavMesh 장면에서 손으로 확인하는 MVP 실험실.</summary>
    public sealed class MovementLabExample : MonoBehaviour
    {
        [SerializeField] private CrowdCoordinator coordinator;
        [SerializeField] private TransformMovementTarget target;
        [SerializeField] private Transform monsterRoot;
        [SerializeField] private MovementLabGizmos gizmos;
        [SerializeField] private Transform ownedRoot;
        [SerializeField] private int crowdSpawnCount = 24;

        [Header("Monster Prefabs")]
        [Tooltip("소환할 실제 몬스터 프리팹(모델만 있어도 됨). 이동 컴포넌트는 소환 시 코드로 붙인다. 순서대로 돌려 쓴다")]
        [SerializeField] private GameObject[] monsterPrefabs;
        [Tooltip("프리팹 크기 배율")]
        [SerializeField, Min(0.01f)] private float monsterVisualScale = 1f;

        [Header("Crowd Think")]
        [Tooltip("개체당 Think 간격 (s). 코디네이터는 씬에 1개라 모든 몬스터에 공통 적용된다. Lab 슬라이더는 Play 중 임시 조정")]
        [SerializeField, Min(0.02f)] private float thinkCycle = 0.5f;

        private readonly List<MovementLabOwnedMonster> ownedMonsters = new List<MovementLabOwnedMonster>();
        private int spawnSerial;
        // 조립 중인 몬스터를 잠시 두는 비활성 부모. 이 밑에서는 Awake/OnEnable/OnDisable 이 한 번도 실행되지 않는다
        private Transform spawnHolder;

        public CrowdCoordinator Coordinator => coordinator;
        public TransformMovementTarget Target => target;
        public Transform MonsterRoot => monsterRoot;
        /// <summary>소환된 몬스터 (사망해 비활성화된 개체 포함)</summary>
        public IReadOnlyList<MovementLabOwnedMonster> Monsters => ownedMonsters;

        // 실험 조작 UI(MovementLabController)를 Play 시 자동으로 붙이고, 몬스터 조립용 비활성 부모를 만든다
        private void Awake()
        {
            if (GetComponent<MovementLabController>() == null) gameObject.AddComponent<MovementLabController>();

            var holder = new GameObject("Spawn Holder (inactive)");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            spawnHolder = holder.transform;
        }

        // Play 시작 시 인스펙터 값을 코디네이터에 1회 반영 (씬에 저장된 코디네이터 값은 코드 기본값이 바뀌어도 그대로라서 필요)
        private void Start() => ApplyThinkCycle();

        // 인스펙터 수정 시(Play 중 포함) 즉시 반영
        private void OnValidate() => ApplyThinkCycle();

        /// <summary>인스펙터의 thinkCycle 을 코디네이터 설정에 밀어 넣는다. 코디네이터가 아직 없으면(Build 전) 건너뜀</summary>
        private void ApplyThinkCycle()
        {
            if (coordinator == null) return;
            coordinator.Settings.ThinkCycle = thinkCycle;
        }

        [ContextMenu("Laboratory/Build Laboratory")]
        public void BuildLaboratory()
        {
            if (Application.isPlaying || IsPrefabAsset()) { Debug.LogError("Build Laboratory는 씬의 일반 GameObject에서 Edit mode로 실행하세요.", this); return; }
            ResetLaboratory();
            var root = new GameObject("Monster Movement Laboratory (Owned)");
            root.AddComponent<MovementLabOwnedMonster>();
            ownedRoot = root.transform;
            root.transform.SetParent(transform, false);
            CreateBox(root.transform, "Floor", new Vector3(0f, -0.25f, 0f), new Vector3(30f, 0.5f, 30f), new Color(.24f, .28f, .31f));
            CreateBox(root.transform, "North Wall", new Vector3(0f, 1f, 15f), new Vector3(30f, 2f, .5f), new Color(.32f, .36f, .39f));
            CreateBox(root.transform, "South Wall", new Vector3(0f, 1f, -15f), new Vector3(30f, 2f, .5f), new Color(.32f, .36f, .39f));
            CreateBox(root.transform, "East Wall", new Vector3(15f, 1f, 0f), new Vector3(.5f, 2f, 30f), new Color(.32f, .36f, .39f));
            CreateBox(root.transform, "West Wall", new Vector3(-15f, 1f, 0f), new Vector3(.5f, 2f, 30f), new Color(.32f, .36f, .39f));
            CreateBox(root.transform, "Fixed Test Obstacle", new Vector3(4f, 1f, 2f), new Vector3(2.5f, 2f, 7f), new Color(.4f, .45f, .5f));

            var surface = root.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            var targetObject = new GameObject("WASD Target (WASD)");
            targetObject.transform.SetParent(root.transform, false);
            target = targetObject.AddComponent<TransformMovementTarget>();
            var targetVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            targetVisual.name = "Target Visual (pivot at feet)";
            targetVisual.transform.SetParent(targetObject.transform, false);
            targetVisual.transform.localPosition = Vector3.up;
            DestroyImmediate(targetVisual.GetComponent<Collider>());
            SetColor(targetVisual.GetComponent<Renderer>(), new Color(.18f, .75f, 1f));

            var coordObject = new GameObject("Crowd Coordinator");
            coordObject.transform.SetParent(root.transform, false);
            coordinator = coordObject.AddComponent<CrowdCoordinator>();
            coordinator.SetTarget(target);
            coordinator.Settings.UseSlots = true;

            monsterRoot = new GameObject("Owned Monsters").transform;
            monsterRoot.SetParent(root.transform, false);
            gizmos = coordObject.AddComponent<MovementLabGizmos>();
            gizmos.Configure(coordinator, monsterRoot);
            coordinator.RefreshSlots();
            SetupCamera(root.transform);
            SetupLight(root.transform);
            Debug.Log("Monster Movement Laboratory 생성 완료. Play 후 ContextMenu의 Spawn Slot Crowd 또는 Spawn Token Crowd를 사용하세요.", this);
        }

        [ContextMenu("Laboratory/Reset Laboratory")]
        public void ResetLaboratory()
        {
            if (Application.isPlaying || IsPrefabAsset()) { Debug.LogError("Reset Laboratory는 씬의 일반 GameObject에서 Edit mode로 실행하세요.", this); return; }
            if (ownedRoot != null && ownedRoot.GetComponent<MovementLabOwnedMonster>() != null)
                DestroyImmediate(ownedRoot.gameObject);
            ownedRoot = null; coordinator = null; target = null; monsterRoot = null; gizmos = null;
        }

        [ContextMenu("Play/Spawn Single MVP")]
        public void SpawnSingleMvp()
        {
            if (!ValidatePlaySetup()) return;
            ClearOwnedMonstersInternal();
            SpawnMonster(MovementPolicyKind.Melee, Vector3.right * -5f, false);
        }

        [ContextMenu("Play/Spawn Crowd MVP")]
        public void SpawnCrowdMvp()
        {
            if (!ValidatePlaySetup()) return;
            ClearOwnedMonstersInternal();
            for (int i = 0; i < crowdSpawnCount; i++)
            {
                float angle = i * Mathf.PI * 2f / crowdSpawnCount;
                SpawnMonster(MovementPolicyKind.Melee, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 12f, false);
            }
        }

        [ContextMenu("Play/Spawn Slot Crowd")]
        public void SpawnSlotCrowd()
        {
            SpawnCoordinatorCrowd(true);
        }

        [ContextMenu("Play/Spawn Token Crowd")]
        public void SpawnTokenCrowd()
        {
            SpawnCoordinatorCrowd(false);
        }

        private void SpawnCoordinatorCrowd(bool useSlots)
        {
            if (!ValidatePlaySetup()) return;
            ClearOwnedMonstersInternal();
            coordinator.Settings.UseSlots = useSlots;
            if (useSlots) coordinator.RefreshSlots();
            for (int i = 0; i < crowdSpawnCount; i++)
            {
                float angle = i * Mathf.PI * 2f / crowdSpawnCount;
                MovementPolicyKind kind = i % 7 == 0 ? MovementPolicyKind.KeepDistance
                    : i % 4 == 0 ? MovementPolicyKind.Flank : MovementPolicyKind.Melee;
                SpawnMonster(kind, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 12f, true);
            }
        }

        [ContextMenu("Play/Remove Assigned Monster")]
        public void RemoveAssignedMonster()
        {
            if (!ValidatePlaySetup()) return;
            for (int i = monsterRoot.childCount - 1; i >= 0; i--)
            {
                var child = monsterRoot.GetChild(i);
                if (child.GetComponent<MovementLabOwnedMonster>() == null) continue;
                var movement = child.GetComponent<MonsterMovement>();
                if (movement == null || movement.Directive.Kind != DirectiveKind.Engage || !movement.Directive.HasSlotPosition) continue;
                movement.enabled = false;
                ownedMonsters.Remove(child.GetComponent<MovementLabOwnedMonster>());
                Destroy(child.gameObject);
                return;
            }
            Debug.Log("제거할 슬롯 할당 몬스터가 없습니다. Slot Crowd를 실행하고 배분을 기다리세요.", this);
        }

        [ContextMenu("Play/Clear Owned Monsters")]
        public void ClearOwnedMonsters()
        {
            if (!ValidatePlaySetup()) return;
            ClearOwnedMonstersInternal();
        }

        [ContextMenu("Play/Dump Slots")]
        public void DumpSlots()
        {
            if (!ValidatePlaySetup()) return;
            if (!coordinator.Settings.UseSlots) { Debug.Log("토큰 모드에는 예약 슬롯이 없습니다.", coordinator); return; }
            var lines = new List<string>();
            for (int i = 0; i < coordinator.SlotCount; i++)
            {
                var s = coordinator.GetSlotSnapshot(i);
                lines.Add($"#{s.Id} ring:{s.RingIndex} {s.Use} {s.Validity} owner={s.OwnerId} pos={s.Position} congested={s.Congested}");
            }
            Debug.Log(lines.Count == 0 ? "슬롯 없음" : string.Join("\n", lines), coordinator);
        }

        [ContextMenu("Play/Move Target To Center")]
        public void MoveTargetToCenter()
        {
            if (!ValidatePlaySetup()) return;
            target.transform.position = Vector3.zero;
            coordinator.RefreshSlots();
        }

        private void SpawnMonster(MovementPolicyKind kind, Vector3 position, bool useCoordinator)
        {
            if (!ValidatePlaySetup()) return;
            if (monsterPrefabs == null || monsterPrefabs.Length == 0)
            {
                Debug.LogError("Monster Prefabs 가 비어 있습니다. MovementLabExample 의 Monster Prefabs 에 몬스터 프리팹을 지정하세요.", this);
                return;
            }
            var prefab = monsterPrefabs[spawnSerial++ % monsterPrefabs.Length];
            // 비활성 부모 밑에 만들어 Configure 가 끝날 때까지 Awake/OnEnable 을 막는다.
            // SetActive(false) 는 MonsterController.OnDisable 이 movement 를 꺼 버려서 쓰지 않는다
            var go = Instantiate(prefab, position, Quaternion.identity, spawnHolder);
            go.name = $"Lab {kind} {prefab.name}";
            go.transform.localScale = prefab.transform.localScale * monsterVisualScale;
            // 몬스터 프리팹 기준 스폰
            var ownedMonster = go.AddComponent<MovementLabOwnedMonster>();
            var movement = go.GetComponent<MonsterMovement>();
            var agent = go.GetComponent<NavMeshAgent>();
            agent.radius = .45f; agent.height = 1.8f; agent.speed = 3.5f; agent.stoppingDistance = .1f;
            agent.avoidancePriority = Random.Range(20, 80);
            // 프리팹의 Profile 은 프로젝트 에셋이다. 복사본을 넘겨야 Lab 이 파괴/교체(SetPolicy)해도 에셋이 안전하다
            ownedMonster.Initialize(Instantiate(movement.Profile), useCoordinator ? coordinator : null, target);
            ownedMonsters.Add(ownedMonster);

            // 빈 몬스터 프리팹 기준 스폰
            // var ownedMonster = go.AddComponent<MovementLabOwnedMonster>();
            // var agent = go.AddComponent<NavMeshAgent>();
            // agent.radius = .45f; agent.height = 1.8f; agent.speed = 3.5f; agent.stoppingDistance = .1f;
            // agent.avoidancePriority = Random.Range(20, 80);
            // go.AddComponent<NavMeshMoveExecutor>();
            // go.AddComponent<MonsterMovement>();
            // ownedMonster.Initialize(MovementLabProfiles.CreateRuntime(kind), useCoordinator ? coordinator : null, target);
            // ownedMonsters.Add(ownedMonster);
            go.transform.SetParent(monsterRoot);   // 활성 부모로 옮기는 순간 Awake/OnEnable/Start 가 처음 실행된다 (월드 위치 유지)
        }

        private static GameObject CreateBox(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name; box.transform.SetParent(parent, false); box.transform.localPosition = position; box.transform.localScale = scale;
            SetColor(box.GetComponent<Renderer>(), color);
            return box;
        }

        private static void SetupCamera(Transform parent)
        {
            var go = new GameObject("Lab Camera");
            var camera = go.AddComponent<Camera>(); go.AddComponent<AudioListener>();
            camera.transform.SetParent(parent, true);
            camera.transform.position = new Vector3(0f, 25f, -27f);
            camera.transform.rotation = Quaternion.Euler(42f, 0f, 0f);
            camera.orthographic = false; camera.fieldOfView = 55f;
        }

        private static void SetupLight(Transform parent)
        {
            var light = new GameObject("Lab Directional Light").AddComponent<Light>();
            light.transform.SetParent(parent, false); light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private bool ValidatePlaySetup()
        {
            if (!Application.isPlaying || coordinator == null || target == null || monsterRoot == null || ownedRoot == null ||
                ownedRoot.GetComponent<MovementLabOwnedMonster>() == null)
            {
                Debug.LogError("Play mode에서 Build Laboratory로 만든 실험실이 필요합니다.", this);
                return false;
            }
            return true;
        }

        private void ClearOwnedMonstersInternal()
        {
            ownedMonsters.Clear();
            for (int i = monsterRoot.childCount - 1; i >= 0; i--)
            {
                var child = monsterRoot.GetChild(i);
                if (child.GetComponent<MovementLabOwnedMonster>() == null) continue;
                var movement = child.GetComponent<MonsterMovement>();
                if (movement != null) movement.enabled = false;
                Destroy(child.gameObject);
            }
        }

        private static void SetColor(Renderer renderer, Color color)
        {
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }

        private bool IsPrefabAsset()
        {
#if UNITY_EDITOR
            return PrefabUtility.IsPartOfPrefabAsset(gameObject);
#else
            return false;
#endif
        }
    }
}
