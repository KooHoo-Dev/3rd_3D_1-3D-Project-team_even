using System.IO;
using System.Linq;
using HybridShooter.Audio;
using HybridShooter.Audio.Editor;
using HybridShooter.UI.MainMenu;
using HybridShooter.UI.Theme;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;
using UnityEngine.UI;
using static HybridShooter.UI.Options.Editor.OptionsPopupBuilder;

namespace HybridShooter.UI.Ending.Editor
{
    /// <summary>
    /// 엔딩 씬 생성. 상단 메뉴 Tools/Ending/Rebuild Ending Scene 또는 batchmode
    /// (-executeMethod HybridShooter.UI.Ending.Editor.EndingSceneBuilder.RebuildBatch). 다시 실행하면 씬·Timeline 을 새로 만든다 (크레딧 문구 에셋은 유지).
    /// 시각은 모두 EndingTimeline (BGM 마디). 만든 뒤 Timeline 창(Ending Director 선택)에서 키·컷 길이를 직접 다듬을 수 있다 —
    /// 다시 빌드하면 그 수정은 사라지므로, 굳힐 값은 이 파일의 상수로 옮긴다
    ///
    /// 장면 두 개를 Timeline 의 Activation 트랙이 번갈아 켠다
    ///  - Dawn Set (0 ~ 24초, 원점): 새벽 착륙장. 구조선(인트로와 같은 기체)이 착륙 패드 위에, 뒤쪽 배 아래로 경사로가 내려와 있다.
    ///    플레이어(메인 메뉴 Player_Display)가 경사로를 걸어 올라 해치 빛 속으로 → 경사로가 닫히고 점화(수직 분사·먼지) → 이륙해 기수를 들고 하늘로
    ///  - Orbit Set (24초 ~): 행성(인트로 HivePlanet, 핏줄을 어둡게 가라앉힘) 위 궤도. 구조선이 대기권에서 올라와 행성 너머 떠오르는 해 쪽으로 →
    ///    36초 도약(섬광) → 멀리서 본 행성 옆으로 크레딧. 크레딧 중 51초부터 금빛 균열 → 63초 폭발
    ///    (섬광 속에 행성이 사라지고 잔해 입자·충격파 고리·광원, EndingPlanetShatter 가 시각으로 계산)
    /// 카메라는 Cinemachine 트랙의 샷 6개 (넓게 → 뒤따라 → 낮게 → 멀리서 올려다봄 → 궤도 → 크레딧), 음악은 Audio 트랙(믹서 Bgm 그룹).
    /// 건너뛰기를 시도하면 EndingSkipPrank 의 창이 흔들리다 폭발한다 (건너뛰지 않는다)
    /// 글자 UI 는 UiTheme 색: 맺음 로고 HIVE PURGE(달빛 흰색) + 작은 TEAM EVEN + 붉은 선, 건너뛰기 게이지·창 윗띠 붉게
    /// </summary>
    public static class EndingSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/MS/Ending.unity";
        private const string Root = "Assets/Prefabs/Ending/";
        private const string TextPath = Root + "EndingCreditsText.asset";
        private const string TimelinePath = Root + "EndingTimeline.playable";
        private const string MaterialDir = Root + "Materials/";
        private const string SpaceVolumeProfilePath = "Assets/Settings/EndingSpaceVolumeProfile.asset";

        // 인트로 에셋 재사용 (행성 메시·셰이더·우주 하늘·Orbit 폰트)
        private const string IntroShaderDir = "Assets/Prefabs/Intro/Shaders/";
        private const string PlanetMeshPath = "Assets/Prefabs/Intro/PlanetSphere.asset";
        private const string DeepSpaceSkyPath = "Assets/Prefabs/Intro/Materials/DeepSpaceSky.mat";
        private const string OrbitAssetPath = "Assets/ExternalAssets/Fonts/Orbit/Orbit-Regular SDF.asset";
        // 메인 메뉴 에셋 재사용 (땅·바위·연기·플레이어)
        private const string GroundMaterialPath = "Assets/Materials/MainMenu/MainMenuGround.mat";
        private const string SmokeMaterialPath = "Assets/Materials/MainMenu/MainMenuFog.mat";
        private const string RockPath = "Assets/ExternalAssets/Creepy_Cat/_3D Scifi Kit Vol 2/Prefabs/_Exteriors/Rock_02.prefab";
        private const string PlayerDisplayPath = "Assets/Prefabs/MainMenu/Display/Player_Display.prefab";

        private const string MusicPath = "Assets/Resources/Audio/Bgm/Ending.wav";
        private const string MixerPath = "Assets/Resources/Audio/GameMixer.mixer";
        private const string NoiseDir = "Packages/com.unity.cinemachine/Presets/Noise/";
        private const string MainMenuScene = "Test_MainMenu";

        private const string SkipLabel = EndingSkipPrank.SkipQuestion;
        private const string SkipHint = "아무 키나 길게 눌러 건너뛰기";

        /// <summary>맺음 로고 (예전 금색 자리)</summary>
        private static readonly Color LogoColor = UiTheme.MoonWhite;
        private static readonly Color Steel = UiTheme.Steel;
        /// <summary>맺음 로고 아래 작은 팀 이름 (게임 이름은 에셋의 logo)</summary>
        private const string TeamText = "TEAM EVEN";

        // ── 착륙장 (Dawn Set, 구조선 패드 가운데가 원점, 구조선 기수 +Z) ──
        private const float ShipLength = 24f;
        /// <summary>착륙한 구조선 배 밑면 높이 (다리 길이)</summary>
        private const float RestHeight = 2f;
        private const float RampLength = 6.2f;
        private const float RampWidth = 2.6f;
        /// <summary>경사로 경첩 = 배 밑면에서 길이의 이 비율만큼 뒤쪽</summary>
        private const float HatchBack = 0.18f;
        private const float PadRadius = 14f;
        /// <summary>플레이어가 걷기 시작하는 곳 (경사로 아래 끝 기준)</summary>
        private static readonly Vector3 WalkFrom = new Vector3(-4.5f, 0f, -9.5f);
        private const float WalkStart = 0.8f;
        /// <summary>해 방향 (카메라에서 본 방위·고도) — 넓은 컷에서 구조선 오른쪽 뒤 지평선 위</summary>
        private const float SunYaw = 48f;
        private const float SunElevation = 7f;

        // ── 궤도 (Orbit Set) ──
        private static readonly Vector3 OrbitCamera = new Vector3(0f, 20f, -40f);
        private static readonly Vector3 PlanetCenter = new Vector3(0f, -600f, 400f);
        private const float PlanetRadius = 600f;
        private const float SunDistance = 2500f;
        private static readonly Vector3 CreditsCamera = new Vector3(-1100f, 450f, -2600f);

        // ── 행성 붕괴 (크레딧 중) ──
        /// <summary>균열에서 새는 빛: 붉은 핏줄과 대비되는 금빛·흰빛 (위협이 아니라 정화)</summary>
        private static readonly Color CrackColor = new Color(6f, 4.2f, 2.2f);

        /// <summary>Timeline 길이 (음악 꼬리까지)</summary>
        private const float Total = EndingTimeline.End + 6f;

