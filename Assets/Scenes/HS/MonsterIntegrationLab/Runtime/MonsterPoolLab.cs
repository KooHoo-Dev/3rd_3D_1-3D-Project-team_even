using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Services;
using HybridShooter.Monster.Behaviour;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace Scenes.HS.MonsterIntegrationLab.Runtime
{
    /// <summary>
    /// 테스트 전용. 통합 씬에서 MonsterPool 로 몬스터를 플레이어 주위 원에 소환한다.
    /// F5~F12 = monsterIds 0~7번 소환. 오른쪽 위 조작 창에서도 소환·웨이브 소환·전체 제거·스테이지 풀 보충을 한다.
    /// </summary>
    /// <remarks>
    /// 소환·반납·회수는 전부 MonsterPool 이 한다. 이 클래스는 입력과 조작 창만 맡는다.
    /// 대상(플레이어)은 몬스터가 스스로 찾는다 (ServiceLocator 의 IPlayerTarget). 군중 묶음을 배정하지 않으므로 몬스터는 직접 추적한다.
    /// </remarks>
    public sealed class MonsterPoolLab : MonoBehaviour
    {
        [Header("디버그")]
        [Tooltip("켜면 조작 창(Game/Scene 뷰)과 F5~F12 소환 키를 쓴다. 끄면 전부 숨긴다")]
        [SerializeField] private bool isDebug = true;

        [Header("풀")]
        [Tooltip("소환에 쓸 씬의 MonsterPool")]
        [SerializeField] private MonsterPool monsterPool;

        [Header("소환 대상")]
        [Tooltip("소환할 몬스터의 MonsterTable Id. 0~7번이 F5~F12 에 대응")]
        [SerializeField] private int[] monsterIds = { 1002, 1003, 2001, 2002, 2003 };

        [Header("소환 위치")]
        [Tooltip("한 번 소환할 때 만드는 몬스터 수. 조작 창에서 Play 중 바꾼다")]
        [SerializeField, Min(1)] private int spawnCount = 8;
        [Tooltip("소환 원의 반지름 (m). 원의 중심은 소환하는 순간의 플레이어 위치")]
        [SerializeField, Min(0f)] private float spawnRadius = 12f;
        [Tooltip("소환점에서 이 거리 (m) 안의 가장 가까운 NavMesh 위로 옮긴다. 못 찾으면 원래 점에 소환")]
        [SerializeField, Min(0.1f)] private float spawnSampleDistance = 3f;

        [Header("웨이브 소환")]
        [Tooltip("조작 창 '웨이브 소환' 버튼이 실행할 WaveTable Id. 조작 창에서 Play 중 바꾼다 (예: 109 = 표의 9번째 웨이브)")]
        [SerializeField] private int waveId = 109;

        private static readonly Key[] SpawnKeys = { Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12 };

        // ── 실행 중 웨이브 ──
        private readonly List<MonsterWave> runningWaves = new List<MonsterWave>();

        // ── 조작 창 ──
        private const float WindowWidth = 320f;
        private Rect windowRect = new Rect(0f, 12f, WindowWidth, 0f);

        /// <summary>에디터에서는 Scene 뷰에도 조작 창을 그리도록 구독한다</summary>
        private void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui += OnSceneGUI;
#endif
        }

        /// <summary>Scene 뷰 구독을 해제한다</summary>
        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.SceneView.duringSceneGui -= OnSceneGUI;
#endif
        }

        /// <summary>실행 중 웨이브의 시간을 진행하고, 눌린 F 키의 몬스터를 소환한다</summary>
        private void Update()
        {
            TickRunningWaves();
            SpawnPressedKeys();
        }

        /// <summary>F5~F12 중 이번 프레임 눌린 키의 번호로 소환한다. 배열에 없는 번호의 키는 무시한다</summary>
        private void SpawnPressedKeys()
        {
            if (isDebug == false) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            for (int i = 0; i < SpawnKeys.Length; i++)
            {
                bool isPressed = keyboard[SpawnKeys[i]].wasPressedThisFrame;
                bool hasMonsterId = i < monsterIds.Length;
                if (isPressed && hasMonsterId) SpawnAround(i);
            }
        }

        /// <summary>
        /// monsterIds[index] 몬스터를 spawnCount 마리, 플레이어 주위 원에 균등 간격으로 풀에서 소환한다.
        /// F 키와 조작 창 버튼이 호출한다.
        /// </summary>
        /// <param name="index">monsterIds 배열 번호 (0부터)</param>
        public void SpawnAround(int index)
        {
            MonsterDef def = DataManager.Monsters.Get(monsterIds[index]);
            for (int i = 0; i < spawnCount; i++)
            {
                float angle = i * Mathf.PI * 2f / spawnCount;
                Vector3 offsetFromPlayer = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnRadius;
                monsterPool.Spawn(def, GetSpawnPoint(offsetFromPlayer), Quaternion.identity);
            }
        }

        /// <summary>
        /// WaveTable 의 웨이브 한 줄을 새로 시작한다. 스테이지·라운드 없이 그 웨이브의 스포너만 StartDelay 대로 소환한다.
        /// MonsterWave 단위로 돌리므로 같은 StartDelay 의 Nest 줄들은 둥지 하나를 같이 쓴다. 조작 창 버튼이 호출한다.
        /// </summary>
        /// <param name="id">WaveTable Id</param>
        public void RunWave(int id)
        {
            var wave = new MonsterWave(DataManager.Waves.Get(id), monsterPool, ServiceLocator.Get<ISpawnZoneProvider>());
            wave.Finished += OnWaveFinished;
            runningWaves.Add(wave);
            Debug.Log($"[MonsterPoolLab] 웨이브 {id} 시작 (실행 중 {runningWaves.Count}개)", this);
        }

        /// <summary>
        /// 실행 중 웨이브에 Time.deltaTime 을 넘긴다. 끝난 웨이브는 Finished 신호로 OnWaveFinished 가 뺀다.
        /// 뒤에서부터 돌아서, 도중에 목록에서 빠져도 아직 안 돈 앞쪽 인덱스는 그대로다.
        /// </summary>
        private void TickRunningWaves()
        {
            for (int i = runningWaves.Count - 1; i >= 0; i--) runningWaves[i].Tick(Time.deltaTime);
        }

        /// <summary>웨이브가 스스로 끝나면 목록에서 뺀다. 웨이브 Tick 안에서 불린다</summary>
        /// <param name="wave">방금 끝난 웨이브</param>
        private void OnWaveFinished(MonsterWave wave)
        {
            runningWaves.Remove(wave);
            Debug.Log($"[MonsterPoolLab] 웨이브 끝 (실행 중 {runningWaves.Count}개)", this);
        }

        /// <summary>
        /// 실행 중 웨이브의 남은 소환을 취소하고, 살아 있는 몬스터를 보상 없이 전부 죽인다.
        /// 사망 연출(despawnDelay) 뒤 각자 풀로 반납된다.
        /// </summary>
        [ContextMenu("전체 제거")]
        public void KillAll()
        {
            foreach (MonsterWave wave in runningWaves) wave.Stop();
            runningWaves.Clear();
            monsterPool.WipeAll();
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

        // ════════ IMGUI ════════

        /// <summary>Game 뷰 오른쪽 위에 조작 창을 그린다. 옛 MovementLabController 창(왼쪽 위)과 겹치지 않게 한다</summary>
        private void OnGUI()
        {
            if (isDebug == false) return;
            DrawControlWindow(Screen.width);
        }

        /// <summary>
        /// 조작 창을 뷰 오른쪽 위에 그린다. Game 뷰(OnGUI)와 Scene 뷰(OnSceneGUI)가 같이 쓴다.
        /// </summary>
        /// <param name="viewWidth">창을 그릴 뷰의 너비 (px). 창을 오른쪽 끝에 붙이는 데 쓴다</param>
        private void DrawControlWindow(float viewWidth)
        {
            windowRect.x = viewWidth - WindowWidth - 12f;
            windowRect.height = 0f; // 내용 길이에 맞게 늘고 줄게 매번 리셋
            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "Monster Pool Lab");
        }

