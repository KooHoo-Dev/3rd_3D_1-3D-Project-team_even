using System.IO;
using System.Linq;
using HybridShooter.Audio;
using HybridShooter.Audio.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using static HybridShooter.UI.Options.Editor.OptionsPopupBuilder;

namespace HybridShooter.UI.Intro.Editor
{
    /// <summary>
    /// 인트로 씬 생성. 상단 메뉴 Tools/Intro/Rebuild Intro Scene 또는 batchmode
    /// (-executeMethod HybridShooter.UI.Intro.Editor.IntroSceneBuilder.RebuildBatch). 다시 실행하면 씬을 새로 만든다 (문구 에셋은 유지).
    /// 배치 (카메라는 원점, 처음엔 정면을 보고 크롤이 끝나면 아래로 tiltAngle 만큼 숙인다)
    ///  - 크롤: 화면 아래 끝(시야 아래 경계 위 6m)을 원점으로, 바닥 쪽으로 눕힌(X 63°) 월드 캔버스. 1px = 0.01m
    ///    → 글이 멀어질수록 위로 올라가며 작아지고, 소실점(27°)은 화면 위 경계(25°) 바깥이라 그 전에 알파로 사라진다
    ///  - 행성: 아래 75° 방향 300m 에 반지름 200m → 크롤 동안은 시야 밖, 카메라를 45° 숙이면 화면 아래 3/4 를 채운다
    ///  - 빛: 왼쪽에서 비춰 행성 왼쪽만 밝고 오른쪽 밤 면에 혈관이 빛난다
    ///  - 우주선: 카메라 뒤·위에서 숙인 시선 방향으로 내려가다 행성 위 약 70m 에서 격추 → 표면으로 추락
    /// 폰트: 크롤·도입 문구는 Orbit(한글 SF 서체, OFL), 로고·영문 안내는 Aldrich
    /// </summary>
    public static class IntroSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MS/IntroCrawl.unity";
        private const string Root = "Assets/Prefabs/Intro/";
        private const string TextPath = Root + "IntroCrawlText.asset";
        private const string MaterialDir = Root + "Materials/";
        private const string ShaderDir = Root + "Shaders/";
        private const string PlanetMeshPath = Root + "PlanetSphere.asset";
        private const string VolumeProfilePath = "Assets/Settings/IntroVolumeProfile.asset";
        private const string OrbitFontPath = "Assets/ExternalAssets/Fonts/Orbit/Orbit-Regular.ttf";
        private const string OrbitAssetPath = "Assets/ExternalAssets/Fonts/Orbit/Orbit-Regular SDF.asset";
        private const string LogoFontPath = "Assets/ExternalAssets/SCI-FI UI Pack Pro/Common/Fonts/Aldrich-Regular SDF.asset";
        private const string ShipPrefabPath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Prefabs/_Spaceship_01/Spaceship_01.prefab";

        private const string SkipLabel = "HOLD TO SKIP";
        private const string SkipHint = "아무 키나 길게 눌러 건너뛰기";

        // ── 색 ──
        /// <summary>크롤 글자: 따뜻한 금색 (머티리얼에서 HDR 로 조금 더 밝혀 은은하게 번진다)</summary>
        private static readonly Color CrawlGold = new Color(1f, 0.83f, 0.34f);
        /// <summary>도입 문구: 차가운 하늘색</summary>
        private static readonly Color OpeningBlue = new Color(0.33f, 0.72f, 0.95f);
        private static readonly Color Steel = new Color(0.6f, 0.68f, 0.78f);

        // ── 카메라·크롤 ──
        private const float FieldOfView = 50f;
        private const float CrawlTilt = 63f;
        /// <summary>시야 아래 경계를 따라 이 거리에 크롤 원점</summary>
        private const float CrawlDistance = 6f;
        private const float CrawlWidth = 760f;
        private const float CrawlFontSize = 38f;
        private const float TiltAngle = 45f;