        [MenuItem("Tools/Ending/Rebuild Ending Scene")]
        public static void Rebuild()
        {
            if (Application.isBatchMode == false && EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;

            // 씬을 먼저 만든다. NewScene(Single) 은 씬에서 참조하지 않는 에셋을 언로드해, 그 전에 읽어 둔 에셋이 null 이 된다
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (LoadFonts() == false) return;
            EndingDawnLook.EnsureFolder(MaterialDir.TrimEnd('/'));
            EndingDawnLook.EnsureFolder(Path.GetDirectoryName(ScenePath));

            var text = LoadOrCreateText();
            var orbit = LoadOrbit(text.AllCharacters() + SkipHint + EndingSkipPrank.AllCharacters);
            var music = Load<AudioClip>(MusicPath);
            var mixer = Load<AudioMixer>(MixerPath);

            var camera = CreateCamera();
            var sound = new GameObject("SoundService", typeof(SoundService));
            SoundServiceMenu.AssignMixer(sound.GetComponent<SoundService>());
            var musicSource = CreateMusicSource(mixer);

            var dawn = CreateDawnSet();
            var space = CreateOrbitSet();
            var ui = CreateOverlay(orbit, text);

            var go = new GameObject("Ending Director", typeof(PlayableDirector), typeof(EndingDirector));
            var director = go.GetComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.Hold;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            director.playableAsset = BuildTimeline(director, camera.GetComponent<CinemachineBrain>(), dawn, space, musicSource, music);

            var so = new SerializedObject(go.GetComponent<EndingDirector>());
            Set(so, "timeline", director);
            SetEnvironment(so, "dawn", dawn.Environment);
            SetEnvironment(so, "space", space.Environment);
            Set(so, "player", dawn.Player);
            so.FindProperty("walkValue").floatValue = dawn.WalkSpeed;
            so.FindProperty("walkStart").floatValue = WalkStart;
            Set(so, "credits", ui.Credits);
            Set(so, "planetShatter", space.Shatter);
            Set(so, "blackout", ui.Blackout);
            Set(so, "flash", ui.Flash);
            Set(so, "barTop", ui.BarTop);
            Set(so, "barBottom", ui.BarBottom);
            Set(so, "skipGroup", ui.SkipGroup);
            Set(so, "skipBar", ui.SkipBar);
            Set(so, "prank", ui.Prank);
            Set(so, "music", musicSource);
            so.FindProperty("mainMenuScene").stringValue = MainMenuScene;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 씬을 열었을 때 첫 화면(새벽)으로 보이게
            dawn.Environment.Apply();
            space.Root.SetActive(false);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log($"[EndingSceneBuilder] {ScenePath} 를 만들었습니다 (Timeline: {TimelinePath})");
        }

        /// <summary>batchmode 용</summary>
        public static void RebuildBatch() => Rebuild();

        // ───────────────────────── 에셋 ─────────────────────────

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new FileNotFoundException($"[EndingSceneBuilder] 에셋이 없습니다 ({typeof(T).Name})", path);
            return asset;
        }

        private static EndingCreditsText LoadOrCreateText()
        {
            var text = AssetDatabase.LoadAssetAtPath<EndingCreditsText>(TextPath);
            if (text != null) return text;
            EndingDawnLook.EnsureFolder(Root.TrimEnd('/'));
            text = ScriptableObject.CreateInstance<EndingCreditsText>();
            AssetDatabase.CreateAsset(text, TextPath);
            return text;
        }

        /// <summary>인트로의 Orbit 동적 SDF 폰트. 크레딧 글자를 미리 아틀라스에 굽는다 (빌드에서 첫 프레임 끊김 방지)</summary>
        private static TMP_FontAsset LoadOrbit(string characters)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(OrbitAssetPath);
            if (asset == null) throw new FileNotFoundException("[EndingSceneBuilder] Orbit 폰트가 없습니다. 먼저 Tools > Intro > Rebuild Intro Scene 을 실행해주세요", OrbitAssetPath);
            const string ascii = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~…—·";
            if (asset.TryAddCharacters(ascii + characters, out string missing) == false && string.IsNullOrEmpty(missing) == false)
                Debug.LogWarning($"[EndingSceneBuilder] Orbit 에 없는 글자: {missing}");
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

        private static Material GlowMaterial(string name, Color color, bool radial, float softness)
        {
            var shader = Load<Shader>(IntroShaderDir + "AdditiveGlow.shader");
            var material = SaveMaterial(name, shader);
            material.SetColor("_Color", color);
            material.SetFloat("_Radial", radial ? 1f : 0f);
            material.SetFloat("_Softness", softness);
            return material;
        }

        private static Material LitMaterial(string name, Color color, float metallic, float smoothness)
        {
            var material = SaveMaterial(name, Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        // ───────────────────────── 카메라·소리 ─────────────────────────

        private static Camera CreateCamera()
        {
            var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener), typeof(CinemachineBrain)) { tag = "MainCamera" };
            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 8000f;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            // 컷은 Timeline 샷 경계에서 바로 바뀐다
            go.GetComponent<CinemachineBrain>().DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            return camera;
        }

        /// <summary>Timeline 오디오 트랙이 재생하는 2D 소스. 믹서 Bgm 그룹으로 보내 옵션 BGM 볼륨을 따른다</summary>
        private static AudioSource CreateMusicSource(AudioMixer mixer)
        {
            var source = new GameObject("Ending Music", typeof(AudioSource)).GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            var group = mixer.FindMatchingGroups("Bgm").FirstOrDefault();
            if (group == null) throw new FileNotFoundException("[EndingSceneBuilder] GameMixer 에 Bgm 그룹이 없습니다", MixerPath);
            source.outputAudioMixerGroup = group;
            return source;
        }

        private static CinemachineCamera NewShot(Transform parent, string name, Vector3 position, float fov, float far = 1000f)
        {
            var go = new GameObject(name, typeof(CinemachineCamera));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var cm = go.GetComponent<CinemachineCamera>();
            cm.Priority = 0;
            var lens = cm.Lens;
            lens.FieldOfView = fov;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = far;
            cm.Lens = lens;
            return cm;
        }

        private static void LookAt(CinemachineCamera cm, Transform target, float damping)
        {
            cm.Target.LookAtTarget = target;
            cm.Target.CustomLookAtTarget = true;
            var composer = cm.gameObject.AddComponent<CinemachineRotationComposer>();
            composer.Damping = new Vector2(damping, damping);
            cm.transform.rotation = Quaternion.LookRotation(target.position - cm.transform.position);
        }

        private static void Noise(CinemachineCamera cm, string profile, float amplitude, float frequency)
        {
            var noise = cm.gameObject.AddComponent<CinemachineBasicMultiChannelPerlin>();
            noise.NoiseProfile = Load<NoiseSettings>(NoiseDir + profile + ".asset");
            noise.AmplitudeGain = amplitude;
            noise.FrequencyGain = frequency;
        }

        // ───────────────────────── 새벽 착륙장 ─────────────────────────

        private sealed class DawnSet
        {
            public GameObject Root;
            public Animator Animator;
            public SceneEnvironment Environment;
            public DisplayActor Player;
            public Transform PlayerHolder;
            public ParticleSystem Dust;
            public float WalkSpeed;
            public CinemachineCamera Wide, Board, Ignite, Liftoff;
            public AnimationClip Motion;
        }