#if UNITY_EDITOR
        /// <summary>Scene 뷰 위에 조작 창을 띄운다. Handles.BeginGUI 구간에서 DrawControlWindow 를 그린다</summary>
        private void OnSceneGUI(UnityEditor.SceneView sceneView)
        {
            if (isDebug == false) return;
            UnityEditor.Handles.BeginGUI();
            DrawControlWindow(sceneView.position.width);
            UnityEditor.Handles.EndGUI();
            sceneView.Repaint(); // 활성/풀 대기 수가 실시간으로 갱신되도록 계속 다시 그린다
        }
#endif

        /// <summary>조작 창 내용: 소환 수, 종류별 소환 버튼(F 키 표시), 웨이브 소환, 전체 제거, 스테이지 풀 보충, 활성/대기 수</summary>
        private void DrawWindow(int id)
        {
            DrawSpawnCountField();

            for (int i = 0; i < monsterIds.Length; i++)
            {
                string keyLabel = i < SpawnKeys.Length ? SpawnKeys[i].ToString() : "-";
                if (GUILayout.Button($"{keyLabel}  {DataManager.Monsters.Get(monsterIds[i]).Name}")) SpawnAround(i);
            }

            GUILayout.Space(4f);
            DrawWaveRow();

            GUILayout.Space(4f);
            if (GUILayout.Button("전체 제거 (웨이브 중단 + 보상 없이 사망)")) KillAll();

            GUILayout.Space(4f);
            GUILayout.Label("풀 보충 = 비활성 개체만 미리 생성 (소환 아님)");
            foreach (WaveStageDef stage in DataManager.WaveStages.All)
            {
                if (GUILayout.Button($"스테이지 {stage.Id} 풀 보충 (라운드 {stage.RoundNo})")) monsterPool.WarmupStage(stage);
            }

            GUILayout.Label($"활성 {CountActiveMonsters()} | 풀 대기 {CountIdleMonsters()}");
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

        /// <summary>
        /// 웨이브 소환 줄: WaveTable Id 입력 칸 + 소환 버튼 + 실행 중 웨이브 수.
        /// 입력 칸은 숫자가 아니면 무시한다(타이핑 중 빈 칸 허용). 없는 Id 로 누르면 DataManager 가 예외를 낸다.
        /// </summary>
        private void DrawWaveRow()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("웨이브 Id", GUILayout.Width(60f));
            string typed = GUILayout.TextField(waveId.ToString(), GUILayout.Width(50f));
            if (int.TryParse(typed, out int typedValue)) waveId = typedValue;
            if (GUILayout.Button($"웨이브 {waveId} 소환")) RunWave(waveId);
            GUILayout.EndHorizontal();

            GUILayout.Label($"실행 중 웨이브 {runningWaves.Count}");
        }

        /// <summary>씬에서 켜져 있는 몬스터 수를 센다. 조작 창 표시용</summary>
        /// <returns>켜져 있는 MonsterController 수</returns>
        private static int CountActiveMonsters() =>
            FindObjectsByType<MonsterController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;

        /// <summary>풀의 종류 루트들 아래 쉬는 개체 수를 더한다. 조작 창 표시용</summary>
        /// <returns>쉬고 있는 몬스터 총 수</returns>
        private int CountIdleMonsters()
        {
            int total = 0;
            foreach (Transform typeRoot in monsterPool.transform) total += typeRoot.childCount;
            return total;
        }
    }
}
