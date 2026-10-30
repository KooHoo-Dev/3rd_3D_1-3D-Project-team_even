using UnityEditor;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace HybridShooter.Atmosphere.EditorTools
{
    /// <summary>
    /// 외계 행성 대기(청회색 포자 안개) 를 현재 열린 씬에 적용하는 메뉴.
    /// 공용 설정(URP 에셋·렌더러·Player 프리팹·SupplyCrate)은 건드리지 않고, 씬 단위 설정과 이 폴더의 에셋만 만든다.
    ///
    ///  1. 에셋 준비 (Assets/Prefabs/Atmosphere, 없을 때만 생성 → 직접 조정한 값 유지)
    ///     프리셋 · 하늘/빛기둥/연기 재질 · 포스트 프로세싱 Volume Profile · 주변 연기/연기 덩어리 프리팹 · AtmosphereRig 프리팹
    ///  2. 씬 RenderSettings: Exponential Squared 안개, 밤하늘(별·성운·거대한 달), Gradient 환경광  (프리셋 값)
    ///  3. 달빛(Directional Light): 하늘의 달과 같은 방향의 차가운 빛  (프리셋 값)
    ///  4. AtmosphereRig 프리팹을 씬에 배치 (Global Volume + 카메라 포스트 프로세싱 + 보급 빛기둥 교체 + 주변 연기)
    ///  5. 씬 전용 Lighting Settings 를 만들어 연결 후 Generate Lighting
    ///     (기존에 여러 씬이 공유하던 Creepy_Cat 예제 Lighting Settings / Lighting Data 는 수정하지 않는다)
    ///
    /// 수치 조정: AlienAtmospherePreset 에셋 값 변경 → 메뉴 다시 실행.
    /// 포스트 프로세싱·연기 모양은 각 에셋(Volume Profile, 프리팹)을 직접 조정한다.
    /// </summary>
    public static class AtmosphereSetupMenu
    {
        private const string Root = "Assets/Prefabs/Atmosphere";
        private const string MaterialFolder = Root + "/Materials";
        private const string LightingFolder = Root + "/Lighting";
        private const string PresetPath = Root + "/AlienAtmospherePreset.asset";
        private const string ProfilePath = Root + "/AlienAtmosphereVolume.asset";
        private const string SkyMaterialPath = MaterialFolder + "/AtmosphereSky.mat";
        private const string BeaconMaterialPath = MaterialFolder + "/SupplyBeaconNoFog.mat";
        private const string SmokeMaterialPath = MaterialFolder + "/AtmosphereSmoke.mat";
        private const string AmbientSmokePath = Root + "/AmbientSmoke.prefab";
        private const string SmokeBankPath = Root + "/SmokeBank.prefab";
        private const string RigPath = Root + "/AtmosphereRig.prefab";
        private const string SmokeTexturePath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Textures/Smoke.png";

        private const string SkyShader = "HybridShooter/Atmosphere/Alien Night Sky";
        private const string BeaconShader = "HybridShooter/Atmosphere/Supply Beacon (No Fog)";
        private const string SmokeShader = "Universal Render Pipeline/Particles/Unlit";

        private const string Title = "외계 대기 적용";

        private sealed class AtmosphereAssets
        {
            public AtmospherePreset Preset;
            public Material Sky;
            public GameObject Rig;
            public GameObject SmokeBank;
        }

        [MenuItem("Tools/HybridShooter/Atmosphere/현재 씬에 외계 대기 적용 (청회색 안개)")]
        private static void ApplyToCurrentScene()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(Title, "플레이 모드를 끄고 실행하세요.", "확인");
                return;
            }
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog(Title, "씬을 먼저 저장하세요. (씬 전용 Lighting 에셋 이름에 씬 이름을 쓴다)", "확인");
                return;
            }

            var assets = EnsureAssets(rebuild: false);
            if (assets == null) return;

            ApplyRenderSettings(assets.Preset, assets.Sky);
            ApplySun(assets.Preset);
            EnsureRigInScene(assets.Rig, scene);
            AssignSceneLightingSettings(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            Lightmapping.BakeAsync();
            Debug.Log($"[Atmosphere] '{scene.name}' 에 외계 대기를 적용했습니다. 환경광·반사를 다시 계산합니다 (Generate Lighting). 씬을 저장하세요.");
        }

        [MenuItem("Tools/HybridShooter/Atmosphere/씬 뷰 중심에 연기 덩어리 배치")]
        private static void PlaceSmokeBank()
        {
            var assets = EnsureAssets(rebuild: false);
            if (assets == null) return;

            var sceneView = SceneView.lastActiveSceneView;
            Vector3 position = sceneView != null ? sceneView.pivot : Vector3.zero;
            // 씬 뷰 중심 아래 지면에 붙인다
            if (Physics.Raycast(position + Vector3.up * 200f, Vector3.down, out var hit, 500f)) position = hit.point;

            var parent = GameObject.Find("SmokeBanks");
            if (parent == null || parent.scene != SceneManager.GetActiveScene())
            {
                parent = new GameObject("SmokeBanks");
                Undo.RegisterCreatedObjectUndo(parent, "Create SmokeBanks");
            }

            var bank = (GameObject)PrefabUtility.InstantiatePrefab(assets.SmokeBank, parent.transform);
            bank.transform.position = position;
            Undo.RegisterCreatedObjectUndo(bank, "Place Smoke Bank");
            Selection.activeGameObject = bank;
        }

        [MenuItem("Tools/HybridShooter/Atmosphere/에셋 기본값으로 다시 만들기 (볼륨·연기·리그 덮어쓰기)")]
        private static void RebuildAssets()
        {
            if (EditorUtility.DisplayDialog(Title,
                    "Volume Profile, 연기 프리팹, AtmosphereRig 프리팹을 기본값으로 덮어씁니다.\n직접 조정한 값은 사라집니다. (프리셋은 유지)",
                    "덮어쓰기", "취소") == false) return;
            if (EnsureAssets(rebuild: true) != null) Debug.Log("[Atmosphere] 에셋을 기본값으로 다시 만들었습니다.");
        }

        // ───────────────────────── 에셋 준비 ─────────────────────────

        /// <summary>필요한 에셋을 만든다. rebuild 가 false 면 이미 있는 에셋(프로파일·프리팹)은 그대로 둔다. 재질 값은 매번 프리셋으로 갱신</summary>
        private static AtmosphereAssets EnsureAssets(bool rebuild)
        {
            var skyShader = Shader.Find(SkyShader);
            var beaconShader = Shader.Find(BeaconShader);
            var smokeShader = Shader.Find(SmokeShader);
            if (skyShader == null || beaconShader == null || smokeShader == null)
            {
                EditorUtility.DisplayDialog(Title, $"셰이더를 찾지 못했습니다.\n{SkyShader}\n{BeaconShader}\n{SmokeShader}\n(에셋 임포트가 끝난 뒤 다시 실행하세요)", "확인");
                return null;
            }

            EnsureFolder(Root);
            EnsureFolder(MaterialFolder);
            EnsureFolder(LightingFolder);

            var preset = AssetDatabase.LoadAssetAtPath<AtmospherePreset>(PresetPath);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<AtmospherePreset>();
                AssetDatabase.CreateAsset(preset, PresetPath);
            }

            var sky = LoadOrCreateMaterial(SkyMaterialPath, skyShader);
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

            var beacon = LoadOrCreateMaterial(BeaconMaterialPath, beaconShader);

            var smoke = LoadOrCreateMaterial(SmokeMaterialPath, smokeShader);
            ConfigureSmokeMaterial(smoke, preset.smokeColor);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
                FillVolumeProfile(profile);
            }
            else if (rebuild)
            {
                FillVolumeProfile(profile);
            }

            var ambientSmoke = AssetDatabase.LoadAssetAtPath<GameObject>(AmbientSmokePath);
            if (ambientSmoke == null || rebuild) ambientSmoke = SavePrefab(BuildAmbientSmoke(smoke), AmbientSmokePath);

            var smokeBank = AssetDatabase.LoadAssetAtPath<GameObject>(SmokeBankPath);
            if (smokeBank == null || rebuild) smokeBank = SavePrefab(BuildSmokeBank(smoke), SmokeBankPath);

            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
            if (rig == null || rebuild) rig = SavePrefab(BuildRig(profile, beacon, ambientSmoke), RigPath);

            AssetDatabase.SaveAssets();
            return new AtmosphereAssets { Preset = preset, Sky = sky, Rig = rig, SmokeBank = smokeBank };
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        private static Material LoadOrCreateMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            return material;
        }

        private static GameObject SavePrefab(GameObject temp, string path)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(temp, path); // 같은 경로면 GUID 유지 → 씬 연결 그대로
            Object.DestroyImmediate(temp);
            return prefab;
        }

        /// <summary>
        /// 연기 재질: URP Particles/Unlit, 알파 블렌드.
        ///  - Soft Particles: 지면과 만나는 부분이 칼같이 잘리지 않게
        ///  - Camera Fading: 카메라 바로 앞 연기는 사라져 시야를 가리지 않게 (4m 이내 투명 ~ 14m 부터 원래 진하기)
        ///  - 안개는 셰이더 기본값대로 받는다 → 먼 연기는 안개 색에 자연스럽게 섞인다
        /// </summary>
        private static void ConfigureSmokeMaterial(Material material, Color color)
        {
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(SmokeTexturePath));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 0f);   // Alpha
            material.SetFloat("_ColorMode", 0f);
            material.SetFloat("_SoftParticlesEnabled", 1f);
            material.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            material.SetFloat("_SoftParticlesFarFadeDistance", 3f);
            material.SetFloat("_CameraFadingEnabled", 1f);
            material.SetFloat("_CameraNearFadeDistance", 4f);
            material.SetFloat("_CameraFarFadeDistance", 14f);
            BaseShaderGUI.SetMaterialKeywords(material, null, ParticleGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
        }

        // ───────────────────────── 포스트 프로세싱 ─────────────────────────

        /// <summary>
        /// 청회색 안개 톤 포스트 프로세싱.
        ///  - ACES 톤매핑, 채도↓ 대비↑ 로 탁하고 무거운 공기
        ///  - Split Toning: 그림자는 차가운 청록, 밝은 곳은 옅은 보라 → 달빛·성운 톤. 보급·화염은 HDR Bloom 으로 따뜻하게 튄다
        ///  - Bloom Threshold 1.1: HDR 로 빛나는 것(보급 빛기둥·총구 화염·발광부)만 번진다
        /// </summary>
        private static void FillVolumeProfile(VolumeProfile profile)
        {
            foreach (var component in profile.components) Object.DestroyImmediate(component, true);
            profile.components.Clear();

            var tonemapping = AddComponent<Tonemapping>(profile);
            tonemapping.mode.Override(TonemappingMode.ACES);

            var colorAdjustments = AddComponent<ColorAdjustments>(profile);
            colorAdjustments.postExposure.Override(0.15f);
            colorAdjustments.contrast.Override(15f);
            colorAdjustments.saturation.Override(-15f);

            var splitToning = AddComponent<SplitToning>(profile);
            splitToning.shadows.Override(new Color(0.35f, 0.5f, 0.55f));
            splitToning.highlights.Override(new Color(0.68f, 0.64f, 0.78f));
            splitToning.balance.Override(-20f);

            var bloom = AddComponent<Bloom>(profile);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.8f);
            bloom.scatter.Override(0.65f);

            var vignette = AddComponent<Vignette>(profile);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.45f);

            var filmGrain = AddComponent<FilmGrain>(profile);
            filmGrain.type.Override(FilmGrainLookup.Medium1);
            filmGrain.intensity.Override(0.15f);
            filmGrain.response.Override(0.8f);

            EditorUtility.SetDirty(profile);
        }

        private static T AddComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            var component = profile.Add<T>();
            component.name = typeof(T).Name;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        // ───────────────────────── 연기 파티클 ─────────────────────────

        /// <summary>
        /// 카메라를 따라다니며 25~55m 고리 모양으로 큰 연기 덩어리를 깔아 둔다.
        /// 월드 공간 시뮬레이션이라 이미 생긴 연기는 제자리에 남는다 → 어디서든 중거리가 연기로 막혀 보인다.
        /// </summary>
        private static GameObject BuildAmbientSmoke(Material material)
        {
            var go = new GameObject("AmbientSmoke");
            go.AddComponent<FollowCameraGround>();
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(16f, 24f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(10f, 18f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.6f), Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 160;

            var emission = ps.emission;
            emission.rateOverTime = 7f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 55f;
            shape.radiusThickness = 0.55f; // 바깥쪽 55% 에서만 → 약 25~55m
            shape.rotation = new Vector3(-90f, 0f, 0f); // XY 평면 → 지면(XZ)
            shape.randomPositionAmount = 2f;

            ApplyCommonSmokeModules(ps, new Vector3(0.4f, 0.05f, 0.2f));
            SetupRenderer(go, material);
            return go;
        }

        /// <summary>몬스터 스폰 지점 등에 직접 놓는 짙은 연기 덩어리 (24 × 24m)</summary>
        private static GameObject BuildSmokeBank(Material material)
        {
            var go = new GameObject("SmokeBank");
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 10f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.7f), Color.white);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;

            var emission = ps.emission;
            emission.rateOverTime = 4f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(24f, 3f, 24f);
            shape.position = new Vector3(0f, 1.5f, 0f);

            ApplyCommonSmokeModules(ps, new Vector3(0.2f, 0.1f, 0.1f));
            SetupRenderer(go, material);
            return go;
        }

        /// <summary>바람 흐름 · 천천히 나타났다 사라짐 · 조금씩 커짐 · 느린 회전</summary>
        private static void ApplyCommonSmokeModules(ParticleSystem ps, Vector3 wind)
        {
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(wind.x);
            velocity.y = new ParticleSystem.MinMaxCurve(wind.y);
            velocity.z = new ParticleSystem.MinMaxCurve(wind.z);

            var alpha = new Gradient();
            alpha.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            var color = ps.colorOverLifetime;
            color.enabled = true;
            color.color = new ParticleSystem.MinMaxGradient(alpha);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.2f));

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.08f, 0.08f);
        }

        private static void SetupRenderer(GameObject go, Material material)
        {
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.maxParticleSize = 3f; // 화면 비율. 기본 0.5 면 가까운 큰 연기가 잘려 작아 보인다
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // ───────────────────────── 리그 프리팹 ─────────────────────────

        /// <summary>씬에 하나만 두는 대기 묶음: Global Volume + 카메라 포스트 프로세싱 + 보급 빛기둥 교체 + 주변 연기</summary>
        private static GameObject BuildRig(VolumeProfile profile, Material beaconMaterial, GameObject ambientSmoke)
        {
            var rig = new GameObject("AtmosphereRig");
            rig.AddComponent<CameraPostProcessSwitch>();

            var beaconOverride = rig.AddComponent<SupplyBeaconOverride>();
            var serialized = new SerializedObject(beaconOverride);
            serialized.FindProperty("beaconMaterial").objectReferenceValue = beaconMaterial;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var volumeGo = new GameObject("GlobalVolume");
            volumeGo.transform.SetParent(rig.transform, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            var smoke = (GameObject)PrefabUtility.InstantiatePrefab(ambientSmoke);
            smoke.transform.SetParent(rig.transform, false);
            return rig;
        }

        // ───────────────────────── 씬 적용 ─────────────────────────

        /// <summary>안개·밤하늘·환경광 (씬마다 따로 저장되는 RenderSettings)</summary>
        private static void ApplyRenderSettings(AtmospherePreset preset, Material sky)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = preset.fogColor;
            RenderSettings.fogDensity = preset.fogDensity;

            RenderSettings.skybox = sky;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = preset.ambientSkyColor;
            RenderSettings.ambientEquatorColor = preset.ambientEquatorColor;
            RenderSettings.ambientGroundColor = preset.ambientGroundColor;

            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = preset.reflectionIntensity;
        }

        /// <summary>씬의 달빛 = RenderSettings.sun (없으면 첫 Directional Light). 하늘의 달은 이 빛의 방향에 그려진다</summary>
        private static void ApplySun(AtmospherePreset preset)
        {
            var sun = RenderSettings.sun;
            if (sun == null)
            {
                foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (light.type != LightType.Directional) continue;
                    sun = light;
                    break;
                }
            }
            if (sun == null)
            {
                Debug.LogWarning("[Atmosphere] Directional Light 가 없어 태양 설정을 건너뜁니다.");
                return;
            }

            Undo.RecordObjects(new Object[] { sun, sun.transform }, "Apply Atmosphere Moonlight");
            sun.useColorTemperature = false;
            sun.color = preset.moonLightColor;
            sun.intensity = preset.moonLightIntensity;
            sun.transform.rotation = Quaternion.Euler(preset.moonLightRotation);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = preset.moonLightShadowStrength;
            RenderSettings.sun = sun;
        }

        /// <summary>AtmosphereRig 프리팹 인스턴스가 씬에 없으면 배치</summary>
        private static void EnsureRigInScene(GameObject rigPrefab, Scene scene)
        {
            foreach (var existing in Object.FindObjectsByType<CameraPostProcessSwitch>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (existing.gameObject.scene != scene) continue;
                if (PrefabUtility.GetCorrespondingObjectFromSource(existing.gameObject) == rigPrefab) return;
            }

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            Undo.RegisterCreatedObjectUndo(rig, "Place AtmosphereRig");
        }

        /// <summary>
        /// 씬 전용 Lighting Settings 연결. 지금은 여러 씬이 Creepy_Cat 예제 설정·데이터를 같이 쓰고 있어서
        /// 그대로 굽으면 다른 씬에 영향이 갈 수 있다. 라이트맵(GI) 은 끄고 환경광·반사만 계산한다 (안개가 먼 곳을 가리므로 충분)
        /// </summary>
        private static void AssignSceneLightingSettings(Scene scene)
        {
            string path = $"{LightingFolder}/{scene.name}_Atmosphere.lighting";
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(path);
            if (settings == null)
            {
                settings = new LightingSettings
                {
                    name = $"{scene.name}_Atmosphere",
                    bakedGI = false,
                    realtimeGI = false,
                };
                AssetDatabase.CreateAsset(settings, path);
            }
            Lightmapping.lightingSettings = settings;
        }
    }
}