        private static DawnSet CreateDawnSet()
        {
            var set = new DawnSet { Root = new GameObject("Dawn Set", typeof(Animator)) };
            set.Animator = set.Root.GetComponent<Animator>();
            var root = set.Root.transform;

            var sunDirection = Quaternion.Euler(-SunElevation, SunYaw, 0f) * Vector3.forward;
            var sun = EndingDawnLook.CreateSun(root, Quaternion.LookRotation(-sunDirection));
            set.Environment = EndingDawnLook.Environment(sun);
            EndingDawnLook.CreateVolume(root, EndingDawnLook.EndingVolumeProfilePath);
            CreateGround(root);

            // ── 구조선 ──
            var ship = new GameObject("Rescue Ship").transform;
            ship.SetParent(root, false);
            var size = EndingDawnLook.AddShipModel(ship, ShipLength);
            var rest = new Vector3(0f, RestHeight + size.y * 0.5f, 0f);
            ship.localPosition = rest;
            float belly = -size.y * 0.5f;
            float hatchZ = -size.z * HatchBack;
            var metal = LitMaterial("Ending Pad Metal", new Color(0.2f, 0.21f, 0.23f), 0.55f, 0.45f);
            foreach (var x in new[] { -size.x * 0.2f, size.x * 0.2f })
            foreach (var z in new[] { -size.z * 0.3f, size.z * 0.25f })
                Primitive(PrimitiveType.Cylinder, ship, "Leg", new Vector3(x, belly - RestHeight * 0.5f, z), new Vector3(0.45f, RestHeight * 0.5f, 0.45f), metal);

            // 경사로: 경첩(배 밑면 뒤쪽)에서 뒤로 내려간다. 경첩 X 회전 -각도 = 열림, 0 = 배 밑에 접힘
            var hinge = new GameObject("Ramp Hinge").transform;
            hinge.SetParent(ship, false);
            hinge.localPosition = new Vector3(0f, belly, hatchZ);
            Primitive(PrimitiveType.Cube, hinge, "Ramp", new Vector3(0f, -0.08f, -RampLength * 0.5f), new Vector3(RampWidth, 0.16f, RampLength), metal);
            float rampAngle = Mathf.Asin(RestHeight / RampLength) * Mathf.Rad2Deg;
            hinge.localRotation = Quaternion.Euler(-rampAngle, 0f, 0f);

            // 해치 빛: 배 밑에서 새어 나오는 따뜻한 빛 (플레이어가 이 안으로 사라진다)
            var hatch = new GameObject("Hatch Light", typeof(Light)).GetComponent<Light>();
            hatch.transform.SetParent(ship, false);
            hatch.transform.localPosition = new Vector3(0f, belly + 0.6f, hatchZ + 0.6f);
            hatch.type = LightType.Point;
            hatch.color = new Color(1f, 0.82f, 0.6f);
            hatch.range = 9f;
            hatch.intensity = 8f;
            var hatchGlow = Primitive(PrimitiveType.Quad, hatch.transform, "Glow", new Vector3(0f, -0.58f, 0f), new Vector3(RampWidth, 2.4f, 1f),
                GlowMaterial("Glow Hatch", new Color(2.6f, 2.1f, 1.5f), false, 2.5f));
            hatchGlow.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);

            // 수직 분사 2개 (배 밑 아래로) + 주 엔진 (뒤로). 크기 0 에서 Timeline 이 키운다
            var jetMaterial = GlowMaterial("Glow Lift Jet", new Color(0.8f, 1.6f, 3.2f), false, 3f);
            var jets = new GameObject("Lift Jets").transform;
            jets.SetParent(ship, false);
            jets.localPosition = new Vector3(0f, belly, 0f);
            foreach (var x in new[] { -size.x * 0.22f, size.x * 0.22f })
                Line(jets, "Jet", jetMaterial, new Vector3(x, 0f, 0f), new Vector3(x, -4.5f, 0f), 1.4f);
            jets.localScale = new Vector3(1f, 0f, 1f);
            var engine = new GameObject("Main Engine").transform;
            engine.SetParent(ship, false);
            engine.localPosition = new Vector3(0f, 0f, -size.z * 0.5f);
            Line(engine, "Flame", GlowMaterial("Glow Main Engine", new Color(0.7f, 1.5f, 3.4f), false, 3f), Vector3.zero, new Vector3(0f, 0f, -9f), size.y * 0.45f);
            engine.localScale = new Vector3(1f, 1f, 0f);

            var lookTarget = new GameObject("Look Target").transform;
            lookTarget.SetParent(ship, false);
            lookTarget.localPosition = new Vector3(0f, size.y * 0.1f, 0f);

