using System;
using System.Collections.Generic;
using System.IO;
using HybridShooter.UI.Options.Editor;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace HybridShooter.UI.MainMenu.Editor
{
    /// <summary>
    /// 메인 메뉴 대치 장면(디오라마) 생성. 상단 메뉴 Tools/MainMenu/Rebuild Diorama.
    ///  1) *_Display 프리팹 (Assets/Prefabs/MainMenu/Display/): 게임 프리팹을 풀어서 렌더러·Animator 만 남기고
    ///     (AI·입력·물리·전투·사운드 이미터 제거) 에셋 팩 Animator 컨트롤러 + DisplayActor 를 붙인다.
    ///     게임 프리팹이 없는 종류(부포모프·덴솝테르)는 에셋 팩 프리팹을 그대로 쓴다
    ///  2) 씬: Environment(인게임 맵 대신 같은 땅 텍스처의 바닥·바위 + 인게임과 같은 하늘·조명 + 원경 안개·포스트프로세싱) + Diorama(플레이어·몬스터 무리·원경 보스) + CM_Menu 카메라
    /// 배치는 고정 시드라 다시 만들어도 같다. 구도·수치는 아래 상수에서 조정
    /// </summary>
    public static class MainMenuDioramaBuilder
    {
        private const string DisplayFolder = "Assets/Prefabs/MainMenu/Display";
        private const string VolumeProfilePath = "Assets/Settings/MainMenuVolumeProfile.asset";
        private const string GroundMaterialPath = "Assets/Materials/MainMenu/MainMenuGround.mat";
        private const string RockPath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Prefabs/_Exteriors/Rock_02.prefab";
        private const float GroundSize = 300f;
        /// <summary>인게임 지형 레이어(layer_Sand_01)의 텍스처·타일 크기</summary>
        private const string GroundTexturePath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Textures/Sand_01.png";
        private const float GroundTileSize = 30f;
        private const string NoisePath = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";
        private const string MonsterPack = "Assets/ExternalAssets/Monsters Full Pack Vol 1/";
        private const string GamePrefabs = "Assets/Resources/Prefabs/Monsters/";
        private const int Seed = 1028;

        // 디오라마 기준(플레이어 발밑) 좌표. +Z = 몬스터 쪽
        private static readonly Vector3 CameraPosition = new Vector3(0.75f, 1.7f, -2.4f);
        private static readonly Vector3 CameraTarget = new Vector3(2.5f, 1.4f, 16f);
        private const float CameraFov = 45f;
        private static readonly Vector3 BossPosition = new Vector3(6f, 0f, 50f);

        /// <summary>바위 (위치, 크기, 회전). 원경 안개 속 실루엣 (2026-10-28 씬에서 조정한 값)</summary>
        private static readonly (Vector3 Position, float Scale, float Yaw)[] Rocks =
        {
            (new Vector3(-7f, 0f, 163f), 2f, 110f),
            (new Vector3(131f, 0f, 228f), 4f, 300f),
            (new Vector3(-75f, 0f, 150f), 1.2f, 30f),
        };

        /// <summary>원경을 덮는 안개. 스카이박스 지평선과 비슷한 회청색이라 땅 끝이 하늘로 녹아든다</summary>
        private static readonly Color FogColor = new Color(0.46f, 0.5f, 0.56f);
        /// <summary>지수² 안개 농도. 50m 원경 보스가 실루엣만 남는 정도</summary>
        private const float FogDensity = 0.022f;
        /// <summary>인게임 씬 Directional Light 회전</summary>
        private static readonly Quaternion InGameSunRotation = new Quaternion(0.40821794f, -0.23456973f, 0.10938166f, 0.8754261f);

        /// <summary>디스플레이 캐릭터 한 종류</summary>
        private sealed class ActorSpec
        {
            public string Name;
            public string Source;
            /// <summary>null = 원래 컨트롤러 유지</summary>
            public string Controller;
            public string Idle;
            public string Threat;
            public string ThreatSound;
            public bool Hover;
        }

        private static readonly ActorSpec Player = new ActorSpec
        {
            Name = "Player", Source = "Assets/Scenes/MS/Prefabs/Player.prefab", Idle = "Locomotion",
        };

        private static readonly ActorSpec Boss = new ActorSpec
        {
            Name = "Clypeosaurus", Source = "Assets/Resources/Boss/Clypeosaurus/Prefab/Clypeosaurus.prefab",
            Idle = "IdleBreathe", Threat = "RoarAntennaElectricAttack", ThreatSound = "mon_clypeosaurus_shout",
        };

        private static readonly ActorSpec Bufomorph = new ActorSpec
        {
            Name = "Bufomorph", Source = MonsterPack + "Monsters Pack Vol 5/Bufomorph/Prefabs/Buformorph_Dark Variant.prefab",
            Controller = MonsterPack + "Monsters Pack Vol 5/Bufomorph/Bufomorph_Controller.controller",
            Idle = "IdleBreathe", Threat = "SpitAttack", ThreatSound = "mon_bufomorph_idle",
        };

        private static readonly ActorSpec MuscomorphGround = new ActorSpec
        {
            Name = "Muscomorph_Ground", Source = GamePrefabs + "Muscomorph_Ground.prefab",
            Controller = MonsterPack + "Monsters Pack Vol 2/Muscomorph/Muscomorph Controller.controller",
            Idle = "IdleBreathe", Threat = "Roar", ThreatSound = "mon_muscomorph_idle",
        };

        private static readonly ActorSpec MuscomorphAir = new ActorSpec
        {
            Name = "Muscomorph_Air", Source = GamePrefabs + "Muscomorph_Air.prefab",
            Controller = MonsterPack + "Monsters Pack Vol 2/Muscomorph/Muscomorph Controller.controller",
            Idle = "Fly", Threat = "FlyingRoar", ThreatSound = "mon_muscomorph_fly_idle", Hover = true,
        };

        private static readonly ActorSpec Densoptere = new ActorSpec
        {
            Name = "Densoptere", Source = MonsterPack + "Monsters Pack Vol 6/Densoptere/Prefabs/Densoptere.prefab",
            Controller = MonsterPack + "Monsters Pack Vol 6/Densoptere/Densoptere_Controller.controller",
            Idle = "FlyStationary", Threat = "FlyStationaryBite", ThreatSound = "mon_densoptere_idle", Hover = true,
        };

        private static readonly ActorSpec Letalobrach = new ActorSpec
        {
            Name = "Letalobrach", Source = GamePrefabs + "Letalobrach.prefab",
            Controller = MonsterPack + "Monsters Pack Vol 4/Letalobrach/Letalobrach_Controller.controller",
            Idle = "IdleBreathe", Threat = "Roar", ThreatSound = "mon_letalobrach_idle",
        };

        private static readonly ActorSpec Pardathrox = new ActorSpec
        {
            Name = "Pardathrox", Source = GamePrefabs + "Pardathrox.prefab",
            Controller = MonsterPack + "Monsters Pack Vol 5/Pardathrox/Pardathrox_Controller.controller",
            Idle = "IdleBreathe", Threat = "Taunt", ThreatSound = "mon_pardathrox_idle",
        };

        private static readonly ActorSpec GiantSlug = new ActorSpec
        {
            Name = "GiantSlug", Source = GamePrefabs + "GiantSlug.prefab",
            Controller = MonsterPack + "Monsters Pack Vol 3/Giant Slug/GiantSlug_Controller.controller",
            Idle = "IdleBreathe", Threat = "SpittersAttack", ThreatSound = "mon_giant_slug_idle",
        };

        /// <summary>
        /// 무리 구성: 종류, 마리 수, 플레이어로부터 거리(고리), 비행 높이(0 = 지상), 차지하는 반지름(m).
        /// 플레이어를 부채꼴 고리로 포위하고, 작은 것(무스코모프·부포모프)이 안쪽, 크기 순으로 바깥 고리 (원경에 보스)
        /// </summary>
        private static readonly (ActorSpec Spec, int Count, Vector2 Distance, Vector2 Height, float Radius)[] Crowd =
        {
            (MuscomorphGround, 9, new Vector2(7f, 12f), Vector2.zero, 1.0f),
            (Bufomorph, 9, new Vector2(9f, 14f), Vector2.zero, 0.9f),
            (Pardathrox, 4, new Vector2(15f, 20f), Vector2.zero, 1.8f),
            (Letalobrach, 3, new Vector2(19f, 25f), Vector2.zero, 2.2f),
            (GiantSlug, 3, new Vector2(24f, 31f), Vector2.zero, 2.5f),
            (MuscomorphAir, 4, new Vector2(8f, 14f), new Vector2(3f, 5.5f), 1.2f),
            (Densoptere, 3, new Vector2(15f, 26f), new Vector2(4.5f, 8f), 1.4f),
        };

        /// <summary>포위 부채꼴 각도(도, 0 = 정면 +Z, + = 오른쪽). 화면 양 끝까지 차도록 시야보다 조금 넓게</summary>
        private static readonly Vector2 ArcAngles = new Vector2(-60f, 70f);

        // ───────────────────────── 메뉴 ─────────────────────────

        /// <summary>Test_MainMenu 를 열어 디오라마만 다시 만든다 (UI·SoundService 는 그대로)</summary>
        [MenuItem("Tools/MainMenu/Rebuild Diorama")]
        public static void Rebuild()
        {
            if (Application.isBatchMode == false && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            if (File.Exists(OptionsPopupBuilder.TestScenePath) == false)
            {
                OptionsPopupBuilder.CreateTestScene(); // 씬 생성 중에 디오라마도 만든다
                return;
            }
            var scene = EditorSceneManager.OpenScene(OptionsPopupBuilder.TestScenePath, OpenSceneMode.Single);
            var camera = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogError("[MainMenuDioramaBuilder] 씬에 카메라가 없습니다");
                return;
            }
            BuildInScene(camera);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MainMenuDioramaBuilder] 디오라마를 다시 만들었습니다");
        }

        /// <summary>batchmode 용</summary>
        public static void RebuildBatch() => Rebuild();

        // ───────────────────────── 씬 ─────────────────────────

        /// <summary>열린 씬에 Environment·Diorama·CM_Menu 를 (있으면 지우고) 만든다</summary>
        public static void BuildInScene(Camera mainCamera)
        {
            var prefabs = BuildDisplayPrefabs();

            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                // 최상위 "Directional Light" 는 씬에서 직접 추가했던 보조광 → 지금은 Environment 의 Fill 로 만든다 (두 번 켜지지 않게)
                if (root.name == "Environment" || root.name == "Diorama" || root.name == "CM_Menu"
                    || (root.name == "Directional Light" && root.GetComponent<Light>() != null))
                    Object.DestroyImmediate(root);

            var environment = new GameObject("Environment");
            CreateGround(environment.transform);
            CreateLighting(environment.transform);
            CreateVolume(environment.transform);

            var diorama = new GameObject("Diorama");
            PlaceActors(diorama.transform, prefabs);

            SetupCamera(mainCamera, diorama.transform);
            MainMenuUIBuilder.LinkCamera(Object.FindAnyObjectByType<MenuCameraSway>()); // CM_Menu 를 새로 만들었으므로
        }

        /// <summary>
        /// 인게임 맵 대신 평평한 바닥 + 바위 몇 개 (연출 전용이라 가볍게). 원경은 안개가 덮으므로 바닥 끝이 보이지 않을 만큼만 크게.
        /// 바닥은 물리가 필요 없어 콜라이더를 뺀다
        /// </summary>
        private static void CreateGround(Transform parent)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localPosition = new Vector3(0f, 0f, 40f);
            ground.transform.localScale = new Vector3(GroundSize / 10f, 1f, GroundSize / 10f); // Plane 은 10m
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.GetComponent<MeshRenderer>().sharedMaterial = GroundMaterial();

            var rock = AssetDatabase.LoadAssetAtPath<GameObject>(RockPath);
            if (rock == null)
            {
                Debug.LogWarning($"[MainMenuDioramaBuilder] 바위 프리팹이 없어 바닥만 둡니다: {RockPath}");
                return;
            }
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(parent, false);
            foreach (var (position, scale, yaw) in Rocks)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(rock, rocks);
                go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                go.transform.localScale = Vector3.one * scale;
            }
        }

        /// <summary>
        /// 인게임 지형(Creepy_Cat Terrain)과 같은 땅: 지형 레이어 Sand_01 텍스처, 30m 타일. 재질은 없으면 만들고 값은 매번 다시 넣는다
        /// </summary>
        private static Material GroundMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                EnsureFolder(Path.GetDirectoryName(GroundMaterialPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(material, GroundMaterialPath);
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexturePath);
            if (texture == null) Debug.LogWarning($"[MainMenuDioramaBuilder] 땅 텍스처가 없어 단색으로 둡니다: {GroundTexturePath}");
            float tiling = GroundSize / GroundTileSize;
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            material.SetColor("_BaseColor", texture != null ? Color.white : new Color(0.45f, 0.4f, 0.33f));
            material.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 인게임 씬(Monster Player Integration)과 같은 하늘·조명: 기본 스카이박스, 스카이박스 주변광, 흰색 Directional 1.2 (같은 방향).
        /// 여기에 디오라마 연출만 더한다 — 같은 방향 보조광 1.0, 몬스터 뒤 약한 역광(윤곽), 원경을 덮는 안개(신비감)
        /// </summary>
        private static void CreateLighting(Transform parent)
        {
            var sun = CreateLight(parent, "Directional Light", InGameSunRotation, Color.white, 1.2f, LightShadows.Soft);
            // 같은 방향 보조광 (씬에서 추가해 조정한 값) — 인게임보다 밝게
            CreateLight(parent, "Directional Light (Fill)", InGameSunRotation, Color.white, 1f, LightShadows.Soft);
            CreateLight(parent, "RimLight", Quaternion.Euler(15f, 200f, 0f), new Color(1f, 0.55f, 0.35f), 0.8f, LightShadows.None);

            RenderSettings.skybox = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;
            DynamicGI.UpdateEnvironment(); // 굽지 않은 씬도 스카이박스 주변광이 바로 반영되게
        }

        private static Light CreateLight(Transform parent, string name, Quaternion rotation, Color color, float intensity, LightShadows shadows)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(parent, false);
            go.transform.rotation = rotation;
            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = shadows;
            return light;
        }

        /// <summary>
        /// 인게임(PC 렌더러 볼륨 SampleSceneProfile: Bloom 0.25·Vignette 0.2·Neutral 톤매핑)과 같은 색감에 Bloom·Vignette 만 조금 더.
        /// 프로필은 다시 만들 때마다 새로 쓴다
        /// </summary>
        private static void CreateVolume(Transform parent)
        {
            AssetDatabase.DeleteAsset(VolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            var bloom = AddOverride<Bloom>(profile);
            bloom.intensity.Override(0.4f);
            bloom.threshold.Override(1f);
            bloom.scatter.Override(0.6f);

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.4f);

            AddOverride<Tonemapping>(profile).mode.Override(TonemappingMode.Neutral);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Global Volume", typeof(Volume));
            go.transform.SetParent(parent, false);
            var volume = go.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
        }

        private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>플레이어(기준점) + 고정 시드로 흩뿌린 몬스터 무리 + 원경 보스</summary>
        private static void PlaceActors(Transform diorama, Dictionary<ActorSpec, GameObject> prefabs)
        {
            Spawn(prefabs[Player], diorama, Vector3.zero, Quaternion.identity);

            var rng = new System.Random(Seed);
            var placed = new List<(Vector3 Position, float Radius)> { (Vector3.zero, 5f) }; // 플레이어 주변은 비워 둔다
            var crowd = new GameObject("Crowd").transform;
            crowd.SetParent(diorama, false);

            foreach (var group in Crowd)
            {
                for (int i = 0; i < group.Count; i++)
                {
                    if (TryPick(rng, group.Distance, group.Radius, placed, out var p) == false) continue;
                    placed.Add((p, group.Radius));
                    float height = group.Height == Vector2.zero ? 0f : Lerp(rng, group.Height);
                    Spawn(prefabs[group.Spec], crowd, p + Vector3.up * height, FacePlayer(p, rng));
                }
            }

            Spawn(prefabs[Boss], diorama, BossPosition, FacePlayer(BossPosition, rng));
        }

        /// <summary>플레이어 둘레 부채꼴(ArcAngles) 고리(distance) 안에서 다른 개체와 겹치지 않는 자리</summary>
        private static bool TryPick(System.Random rng, Vector2 distance, float radius, List<(Vector3 Position, float Radius)> placed, out Vector3 position)
        {
            for (int attempt = 0; attempt < 40; attempt++)
            {
                float angle = Lerp(rng, ArcAngles) * Mathf.Deg2Rad;
                float r = Lerp(rng, distance);
                position = new Vector3(Mathf.Sin(angle) * r, 0f, Mathf.Cos(angle) * r);

                bool free = true;
                foreach (var other in placed)
                {
                    var d = position - other.Position;
                    d.y = 0f;
                    if (d.magnitude < radius + other.Radius) { free = false; break; }
                }
                if (free) return true;
            }
            position = default;
            return false;
        }

        private static Quaternion FacePlayer(Vector3 position, System.Random rng)
        {
            var toPlayer = -position;
            toPlayer.y = 0f;
            float jitter = Lerp(rng, new Vector2(-12f, 12f));
            return Quaternion.LookRotation(toPlayer.sqrMagnitude > 0.01f ? toPlayer : Vector3.back) * Quaternion.Euler(0f, jitter, 0f);
        }

        private static float Lerp(System.Random rng, Vector2 range) => Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());

        /// <summary>바닥(y = 0) 위에, 비행형은 local.y 높이에 놓는다</summary>
        private static void Spawn(GameObject prefab, Transform parent, Vector3 local, Quaternion rotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetLocalPositionAndRotation(local, rotation);
        }

        /// <summary>메인 카메라에 CinemachineBrain·포스트프로세싱, CM_Menu 에 오버숄더 구도·Noise·흔들림</summary>
        private static void SetupCamera(Camera mainCamera, Transform diorama)
        {
            mainCamera.clearFlags = CameraClearFlags.Skybox; // 인게임과 같은 하늘
            mainCamera.fieldOfView = CameraFov;
            mainCamera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            if (mainCamera.GetComponent<CinemachineBrain>() == null) mainCamera.gameObject.AddComponent<CinemachineBrain>();

            var go = new GameObject("CM_Menu", typeof(CinemachineCamera), typeof(CinemachineBasicMultiChannelPerlin), typeof(MenuCameraSway));
            var position = diorama.TransformPoint(CameraPosition);
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(diorama.TransformPoint(CameraTarget) - position));

            var cm = go.GetComponent<CinemachineCamera>();
            cm.Priority = 10;
            var lens = cm.Lens;
            lens.FieldOfView = CameraFov;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 300f;
            cm.Lens = lens;

            var noise = go.GetComponent<CinemachineBasicMultiChannelPerlin>();
            noise.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoisePath);
            noise.AmplitudeGain = 0.25f;
            noise.FrequencyGain = 0.35f;

            mainCamera.transform.SetPositionAndRotation(go.transform.position, go.transform.rotation);
        }

        // ───────────────────────── 디스플레이 프리팹 ─────────────────────────

        private static Dictionary<ActorSpec, GameObject> BuildDisplayPrefabs()
        {
            EnsureFolder(DisplayFolder);
            var result = new Dictionary<ActorSpec, GameObject>();
            var specs = new List<ActorSpec> { Player, Boss };
            foreach (var group in Crowd)
                if (specs.Contains(group.Spec) == false) specs.Add(group.Spec);

            foreach (var spec in specs) result[spec] = BuildDisplayPrefab(spec);
            return result;
        }

        private static GameObject BuildDisplayPrefab(ActorSpec spec)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.Source);
            if (source == null) throw new FileNotFoundException("[MainMenuDioramaBuilder] 프리팹이 없습니다", spec.Source);

            var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = spec.Name + "_Display";
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                if (spec == Player) TrimPlayer(go);
                StripToDisplay(go);

                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    if (spec.Controller != null)
                        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(spec.Controller);
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                }
                else Debug.LogWarning($"[MainMenuDioramaBuilder] {spec.Name} 에 Animator 가 없습니다");

                var actor = go.AddComponent<DisplayActor>();
                var so = new SerializedObject(actor);
                so.FindProperty("animator").objectReferenceValue = animator;
                so.FindProperty("idleState").stringValue = spec.Idle ?? "";
                so.FindProperty("threatState").stringValue = spec.Threat ?? "";
                so.FindProperty("threatSoundKey").stringValue = spec.ThreatSound ?? "";
                so.FindProperty("hover").boolValue = spec.Hover;
                so.ApplyModifiedPropertiesWithoutUndo();

                string path = $"{DisplayFolder}/{go.name}.prefab";
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>플레이어: 카메라 리그·조준점·HUD 제거, 무기는 기본 소총(Weapon_Rifle)만 남김</summary>
        private static void TrimPlayer(GameObject player)
        {
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t == player.transform) continue;
                string n = t.name;
                bool remove = n == "ViewRig" || n == "AimTarget" || n == "DamageVignetteHud"
                    || (n.StartsWith("Weapon_") && n != "Weapon_Rifle");
                if (remove) Object.DestroyImmediate(t.gameObject);
            }
            foreach (var t in player.GetComponentsInChildren<Transform>(true))
                if (t.name == "Weapon_Rifle")
                    foreach (var child in t.GetComponentsInChildren<Transform>(true)) child.gameObject.SetActive(true);
        }

        /// <summary>
        /// 화면에 필요한 것(Transform·렌더러·Animator·파티클)만 남기고 전부 제거. UI 캔버스는 오브젝트째 제거.
        /// RequireComponent 로 묶인 컴포넌트는 의존하는 쪽부터 지운다 (예: BossSoundEmitter → BossController)
        /// </summary>
        private static void StripToDisplay(GameObject root)
        {
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                if (canvas != null && canvas.gameObject != root) Object.DestroyImmediate(canvas.gameObject);

            for (int pass = 0; pass < 10; pass++)
            {
                bool removed = false;
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (component == null || Keep(component) || IsRequired(component)) continue;
                    Object.DestroyImmediate(component);
                    removed = true;
                }
                if (removed == false) return;
            }
        }

        private static bool Keep(Component c)
            => c is Transform || c is Animator || c is Renderer || c is MeshFilter || c is LODGroup || c is ParticleSystem;

        /// <summary>같은 오브젝트의 다른 컴포넌트가 [RequireComponent] 로 c 의 타입을 요구하는지</summary>
        private static bool IsRequired(Component c)
        {
            foreach (var other in c.GetComponents<Component>())
            {
                if (other == null || other == c) continue;
                foreach (RequireComponent require in Attribute.GetCustomAttributes(other.GetType(), typeof(RequireComponent), true))
                    if (Matches(require.m_Type0, c) || Matches(require.m_Type1, c) || Matches(require.m_Type2, c)) return true;
            }
            return false;
        }

        private static bool Matches(Type required, Component c) => required != null && required.IsInstanceOfType(c);

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
