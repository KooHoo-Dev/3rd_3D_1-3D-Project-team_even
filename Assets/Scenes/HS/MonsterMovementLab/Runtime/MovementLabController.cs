using HybridShooter.Monster.Movement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>
    /// Play 중 실험 조작 UI(IMGUI) + 마우스 입력.
    /// 1: 이동 패턴 변경(클릭/드래그한 지점 반경의 몬스터 정책 교체), 2: 몬스터 죽이기(클릭), 3: CrowdCoordinator 값 조절, 0/Esc: 해제.
    /// </summary>
    public sealed class MovementLabController : MonoBehaviour
    {
        public enum LabMode { None, Policy, Kill, Crowd }

        [SerializeField] private MovementLabExample lab;
        [SerializeField] private Camera labCamera;
        [SerializeField] private LabMode mode = LabMode.None;

        [Header("이동 패턴 변경 브러시")]
        [SerializeField] private MovementPolicyKind brushPolicy = MovementPolicyKind.Flank;
        [SerializeField, Min(0.1f)] private float brushRadius = 2.5f;

        [Header("표시")]
        [SerializeField] private bool showMonsterLabels = true;
        // 링(슬롯) 위 텍스트는 기본 꺼짐. 씬에 저장된 값과 무관하게 Awake 에서 이 값을 Gizmos 에 적용한다
        [SerializeField] private bool showRingLabels = false;

        private static readonly string[] ModeNames = { "없음(0)", "정책(1)", "죽이기(2)", "군중(3)" };
        private static readonly string[] PolicyNames = System.Enum.GetNames(typeof(MovementPolicyKind));

        private Rect windowRect = new Rect(12f, 12f, 360f, 10f);
        private GUIStyle labelStyle;
        private Vector3 brushPoint;
        private bool brushVisible;
        // ── Scene 뷰 텍스트(링/몬스터)를 그리는 Gizmos. 토글 값을 여기에 전달한다 ──
        private MovementLabGizmos gizmos;

        private void Awake()
        {
            if (lab == null) lab = GetComponent<MovementLabExample>();
            if (labCamera == null) labCamera = Camera.main != null ? Camera.main : Camera.allCameras[0];
            gizmos = FindFirstObjectByType<MovementLabGizmos>();
            ApplyLabelToggles();
        }

        /// <summary>GUI 토글 값(몬스터/링 텍스트)을 Gizmos 의 Scene 뷰 텍스트 표시 여부에 반영한다</summary>
        private void ApplyLabelToggles()
        {
            if (gizmos == null) return;
            gizmos.ShowMonsterIds = showMonsterLabels;
            gizmos.ShowSlotIds = showRingLabels;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) mode = LabMode.Policy;
                else if (keyboard.digit2Key.wasPressedThisFrame) mode = LabMode.Kill;
                else if (keyboard.digit3Key.wasPressedThisFrame) mode = LabMode.Crowd;
                else if (keyboard.digit0Key.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame) mode = LabMode.None;
            }

            brushVisible = false;
            var mouse = Mouse.current;
            if (mouse == null || (mode != LabMode.Policy && mode != LabMode.Kill)) return;
            Vector2 screen = mouse.position.ReadValue();

            Ray ray = labCamera.ScreenPointToRay(screen);
            if (mode == LabMode.Policy)
            {
                if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter)) return;
                brushPoint = ray.GetPoint(enter);
                brushVisible = true;
                if (mouse.leftButton.isPressed) ApplyPolicyBrush(brushPoint);
            }
            else if (mouse.leftButton.wasPressedThisFrame)
                KillUnder(ray);
        }

        private void ApplyPolicyBrush(Vector3 point)
        {
            var monsters = lab.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (!monster.gameObject.activeInHierarchy || monster.Profile.PolicyKind == brushPolicy) continue;
                Vector3 d = monster.transform.position - point;
                if (d.x * d.x + d.z * d.z <= brushRadius * brushRadius) monster.SetPolicy(brushPolicy);
            }
        }

        /// <summary>광선에 가장 먼저 맞는 몬스터를 SetActive(false). 코어 MonsterMovement.OnDisable → Unregister 경로를 탄다</summary>
        private void KillUnder(Ray ray)
        {
            MovementLabOwnedMonster best = null;
            float bestDistance = float.MaxValue;
            var monsters = lab.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (!monster.gameObject.activeInHierarchy) continue;
                if (HitBounds(monster).IntersectRay(ray, out float distance) && distance < bestDistance)
                {
                    best = monster;
                    bestDistance = distance;
                }
            }
            if (best == null) return;
            Debug.Log($"몬스터 사망 처리(SetActive false): {best.name}", best);
            best.gameObject.SetActive(false);
        }

        /// <summary>클릭 판정 범위: 에이전트 캡슐 + 모델 렌더러 범위</summary>
        private static Bounds HitBounds(MovementLabOwnedMonster monster)
        {
            var agent = monster.GetComponent<NavMeshAgent>();
            var bounds = new Bounds(monster.transform.position + Vector3.up * agent.height * .5f,
                new Vector3(agent.radius * 2f, agent.height, agent.radius * 2f));
            foreach (var renderer in monster.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private void ReviveAll()
        {
            var monsters = lab.Monsters;
            for (int i = 0; i < monsters.Count; i++)
                if (!monsters[i].gameObject.activeSelf) monsters[i].gameObject.SetActive(true);
        }

        // ════════ IMGUI ════════

        /// <summary>Game 뷰 OnGUI 에는 몬스터 정책 라벨만 그린다. 조작 창은 Scene 뷰에 그린다(OnSceneGUI)</summary>
        private void OnGUI()
        {
            if (showMonsterLabels) DrawMonsterLabels();
        }

#if UNITY_EDITOR
        // Scene 뷰의 duringSceneGui 구독: 활성화되는 동안만 Scene 뷰에 조작 창을 그린다
        private void OnEnable() => UnityEditor.SceneView.duringSceneGui += OnSceneGUI;
        private void OnDisable() => UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;

        /// <summary>Scene 뷰 위에 조작 창(DrawWindow)을 띄운다. Handles.BeginGUI 구간에서 GUILayout.Window 를 그린다</summary>
        private void OnSceneGUI(UnityEditor.SceneView sceneView)
        {
            UnityEditor.Handles.BeginGUI();
            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "Monster Movement Lab");
            UnityEditor.Handles.EndGUI();
            sceneView.Repaint(); // 스냅샷 등 실시간 값이 갱신되도록 계속 다시 그린다
        }
#endif

        private void DrawWindow(int id)
        {
            // 스크롤뷰 제거: 창 높이가 내용 길이에 맞춰 자동으로 늘어난다
            mode = (LabMode)GUILayout.Toolbar((int)mode, ModeNames);
            GUILayout.Space(4f);

            if (mode == LabMode.Policy)
            {
                GUILayout.Label("정책을 고르고 Game 뷰를 클릭/드래그 (브러시 반경 안 몬스터가 바뀜)");
                brushPolicy = (MovementPolicyKind)GUILayout.SelectionGrid((int)brushPolicy, PolicyNames, PolicyNames.Length);
                brushRadius = Slider("브러시 반경", brushRadius, 0.5f, 10f);
            }
            else if (mode == LabMode.Kill)
            {
                GUILayout.Label("몬스터를 클릭하면 사망(SetActive false → Unregister)");
                if (GUILayout.Button("죽은 몬스터 전부 되살리기")) ReviveAll();
            }
            else if (mode == LabMode.Crowd)
                DrawCrowdSettings();

            GUILayout.Space(6f);
            GUILayout.Label("소환");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("슬롯 군중")) lab.SpawnSlotCrowd();
            if (GUILayout.Button("토큰 군중")) lab.SpawnTokenCrowd();
            if (GUILayout.Button("전체 제거")) lab.ClearOwnedMonsters();
            GUILayout.EndHorizontal();
            DrawSnapshot();

            // 텍스트 표시 토글: 몬스터 텍스트(Game 뷰 + Scene 뷰), 링 텍스트(Scene 뷰)
            GUILayout.BeginHorizontal();
            showMonsterLabels = GUILayout.Toggle(showMonsterLabels, "몬스터 텍스트");
            showRingLabels = GUILayout.Toggle(showRingLabels, "링 텍스트");
            GUILayout.EndHorizontal();
            ApplyLabelToggles();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        /// <summary>CrowdSettings 를 직접 수정한다. 링 설정은 다음 갱신(AllocationInterval)에 반영되며, 여기서는 즉시 RefreshSlots 로 당긴다</summary>
        private void DrawCrowdSettings()
        {
            var crowd = lab.Coordinator;
            var s = crowd.Settings;
            var slots = s.Slots;
            bool slotChanged = false;

            s.EnableCrowdControl = GUILayout.Toggle(s.EnableCrowdControl, "군중 제어 사용");
            bool useSlots = GUILayout.Toggle(s.UseSlots, "슬롯 모드 (끄면 토큰 모드)");
            if (useSlots != s.UseSlots) { s.UseSlots = useSlots; slotChanged = true; }

            int capacity = Mathf.RoundToInt(Slider("동시 접근 허용(EngageCapacity)", s.EngageCapacity, 0f, 24f));
            if (capacity != s.EngageCapacity) crowd.SetEngageCapacity(capacity);
            s.NearRadius = Slider("NearRadius", s.NearRadius, 0f, 20f);
            s.ReleaseRadius = Slider("ReleaseRadius", s.ReleaseRadius, 0f, 30f);
            s.AllocationInterval = Slider("AllocationInterval", s.AllocationInterval, 0.02f, 1f);
            s.ThinkCycle = Slider("ThinkCycle", s.ThinkCycle, 0.02f, 1f);

            GUILayout.Label("── 링(슬롯) ──");
            slotChanged |= Slider("접근 링 반지름(ApproachRadius)", ref slots.ApproachRadius, 0.5f, 15f);
            slotChanged |= Slider("접근 링 후보 수(ApproachCount)", ref slots.ApproachCount, 1, 16);
            slotChanged |= Slider("링 간격(MinimumRingGap)", ref slots.MinimumRingGap, 0.3f, 5f);
            slotChanged |= Slider("최대 링 수(MaxRingCount)", ref slots.MaxRingCount, 2, 10);
            slotChanged |= Slider("후보 최소 간격(MinimumSpacing)", ref slots.MinimumSpacing, 0.3f, 3f);
            slotChanged |= Slider("NavMesh 투영(SampleRadius)", ref slots.SampleRadius, 0.1f, 3f);
            slotChanged |= Slider("도착 판정(ArrivalDistance)", ref slots.ArrivalDistance, 0.1f, 2f);
            slotChanged |= Slider("진행 타임아웃(ProgressTimeout)", ref slots.ProgressTimeout, 0.5f, 15f);
            slotChanged |= Slider("거절 유지(RejectionSeconds)", ref slots.RejectionSeconds, 0.5f, 10f);
            if (!s.UseSlots) slotChanged |= Slider("토큰 모드 대기 반경(WaitingRadius)", ref slots.WaitingRadius, 1f, 20f);

            if (slotChanged && s.EnableCrowdControl && s.UseSlots) crowd.RefreshSlots();
        }

        private void DrawSnapshot()
        {
            var snapshot = lab.Coordinator.GetSnapshot();
            var sum = snapshot.Summary;
            GUILayout.Label($"등록 {snapshot.Registered} | 접근 {sum.Engaged}/{snapshot.Capacity} | 대기 {sum.Waiting} | 제한없음 {sum.Unrestricted} | 슬롯 {lab.Coordinator.SlotCount}");
        }

        private void DrawMonsterLabels()
        {
            if (Event.current.type != EventType.Repaint) return;
            labelStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 11, normal = { textColor = Color.white } };
            var monsters = lab.Monsters;
            for (int i = 0; i < monsters.Count; i++)
            {
                var monster = monsters[i];
                if (!monster.gameObject.activeInHierarchy) continue;
                Vector3 p = labCamera.WorldToScreenPoint(monster.transform.position + Vector3.up * 2.2f);
                if (p.z <= 0f) continue;
                GUI.Label(new Rect(p.x - 60f, Screen.height - p.y - 10f, 120f, 20f), monster.Profile.PolicyKind.ToString(), labelStyle);
            }
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.##}", GUILayout.Width(210f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

        private static bool Slider(string label, ref float field, float min, float max)
        {
            float next = Slider(label, field, min, max);
            bool changed = !Mathf.Approximately(next, field);
            field = next;
            return changed;
        }

        private static bool Slider(string label, ref int field, int min, int max)
        {
            int next = Mathf.RoundToInt(Slider(label, field, min, max));
            bool changed = next != field;
            field = next;
            return changed;
        }

        private void OnDrawGizmos()
        {
            if (!brushVisible) return;
            Gizmos.color = new Color(1f, .9f, .2f);
            const int segments = 32;
            Vector3 previous = brushPoint + Vector3.forward * brushRadius;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = brushPoint + new Vector3(Mathf.Sin(a) * brushRadius, 0f, Mathf.Cos(a) * brushRadius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