            // ── 착륙 패드·먼지 ──
            Primitive(PrimitiveType.Cylinder, root, "Landing Pad", new Vector3(0f, 0.15f, 0f), new Vector3(PadRadius * 2f, 0.15f, PadRadius * 2f), metal);
            var ring = Line(root, "Pad Ring", GlowMaterial("Glow Pad Ring", new Color(0.5f, 1.3f, 1.8f), false, 2f), Vector3.zero, Vector3.zero, 0.18f);
            ring.loop = true;
            ring.positionCount = 64;
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (PadRadius - 1.2f) + Vector3.up * 0.32f);
            }
            set.Dust = CreateDust(root);

            // ── 플레이어: 경사로 아래 끝까지 걷고 → 경사로 → 해치 안으로 ──
            var hingeWorld = rest + new Vector3(0f, belly, hatchZ);
            var foot = hingeWorld + Quaternion.Euler(-rampAngle, 0f, 0f) * new Vector3(0f, 0f, -RampLength);
            foot.y = 0.3f; // 패드 윗면
            var start = foot + WalkFrom;
            var top = hingeWorld + new Vector3(0f, -0.05f, -0.2f);
            var inside = hingeWorld + new Vector3(0f, 0.5f, 0.6f);
            float leg1 = Vector3.Distance(start, foot), leg2 = Vector3.Distance(foot, top), leg3 = Vector3.Distance(top, inside);
            set.WalkSpeed = (leg1 + leg2 + leg3) / (EndingTimeline.BoardEnd - WalkStart);
            float t1 = WalkStart + leg1 / set.WalkSpeed, t2 = t1 + leg2 / set.WalkSpeed;

            set.PlayerHolder = new GameObject("Player").transform;
            set.PlayerHolder.SetParent(root, false);
            set.PlayerHolder.localPosition = start;
            var player = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(PlayerDisplayPath), set.PlayerHolder);
            player.transform.localPosition = Vector3.zero;
            player.transform.localRotation = Quaternion.identity;
            set.Player = player.GetComponent<DisplayActor>();
            if (set.Player == null) throw new FileNotFoundException("[EndingSceneBuilder] Player_Display 에 DisplayActor 가 없습니다", PlayerDisplayPath);

            // ── 카메라 (Dawn Set 아래 → 이 장면이 꺼지면 함께 꺼진다) ──
            set.Wide = NewShot(root, "CM_Dawn_Wide", new Vector3(-30f, 3.5f, -34f), 40f);
            LookAt(set.Wide, lookTarget, 0.6f);
            Noise(set.Wide, "Handheld_tele_mild", 0.3f, 0.4f);

            set.Board = NewShot(root, "CM_Board", start + new Vector3(1.4f, 1.9f, -3.8f), 45f);
            set.Board.Target.TrackingTarget = set.PlayerHolder;
            var follow = set.Board.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(1.4f, 1.9f, -3.8f);
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            LookAt(set.Board, hatch.transform, 0.4f);
            Noise(set.Board, "Handheld_normal_mild", 0.4f, 0.5f);

            set.Ignite = NewShot(root, "CM_Ignite", new Vector3(-PadRadius - 2f, 0.7f, -6f), 50f);
            LookAt(set.Ignite, lookTarget, 0.3f);
            Noise(set.Ignite, "6D Shake", 0.35f, 0.8f);

            set.Liftoff = NewShot(root, "CM_Liftoff", new Vector3(-62f, 4f, -78f), 38f, 3000f);
            LookAt(set.Liftoff, ship, 0.5f);
            Noise(set.Liftoff, "Handheld_tele_mild", 0.35f, 0.5f);

            // ── 움직임 (Timeline Animation 트랙, 경로는 Dawn Set 기준) ──
            var clip = new AnimationClip { name = "Dawn Set Motion" };
            // 넓은 컷: 천천히 다가간다
            Vec(clip, "CM_Dawn_Wide", "m_LocalPosition", (0f, new Vector3(-30f, 3.5f, -34f)), (6.5f, new Vector3(-24f, 3f, -27f)));
            // 플레이어 걷기 (몸은 걷는 방향으로)
            Vec(clip, "Player", "m_LocalPosition", (0f, start), (WalkStart, start), (t1, foot), (t2, top), (EndingTimeline.BoardEnd, inside));
            float yaw1 = Mathf.Atan2(foot.x - start.x, foot.z - start.z) * Mathf.Rad2Deg;
            Float(clip, "Player", "localEulerAnglesRaw.y", (0f, yaw1), (t1 - 0.4f, yaw1), (t1 + 0.3f, 0f));
            // 경사로 닫힘 · 해치 빛 꺼짐
            Float(clip, "Rescue Ship/Ramp Hinge", "localEulerAnglesRaw.x", (0f, -rampAngle), (11f, -rampAngle), (12.4f, 0f));
            FloatOf<Light>(clip, "Rescue Ship/Hatch Light", "m_Intensity", (0f, 8f), (11.2f, 8f), (12.2f, 0f));
            Float(clip, "Rescue Ship/Hatch Light/Glow", "m_LocalScale.y", (0f, 2.4f), (11.2f, 2.4f), (12.2f, 0f));
            // 점화: 수직 분사 → 이륙 뒤 주 엔진
            float ignite = EndingTimeline.Ignite, lift = EndingTimeline.Liftoff, space = EndingTimeline.Space;
            Float(clip, "Rescue Ship/Lift Jets", "m_LocalScale.y", (0f, 0f), (ignite, 0f), (ignite + 0.4f, 0.35f), (ignite + 1.6f, 0.7f),
                  (lift, 1f), (lift + 3f, 1.1f), (space, 0.4f));
            Float(clip, "Rescue Ship/Main Engine", "m_LocalScale.z", (0f, 0f), (lift + 1.2f, 0f), (lift + 2.4f, 1f), (space, 1.8f));
            // 이륙: 떠올라(18~21) → 기수를 들고(20.5~22.5) 가속하며 하늘로(~24)
            Vec(clip, "Rescue Ship", "m_LocalPosition", (0f, rest), (lift, rest), (lift + 0.6f, rest + Vector3.up * 0.6f), (lift + 3f, rest + Vector3.up * 13f),
                (lift + 4f, rest + new Vector3(0f, 22f, 22f)), (lift + 5f, rest + new Vector3(0f, 42f, 85f)), (space, rest + new Vector3(0f, 85f, 220f)));
            Float(clip, "Rescue Ship", "localEulerAnglesRaw.x", (0f, 0f), (lift + 2.5f, 0f), (lift + 4.5f, -18f), (space, -24f));
            set.Motion = clip;
            return set;
        }

        private static void CreateGround(Transform parent)
        {
            var ground = Primitive(PrimitiveType.Plane, parent, "Ground", Vector3.zero, new Vector3(60f, 1f, 60f), Load<Material>(GroundMaterialPath));
            ground.GetComponent<MeshRenderer>().receiveShadows = true;
            var rock = Load<GameObject>(RockPath);
            var rocks = new GameObject("Rocks").transform;
            rocks.SetParent(parent, false);
            foreach (var (position, scale, yaw) in new[]
                     {
                         (new Vector3(-70f, 0f, 150f), 1.6f, 30f), (new Vector3(110f, 0f, 190f), 2.4f, 300f), (new Vector3(-160f, 0f, 40f), 1.5f, 110f),
                         (new Vector3(60f, 0f, -170f), 1.8f, 200f), (new Vector3(-30f, 0f, 260f), 2.8f, 80f),
                     })
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(rock, rocks);
                go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                go.transform.localScale = Vector3.one * scale;
            }
        }

        /// <summary>패드에서 바깥으로 번지는 흙먼지 (점화 ~ 이륙 뒤, Control 트랙이 켜고 끈다)</summary>
        private static ParticleSystem CreateDust(Transform parent)
        {
            var go = new GameObject("Dust", typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // 도넛 모양이 땅에 눕게
            var particles = go.GetComponent<ParticleSystem>();
            var main = particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(3f, 7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.78f, 0.66f, 0.55f, 0.32f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var emission = particles.emission;
            emission.rateOverTime = 70f;
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 3f;
            shape.radiusThickness = 0f;
            var drag = particles.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 1.2f;
            var alpha = particles.colorOverLifetime;
            alpha.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            alpha.color = gradient;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = Load<Material>(SmokeMaterialPath);
            go.SetActive(false);
            return particles;
        }

        // ───────────────────────── 궤도 ─────────────────────────

        private sealed class OrbitSet
        {
            public GameObject Root;
            public Animator Animator;
            public SceneEnvironment Environment;
            public CinemachineCamera Orbit, Credits;
            public EndingPlanetShatter Shatter;
            public AnimationClip Motion;
        }

        private static OrbitSet CreateOrbitSet()
        {
            var set = new OrbitSet { Root = new GameObject("Orbit Set", typeof(Animator)) };
            set.Animator = set.Root.GetComponent<Animator>();
            var root = set.Root.transform;

            // 행성: 인트로의 HivePlanet 을 핏줄이 가라앉은 색으로 시작 (하이브가 힘을 잃었다) → 크레딧 중 금빛 균열·폭발 (EndingPlanetShatter)
            var planetMaterial = SaveMaterial("HivePlanetCalm", Load<Shader>(IntroShaderDir + "HivePlanet.shader"));
            planetMaterial.SetColor("_BaseColor", new Color(0.06f, 0.04f, 0.06f));
            planetMaterial.SetColor("_FleshColor", new Color(0.24f, 0.12f, 0.16f));
            planetMaterial.SetColor("_VeinColor", new Color(0.7f, 0.22f, 0.2f));
            planetMaterial.SetFloat("_VeinScale", 9f);
            planetMaterial.SetFloat("_VeinWidth", 0.035f);
            planetMaterial.SetFloat("_PulseSpeed", 0.25f);
            planetMaterial.SetFloat("_CrackAmount", 0f);
            planetMaterial.SetColor("_CrackColor", CrackColor);
            planetMaterial.SetFloat("_CrackScale", 5f);
            var planet = new GameObject("Hive Planet", typeof(MeshFilter), typeof(MeshRenderer));
            planet.transform.SetParent(root, false);
            planet.GetComponent<MeshFilter>().sharedMesh = Load<Mesh>(PlanetMeshPath);
            var planetRenderer = planet.GetComponent<MeshRenderer>();
            planetRenderer.sharedMaterial = planetMaterial;
            planetRenderer.shadowCastingMode = ShadowCastingMode.Off;
            planet.transform.localPosition = PlanetCenter;
            planet.transform.localScale = Vector3.one * (PlanetRadius * 2f);
            set.Shatter = CreateShatter(root, planetRenderer);

            // 해: 행성 가장자리(카메라에서 약 17° 아래) 너머에서 떠오른다
            Vector3 SunAt(float pitch) => OrbitCamera + Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward * SunDistance;
            var sunGlow = Primitive(PrimitiveType.Quad, root, "Sun Glow", SunAt(14f), Vector3.zero,
                GlowMaterial("Glow Sunrise", new Color(3.2f, 2.1f, 1.2f), true, 2.2f));
            sunGlow.transform.localRotation = Quaternion.LookRotation(SunAt(14f) - OrbitCamera);
            var starLight = new GameObject("Star Light", typeof(Light)).GetComponent<Light>();
            starLight.transform.SetParent(root, false);
            starLight.transform.rotation = Quaternion.LookRotation(OrbitCamera - SunAt(13f));
            starLight.type = LightType.Directional;
            starLight.color = new Color(1f, 0.9f, 0.78f);
            starLight.intensity = 1.8f;
            starLight.shadows = LightShadows.None;
            CreateSpaceVolume(root);
            set.Environment = new SceneEnvironment
            {
                Skybox = Load<Material>(DeepSpaceSkyPath),
                Sun = starLight,
                Fog = false,
                AmbientSky = new Color(0.07f, 0.075f, 0.1f),
                AmbientEquator = new Color(0.05f, 0.05f, 0.07f),
                AmbientGround = new Color(0.03f, 0.03f, 0.04f),
            };

            // 구조선 (궤도 컷용 — 같은 기체를 하나 더)
            var ship = new GameObject("Space Ship").transform;
            ship.SetParent(root, false);
            var size = EndingDawnLook.AddShipModel(ship, ShipLength);
            var engine = new GameObject("Main Engine").transform;
            engine.SetParent(ship, false);
            engine.localPosition = new Vector3(0f, 0f, -size.z * 0.5f);
            Line(engine, "Flame", GlowMaterial("Glow Main Engine", new Color(0.7f, 1.5f, 3.4f), false, 3f), Vector3.zero, new Vector3(0f, 0f, -9f), size.y * 0.45f);

            set.Orbit = NewShot(root, "CM_Orbit", OrbitCamera, 50f, 8000f);
            LookAt(set.Orbit, ship, 0.8f);
            Noise(set.Orbit, "Handheld_normal_mild", 0.25f, 0.3f);
            set.Credits = NewShot(root, "CM_Credits", CreditsCamera, 35f, 8000f);
            // 행성이 화면 왼쪽 1/3 에 오게 (오른쪽에 크레딧). 흔들림은 폭발 때만 (진폭은 Timeline 곡선)
            set.Credits.transform.rotation = Quaternion.LookRotation(PlanetCenter - CreditsCamera) * Quaternion.Euler(0f, 12f, 0f);
            Noise(set.Credits, "6D Shake", 0f, 0.9f);

            // ── 움직임 ──
            float space = EndingTimeline.Space, breakaway = EndingTimeline.Breakaway, jump = EndingTimeline.Jump;
            var clip = new AnimationClip { name = "Orbit Set Motion" };
            Vec(clip, "Space Ship", "m_LocalPosition", (0f, new Vector3(6f, -34f, 40f)), (space, new Vector3(6f, -34f, 40f)),
                (space + 3f, new Vector3(4f, -6f, 72f)), (breakaway, new Vector3(2f, 16f, 130f)), (breakaway + 3f, new Vector3(1f, 38f, 220f)),
                (jump - 0.2f, new Vector3(0f, 60f, 330f)), (jump, new Vector3(0f, 66f, 370f)), (jump + 0.6f, new Vector3(0f, 220f, 4000f)));
            Float(clip, "Space Ship", "localEulerAnglesRaw.x", (0f, -22f), (breakaway, -14f), (jump, -8f));
            Float(clip, "Space Ship/Main Engine", "m_LocalScale.z", (0f, 1.3f), (jump - 0.3f, 1.3f), (jump, 4f));
            // 해돋이: 가장자리 아래에서 올라오며 커진다
            Vec(clip, "Sun Glow", "m_LocalPosition", (0f, SunAt(20f)), (breakaway - 1.5f, SunAt(19f)), (breakaway + 3f, SunAt(13.5f)), (Total, SunAt(12f)));
            Vec(clip, "Sun Glow", "m_LocalScale", (0f, Vector3.zero), (breakaway - 1.5f, Vector3.one * 150f), (breakaway + 3f, Vector3.one * 800f), (Total, Vector3.one * 900f));
            // 크레딧: 아주 천천히 옆으로 흐른다
            Vec(clip, "CM_Credits", "m_LocalPosition", (0f, CreditsCamera), (EndingTimeline.Credits, CreditsCamera), (Total, CreditsCamera + new Vector3(180f, -40f, 120f)));
            // 균열이 굵어질수록 미세하게 떨리다가 폭발에 크게 흔들리고 가라앉는다 (화면 UI 는 흔들리지 않는다)
            float shatter = EndingTimeline.Shatter;
            FloatOf<CinemachineBasicMultiChannelPerlin>(clip, "CM_Credits", "AmplitudeGain", (0f, 0f), (shatter - 4f, 0f), (shatter - 0.1f, 0.25f),
                (shatter, 2.4f), (shatter + 2.5f, 0.5f), (shatter + 7f, 0f));
            set.Motion = clip;
            return set;
        }

        /// <summary>
        /// 행성 붕괴 연출: 가운데 섬광 + 충격파 고리 + 순간 광원 + 잔해 입자(불씨·먼지 구름, 처음엔 꺼짐).
        /// 행성은 섬광 속에서 사라진다 (얇은 껍질 파편은 쓰지 않는다 — 먼 거리에서도 두께가 드러나 어색했다)
        /// </summary>
        private static EndingPlanetShatter CreateShatter(Transform parent, Renderer planet)
        {
            var debrisRoot = new GameObject("Planet Debris").transform;
            debrisRoot.SetParent(parent, false);
            debrisRoot.localPosition = PlanetCenter;
            var embers = CreateEmbers(debrisRoot);
            var dust = CreateDustCloud(debrisRoot);
            debrisRoot.gameObject.SetActive(false);

            var core = Primitive(PrimitiveType.Quad, parent, "Blast Core", PlanetCenter, Vector3.one, GlowMaterial("Glow Blast Core", Color.white, true, 2.2f));
            var coreRenderer = core.GetComponent<MeshRenderer>();
            coreRenderer.shadowCastingMode = ShadowCastingMode.Off;
            coreRenderer.enabled = false;

            // 충격파 고리: 행성 적도면을 살짝 기울인 반지름 1 원 (감독이 크기를 키운다)
            var ring = new GameObject("Shock Ring", typeof(LineRenderer)).GetComponent<LineRenderer>();
            ring.transform.SetParent(parent, false);
            ring.transform.localPosition = PlanetCenter;
            ring.transform.localRotation = Quaternion.Euler(8f, 0f, 14f);
            ring.sharedMaterial = GlowMaterial("Glow Shock Ring", Color.white, false, 2f);
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 96;
            for (int i = 0; i < 96; i++)
            {
                float a = i / 96f * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }
            ring.textureMode = LineTextureMode.Stretch;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.enabled = false;

            var blast = new GameObject("Blast Light", typeof(Light)).GetComponent<Light>();
            blast.transform.SetParent(parent, false);
            blast.transform.localPosition = PlanetCenter;
            blast.type = LightType.Point;
            blast.color = new Color(1f, 0.82f, 0.55f);
            blast.range = PlanetRadius * 10f;
            blast.intensity = 0f;
            blast.enabled = false;

            var shatter = new GameObject("Planet Shatter", typeof(EndingPlanetShatter)).GetComponent<EndingPlanetShatter>();
            shatter.transform.SetParent(parent, false);
            var so = new SerializedObject(shatter);
            Set(so, "planet", planet);
            Set(so, "debrisRoot", debrisRoot.gameObject);
            var debris = so.FindProperty("debris");
            debris.arraySize = 2;
            debris.GetArrayElementAtIndex(0).objectReferenceValue = embers;
            debris.GetArrayElementAtIndex(1).objectReferenceValue = dust;
            Set(so, "core", coreRenderer);
            Set(so, "ring", ring);
            Set(so, "blast", blast);
            so.ApplyModifiedPropertiesWithoutUndo();
            return shatter;
        }

        /// <summary>
        /// 불씨 잔해: 행성 겉껍질에서 바깥으로 튀어 나가는 작고 밝은 금빛 조각. 움직이는 방향으로 늘여 그리고, 금빛 → 주황 → 어둡게 식으며 작아진다
        /// </summary>
        private static ParticleSystem CreateEmbers(Transform parent)
        {
            var particles = DebrisParticles(parent, "Embers", 600, 6f, 10f, 700, 11);
            var main = particles.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(60f, 320f);
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 22f);
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = PlanetRadius * 0.95f;
            shape.radiusThickness = 0.15f; // 겉껍질 근처에서 바깥으로
            var drag = particles.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 0.35f;
            var color = particles.colorOverLifetime;
            color.enabled = true;
            // 입자 색은 0~1 로 잘리므로 밝기(HDR → Bloom)는 재질 색이 맡고, 여기서는 식어 가는 색·투명도만
            color.color = DebrisGradient((new Color(1f, 0.9f, 0.65f), 0f, 1f), (new Color(1f, 0.55f, 0.2f), 0.35f, 0.9f),
                                         (new Color(0.45f, 0.14f, 0.07f), 0.8f, 0.4f), (Color.black, 1f, 0f));
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.25f)));
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1.5f;
            renderer.sharedMaterial = GlowMaterial("Glow Ember", new Color(4f, 3.2f, 2.2f), true, 2.2f);
            return particles;
        }

        /// <summary>먼지 구름: 행성이 있던 자리에서 천천히 부푸는 큰 연기 덩어리. 따뜻한 색 → 회색으로 바래며 로고 전에 옅어진다 (화면 왼쪽에 머물 만큼만 퍼진다)</summary>
        private static ParticleSystem CreateDustCloud(Transform parent)
        {
            var particles = DebrisParticles(parent, "Dust Cloud", 45, 9f, 12f, 60, 23);
            var main = particles.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 45f);
            main.startSize = new ParticleSystem.MinMaxCurve(500f, 1000f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = PlanetRadius * 0.7f;
            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-3f * Mathf.Deg2Rad, 3f * Mathf.Deg2Rad);
            var color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = DebrisGradient((new Color(1f, 0.85f, 0.6f), 0f, 0f), (new Color(1f, 0.7f, 0.45f), 0.08f, 0.35f),
                                         (new Color(0.45f, 0.42f, 0.48f), 0.6f, 0.2f), (new Color(0.3f, 0.3f, 0.35f), 1f, 0f));
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.4f)));
            particles.GetComponent<ParticleSystemRenderer>().sharedMaterial = Load<Material>(SmokeMaterialPath);
            return particles;
        }

        /// <summary>
        /// 폭발 순간 한 번에 뿜는(burst) 반복 없는 입자. 시드 고정 · 자동 재생 안 함 · 로컬 공간 (m) —
        /// EndingPlanetShatter 가 Simulate 로 직접 시간을 맞춘다
        /// </summary>
        private static ParticleSystem DebrisParticles(Transform parent, string name, int count, float lifeMin, float lifeMax, int max, uint seed)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent, false);
            var particles = go.GetComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = seed;
            var main = particles.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startColor = Color.white;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = max;
            var emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 3f; // 화면을 덮을 만큼 커져도 잘리지 않게 (기본 0.5)
            return particles;
        }

        private static Gradient DebrisGradient(params (Color Color, float Time, float Alpha)[] keys)
        {
            var gradient = new Gradient();
            gradient.SetKeys(keys.Select(k => new GradientColorKey(k.Color, k.Time)).ToArray(),
                             keys.Select(k => new GradientAlphaKey(k.Alpha, k.Time)).ToArray());
            return gradient;
        }

        private static void CreateSpaceVolume(Transform parent)
        {
            AssetDatabase.DeleteAsset(SpaceVolumeProfilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, SpaceVolumeProfilePath);
            AddOverride<Tonemapping>(profile).mode.Override(TonemappingMode.ACES);
            AddOverride<ColorAdjustments>(profile).contrast.Override(10f);
            var bloom = AddOverride<Bloom>(profile);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.3f);
            bloom.scatter.Override(0.75f);
            var vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.5f);
            var grain = AddOverride<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.1f);
            AssetDatabase.SaveAssets();

            var volume = new GameObject("Space Volume", typeof(Volume)).GetComponent<Volume>();
            volume.transform.SetParent(parent, false);
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

        // ───────────────────────── Timeline ─────────────────────────

        private static TimelineAsset BuildTimeline(PlayableDirector director, CinemachineBrain brain, DawnSet dawn, OrbitSet orbit, AudioSource source, AudioClip music)
        {
            AssetDatabase.DeleteAsset(TimelinePath);
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, TimelinePath);
            director.playableAsset = timeline;

            // 장면 켜고 끄기
            Activation(timeline, director, "Dawn Set", dawn.Root, 0f, EndingTimeline.Space);
            Activation(timeline, director, "Orbit Set", orbit.Root, EndingTimeline.Space, Total);
            Activation(timeline, director, "Player", dawn.PlayerHolder.gameObject, 0f, EndingTimeline.BoardEnd);

            // 움직임 (장면 루트의 Animator → 자식 경로)
            Motion(timeline, director, "Dawn Motion", dawn.Animator, dawn.Motion);
            Motion(timeline, director, "Orbit Motion", orbit.Animator, orbit.Motion);

            // 먼지 (점화 ~ 이륙 4초 뒤)
            var dust = timeline.CreateTrack<ControlTrack>(null, "Dust");
            var dustClip = dust.CreateClip<ControlPlayableAsset>();
            dustClip.displayName = "Dust";
            dustClip.start = EndingTimeline.Ignite;
            dustClip.duration = EndingTimeline.Liftoff + 4f - EndingTimeline.Ignite;
            var control = (ControlPlayableAsset)dustClip.asset;
            control.sourceGameObject.exposedName = GUID.Generate().ToString();
            director.SetReferenceValue(control.sourceGameObject.exposedName, dawn.Dust.gameObject);
            control.updateParticle = true;
            control.active = true;

            // 카메라 컷
            var cameras = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
            Shot(cameras, director, "넓게", dawn.Wide, 0f, 6f);
            Shot(cameras, director, "탑승", dawn.Board, 6f, EndingTimeline.Ignite);
            Shot(cameras, director, "점화", dawn.Ignite, EndingTimeline.Ignite, EndingTimeline.Liftoff);
            Shot(cameras, director, "이륙", dawn.Liftoff, EndingTimeline.Liftoff, EndingTimeline.Space);
            Shot(cameras, director, "궤도", orbit.Orbit, EndingTimeline.Space, EndingTimeline.Credits);
            Shot(cameras, director, "크레딧", orbit.Credits, EndingTimeline.Credits, Total);
            director.SetGenericBinding(cameras, brain);

            // 음악
            var musicTrack = timeline.CreateTrack<AudioTrack>(null, "Music");
            var musicClip = musicTrack.CreateClip(music);
            musicClip.start = 0;
            director.SetGenericBinding(musicTrack, source);

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        private static void Activation(TimelineAsset timeline, PlayableDirector director, string name, GameObject target, float start, float end)
        {
            var track = timeline.CreateTrack<ActivationTrack>(null, name);
            var clip = track.CreateDefaultClip();
            clip.displayName = "Active";
            clip.start = start;
            clip.duration = end - start;
            track.postPlaybackState = ActivationTrack.PostPlaybackState.LeaveAsIs;
            director.SetGenericBinding(track, target);
        }

        private static void Motion(TimelineAsset timeline, PlayableDirector director, string name, Animator animator, AnimationClip clip)
        {
            AssetDatabase.AddObjectToAsset(clip, timeline);
            var track = timeline.CreateTrack<AnimationTrack>(null, name);
            var timelineClip = track.CreateClip(clip);
            timelineClip.start = 0;
            timelineClip.duration = Total;
            director.SetGenericBinding(track, animator);
        }

        private static void Shot(CinemachineTrack track, PlayableDirector director, string name, CinemachineCamera camera, float start, float end)
        {
            var clip = track.CreateClip<CinemachineShot>();
            clip.displayName = name;
            clip.start = start;
            clip.duration = end - start;
            var shot = (CinemachineShot)clip.asset;
            shot.VirtualCamera.exposedName = GUID.Generate().ToString();
            director.SetReferenceValue(shot.VirtualCamera.exposedName, camera);
        }

        // ── 애니메이션 키 (부드러운 접선, 모든 곡선을 Total 까지 이어 Timeline 클립 길이를 맞춘다) ──

        private static void Float(AnimationClip clip, string path, string property, params (float Time, float Value)[] keys) =>
            Curve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), keys);

        private static void FloatOf<T>(AnimationClip clip, string path, string property, params (float Time, float Value)[] keys) where T : Component =>
            Curve(clip, EditorCurveBinding.FloatCurve(path, typeof(T), property), keys);

        private static void Vec(AnimationClip clip, string path, string property, params (float Time, Vector3 Value)[] keys)
        {
            Float(clip, path, property + ".x", keys.Select(k => (k.Time, k.Value.x)).ToArray());
            Float(clip, path, property + ".y", keys.Select(k => (k.Time, k.Value.y)).ToArray());
            Float(clip, path, property + ".z", keys.Select(k => (k.Time, k.Value.z)).ToArray());
        }

        private static void Curve(AnimationClip clip, EditorCurveBinding binding, (float Time, float Value)[] keys)
        {
            var curve = new AnimationCurve(keys.Select(k => new Keyframe(k.Time, k.Value)).ToArray());
            if (keys[keys.Length - 1].Time < Total) curve.AddKey(Total, keys[keys.Length - 1].Value);
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        // ───────────────────────── 화면 UI ─────────────────────────

        private struct Overlay
        {
            public EndingCredits Credits;
            public Graphic Blackout;
            public Graphic Flash;
            public RectTransform BarTop;
            public RectTransform BarBottom;
            public CanvasGroup SkipGroup;
            public RectTransform SkipBar;
            public EndingSkipPrank Prank;
        }

        /// <summary>순서: 크레딧 → 로고 → 검은 띠 → 도약 섬광 → 암전 → 건너뛰기 안내 → 건너뛰기 창(폭발)</summary>
        private static Overlay CreateOverlay(TMP_FontAsset orbit, EndingCreditsText text)
        {
            var canvas = CreateCanvas("Overlay");
            canvas.GetComponent<Canvas>().sortingOrder = 10;
            var overlay = new Overlay { Credits = CreateCredits(canvas, orbit, text) };

            overlay.BarTop = Bar(canvas, "Bar Top", 1f);
            overlay.BarBottom = Bar(canvas, "Bar Bottom", 0f);
            overlay.Flash = FullScreen(canvas, "Flash", new Color(1f, 0.95f, 0.88f, 0f));
            overlay.Blackout = FullScreen(canvas, "Blackout", Color.black);

            var skip = NewRect("Skip", canvas);
            Place(skip.gameObject, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-80f, 60f), new Vector2(420f, 80f), new Vector2(1f, 0f));
            overlay.SkipGroup = skip.gameObject.AddComponent<CanvasGroup>();
            overlay.SkipGroup.alpha = 0f;
            overlay.SkipGroup.interactable = false;
            overlay.SkipGroup.blocksRaycasts = false;
            var label = CreateText(skip, "Label", SkipLabel, orbit, 24f, UiTheme.MoonWhite, TextAlignmentOptions.BottomRight);
            label.characterSpacing = 2f;
            Place(label.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 34f), new Vector2(1f, 1f));
            var hint = CreateText(skip, "Hint", SkipHint, orbit, 18f, Steel, TextAlignmentOptions.TopRight);
            Place(hint.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 6f), new Vector2(0f, 26f), new Vector2(1f, 0f));
            var track = SkipLine(skip, "Track", UiTheme.GaugeBack);
            overlay.SkipBar = SkipLine(skip, "Fill", UiTheme.Red);
            overlay.SkipBar.localScale = new Vector3(0f, 1f, 1f);
            track.SetAsFirstSibling();

            overlay.Prank = CreateSkipDialog(canvas, orbit);
            return overlay;
        }

        private static readonly Vector2 DialogSize = new Vector2(900f, 420f);
        private static readonly Color DialogColor = new Color(UiTheme.Night.r, UiTheme.Night.g, UiTheme.Night.b, 0.94f);
        /// <summary>창·조각의 가는 테두리 (Outline 은 그래픽 알파를 따라가므로 조각이 흐려질 때 같이 사라진다)</summary>
        private static readonly Color DialogEdge = new Color(UiTheme.Steel.r, UiTheme.Steel.g, UiTheme.Steel.b, 0.35f);

        /// <summary>
        /// 건너뛰기 창 (가운데). 붉은 윗띠 + SKIP 라벨 + "터질듯한 스킵 버튼?" + 짧은 붉은 선, 폭발용 조각(창을 4×2 로 나눈 판)과
        /// 둥근 섬광(호박색), 폭발 뒤 문구("터져서 없어짐", 달빛 흰색)는 꺼 둔 채 같은 자리에
        /// </summary>
        private static EndingSkipPrank CreateSkipDialog(RectTransform canvas, TMP_FontAsset orbit)
        {
            var root = NewRect("Skip Dialog", canvas);
            Stretch(root);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            var panel = NewRect("Panel", root);
            Place(panel.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, DialogSize);
            var background = panel.gameObject.AddComponent<Image>();
            background.color = DialogColor;
            background.raycastTarget = false;
            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = DialogEdge;
            outline.effectDistance = new Vector2(1f, -1f);
            var topBand = NewRect("Top Band", panel);
            Place(topBand.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 6f), new Vector2(0.5f, 1f));
            var topBandImage = topBand.gameObject.AddComponent<Image>();
            topBandImage.color = UiTheme.Red;
            topBandImage.raycastTarget = false;

            // SKIP → 질문 → 짧은 붉은 선 (가운데 기준)
            var header = CreateText(panel, "Header", "SKIP", TitleFont, 20f, Steel, TextAlignmentOptions.Center);
            header.characterSpacing = 16f;
            Place(header.gameObject, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 76f), new Vector2(0f, 30f));
            var question = CreateText(panel, "Question", EndingSkipPrank.SkipQuestion, orbit, 34f, UiTheme.MoonWhite, TextAlignmentOptions.Center);
            question.textWrappingMode = TextWrappingModes.Normal;
            Place(question.gameObject, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 4f), new Vector2(-80f, 110f));
            var line = NewRect("Line", panel);
            Place(line.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -66f), new Vector2(UiTheme.HeaderLineLength, 3f));
            var lineImage = line.gameObject.AddComponent<Image>();
            lineImage.color = UiTheme.Red;
            lineImage.raycastTarget = false;

            // 조각: 창과 같은 자리·크기를 4×2 로 나눈 판 (가운데 기준 좌표 → 날아가는 방향)
            var shardRoot = NewRect("Shards", root);
            Place(shardRoot.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, DialogSize);
            const int columns = 4, rows = 2;
            var shardSize = new Vector2(DialogSize.x / columns, DialogSize.y / rows);
            var shards = new RectTransform[columns * rows];
            for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                var shard = NewRect($"Shard {y * columns + x}", shardRoot);
                var center = new Vector2((x + 0.5f) * shardSize.x - DialogSize.x * 0.5f, (y + 0.5f) * shardSize.y - DialogSize.y * 0.5f);
                Place(shard.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), center, shardSize - new Vector2(4f, 4f));
                var image = shard.gameObject.AddComponent<Image>();
                image.color = DialogColor;
                image.raycastTarget = false;
                var edge = shard.gameObject.AddComponent<Outline>();
                edge.effectColor = DialogEdge;
                edge.effectDistance = new Vector2(1f, -1f);
                shards[y * columns + x] = shard;
            }

            var boomRect = NewRect("Boom", root);
            Place(boomRect.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(400f, 400f));
            var boom = boomRect.gameObject.AddComponent<Image>();
            boom.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            boom.color = new Color(UiTheme.Ember.r, UiTheme.Ember.g, UiTheme.Ember.b, 0f);
            boom.raycastTarget = false;

            // 섬광 위에 그려지도록 Boom 다음에
            var caption = CreateText(root, "Caption", EndingSkipPrank.GoneCaption, orbit, 40f, UiTheme.MoonWhite, TextAlignmentOptions.Center);
            Place(caption.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(DialogSize.x, 120f));
            caption.gameObject.SetActive(false);

            var prank = root.gameObject.AddComponent<EndingSkipPrank>();
            var so = new SerializedObject(prank);
            Set(so, "group", group);
            Set(so, "panel", panel);
            Set(so, "caption", caption);
            var list = so.FindProperty("shards");
            list.arraySize = shards.Length;
            for (int i = 0; i < shards.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = shards[i];
            Set(so, "boom", boom);
            so.ApplyModifiedPropertiesWithoutUndo();
            return prank;
        }

        /// <summary>오른쪽 절반: 위아래가 부드럽게 잘리는 크롤 + 가운데 로고</summary>
        private static EndingCredits CreateCredits(RectTransform canvas, TMP_FontAsset orbit, EndingCreditsText text)
        {
            var root = NewRect("Credits", canvas);
            Stretch(root);
            var credits = root.gameObject.AddComponent<EndingCredits>();

            var viewport = NewRect("Viewport", root);
            Place(viewport.gameObject, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-120f, 0f), new Vector2(0.5f, 0.5f));
            viewport.offsetMin = new Vector2(60f, 0f);
            viewport.offsetMax = new Vector2(-60f, 0f);
            var mask = viewport.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(0, 220);
            var rollGroup = viewport.gameObject.AddComponent<CanvasGroup>();
            rollGroup.alpha = 0f;
            rollGroup.interactable = false;
            rollGroup.blocksRaycasts = false;

            var roll = CreateText(viewport, "Roll", "", orbit, 34f, UiTheme.MoonWhite, TextAlignmentOptions.Top);
            roll.textWrappingMode = TextWrappingModes.Normal;
            roll.richText = true;
            roll.lineSpacing = 8f;
            Place(roll.gameObject, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(760f, 100f), new Vector2(0.5f, 1f));

            var logo = NewRect("Logo", root);
            Place(logo.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 300f));
            var logoGroup = logo.gameObject.AddComponent<CanvasGroup>();
            logoGroup.alpha = 0f;
            logoGroup.interactable = false;
            logoGroup.blocksRaycasts = false;
            // 게임 이름(에셋 logo) → 작은 팀 이름 → 붉은 선 → THANK YOU FOR PLAYING
            var logoText = CreateText(logo, "Title", "", TitleFont, 110f, LogoColor, TextAlignmentOptions.Center);
            logoText.characterSpacing = 24f;
            Place(logoText.gameObject, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 50f), new Vector2(0f, 140f));
            var team = CreateText(logo, "Team", TeamText, TitleFont, 28f, Steel, TextAlignmentOptions.Center);
            team.characterSpacing = 12f;
            Place(team.gameObject, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -36f), new Vector2(0f, 40f));
            var line = NewRect("Line", logo);
            Place(line.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -72f), new Vector2(140f, 3f));
            var lineImage = line.gameObject.AddComponent<Image>();
            lineImage.color = UiTheme.Red;
            lineImage.raycastTarget = false;
            var thanks = CreateText(logo, "Thanks", "", TitleFont, 22f, Steel, TextAlignmentOptions.Center);
            thanks.characterSpacing = 16f;
            Place(thanks.gameObject, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -108f), new Vector2(0f, 36f));

            var so = new SerializedObject(credits);
            Set(so, "content", text);
            Set(so, "viewport", viewport);
            Set(so, "roll", roll.rectTransform);
            Set(so, "rollText", roll);
            Set(so, "rollGroup", rollGroup);
            Set(so, "logoGroup", logoGroup);
            Set(so, "logoText", logoText);
            Set(so, "thanksText", thanks);
            so.ApplyModifiedPropertiesWithoutUndo();
            return credits;
        }

        /// <summary>위(anchorY 1) 또는 아래(0) 검은 띠. 높이(sizeDelta.y)는 EndingDirector 가 바꾼다</summary>
        private static RectTransform Bar(RectTransform parent, string name, float anchorY)
        {
            var rect = NewRect(name, parent);
            Place(rect.gameObject, new Vector2(0f, anchorY), new Vector2(1f, anchorY), Vector2.zero, new Vector2(0f, 130f), new Vector2(0.5f, anchorY));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            return rect;
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
        private static RectTransform SkipLine(RectTransform parent, string name, Color color)
        {
            var rect = NewRect(name, parent);
            Place(rect.gameObject, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-260f, 0f), new Vector2(260f, 2f), new Vector2(0f, 0.5f));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        // ───────────────────────── 공통 ─────────────────────────

        private static GameObject Primitive(PrimitiveType type, Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>부모 기준 좌표의 빛 줄기 (부모 크기를 따라 늘고 준다)</summary>
        private static LineRenderer Line(Transform parent, string name, Material material, Vector3 from, Vector3 to, float width)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
            line.widthMultiplier = width;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static void SetEnvironment(SerializedObject so, string property, SceneEnvironment env)
        {
            so.FindProperty(property + ".Skybox").objectReferenceValue = env.Skybox;
            so.FindProperty(property + ".Sun").objectReferenceValue = env.Sun;
            so.FindProperty(property + ".Fog").boolValue = env.Fog;
            so.FindProperty(property + ".FogColor").colorValue = env.FogColor;
            so.FindProperty(property + ".FogDensity").floatValue = env.FogDensity;
            so.FindProperty(property + ".AmbientSky").colorValue = env.AmbientSky;
            so.FindProperty(property + ".AmbientEquator").colorValue = env.AmbientEquator;
            so.FindProperty(property + ".AmbientGround").colorValue = env.AmbientGround;
        }

        private static void AddToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == ScenePath)) return;
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