        // ── 행성·빛·우주선 ──
        private const float PlanetPitch = 75f;
        private const float PlanetDistance = 300f;
        private const float PlanetRadius = 200f;
        private static readonly Vector3 SunDirection = new Vector3(1f, -0.35f, -0.25f);
        private const float ShipLength = 1.6f;
        private const int FlakCount = 12;
        private const int TrailPoints = 36;

        [MenuItem("Tools/Intro/Rebuild Intro Scene")]
        public static void Rebuild()
        {
            if (Application.isBatchMode == false && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;

            // 씬을 먼저 만든다. NewScene(Single) 은 씬에서 참조하지 않는 에셋을 언로드해, 그 전에 읽어 둔 문구 에셋·폰트가 null 이 된다
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (LoadFonts() == false) return; // OptionsPopupBuilder 의 Aldrich·한글 폰트 (CreateText 가 쓴다)

            EnsureFolder(Root.TrimEnd('/'));
            EnsureFolder(MaterialDir.TrimEnd('/'));
            EnsureFolder(Path.GetDirectoryName(ScenePath));

            var text = LoadOrCreateText();
            var orbit = LoadOrCreateOrbit(text.AllCharacters() + SkipHint);
            var logoFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LogoFontPath);
            if (orbit == null || logoFont == null)
            {
                Debug.LogError("[IntroSceneBuilder] 폰트를 만들거나 읽지 못했습니다");
                return;
            }

            var camera = CreateCamera();
            CreateEnvironment();
            var sound = new GameObject("SoundService", typeof(SoundService));
            SoundServiceMenu.AssignMixer(sound.GetComponent<SoundService>());

            var (crawlRect, crawlText) = CreateCrawl(camera, orbit);
            var logo = CreateLogo(logoFont);
            var planet = CreatePlanet();
            var shipCut = CreateShipCut(planet);
            var overlay = CreateOverlay(orbit);

            var director = new GameObject("Intro Director").AddComponent<IntroCrawlDirector>();
            var so = new SerializedObject(director);
            Set(so, "content", text);
            Set(so, "viewCamera", camera.transform);
            Set(so, "openingGroup", overlay.OpeningGroup);
            Set(so, "openingText", overlay.OpeningText);
            Set(so, "logoText", logo);
            Set(so, "crawlRect", crawlRect);
            Set(so, "crawlText", crawlText);
            Set(so, "blackout", overlay.Blackout);
            Set(so, "flash", overlay.Flash);
            Set(so, "shipCut", shipCut);
            Set(so, "skipGroup", overlay.SkipGroup);
            Set(so, "skipBar", overlay.SkipBar);
            so.FindProperty("tiltAngle").floatValue = TiltAngle;
            so.ApplyModifiedPropertiesWithoutUndo();
            director.Evaluate(0f); // 씬을 열었을 때 첫 화면(검은 화면) 상태로 저장

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[IntroSceneBuilder] {ScenePath} 를 만들었습니다");
        }

        /// <summary>batchmode 용</summary>
        public static void RebuildBatch() => Rebuild();

        // ───────────────────────── 에셋 ─────────────────────────

        private static IntroCrawlText LoadOrCreateText()
        {
            var text = AssetDatabase.LoadAssetAtPath<IntroCrawlText>(TextPath);
            if (text != null) return text;
            text = ScriptableObject.CreateInstance<IntroCrawlText>();
            AssetDatabase.CreateAsset(text, TextPath);
            return text;
        }

