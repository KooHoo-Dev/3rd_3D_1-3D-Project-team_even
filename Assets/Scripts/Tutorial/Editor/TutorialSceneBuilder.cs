using System.Collections.Generic;
using System.IO;
using HybridShooter.Atmosphere;
using HybridShooter.Audio;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Reward;
using HybridShooter.Supply;
using HybridShooter.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HybridShooter.Tutorial.Editor
{
    /// <summary>
    /// 튜토리얼 씬(TutorialMap.unity) 진행 오브젝트 조립. 메뉴: Tools/Tutorial/Setup Tutorial Scene. 기획: Docs/Design/Tutorial_Proposal_v3.md
    ///  1) 맵 마커(Markers/Tutorial/Tut_*, TutorialMapBuilder 가 만든다)를 찾아 트리거·피해 차단막·진행 차단막을 붙인다
    ///  2) 플레이어 프리팹을 PlayerStart 에 놓고 미리보기 카메라를 끈다
    ///  3) 게임 씬의 시스템(SoundService·GameSoundRouter·MonsterPool·SupplyDropper·KillRewardCollector)을 설정값째 복사해 오고,
    ///     강화 체험용 UpgradeManager·UpgradeScreen 을 붙인다
    ///  4) TutorialScript 에셋을 만들거나 불러와 대사를 대사본 TSV(Docs/Design/Tutorial_VoiceScript.tsv)로 다시 채운다 (목표 문구는 없는 것만 기본값)
    ///  5) TutorialDirector 의 참조를 모두 잇고, Build Settings 에 씬을 등록한다
    /// 다시 실행하면 TutorialSystems 루트만 새로 만든다 (맵·대사 에셋은 그대로)
    /// </summary>
    public static class TutorialSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/GU/TutorialMap.unity";
        private const string GameScenePath = "Assets/Scenes/TestScenes/Monster Player Integration.unity";
        private const string PlayerPrefabPath = "Assets/Scenes/MS/Prefabs/Player.prefab";
        private const string DataFolder = "Assets/Data/Tutorial";
        private const string ScriptAssetPath = DataFolder + "/TutorialScript.asset";
        private const string VoiceScriptTsv = "Docs/Design/Tutorial_VoiceScript.tsv";
        private const string RootName = "TutorialSystems";
        private const string MarkersPath = "TutorialMap/Markers";

        // ── 차단막 겉모양 (QFX Sci-Fi VFX 원본은 건드리지 않고 DataFolder 에 복사해서 쓴다) ──
        private const string QfxFolder = "Assets/ExternalAssets/QFX/Sci-Fi VFX";
        /// <summary>진행 차단막: 파란 에너지 막 (QFX/SFX/EnergyWall)</summary>
        private const string QfxFieldMaterial = QfxFolder + "/Resources/Materials/Energy Wall/GO_EnergyWallBlue.mat";
        /// <summary>피해 차단막 배경: 붉은 레이저 연기 막 (QFX/SFX/LaserWall)</summary>
        private const string QfxLaserHazeMaterial = QfxFolder + "/Resources/Materials/Energy Wall/GO_EnergyBoxLaser.mat";
        /// <summary>피해 차단막 피격 불꽃</summary>
        private const string QfxLaserImpact = QfxFolder + "/Prefabs/Area Protector/PS_AreaProtector_LaserImpact.prefab";
        /// <summary>레이저 선 색 (HDR — 블룸에서 빛난다)</summary>
        private static readonly Color LaserBeamColor = new Color(4f, 0.25f, 0.15f, 1f);
        private static readonly Color PylonColor = new Color(0.1f, 0.11f, 0.13f, 1f);
        /// <summary>레이저 선 높이(m). 서서 지나가면 반드시 닿고, 구르기로만 지나갈 수 있다는 인상</summary>
        private static readonly float[] LaserHeights = { 0.25f, 0.55f, 0.85f, 1.15f, 1.45f, 1.75f };
        private const float LaserThickness = 0.035f;
        private const float BarrierPylonHeight = 2.1f;
        private const float GateHeight = 6f;
        private const float PylonWidth = 0.35f;

        /// <summary>차단막 겉모양 재료 묶음</summary>
        private sealed class CourseLook
        {
            public Material Field, LaserHaze, LaserBeam, Pylon;
            public GameObject LaserImpact;
        }

        /// <summary>목표 문구 안 키 이름 (메인 메뉴 조작 힌트처럼 붉게)</summary>
        private static string Key(string name) => $"<color=#D94238>{name}</color>";

        /// <summary>
        /// 목표 패널 기본 문구. 에셋에 없는 Id 는 넣고, 이전 기본값([Shift] 대괄호 표기) 그대로인 문구는 새 기본값으로 바꾼다.
        /// 인스펙터에서 직접 고친 문구는 그대로 둔다
        /// </summary>
        private static readonly (string Id, string Text)[] DefaultObjectives =
        {
            ("obj_move", $"{Key("W A S D")}  표시된 지점까지 이동"),
            ("obj_sprint", $"{Key("SHIFT")}  질주   {{0}} / {{1}}초"),
            ("obj_roll", $"{Key("CTRL")}  구르기로 차단막 통과   {{0}} / {{1}}"),
            ("obj_aim", $"{Key("우클릭")} 조준 · {Key("좌클릭")} 사격   처치 {{0}} / {{1}}"),
            ("obj_switch", $"{Key("2")} / {Key("휠")}  주무기로 교체"),
            ("obj_switch_kill", $"{Key("좌클릭")}  주무기로 표적 처치   {{0}} / {{1}}"),
            ("obj_grenade", $"{Key("G")}  수류탄으로 무리 처치   {{0}} / {{1}}"),
            ("obj_next", "다음 구역으로 이동"),
            ("obj_pickup", $"{Key("F")}  보급 상자 회수   {{0}} / {{1}}"),
            ("obj_heal", $"{Key("V")}  회복 팩 사용"),
            ("obj_weapon_pickup", $"{Key("F")}  무기 보급 상자(보라) 회수"),
            ("obj_weapon", $"{Key("4")}  보급 무기로 무리 처치   {{0}} / {{1}}"),
            ("obj_upgrade", "장비 개조 구역으로 이동"),
            ("obj_exit", "전초기지 게이트로 이동"),
            // 목적지 화면 표식 이름 (TutorialWaypointHud)
            ("wp_move", "목표 지점"),
            ("wp_next", "다음 과목"),
            ("wp_upgrade", "장비 개조 구역"),
            ("wp_exit", "전초기지 게이트"),
        };

        [MenuItem("Tools/Tutorial/Setup Tutorial Scene")]
        private static void Setup()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[TutorialSceneBuilder] 플레이 모드에서는 실행할 수 없습니다.");
                return;
            }
            if (File.Exists(ScenePath) == false)
            {
                Debug.LogError($"[TutorialSceneBuilder] {ScenePath} 가 없습니다. Tools/Map/Build Tutorial Map 을 먼저 실행하세요.");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform markers = FindPath(scene, MarkersPath);
            Transform tut = markers != null ? markers.Find("Tutorial") : null;
            if (tut == null)
            {
                Debug.LogError("[TutorialSceneBuilder] Markers/Tutorial 마커가 없습니다. Tools/Map/Build Tutorial Map 을 다시 실행해 마커를 만드세요.");
                return;
            }

            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == RootName) Object.DestroyImmediate(go);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);

            Directory.CreateDirectory(DataFolder);
            var look = LoadCourseLook();

            PlacePlayer(scene, markers.Find("PlayerStart"));
            CopyGameSystems(root.transform);
            Group(root.transform, "Hud").AddComponent<SupplyInfoCardHud>();
            // ⑤ 강화 체험: 본편과 같은 강화 상점·화면 (겉모양은 UpgradeUiSkin, 화면은 Manager 를 씬에서 찾는다)
            var upgrade = Group(root.transform, "Upgrade");
            upgrade.AddComponent<UpgradeManager>();
            upgrade.AddComponent<UpgradeScreen>();

            // 진행 오브젝트
            Transform course = Group(root.transform, "Course").transform;
            var moveTarget = CreateZone(course, tut, "Tut_MoveTarget", new Vector3(6f, 4f, 6f));
            var barriers = new List<TutorialDamageBarrier>();
            foreach (string name in new[] { "Tut_RollBarrier_1", "Tut_RollBarrier_2" })
            {
                var barrier = CreateBarrier(course, tut, name, look);
                if (barrier != null) barriers.Add(barrier);
            }
            var aimPoints = new List<Transform>();
            for (int i = 1; tut.Find($"Tut_Aim_{i}") != null; i++) aimPoints.Add(tut.Find($"Tut_Aim_{i}"));

            var director = Group(root.transform, "TutorialDirector");
            director.AddComponent<TutorialHud>();
            director.AddComponent<TutorialWaypointHud>();
            var so = new SerializedObject(director.AddComponent<TutorialDirector>());
            so.FindProperty("script").objectReferenceValue = GetOrCreateScript();
            so.FindProperty("moveTarget").objectReferenceValue = moveTarget;
            SetArray(so.FindProperty("rollBarriers"), barriers);
            so.FindProperty("movementGate").objectReferenceValue = CreateGate(course, tut, "Tut_Gate_Movement", look);
            SetArray(so.FindProperty("aimSpawnPoints"), aimPoints);
            so.FindProperty("aimGate").objectReferenceValue = CreateGate(course, tut, "Tut_Gate_Aim", look);
            so.FindProperty("grenadeGroupCenter").objectReferenceValue = tut.Find("Tut_GrenadeGroup");
            so.FindProperty("grenadeGate").objectReferenceValue = CreateGate(course, tut, "Tut_Gate_Grenade", look);
            so.FindProperty("supplyEntrance").objectReferenceValue = CreateZone(course, tut, "Tut_Zone_SupplyEntrance", new Vector3(0f, 6f, 3f));
            so.FindProperty("healDropPoint").objectReferenceValue = tut.Find("Tut_Drop_Heal");
            so.FindProperty("grenadeDropPoint").objectReferenceValue = tut.Find("Tut_Drop_Grenade");
            so.FindProperty("weaponDropPoint").objectReferenceValue = tut.Find("Tut_Drop_Weapon");
            so.FindProperty("weaponGroupCenter").objectReferenceValue = tut.Find("Tut_WeaponGroup");
            so.FindProperty("supplyGate").objectReferenceValue = CreateGate(course, tut, "Tut_Gate_Supply", look);
            so.FindProperty("upgradeZone").objectReferenceValue = CreateZone(course, tut, "Tut_Zone_Upgrade", new Vector3(0f, 6f, 3f));
            so.FindProperty("upgradeGate").objectReferenceValue = CreateGate(course, tut, "Tut_Gate_Upgrade", look);
            so.FindProperty("exitZone").objectReferenceValue = CreateZone(course, tut, "Tut_Zone_Exit", new Vector3(0f, 8f, 6f));
            so.ApplyModifiedPropertiesWithoutUndo();

            RegisterInBuildSettings();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = director;
            Debug.Log("[TutorialSceneBuilder] 튜토리얼 씬 조립 완료. 대사·목표·수치는 " + ScriptAssetPath + " 에서 고친다.");
        }

        #region 플레이어 · 시스템
        private static void PlacePlayer(Scene scene, Transform start)
        {
            if (Object.FindAnyObjectByType<Player.PlayerController>() == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                if (prefab == null)
                {
                    Debug.LogError($"[TutorialSceneBuilder] 플레이어 프리팹이 없습니다: {PlayerPrefabPath}");
                    return;
                }
                var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                if (start != null) player.transform.SetPositionAndRotation(start.position + Vector3.up * 0.1f, start.rotation);
            }

            // 맵 빌더의 미리보기 카메라는 끈다 (플레이어 카메라가 화면을 그린다)
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.TryGetComponent(out Camera _) && go.GetComponentInChildren<Player.PlayerController>() == null) go.SetActive(false);
        }

        /// <summary>
        /// 게임 씬에 놓인 시스템 컴포넌트를 설정값째 복사한다 (튜토리얼 씬에 이미 있으면 건너뛴다. 프리팹 인스턴스 안의 것도 찾는다).
        /// SupplyBeaconOverride: 게임 씬은 AtmosphereRig 프리팹 안에 있어 보급 빛기둥이 안개를 무시하는 높은 HDR 기둥이 된다.
        /// 튜토리얼은 조명·안개가 따로라 리그 전체 대신 이것만 가져와 보급 빛기둥을 본편과 같게 한다
        /// </summary>
        private static void CopyGameSystems(Transform root)
        {
            var types = new[] { typeof(SoundService), typeof(GameSoundRouter), typeof(MonsterPool), typeof(SupplyDropper), typeof(KillRewardCollector), typeof(SupplyBeaconOverride) };
            Scene game = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Additive);
            Transform systems = Group(root, "Systems").transform;
            foreach (var type in types)
            {
                if (HasComponentOutside(type, game)) continue;
                var target = Group(systems, type.Name);
                var source = FindInScene(game, type);
                if (source != null && ComponentUtility.CopyComponent(source) && ComponentUtility.PasteComponentAsNew(target)) continue;
                target.AddComponent(type);
                Debug.LogWarning($"[TutorialSceneBuilder] 게임 씬에서 {type.Name} 을 찾지 못해 기본값으로 붙였습니다.");
            }
            EditorSceneManager.CloseScene(game, true);
        }

        private static bool HasComponentOutside(System.Type type, Scene excluded)
        {
            foreach (var found in Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (((Component)found).gameObject.scene != excluded) return true;
            return false;
        }

        private static Component FindInScene(Scene scene, System.Type type)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                var found = go.GetComponentInChildren(type, true);
                if (found != null) return found;
            }
            return null;
        }
        #endregion

        #region 진행 오브젝트
        /// <summary>마커 위치의 트리거 (목적지는 화면 표식 TutorialWaypointHud 가 보여 준다). size.x 가 0 이면 마커의 협곡 폭(localScale.x)을 쓴다</summary>
        private static TutorialZone CreateZone(Transform parent, Transform markers, string name, Vector3 size)
        {
            Transform marker = FindMarker(markers, name);
            if (marker == null) return null;
            var go = Place(parent, marker, name);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x > 0f ? size.x : marker.localScale.x, size.y, size.z);
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            return go.AddComponent<TutorialZone>();
        }

        /// <summary>
        /// 피해 차단막 = 붉은 레이저 그리드. 양옆 발사 기둥(남는다) + Visual(통과하면 꺼짐: 가로 레이저 선 여러 줄 + 옅은 붉은 레이저 막)
        /// </summary>
        private static TutorialDamageBarrier CreateBarrier(Transform parent, Transform markers, string name, CourseLook look)
        {
            Transform marker = FindMarker(markers, name);
            if (marker == null) return null;
            var go = Place(parent, marker, name);
            float width = marker.localScale.x;
            float span = width - PylonWidth * 2f; // 기둥 안쪽 사이
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(width, 3f, 0.6f);
            box.center = new Vector3(0f, 1.5f, 0f);

            Pylons(go.transform, width, BarrierPylonHeight, look.Pylon);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(go.transform, false);
            foreach (float y in LaserHeights)
                Panel(visual, "Beam", new Vector3(span, LaserThickness, LaserThickness), y, look.LaserBeam);
            float hazeHeight = LaserHeights[LaserHeights.Length - 1] + 0.15f;
            Panel(visual, "Haze", new Vector3(span, hazeHeight, 0.02f), hazeHeight * 0.5f, look.LaserHaze);

            var barrier = go.AddComponent<TutorialDamageBarrier>();
            var so = new SerializedObject(barrier);
            so.FindProperty("visual").objectReferenceValue = visual.gameObject;
            so.FindProperty("hitEffect").objectReferenceValue = look.LaserImpact;
            so.ApplyModifiedPropertiesWithoutUndo();
            return barrier;
        }

        /// <summary>진행 차단막 = 파란 자기장 막 + 양옆 발생 기둥(남는다). 열리면 막(Visual)이 접히며 사라진다</summary>
        private static TutorialGate CreateGate(Transform parent, Transform markers, string name, CourseLook look)
        {
            Transform marker = FindMarker(markers, name);
            if (marker == null) return null;
            var go = Place(parent, marker, name);
            float width = marker.localScale.x;
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(width, GateHeight, 0.5f);
            box.center = new Vector3(0f, GateHeight * 0.5f, 0f);
            Pylons(go.transform, width, GateHeight + 0.3f, look.Pylon);
            var visual = Panel(go.transform, "Visual", new Vector3(width - PylonWidth * 2f, GateHeight, 0.04f), GateHeight * 0.5f, look.Field);
            var gate = go.AddComponent<TutorialGate>();
            var so = new SerializedObject(gate);
            so.FindProperty("blocker").objectReferenceValue = box;
            so.FindProperty("visual").objectReferenceValue = visual;
            so.ApplyModifiedPropertiesWithoutUndo();
            return gate;
        }


        /// <summary>차단막 양 끝의 발사 기둥 (콜라이더 없음. 협곡 벽 쪽이라 길을 막지 않는다)</summary>
        private static void Pylons(Transform parent, float width, float height, Material material)
        {
            foreach (int side in new[] { -1, 1 })
            {
                var pylon = Panel(parent, side < 0 ? "Pylon_L" : "Pylon_R", new Vector3(PylonWidth, height, PylonWidth), height * 0.5f, material);
                pylon.localPosition = new Vector3(side * (width * 0.5f - PylonWidth * 0.5f), height * 0.5f, 0f);
                pylon.GetComponentInChildren<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>콜라이더 없는 판(큐브). 빈 부모(피벗)를 centerY 높이에 두고 그 가운데에 큐브를 놓는다 (열림 연출이 피벗 크기를 줄인다)</summary>
        private static Transform Panel(Transform parent, string name, Vector3 size, float centerY, Material material)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.localPosition = new Vector3(0f, centerY, 0f);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.transform.SetParent(pivot, false);
            cube.transform.localScale = size;
            var renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return pivot;
        }

        private static GameObject Place(Transform parent, Transform marker, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(marker.position, marker.rotation);
            return go;
        }

        private static Transform FindMarker(Transform markers, string name)
        {
            Transform marker = markers.Find(name);
            if (marker == null) Debug.LogWarning($"[TutorialSceneBuilder] 마커 {name} 가 없습니다. Tools/Map/Build Tutorial Map 을 다시 실행하세요.");
            return marker;
        }
        #endregion

        #region 에셋 · 설정
        private static TutorialScript GetOrCreateScript()
        {
            var script = AssetDatabase.LoadAssetAtPath<TutorialScript>(ScriptAssetPath);
            if (script == null)
            {
                script = ScriptableObject.CreateInstance<TutorialScript>();
                AssetDatabase.CreateAsset(script, ScriptAssetPath);
            }
            ImportLines(script); // 대사 원본은 대사본 TSV 하나 (팀 공유 문서와 게임 자막을 항상 같게)
            // 목표 문구는 인스펙터에서 고친 것을 살리고, 없는 Id 는 기본값으로 채운다.
            // 이전 기본값(키를 [Shift] 처럼 대괄호로 쓴 문구)은 새 표기(붉은 키 이름)로 바꾼다
            foreach (var (id, text) in DefaultObjectives)
            {
                var objective = script.Objectives.Find(o => o.Id == id);
                if (objective == null) script.Objectives.Add(new TutorialScript.Objective { Id = id, Text = text });
                else if (objective.Text != null && objective.Text.Contains("[")) objective.Text = text;
            }
            EditorUtility.SetDirty(script);
            AssetDatabase.SaveAssets();
            return script;
        }

        /// <summary>대사본 TSV (대사 Id · 단계 · 나오는 때 · 자막 · TTS 입력) 에서 Id 와 자막만 가져온다</summary>
        [MenuItem("Tools/Tutorial/Reimport Lines From Voice Script")]
        private static void ReimportLines()
        {
            var script = AssetDatabase.LoadAssetAtPath<TutorialScript>(ScriptAssetPath);
            if (script == null)
            {
                Debug.LogError($"[TutorialSceneBuilder] {ScriptAssetPath} 가 없습니다. Setup Tutorial Scene 을 먼저 실행하세요.");
                return;
            }
            ImportLines(script);
            EditorUtility.SetDirty(script);
            AssetDatabase.SaveAssets();
        }

        private static void ImportLines(TutorialScript script)
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, VoiceScriptTsv);
            if (File.Exists(path) == false)
            {
                Debug.LogWarning($"[TutorialSceneBuilder] 대사본 {VoiceScriptTsv} 가 없어 대사를 채우지 못했습니다.");
                return;
            }
            script.Lines.Clear();
            foreach (string row in File.ReadAllLines(path))
            {
                string[] cols = row.Split('\t');
                if (cols.Length < 4 || string.IsNullOrWhiteSpace(cols[0])) continue;
                script.Lines.Add(new TutorialScript.Line { Id = cols[0].Trim(), Subtitle = cols[3].Trim() });
            }
            Debug.Log($"[TutorialSceneBuilder] 대사 {script.Lines.Count}줄을 가져왔습니다 ({VoiceScriptTsv}).");
        }

        /// <summary>차단막 겉모양 재료. QFX 머티리얼은 DataFolder 로 복사해 쓴다 (원본 그대로 두기). QFX 가 없으면 단색으로 대신한다</summary>
        private static CourseLook LoadCourseLook()
        {
            // 이전 버전에서 만들고 더 쓰지 않는 에셋 (단색 차단막·목표 원판·목적지 고리·빛기둥 — 목적지는 화면 표식만 쓴다)
            foreach (string old in new[] { "M_TutorialBarrier.mat", "M_TutorialGate.mat", "M_TutorialTarget.mat",
                                           "M_TutorialBeaconBeam.mat", "T_TutorialBeaconBeam.png", "M_TutorialBeaconRing.mat", "T_TutorialBeaconRing.png" })
                AssetDatabase.DeleteAsset($"{DataFolder}/{old}");

            return new CourseLook
            {
                Field = CopyMaterial(QfxFieldMaterial, "M_TutorialField", new Color(0.3f, 0.85f, 1f, 0.3f)),
                LaserHaze = CopyMaterial(QfxLaserHazeMaterial, "M_TutorialLaserHaze", new Color(1f, 0.2f, 0.15f, 0.15f)),
                LaserBeam = GetOrCreateOpaqueMaterial("M_TutorialLaserBeam", LaserBeamColor, lit: false),
                Pylon = GetOrCreateOpaqueMaterial("M_TutorialPylon", PylonColor, lit: true),
                LaserImpact = AssetDatabase.LoadAssetAtPath<GameObject>(QfxLaserImpact),
            };
        }

        private static Material CopyMaterial(string sourcePath, string name, Color fallback)
        {
            string path = $"{DataFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            if (AssetDatabase.LoadAssetAtPath<Material>(sourcePath) != null && AssetDatabase.CopyAsset(sourcePath, path))
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            Debug.LogWarning($"[TutorialSceneBuilder] {sourcePath} 가 없어 {name} 을 단색으로 만듭니다.");
            return GetOrCreateMaterial(name, fallback);
        }

        /// <summary>불투명 머티리얼. lit = false 면 URP Unlit (HDR 색이 블룸에서 빛난다), true 면 URP Lit 금속</summary>
        private static Material GetOrCreateOpaqueMaterial(string name, Color color, bool lit)
        {
            string path = $"{DataFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(lit ? "Universal Render Pipeline/Lit" : "Universal Render Pipeline/Unlit");
            material = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            material.SetColor("_BaseColor", color);
            material.color = color;
            if (lit)
            {
                material.SetFloat("_Metallic", 0.7f);
                material.SetFloat("_Smoothness", 0.45f);
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>URP Unlit 반투명 머티리얼</summary>
        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{DataFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(shader != null ? shader : Shader.Find("Unlit/Color"));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", color);
            material.color = color;
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void RegisterInBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == ScenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[TutorialSceneBuilder] Build Settings 에 {ScenePath} 를 추가했습니다.");
        }
        #endregion

        #region 공통
        private static GameObject Group(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        private static Transform FindPath(Scene scene, string path)
        {
            string[] parts = path.Split('/');
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == parts[0]) return parts.Length == 1 ? go.transform : go.transform.Find(string.Join("/", parts, 1, parts.Length - 1));
            return null;
        }

        private static void SetArray<T>(SerializedProperty property, List<T> items) where T : Object
        {
            property.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }
        #endregion
    }
}
