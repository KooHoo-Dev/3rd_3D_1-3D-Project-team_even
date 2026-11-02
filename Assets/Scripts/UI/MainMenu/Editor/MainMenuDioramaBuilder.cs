using System;
using System.Collections.Generic;
using System.IO;
using HybridShooter.Atmosphere;
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
    ///  2) 씬: Environment(인게임 맵 대신 같은 땅 텍스처의 바닥·바위 + 팀 외계 대기 기반의 어두운 밤(밤하늘·거대한 달·달빛·청회색 안개)·포스트프로세싱
    ///     + 바닥 안개·불씨 파티클·원경 섬광 + 호버 연출용 볼륨) + Diorama(플레이어·몬스터 무리·원경 보스·추락한 우주선 + MenuBackdropDirector) + CM_Menu 카메라
    /// 몬스터 배치는 씬에서 직접 옮긴 결과를 Placements 표로 고정해 두었다 (다시 만들어도 같다). 구도·수치는 아래 상수에서 조정
    /// </summary>
    public static class MainMenuDioramaBuilder
    {
        private const string DisplayFolder = "Assets/Prefabs/MainMenu/Display";
        private const string VolumeProfilePath = "Assets/Settings/MainMenuVolumeProfile.asset";
        private const string HoverOptionsProfilePath = "Assets/Settings/MainMenuHoverOptionsProfile.asset";
        private const string HoverQuitProfilePath = "Assets/Settings/MainMenuHoverQuitProfile.asset";
        private const string FogMaterialPath = "Assets/Materials/MainMenu/MainMenuFog.mat";
        private const string EmberMaterialPath = "Assets/Materials/MainMenu/MainMenuEmber.mat";
        private const string SmokeTexturePath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Textures/Smoke.png";
        private const string GlowTexturePath = "Assets/Prefabs/Effects/Textures/T_Glow.png";
        private const string GroundMaterialPath = "Assets/Materials/MainMenu/MainMenuGround.mat";
        private const string RockPath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Prefabs/_Exteriors/Rock_02.prefab";
        private const float GroundSize = 300f;
        /// <summary>인게임 지형 레이어(layer_Sand_01)의 텍스처·타일 크기</summary>
        private const string GroundTexturePath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Textures/Sand_01.png";
        private const float GroundTileSize = 30f;
        private const string NoisePath = "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";
        private const string MonsterPack = "Assets/ExternalAssets/Monsters Full Pack Vol 1/";
        private const string GamePrefabs = "Assets/Resources/Prefabs/Monsters/";

        // 디오라마 기준(플레이어 발밑) 좌표. +Z = 몬스터 쪽
        private static readonly Vector3 CameraPosition = new Vector3(0.75f, 1.7f, -2.4f);
        private static readonly Vector3 CameraTarget = new Vector3(2.5f, 1.4f, 16f);
        private const float CameraFov = 45f;
        private static readonly Vector3 BossPosition = new Vector3(6f, 0f, 50f);
        private const float BossYaw = -169.45f;

        /// <summary>추락한 우주선 (2026-10-29 씬에서 직접 놓은 자리: 오른쪽 원경, 살짝 기울어짐. 크기는 프리팹 기본 0.69)</summary>
        private const string ShipPath = "Assets/Scenes/GU/Prefabs/Spaceship_Broken.prefab";
        private static readonly Vector3 ShipPosition = new Vector3(15.73f, 2.15f, 30.6f);
        private static readonly Quaternion ShipRotation = new Quaternion(-0.025284223f, 0.9988745f, -0.040118933f, -0.001015519f);

        /// <summary>원경 섬광 위치 후보 (몬스터 무리 너머 양옆·정면, 안개 속)</summary>
        private static readonly Vector3[] FlashPoints =
        {
            new Vector3(-35f, 6f, 70f), new Vector3(10f, 8f, 85f), new Vector3(45f, 6f, 65f), new Vector3(-10f, 5f, 95f),
        };
        private static readonly Color FlashColor = new Color(1f, 0.6f, 0.35f);
        private static readonly Color EmberColor = new Color(2.2f, 0.55f, 0.25f); // HDR → Bloom. 달 눈동자와 같은 붉은 주황

        /// <summary>
        /// 바위 (위치, 크기, 회전). 원경 안개 속 실루엣 (2026-10-28 씬에서 조정한 위치).
        /// 어두운 밤하늘에서는 검은 산처럼 보여 달을 가리므로 2026-10-30 가운데 2→1.3, 오른쪽 4→2.6 으로 낮춤
        /// </summary>
        private static readonly (Vector3 Position, float Scale, float Yaw)[] Rocks =
        {
            (new Vector3(-7f, 0f, 163f), 1.3f, 110f),
            (new Vector3(131f, 0f, 228f), 2.6f, 300f),
            (new Vector3(-75f, 0f, 150f), 1.2f, 30f),
        };

        /// <summary>
        /// 메뉴 전용 외계 대기 프리셋 (팀 AlienAtmospherePreset 과 같은 클래스). 없을 때만 MenuAtmosphere() 값으로 만들고,
        /// 그 뒤로는 에셋에서 직접 조정한 값을 그대로 쓴다 (팀 프리셋은 게임 씬용이라 건드리지 않는다)
        /// </summary>
        private const string AtmospherePresetPath = "Assets/Settings/MainMenuAtmospherePreset.asset";
        private const string SkyMaterialPath = "Assets/Materials/MainMenu/MainMenuSky.mat";
        private const string SkyShaderName = "HybridShooter/Atmosphere/Alien Night Sky";

        /// <summary>
        /// 카메라 뒤에서 비추는 약한 차가운 보조광. 달빛이 정면(무리 너머)에서 오므로 이것이 없으면 플레이어 등과 몬스터 얼굴이 완전히 검다
        /// </summary>
        private static readonly Quaternion FillRotation = Quaternion.Euler(20f, 10f, 0f);
        private static readonly Color FillColor = new Color(0.55f, 0.62f, 0.8f);
        private const float FillIntensity = 0.6f; // 0.35 는 플레이어가 검은 실루엣이 됨

        /// <summary>
        /// 무리 위 20m 의 포인트 조명 (2026-10-30 씬에서 직접 단 값). 달빛·환경광만으로는 너무 어두워 무리 가운데와 보스를 위에서 비춘다
        /// </summary>
        private static readonly Vector3 OverheadLightPosition = new Vector3(3.3f, 19.9f, 32.9f);
        private static readonly Color OverheadLightColor = new Color(0.99f, 0.98f, 0.8f);
        private const float OverheadLightIntensity = 1000f;
        private const float OverheadLightRange = 100f;

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
        /// 몬스터 배치: 종류, 위치(플레이어 발밑 기준, 비행형은 y = 높이), 방향(Y 회전, 도).
        /// 2026-10-29 씬(Test_MainMenu)에서 직접 옮긴 배치 — 작은 무스코모프가 바로 앞, 큰 것일수록 뒤.
        /// 씬에서 다시 옮기면 이 표를 그 값으로 바꾼다 (부포모프는 지금 배치에 없음, 표에 넣으면 바로 쓸 수 있다)
        /// </summary>
        private static readonly (ActorSpec Spec, Vector3 Position, float Yaw)[] Placements =
        {
            (MuscomorphGround, new Vector3(5.47f, 0f, 4.67f), -125.04f),
            (MuscomorphGround, new Vector3(-5.51f, 0f, 4.97f), 133.52f),
            (MuscomorphGround, new Vector3(0.83f, 0f, 7.82f), 177.88f),
            (MuscomorphGround, new Vector3(7.98f, 0f, 3.38f), -102.71f),
            (MuscomorphGround, new Vector3(6.11f, 0f, 6.95f), -142.81f),
            (MuscomorphGround, new Vector3(3.82f, 0f, 8.66f), -144.42f),
            (MuscomorphGround, new Vector3(-2.57f, 0f, 9.93f), 154.48f),
            (MuscomorphGround, new Vector3(-0.28f, 0f, 10.59f), -174.99f),
            (MuscomorphGround, new Vector3(2.97f, 0f, 11.43f), -168.08f),
            (Pardathrox, new Vector3(15.76f, 0f, 7.5f), -109.9f),
            (Pardathrox, new Vector3(6.99f, 0f, 17.74f), -168.7f),
            (Pardathrox, new Vector3(-2f, 0f, 19.4f), -177.15f),
            (Pardathrox, new Vector3(-12.21f, 0f, 15.72f), 152.79f),
            (Letalobrach, new Vector3(14.19f, 0f, 15.03f), -148.18f),
            (Letalobrach, new Vector3(-6.48f, 0f, 23.1f), 163.12f),
            (Letalobrach, new Vector3(22.67f, 0f, 9.14f), -109.56f),
            (GiantSlug, new Vector3(-14.8f, 0f, 27.9f), 153.7f),
            (MuscomorphAir, new Vector3(-4.42f, 4.54f, 7.62f), 158.84f),
            (MuscomorphAir, new Vector3(9.78f, 3.83f, 5.13f), -108.12f),
            (MuscomorphAir, new Vector3(-11.48f, 4.22f, 6.74f), 126.7f),
            (MuscomorphAir, new Vector3(-1.22f, 4.18f, 13.81f), -174.09f),
            (Densoptere, new Vector3(-4.59f, 6.67f, 16.23f), 172.6f),
            (Densoptere, new Vector3(-15.66f, 7.55f, 14.31f), 120.61f),
            (Densoptere, new Vector3(11.9f, 5.01f, 26f), 174.78f),
        };

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
                    || (root.name == "Directional Light" && root.GetComponent<Light>() != null)
                    // 씬에서 직접 달았던 포인트 조명 → 지금은 Environment 의 Overhead Light 로 만든다
                    || (root.name == "Point Light" && root.GetComponent<Light>() != null))
                    Object.DestroyImmediate(root);

            var environment = new GameObject("Environment");
            CreateGround(environment.transform);
            CreateLighting(environment.transform);
            CreateVolume(environment.transform);
            var (optionsVolume, quitVolume) = CreateHoverVolumes(environment.transform);
            CreateAtmosphere(environment.transform);

            var diorama = new GameObject("Diorama");
            PlaceActors(diorama.transform, prefabs);

            var menuCamera = SetupCamera(mainCamera, diorama.transform);
            var director = CreateDirector(diorama.transform, menuCamera, optionsVolume, quitVolume);
            MainMenuUIBuilder.LinkDiorama(menuCamera.GetComponent<MenuCameraSway>(), director); // CM_Menu·연출을 새로 만들었으므로
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
        /// 게임 본편과 같은 외계 행성 밤 (팀 외계 대기: 별·성운 밤하늘, 붉은 눈동자 같은 거대한 달, 차가운 달빛, 청회색 안개) 을 메뉴용으로 더 어둡게.
        ///  - 달빛은 무리 너머 오른쪽 위에서 비춘다 → 하늘의 달이 화면 오른쪽에 뜨고, 몬스터는 달을 등진 실루엣 + 윤곽광
        ///  - 카메라 뒤 약한 보조광으로 플레이어 등·몬스터 앞면이 읽히게, 무리 위 포인트 조명으로 가운데·보스를 위에서
        ///  - 환경광은 프리셋의 Gradient(하늘·지평선·땅) 3색
        /// </summary>
        private static void CreateLighting(Transform parent)
        {
            var preset = MenuAtmosphere();
            var moon = CreateLight(parent, "Moonlight", Quaternion.Euler(preset.moonLightRotation), preset.moonLightColor, preset.moonLightIntensity, LightShadows.Soft);
            moon.shadowStrength = preset.moonLightShadowStrength;
            CreateLight(parent, "Fill (Camera)", FillRotation, FillColor, FillIntensity, LightShadows.None);
            var overhead = CreateLight(parent, "Overhead Light", Quaternion.identity, OverheadLightColor, OverheadLightIntensity, LightShadows.None);
            overhead.type = LightType.Point;
            overhead.range = OverheadLightRange;
            overhead.transform.localPosition = OverheadLightPosition;

            RenderSettings.skybox = SkyMaterial(preset);
            RenderSettings.sun = moon; // 하늘 셰이더는 메인 라이트 방향에 달을 그린다
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = preset.ambientSkyColor;
            RenderSettings.ambientEquatorColor = preset.ambientEquatorColor;
            RenderSettings.ambientGroundColor = preset.ambientGroundColor;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = preset.reflectionIntensity;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = preset.fogColor;
            RenderSettings.fogDensity = preset.fogDensity;
            DynamicGI.UpdateEnvironment(); // 굽지 않은 씬도 하늘 반사가 바로 반영되게
        }

        /// <summary>메뉴 프리셋. 없으면 만든다 (있으면 직접 조정한 값 유지)</summary>
        private static AtmospherePreset MenuAtmosphere()
        {
            var preset = AssetDatabase.LoadAssetAtPath<AtmospherePreset>(AtmospherePresetPath);
            if (preset != null) return preset;

            // 팀 프리셋(게임 씬) 기준으로 메뉴용 값: 안개·하늘·환경광을 어둡게, 달은 화면 안에 들어오게 작게 + 눈동자 더 또렷하게
            preset = ScriptableObject.CreateInstance<AtmospherePreset>();
            preset.fogColor = new Color(0.2f, 0.25f, 0.33f);
            preset.fogDensity = 0.022f;
            preset.skyZenithColor = new Color(0.09f, 0.1f, 0.18f);
            preset.skyHorizonBlend = 0.3f;
            preset.starBrightness = 3f;
            preset.starTwinkle = 0.6f;
            preset.nebulaColorA = new Color(0.55f, 0.22f, 0.8f);
            preset.nebulaColorB = new Color(0.15f, 0.6f, 0.7f);
            preset.nebulaIntensity = 0.8f;
            preset.moonRadius = 9f;
            preset.moonColor = new Color(0.88f, 0.9f, 0.95f);
            preset.moonBrightness = 1.2f;
            preset.moonGlowColor = new Color(0.55f, 0.68f, 1f);
            preset.moonGlowIntensity = 0.9f;
            preset.moonEyeColor = new Color(2f, 0.25f, 0.1f);
            preset.moonEyeIntensity = 0.75f;
            preset.ambientSkyColor = new Color(0.2f, 0.225f, 0.29f);
            preset.ambientEquatorColor = new Color(0.15f, 0.165f, 0.2f);
            preset.ambientGroundColor = new Color(0.08f, 0.07f, 0.085f);
            preset.reflectionIntensity = 0.5f;
            preset.moonLightColor = new Color(0.72f, 0.82f, 1f);
            preset.moonLightIntensity = 0.9f;
            // 달 = 빛이 오는 쪽. 고도 12°, 정면에서 오른쪽 14° (메뉴가 있는 왼쪽을 피해 무리 너머)
            preset.moonLightRotation = new Vector3(12f, 194f, 0f);
            preset.moonLightShadowStrength = 0.8f;
            preset.smokeColor = new Color(0.38f, 0.43f, 0.5f, 0.32f);
            EnsureFolder(Path.GetDirectoryName(AtmospherePresetPath).Replace('\\', '/'));
            AssetDatabase.CreateAsset(preset, AtmospherePresetPath);
            return preset;
        }

        /// <summary>팀 밤하늘 셰이더로 메뉴 전용 하늘 재질 (값은 매번 프리셋에서, 팀 AtmosphereSetupMenu 와 같은 대응)</summary>
        private static Material SkyMaterial(AtmospherePreset preset)
        {
            var shader = Shader.Find(SkyShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[MainMenuDioramaBuilder] 하늘 셰이더가 없어 기본 하늘을 씁니다: {SkyShaderName}");
                return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
            }
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (sky == null)
            {
                sky = new Material(shader);
                EnsureFolder(Path.GetDirectoryName(SkyMaterialPath).Replace('\\', '/'));
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }
            sky.shader = shader;
            sky.SetColor("_ZenithColor", preset.skyZenithColor);
            sky.SetColor("_HorizonColor", preset.fogColor);
            sky.SetFloat("_HorizonBlend", preset.skyHorizonBlend);
            sky.SetFloat("_StarBrightness", preset.starBrightness);
            sky.SetFloat("_StarTwinkle", preset.starTwinkle);
            sky.SetColor("_NebulaColorA", preset.nebulaColorA);
            sky.SetColor("_NebulaColorB", preset.nebulaColorB);
            sky.SetFloat("_NebulaIntensity", preset.nebulaIntensity);
            sky.SetFloat("_MoonRadius", preset.moonRadius);
            sky.SetColor("_MoonColor", preset.moonColor);
            sky.SetFloat("_MoonBrightness", preset.moonBrightness);
            sky.SetColor("_MoonGlowColor", preset.moonGlowColor);
            sky.SetFloat("_MoonGlowIntensity", preset.moonGlowIntensity);
            sky.SetColor("_EyeColor", preset.moonEyeColor);
            sky.SetFloat("_EyeIntensity", preset.moonEyeIntensity);
            EditorUtility.SetDirty(sky);
            return sky;
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
        /// 팀 외계 대기 볼륨(AlienAtmosphereVolume)과 같은 색감 — ACES, 채도↓ 대비↑, 그림자 청록·밝은 곳 옅은 보라 Split Toning,
        /// HDR 만 번지는 Bloom, 필름 그레인 — 에 메뉴용으로 노출을 낮추고 비네트를 더 진하게. 프로필은 다시 만들 때마다 새로 쓴다
        /// </summary>
        private static void CreateVolume(Transform parent)
        {
            AssetDatabase.DeleteAsset(VolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            AddOverride<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);

            var color = AddOverride<ColorAdjustments>(profile);
            color.postExposure.Override(-0.25f);
            color.contrast.Override(20f);
            color.saturation.Override(-20f);

            var split = AddOverride<SplitToning>(profile);
            split.shadows.Override(new Color(0.35f, 0.5f, 0.55f));
            split.highlights.Override(new Color(0.68f, 0.64f, 0.78f));
            split.balance.Override(-20f);

            var bloom = AddOverride<Bloom>(profile);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.8f);
            bloom.scatter.Override(0.65f);

            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.45f);

            var grain = AddOverride<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.18f);
            grain.response.Override(0.8f);
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

        // ───────────────────────── 호버 연출·분위기 ─────────────────────────

        /// <summary>
        /// 호버 연출용 전역 볼륨 2개 (가중치 0 으로 두고 MenuBackdropDirector 가 올린다). 기본 볼륨보다 Priority 가 높아 겹치는 값(비네트)은 이쪽으로 섞인다.
        ///  - Options: 채도 낮춤 + 약간 어둡게
        ///  - Quit: 붉은 비네트
        /// </summary>
        private static (Volume Options, Volume Quit) CreateHoverVolumes(Transform parent)
        {
            var options = NewProfile(HoverOptionsProfilePath);
            var color = AddOverride<ColorAdjustments>(options);
            color.saturation.Override(-45f);
            color.postExposure.Override(-0.15f);

            var quit = NewProfile(HoverQuitProfilePath);
            var vignette = AddOverride<Vignette>(quit);
            vignette.color.Override(new Color(0.55f, 0f, 0f));
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.5f);
            AssetDatabase.SaveAssets();

            return (HoverVolume(parent, "Hover Volume (Options)", options), HoverVolume(parent, "Hover Volume (Quit)", quit));
        }

        private static VolumeProfile NewProfile(string path)
        {
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        private static Volume HoverVolume(Transform parent, string name, VolumeProfile profile)
        {
            var go = new GameObject(name, typeof(Volume));
            go.transform.SetParent(parent, false);
            var volume = go.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            return volume;
        }

        /// <summary>바닥에 깔린 안개, 바람에 날리는 불씨, 원경 섬광</summary>
        private static void CreateAtmosphere(Transform parent)
        {
            var root = new GameObject("Atmosphere").transform;
            root.SetParent(parent, false);

            // 바닥 안개: 크고 옅은 연기 덩어리가 천천히 옆으로 흐른다. 카메라 바로 앞은 비워 둔다 (z 7~37)
            var fog = NewParticles(root, "GroundFog", new Vector3(0f, 0.8f, 22f), ParticleMaterial(FogMaterialPath, SmokeTexturePath, false));
            var main = fog.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.45f, 0.52f, 0.65f, 0.22f); // 달빛 받은 청회색 포자 안개
            main.maxParticles = 120;
            SetEmission(fog, 7f);
            SetBox(fog, new Vector3(70f, 0.6f, 30f));
            var fogVelocity = fog.velocityOverLifetime;
            fogVelocity.enabled = true;
            fogVelocity.space = ParticleSystemSimulationSpace.World;
            fogVelocity.x = 0.25f;
            fogVelocity.y = 0f;
            fogVelocity.z = 0f;
            var fogRotation = fog.rotationOverLifetime;
            fogRotation.enabled = true;
            fogRotation.z = new ParticleSystem.MinMaxCurve(-6f * Mathf.Deg2Rad, 6f * Mathf.Deg2Rad);
            SetFadeInOut(fog, 0.2f, 0.7f);

            // 불씨: 작고 밝은 점이 바람을 따라 오른쪽 위로 흩날린다 (HDR 색 → Bloom)
            var embers = NewParticles(root, "Embers", new Vector3(0f, 2f, 12f), ParticleMaterial(EmberMaterialPath, GlowTexturePath, true));
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = EmberColor;
            main.maxParticles = 200;
            SetEmission(embers, 18f);
            SetBox(embers, new Vector3(40f, 4f, 30f));
            var emberVelocity = embers.velocityOverLifetime;
            emberVelocity.enabled = true;
            emberVelocity.space = ParticleSystemSimulationSpace.World;
            emberVelocity.x = 0.6f;
            emberVelocity.y = 0.35f;
            emberVelocity.z = 0f;
            var noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.2f;
            SetFadeInOut(embers, 0.1f, 0.8f);

            // 원경 섬광: 무리 너머 안개 속 Point Light
            var flash = new GameObject("DistantFlash", typeof(Light), typeof(DistantFlash));
            flash.transform.SetParent(root, false);
            flash.transform.localPosition = FlashPoints[0];
            var light = flash.GetComponent<Light>();
            light.type = LightType.Point;
            light.range = 90f;
            light.color = FlashColor;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            var so = new SerializedObject(flash.GetComponent<DistantFlash>());
            var points = so.FindProperty("points");
            points.arraySize = FlashPoints.Length;
            for (int i = 0; i < FlashPoints.Length; i++) points.GetArrayElementAtIndex(i).vector3Value = FlashPoints[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ParticleSystem NewParticles(Transform parent, string name, Vector3 position, Material material)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var particles = go.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // 재생 중에는 main 값 일부를 못 바꾼다

            var main = particles.main;
            main.loop = true;
            main.prewarm = true; // 메뉴가 열리자마자 이미 깔려 있게
            main.playOnAwake = true;
            main.useUnscaledTime = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            return particles;
        }

        private static void SetEmission(ParticleSystem particles, float rate)
        {
            var emission = particles.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;
        }

        private static void SetBox(ParticleSystem particles, Vector3 size)
        {
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = size;
        }

        /// <summary>수명 동안 알파 0 → 1 (fadeIn) … 1 (fadeOut) → 0</summary>
        private static void SetFadeInOut(ParticleSystem particles, float fadeIn, float fadeOut)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(1f, fadeOut), new GradientAlphaKey(0f, 1f) });
            var color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = gradient;
        }

        /// <summary>
        /// URP Particles/Unlit 투명 재질 (없으면 만들고 값은 매번 다시 넣는다). 셰이더 GUI 를 거치지 않으므로 블렌드 값·키워드를 직접 맞춘다
        /// (CrashedShipSmoke.mat 와 같은 설정, additive = 가산 블렌드)
        /// </summary>
        private static Material ParticleMaterial(string path, string texturePath, bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (texture == null) Debug.LogWarning($"[MainMenuDioramaBuilder] 파티클 텍스처가 없습니다: {texturePath}");
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("SHADOWCASTER", false);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Diorama 에 호버 연출을 붙이고 카메라·플레이어·무리·볼륨을 연결</summary>
        private static MenuBackdropDirector CreateDirector(Transform diorama, CinemachineCamera menuCamera, Volume optionsVolume, Volume quitVolume)
        {
            var director = diorama.gameObject.AddComponent<MenuBackdropDirector>();
            var player = diorama.Find(Player.Name + "_Display");
            var so = new SerializedObject(director);
            so.FindProperty("menuCamera").objectReferenceValue = menuCamera;
            so.FindProperty("cameraSway").objectReferenceValue = menuCamera.GetComponent<MenuCameraSway>();
            so.FindProperty("player").objectReferenceValue = player != null ? player.GetComponent<DisplayActor>() : null;
            so.FindProperty("crowd").objectReferenceValue = diorama.Find("Crowd");
            so.FindProperty("optionsVolume").objectReferenceValue = optionsVolume;
            so.FindProperty("quitVolume").objectReferenceValue = quitVolume;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (player == null) Debug.LogWarning("[MainMenuDioramaBuilder] 플레이어를 찾지 못해 조준 연출이 빠집니다");
            return director;
        }

        /// <summary>플레이어(기준점) + 몬스터 무리(Placements) + 원경 보스 + 추락한 우주선</summary>
        private static void PlaceActors(Transform diorama, Dictionary<ActorSpec, GameObject> prefabs)
        {
            Spawn(prefabs[Player], diorama, Vector3.zero, Quaternion.identity);

            var crowd = new GameObject("Crowd").transform;
            crowd.SetParent(diorama, false);
            foreach (var (spec, position, yaw) in Placements)
                Spawn(prefabs[spec], crowd, position, Quaternion.Euler(0f, yaw, 0f));

            Spawn(prefabs[Boss], diorama, BossPosition, Quaternion.Euler(0f, BossYaw, 0f));

            // 연출 전용 소품이라 게임 스크립트가 없어 디스플레이 프리팹을 만들지 않고 그대로 놓는다
            var ship = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPath);
            if (ship != null) Spawn(ship, diorama, ShipPosition, ShipRotation);
            else Debug.LogWarning($"[MainMenuDioramaBuilder] 우주선 프리팹이 없어 빼고 만듭니다: {ShipPath}");
        }

        /// <summary>바닥(y = 0) 위에, 비행형은 local.y 높이에 놓는다</summary>
        private static void Spawn(GameObject prefab, Transform parent, Vector3 local, Quaternion rotation)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetLocalPositionAndRotation(local, rotation);
        }

        /// <summary>메인 카메라에 CinemachineBrain·포스트프로세싱, CM_Menu 에 오버숄더 구도·Noise·흔들림</summary>
        private static CinemachineCamera SetupCamera(Camera mainCamera, Transform diorama)
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
            return cm;
        }

        // ───────────────────────── 디스플레이 프리팹 ─────────────────────────

        private static Dictionary<ActorSpec, GameObject> BuildDisplayPrefabs()
        {
            EnsureFolder(DisplayFolder);
            var result = new Dictionary<ActorSpec, GameObject>();
            // 부포모프는 지금 배치에 없지만 Placements 에 넣으면 바로 쓸 수 있게 함께 만든다
            var specs = new List<ActorSpec> { Player, Boss, Bufomorph };
            foreach (var placement in Placements)
                if (specs.Contains(placement.Spec) == false) specs.Add(placement.Spec);

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
                // 플레이어는 소총 발광부뿐이라 원래대로 (몬스터만 눈이 빛나게)
                if (spec == Player) so.FindProperty("emissionBoost").floatValue = 1f;
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