        /// <summary>Orbit 동적 SDF 폰트. 문구에 쓰인 글자는 미리 아틀라스에 굽는다 (빌드에서 첫 프레임 끊김 방지)</summary>
        private static TMP_FontAsset LoadOrCreateOrbit(string characters)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OrbitAssetPath);
            if (asset == null)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(OrbitFontPath);
                if (font == null)
                {
                    Debug.LogError($"[IntroSceneBuilder] 폰트 파일이 없습니다: {OrbitFontPath}");
                    return null;
                }
                asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 4096, 4096, AtlasPopulationMode.Dynamic, true);
                asset.name = "Orbit-Regular SDF";
                AssetDatabase.CreateAsset(asset, OrbitAssetPath);
                asset.atlasTextures[0].name = asset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
                asset.material.name = asset.name + " Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }

            const string ascii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~…—·";
            if (asset.TryAddCharacters(ascii + characters, out string missing) == false && string.IsNullOrEmpty(missing) == false)
                Debug.LogWarning($"[IntroSceneBuilder] Orbit 에 없는 글자: {missing}");
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static Material SaveMaterial(string name, Shader shader)
        {
            string path = MaterialDir + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>TMP 폰트 머티리얼 복사본 (얼굴색을 HDR 로 밝혀 Bloom 이 살짝 번지게)</summary>
        private static Material FontMaterial(string name, TMP_FontAsset font, float faceIntensity)
        {
            var material = SaveMaterial(name, font.material.shader);
            material.CopyPropertiesFromMaterial(font.material);
            material.SetColor(ShaderUtilities.ID_FaceColor, Color.white * faceIntensity);
            return material;
        }

        private static Material GlowMaterial(string name, Color color, bool radial, float softness)
        {
            var material = SaveMaterial(name, LoadShader("AdditiveGlow.shader"));
            material.SetColor("_Color", color);
            material.SetFloat("_Radial", radial ? 1f : 0f);
            material.SetFloat("_Softness", softness);
            return material;
        }

        private static Shader LoadShader(string file)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + file);
            if (shader == null) throw new FileNotFoundException("[IntroSceneBuilder] 셰이더가 없습니다", ShaderDir + file);
            return shader;
        }

        /// <summary>행성용 고해상도 UV 구 (반지름 0.5). 기본 Sphere 는 화면을 채우면 테두리가 각져 보인다</summary>
        private static Mesh LoadOrCreatePlanetMesh()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(PlanetMeshPath);
            if (mesh != null) return mesh;

            const int lon = 192, lat = 96;
            var vertices = new Vector3[(lon + 1) * (lat + 1)];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            for (int y = 0; y <= lat; y++)
            {
                float v = y / (float)lat;
                float theta = v * Mathf.PI;
                for (int x = 0; x <= lon; x++)
                {
                    float u = x / (float)lon;
                    float phi = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    int i = y * (lon + 1) + x;
                    vertices[i] = n * 0.5f;
                    normals[i] = n;
                    uvs[i] = new Vector2(u, 1f - v);
                }
            }
            var triangles = new int[lon * lat * 6];
            int t = 0;
            for (int y = 0; y < lat; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = y * (lon + 1) + x;
                int b = a + lon + 1;
                triangles[t++] = a; triangles[t++] = a + 1; triangles[t++] = b;
                triangles[t++] = a + 1; triangles[t++] = b + 1; triangles[t++] = b;
            }
            mesh = new Mesh { name = "PlanetSphere", vertices = vertices, normals = normals, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, PlanetMeshPath);
            return mesh;
        }

        // ───────────────────────── 카메라·환경 ─────────────────────────

        private static Camera CreateCamera()
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 2000f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            return camera;
        }

        private static void CreateEnvironment()
        {
            var sky = SaveMaterial("DeepSpaceSky", LoadShader("DeepSpaceSky.shader"));
            sky.SetFloat("_NebulaIntensity", 0.5f); // 성운이 크롤 글자보다 튀지 않게
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.05f, 0.055f, 0.08f);
            RenderSettings.fog = false;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.4f;

            var sun = new GameObject("Star Light", typeof(Light));
            var light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.94f, 0.88f);
            light.intensity = 1.6f;
            light.shadows = LightShadows.None;
            sun.transform.rotation = Quaternion.LookRotation(SunDirection.normalized);
            RenderSettings.sun = light;

            CreateVolume();
        }

        /// <summary>ACES + HDR 만 번지는 Bloom (글자·혈관·폭발·엔진) + 비네트·옅은 그레인. 프로필은 다시 만들 때마다 새로 쓴다</summary>
        private static void CreateVolume()
        {
            AssetDatabase.DeleteAsset(VolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumeProfilePath);

            AddOverride<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            var color = AddOverride<ColorAdjustments>(profile);
            color.contrast.Override(12f);
            var bloom = AddOverride<Bloom>(profile);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.7f);
            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.3f);
            vignette.smoothness.Override(0.5f);
            var grain = AddOverride<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.12f);
            AssetDatabase.SaveAssets();

            var volume = new GameObject("Global Volume", typeof(Volume)).GetComponent<Volume>();
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

        // ───────────────────────── 크롤·로고 ─────────────────────────

        private static (RectTransform Rect, TMP_Text Text) CreateCrawl(Camera camera, TMP_FontAsset font)
        {
            var go = new GameObject("Crawl Canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(1000f, 4000f);
            rect.localScale = Vector3.one * 0.01f;
            // 원점 = 시야 아래 경계 위의 점 → 글이 화면 아래 끝에서 올라온다
            rect.position = Quaternion.Euler(FieldOfView * 0.5f, 0f, 0f) * Vector3.forward * CrawlDistance;
            rect.rotation = Quaternion.Euler(CrawlTilt, 0f, 0f);

            var text = CreateText(rect, "Crawl Text", "", font, CrawlFontSize, CrawlGold, TextAlignmentOptions.Top);
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.lineSpacing = 14f;
            text.fontSharedMaterial = FontMaterial("Orbit Crawl", font, 1.5f);
            Place(text.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(CrawlWidth, 200f), new Vector2(0.5f, 1f));
            return (text.rectTransform, text);
        }

        /// <summary>큰 로고 (3D TextMeshPro). 감독이 z 를 밀어 멀어지게 한다</summary>
        private static TMP_Text CreateLogo(TMP_FontAsset font)
        {
            var go = new GameObject("Logo", typeof(RectTransform), typeof(TextMeshPro));
            var text = go.GetComponent<TextMeshPro>();
            text.font = font;
            text.fontSharedMaterial = FontMaterial("Aldrich Logo", font, 1.25f);
            text.color = CrawlGold;
            text.alignment = TextAlignmentOptions.Center;
            text.characterSpacing = 8f;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.enableAutoSizing = true;
            text.fontSizeMin = 1f;
            text.fontSizeMax = 60f;
            text.text = "LOGO";
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = new Vector2(5f, 1.4f);
            rect.localPosition = new Vector3(0f, 0.1f, 3.2f);
            return text;
        }

        // ───────────────────────── 행성·우주선 ─────────────────────────

        private static Vector3 PlanetCenter => Quaternion.Euler(PlanetPitch, 0f, 0f) * Vector3.forward * PlanetDistance;

        private static Transform CreatePlanet()
        {
            // 용암처럼 보이지 않게: 어두운 자줏빛 살갗 + 가늘고 촘촘한 혈관 (밤 쪽에서 더 빛난다)
            var material = SaveMaterial("HivePlanet", LoadShader("HivePlanet.shader"));
            material.SetColor("_BaseColor", new Color(0.045f, 0.02f, 0.045f));
            material.SetColor("_FleshColor", new Color(0.26f, 0.08f, 0.15f));
            material.SetColor("_VeinColor", new Color(2.4f, 0.32f, 0.28f));
            material.SetFloat("_VeinScale", 9f);
            material.SetFloat("_VeinWidth", 0.045f);
            var go = new GameObject("Hive Planet", typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = LoadOrCreatePlanetMesh();
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.transform.position = PlanetCenter;
            go.transform.localScale = Vector3.one * (PlanetRadius * 2f);
            return go.transform;
        }

        private static IntroShipCut CreateShipCut(Transform planet)
        {
            var root = new GameObject("Ship Cut");
            var ship = new GameObject("Ship").transform;
            ship.SetParent(root.transform, false);
            AddShipModel(ship);

            var engine = CreateLine(root.transform, "Engine Glow", GlowMaterial("Glow Engine", new Color(0.6f, 1.5f, 3f), false, 3f), 2,
                new AnimationCurve(new Keyframe(0f, 0.16f), new Keyframe(1f, 0f)),
                MakeGradient((Color.white, 0f, 1f), (new Color(0.6f, 0.8f, 1f), 1f, 0f)));
            var trail = CreateLine(root.transform, "Fire Trail", GlowMaterial("Glow Fire", new Color(3f, 3f, 3f), false, 2.5f), TrailPoints,
                new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.15f, 1.3f), new Keyframe(1f, 0.15f)),
                MakeGradient((new Color(1f, 0.85f, 0.5f), 0f, 1f), (new Color(1f, 0.42f, 0.1f), 0.25f, 0.85f),
                         (new Color(0.55f, 0.1f, 0.05f), 0.65f, 0.4f), (new Color(0.2f, 0.05f, 0.05f), 1f, 0f)));

            var flakMaterial = GlowMaterial("Glow Flak", new Color(6f, 1.1f, 0.35f), false, 3f);
            var flakRoot = new GameObject("Bio Flak").transform;
            flakRoot.SetParent(root.transform, false);
            var flak = new LineRenderer[FlakCount];
            for (int i = 0; i < FlakCount; i++)
                flak[i] = CreateLine(flakRoot, $"Bolt {i}", flakMaterial, 2,
                    new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 1.1f)),
                    MakeGradient((new Color(1f, 0.3f, 0.15f), 0f, 0f), (new Color(1f, 0.75f, 0.5f), 1f, 1f)));

            var explosion = GameObject.CreatePrimitive(PrimitiveType.Quad);
            explosion.name = "Explosion";
            Object.DestroyImmediate(explosion.GetComponent<Collider>());
            explosion.transform.SetParent(root.transform, false);
            var explosionRenderer = explosion.GetComponent<MeshRenderer>();
            explosionRenderer.sharedMaterial = GlowMaterial("Glow Explosion", Color.white, true, 3f);
            explosionRenderer.shadowCastingMode = ShadowCastingMode.Off;

            var light = new GameObject("Explosion Light", typeof(Light)).GetComponent<Light>();
            light.transform.SetParent(root.transform, false);
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.25f);
            light.range = 40f;

            // 경로: 숙인 시선 방향을 따라 내려가다 행성 위에서 격추 → 표면 쪽으로 추락
            var forward = Quaternion.Euler(TiltAngle, 0f, 0f) * Vector3.forward;
            var path = new[]
            {
                new Vector3(1.2f, 1.2f, 0.8f),
                new Vector3(0.6f, -1.5f, 5f),
                forward * 22f + new Vector3(-2f, 1.5f, 0f),
                forward * 38f + new Vector3(-3f, 2f, 0f),
            };
            var crash = PlanetCenter + (path[3] + new Vector3(6f, 0f, 25f) - PlanetCenter).normalized * (PlanetRadius + 3f);

            var cut = root.AddComponent<IntroShipCut>();
            var so = new SerializedObject(cut);
            Set(so, "ship", ship);
            Set(so, "engineGlow", engine);
            Set(so, "fireTrail", trail);
            Set(so, "explosion", explosionRenderer);
            Set(so, "explosionLight", light);
            Set(so, "planet", planet);
            so.FindProperty("planetRadius").floatValue = PlanetRadius;
            var pathProperty = so.FindProperty("approachPath");
            pathProperty.arraySize = path.Length;
            for (int i = 0; i < path.Length; i++) pathProperty.GetArrayElementAtIndex(i).vector3Value = path[i];
            so.FindProperty("crashPoint").vector3Value = crash;
            var flakProperty = so.FindProperty("flak");
            flakProperty.arraySize = flak.Length;
            for (int i = 0; i < flak.Length; i++) flakProperty.GetArrayElementAtIndex(i).objectReferenceValue = flak[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            return cut;
        }

        /// <summary>팩 우주선을 가장 긴 변(날개폭)이 ShipLength 가 되게 맞춰 가운데에 둔다. 모델은 조종석이 +Z (진행 방향), 엔진이 -Z</summary>
        private static void AddShipModel(Transform ship)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShipPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[IntroSceneBuilder] 우주선 프리팹이 없습니다: {ShipPrefabPath}");
                return;
            }
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, ship);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);

            var bounds = ModelBounds(model);
            float scale = ShipLength / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            model.transform.localScale *= scale;
            bounds = ModelBounds(model);
            model.transform.position -= bounds.center - ship.position;
        }

        private static Bounds ModelBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        private static LineRenderer CreateLine(Transform parent, string name, Material material, int points, AnimationCurve width, Gradient color)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = material;
            line.positionCount = points;
            line.useWorldSpace = true;
            line.widthCurve = width;
            line.colorGradient = color;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private static Gradient MakeGradient(params (Color Color, float Time, float Alpha)[] keys)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                keys.Select(k => new GradientColorKey(k.Color, k.Time)).ToArray(),
                keys.Select(k => new GradientAlphaKey(k.Alpha, k.Time)).ToArray());
            return gradient;
        }

        // ───────────────────────── 화면 UI ─────────────────────────

        private struct Overlay
        {
            public Graphic Blackout;
            public CanvasGroup OpeningGroup;
            public TMP_Text OpeningText;
            public Graphic Flash;
            public CanvasGroup SkipGroup;
            public RectTransform SkipBar;
        }

        /// <summary>순서: 암전 → 도입 문구(암전 위) → 피격 섬광 → 건너뛰기 안내</summary>
        private static Overlay CreateOverlay(TMP_FontAsset orbit)
        {
            var canvas = CreateCanvas("Overlay");
            canvas.GetComponent<Canvas>().sortingOrder = 10;
            var overlay = new Overlay();

            overlay.Blackout = FullScreen(canvas, "Blackout", Color.black);

            var opening = CreateText(canvas, "Opening", "", orbit, 44f, OpeningBlue, TextAlignmentOptions.MidlineLeft);
            opening.lineSpacing = 18f;
            opening.characterSpacing = 4f;
            Place(opening.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-80f, 0f), new Vector2(1100f, 240f));
            overlay.OpeningText = opening;
            overlay.OpeningGroup = opening.gameObject.AddComponent<CanvasGroup>();
            overlay.OpeningGroup.alpha = 0f;

            overlay.Flash = FullScreen(canvas, "Flash", new Color(1f, 0.92f, 0.85f, 0f));

            var skip = NewRect("Skip", canvas);
            Place(skip.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-80f, 60f), new Vector2(420f, 80f), new Vector2(1f, 0f));
            overlay.SkipGroup = skip.gameObject.AddComponent<CanvasGroup>();
            overlay.SkipGroup.alpha = 0f;
            overlay.SkipGroup.interactable = false;
            overlay.SkipGroup.blocksRaycasts = false;

            var label = CreateText(skip, "Label", SkipLabel, TitleFont, 24f, Color.white, TextAlignmentOptions.BottomRight);
            label.characterSpacing = 12f;
            Place(label.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 34f), new Vector2(1f, 1f));
            var hint = CreateText(skip, "Hint", SkipHint, orbit, 18f, Steel, TextAlignmentOptions.TopRight);
            Place(hint.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(0f, 26f), new Vector2(1f, 0f));

            var track = Bar(skip, "Track", new Color(Steel.r, Steel.g, Steel.b, 0.35f));
            var fill = Bar(skip, "Fill", Color.white);
            fill.localScale = new Vector3(0f, 1f, 1f);
            overlay.SkipBar = fill;
            track.SetAsFirstSibling();
            return overlay;
        }

        private static Image FullScreen(RectTransform parent, string name, Color color)
        {
            var rect = NewRect(name, parent);
            Stretch(rect);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>건너뛰기 게이지 (오른쪽 정렬, 왼쪽 끝 기준으로 늘어난다)</summary>
        private static RectTransform Bar(RectTransform parent, string name, Color color)
        {
            var rect = NewRect(name, parent);
            Place(rect.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-260f, 0f), new Vector2(260f, 2f), new Vector2(0f, 0.5f));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        // ───────────────────────── 공통 ─────────────────────────

        private static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == ScenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string path)
        {
            path = path.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) == false) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
