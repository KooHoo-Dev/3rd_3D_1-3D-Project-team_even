using System.Collections.Generic;
using System.IO;
using HybridShooter.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace HybridShooter.Weapon.EditorTools
{
    /// <summary>
    /// 플레이어 무기 이펙트(주황 화염 톤) 만들기 + Player 프리팹·PlayerTest 씬에 연결 (메뉴 한 번).
    ///
    ///  1. 텍스처(별 모양 화염·빛 덩어리·연기·빛줄기)를 코드로 그려 PNG 로 저장
    ///  2. QFX Particle HDR 셰이더로 재질을 만든다 (HDR 색이라 Bloom 에서 빛난다)
    ///  3. 총구 화염 4종(소·중·대·발사기), 탄 궤적 3종(일반·산탄·저격), 탄착 3종(일반·산탄·저격), 로켓·유탄 꼬리를 프리팹으로 저장
    ///  4. QFX 폭발(유탄·로켓)을 복사해 파란색 → 주황색으로 바꾼다 (재질 색 + 파티클·불빛 색). 피해 반경에 맞춰 커지도록 ExplosionVisual 을 붙인다
    ///  5. Player 프리팹과 PlayerTest 씬의 총 8종, 수류탄 프리팹에 연결
    ///
    /// 다시 실행하면 같은 경로의 에셋을 덮어쓴다 (GUID 유지 → 연결은 그대로)
    /// </summary>
    public static class WeaponVfxSetupMenu
    {
        private const string Root = "Assets/Prefabs/Effects";
        private const string TextureFolder = Root + "/Textures";
        private const string MaterialFolder = Root + "/Materials";
        private const string PlayerPrefabPath = "Assets/Scenes/MS/Prefabs/Player.prefab";
        private const string ScenePath = "Assets/Scenes/MS/Scenes/PlayerTest.unity";
        private const string QfxPrefabs = "Assets/ExternalAssets/QFX/Sci-Fi VFX/Prefabs";
        private const string ParticleShader = "QFX/SFX/Particle/Particle HDR";

        private static readonly Color LightColor = new Color(1f, 0.6f, 0.25f);

        private sealed class Materials
        {
            public Material Star, Core, Glow, Spark, Smoke, Tracer;
        }

        private sealed class VfxSet
        {
            public GameObject FlashSmall, FlashMedium, FlashLarge, FlashLauncher;
            public BulletTracer TracerStandard, TracerPellet, TracerSniper;
            public GameObject ImpactBullet, ImpactPellet, ImpactSniper;
            public GameObject ExplosionGrenade, ExplosionRocket;
            public ProjectileTrail RocketTrail, GrenadeTrail;
        }

        private struct FlashSpec
        {
            public string Name;
            public float FlashSize;
            public float SideWidth;
            public float SideLength;
            public int Sparks;
            public int SmokeCount;
            public float SmokeSize;
            public float LightRange;
            public float LightIntensity;
        }

        [MenuItem("Tools/HybridShooter/Setup Weapon VFX (주황 화염)")]
        private static void Setup()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("무기 이펙트 설정", "플레이 모드를 끄고 실행하세요.", "확인");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;

            Shader shader = Shader.Find(ParticleShader);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("무기 이펙트 설정", $"셰이더를 찾지 못했습니다:\n{ParticleShader}\n(QFX Sci-Fi VFX 에셋 필요)", "확인");
                return;
            }

            EnsureFolder(Root);
            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);

            MakeTextures(out var star, out var glow, out var smoke, out var line);
            VfxSet set = BuildAll(MakeMaterials(shader, star, glow, smoke, line));
            if (set.ExplosionGrenade == null || set.ExplosionRocket == null) return;

            int guns = AssignPlayerPrefab(set) + AssignScene(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WeaponVfxSetup] 이펙트 생성 완료 ({Root}), 총 {guns}개에 연결");
            EditorUtility.DisplayDialog("무기 이펙트 설정", $"이펙트를 만들고 총 {guns}개에 연결했습니다.\n{Root}", "확인");
        }

        #region 텍스처·재질
        private static void MakeTextures(out Texture2D star, out Texture2D glow, out Texture2D smoke, out Texture2D line)
        {
            star = WriteTexture("T_MuzzleStar", 128, 128, StarAlpha);
            glow = WriteTexture("T_Glow", 64, 64, (u, v) => Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f));
            smoke = WriteTexture("T_Smoke", 128, 128, SmokeAlpha);
            line = WriteTexture("T_TracerLine", 64, 16, (u, v) =>
            {
                float across = 1f - Mathf.Abs(v * 2f - 1f);
                float ends = Mathf.Clamp01(Mathf.Min(u, 1f - u) * 10f);
                return across * across * ends;
            });
        }

        private static float Radius(float u, float v)
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            return Mathf.Sqrt(x * x + y * y);
        }

        /// <summary>뾰족한 가지 6개 + 둥근 중심. 가지 길이를 조금씩 달리해 기계적으로 보이지 않게</summary>
        private static float StarAlpha(float u, float v)
        {
            float r = Radius(u, v);
            float angle = Mathf.Atan2(v * 2f - 1f, u * 2f - 1f);
            float spike = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 3f)), 8f);
            float reach = (0.25f + 0.75f * spike) * (0.75f + 0.25f * Mathf.Sin(angle * 5f + 1.3f));
            float petal = Mathf.Clamp01(1f - r / Mathf.Max(0.01f, reach));
            float core = Mathf.Clamp01(1f - r / 0.4f);
            return Mathf.Max(petal * petal, core * core);
        }

        private static float SmokeAlpha(float u, float v)
        {
            float falloff = Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 1.5f);
            float noise = Mathf.PerlinNoise(u * 4f + 3.1f, v * 4f + 7.7f) * 0.6f + Mathf.PerlinNoise(u * 9f + 1.7f, v * 9f) * 0.4f;
            return falloff * Mathf.Lerp(0.35f, 1f, noise);
        }

        /// <summary>흰색 + 알파 모양. 색은 재질의 HDR Tint 와 파티클 색이 정한다</summary>
        private static Texture2D WriteTexture(string name, int width, int height, System.Func<float, float, float> alpha)
        {
            string path = $"{TextureFolder}/{name}.png";
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float a = Mathf.Clamp01(alpha((x + 0.5f) / width, (y + 0.5f) / height));
                    pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            tex.SetPixels32(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Materials MakeMaterials(Shader shader, Texture2D star, Texture2D glow, Texture2D smoke, Texture2D line)
        {
            return new Materials
            {
                Star = SaveMaterial("M_MuzzleStar", shader, star, new Color(4.5f, 1.8f, 0.45f, 1f)),
                Core = SaveMaterial("M_FlashCore", shader, glow, new Color(6f, 4.2f, 2.2f, 1f)),
                Glow = SaveMaterial("M_FlameGlow", shader, glow, new Color(4.5f, 1.8f, 0.45f, 1f)),
                Spark = SaveMaterial("M_Spark", shader, glow, new Color(6f, 2.4f, 0.6f, 1f)),
                Smoke = SaveMaterial("M_Smoke", shader, smoke, new Color(0.42f, 0.4f, 0.38f, 0.5f)),
                Tracer = SaveMaterial("M_Tracer", shader, line, new Color(5f, 2.8f, 1.1f, 1f)),
            };
        }

        private static Material SaveMaterial(string name, Shader shader, Texture2D texture, Color tint)
        {
            var material = new Material(shader);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_TintColor", tint);
            return SaveMaterial($"{MaterialFolder}/{name}.mat", material);
        }

        /// <summary>같은 경로에 있으면 내용만 덮어써 GUID(연결)를 유지한다</summary>
        private static Material SaveMaterial(string path, Material material)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
            existing.shader = material.shader;
            existing.CopyPropertiesFromMaterial(material);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(material);
            return existing;
        }
        #endregion

        #region 프리팹
        private static VfxSet BuildAll(Materials m)
        {
            return new VfxSet
            {
                FlashSmall = BuildMuzzleFlash(m, new FlashSpec
                {
                    Name = "MuzzleFlash_Small", FlashSize = 0.18f, SideWidth = 0.07f, SideLength = 3f,
                    Sparks = 3, SmokeCount = 1, SmokeSize = 0.08f, LightRange = 3f, LightIntensity = 2f,
                }),
                FlashMedium = BuildMuzzleFlash(m, new FlashSpec
                {
                    Name = "MuzzleFlash_Medium", FlashSize = 0.26f, SideWidth = 0.1f, SideLength = 3.5f,
                    Sparks = 5, SmokeCount = 2, SmokeSize = 0.12f, LightRange = 4f, LightIntensity = 3f,
                }),
                FlashLarge = BuildMuzzleFlash(m, new FlashSpec
                {
                    Name = "MuzzleFlash_Large", FlashSize = 0.38f, SideWidth = 0.15f, SideLength = 3.5f,
                    Sparks = 8, SmokeCount = 3, SmokeSize = 0.2f, LightRange = 5f, LightIntensity = 4f,
                }),
                FlashLauncher = BuildMuzzleFlash(m, new FlashSpec
                {
                    Name = "MuzzleFlash_Launcher", FlashSize = 0.3f, SideWidth = 0.14f, SideLength = 2.5f,
                    Sparks = 4, SmokeCount = 5, SmokeSize = 0.35f, LightRange = 4f, LightIntensity = 3f,
                }),
                TracerStandard = BuildTracer(m, "Tracer_Standard", 0.03f, 180f, 4f, 0.35f),
                TracerPellet = BuildTracer(m, "Tracer_Pellet", 0.018f, 160f, 2f, 0.2f),
                TracerSniper = BuildTracer(m, "Tracer_Sniper", 0.06f, 320f, 10f, 0.7f),
                ImpactBullet = BuildImpact(m, "Impact_Bullet", 1f, 6),
                ImpactPellet = BuildImpact(m, "Impact_Pellet", 0.6f, 3),
                ImpactSniper = BuildImpact(m, "Impact_Sniper", 1.8f, 12),
                RocketTrail = BuildRocketTrail(m),
                GrenadeTrail = BuildGrenadeTrail(m),
                ExplosionGrenade = BuildOrangeExplosion("Grenade/PS_Explosion_Grenade", "Explosion_Grenade"),
                ExplosionRocket = BuildOrangeExplosion("Rocket/PS_Explosion_Rocket", "Explosion_Rocket"),
            };
        }

        /// <summary>
        /// 앞: 별 모양 화염 + 밝은 중심 (총구에 붙어 있음, 0.05초)
        /// 옆: 총구 방향으로 늘린 화염 (3인칭에서 보이는 불기둥)
        /// 불똥·연기는 월드에 남아 총을 움직여도 제자리에서 흩어진다
        /// </summary>
        private static GameObject BuildMuzzleFlash(Materials m, in FlashSpec s)
        {
            var root = new GameObject(s.Name);

            var front = AddSystem(root.transform, "Front", m.Star, local: true);
            Burst(front, 1, 1);
            SetMain(front, lifetime: (0.04f, 0.06f), size: (s.FlashSize * 0.8f, s.FlashSize * 1.2f), randomRotation: true);
            Grow(front, 0.6f);

            var core = AddSystem(root.transform, "Core", m.Core, local: true);
            Burst(core, 1, 1);
            SetMain(core, lifetime: (0.04f, 0.05f), size: (s.FlashSize * 0.45f, s.FlashSize * 0.55f));

            var side = AddSystem(root.transform, "Side", m.Glow, local: true);
            Burst(side, 1, 1);
            SetMain(side, lifetime: (0.04f, 0.06f), size: (s.SideWidth * 0.85f, s.SideWidth * 1.15f), speed: (0.01f, 0.01f));
            Cone(side, 0f, 0.001f);
            Stretch(side, s.SideLength, 0f);
            side.transform.localPosition = new Vector3(0f, 0f, s.SideWidth * s.SideLength * 0.5f);

            var sparks = AddSystem(root.transform, "Sparks", m.Spark, local: false);
            Burst(sparks, Mathf.Max(1, s.Sparks - 2), s.Sparks);
            SetMain(sparks, lifetime: (0.08f, 0.18f), size: (0.015f, 0.03f), speed: (4f, 10f));
            Cone(sparks, 20f, 0.01f);
            Stretch(sparks, 1f, 0.015f);

            var smoke = AddSystem(root.transform, "Smoke", m.Smoke, local: false);
            Burst(smoke, s.SmokeCount, s.SmokeCount);
            SetMain(smoke, lifetime: (0.5f, 0.8f), size: (s.SmokeSize * 0.8f, s.SmokeSize * 1.2f), speed: (0.3f, 0.8f), randomRotation: true);
            Cone(smoke, 15f, 0.02f);
            Grow(smoke, 3f);
            Fade(smoke, 0.5f, fadeIn: true);

            var light = AddLight(root.transform, s.LightRange, s.LightIntensity);
            light.transform.localPosition = new Vector3(0f, 0f, 0.05f);

            var flash = root.AddComponent<MuzzleFlash>();
            var so = new SerializedObject(flash);
            so.FindProperty("flashLight").objectReferenceValue = light;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, s.Name);
        }

        /// <summary>꼬리(0) 투명 → 머리(1) 밝게. 머리 쪽이 조금 더 굵다. 밝은 총알 머리(headLength)는 BulletTracer 가 실행 중에 만든다</summary>
        private static BulletTracer BuildTracer(Materials m, string name, float width, float speed, float length, float headLength)
        {
            var root = new GameObject(name);
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = m.Tracer;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPositions(new[] { Vector3.zero, Vector3.forward });
            line.widthMultiplier = width;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1f));
            line.colorGradient = MakeGradient(new Color(1f, 0.55f, 0.25f), Color.white, 0f, 1f);
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;

            var tracer = root.AddComponent<BulletTracer>();
            var so = new SerializedObject(tracer);
            so.FindProperty("speed").floatValue = speed;
            so.FindProperty("length").floatValue = length;
            so.FindProperty("headLength").floatValue = headLength;
            so.ApplyModifiedPropertiesWithoutUndo();

            return SavePrefab(root, name).GetComponent<BulletTracer>();
        }

        /// <summary>
        /// 히트스캔 탄착: 밝은 섬광 + 튀는 불똥(중력) + 먼지. 로컬 +Z = 맞은 면의 법선 (Gun.SpawnImpact 가 LookRotation(normal) 로 만든다).
        /// 연사가 빨라 불빛(Light)은 넣지 않는다. 총이 2초 뒤 지운다
        /// </summary>
        private static GameObject BuildImpact(Materials m, string name, float scale, int sparkCount)
        {
            var root = new GameObject(name);

            var flash = AddSystem(root.transform, "Flash", m.Core, local: true);
            Burst(flash, 1, 1);
            SetMain(flash, lifetime: (0.05f, 0.07f), size: (0.12f * scale, 0.16f * scale));
            flash.transform.localPosition = new Vector3(0f, 0f, 0.02f); // 면에 묻히지 않게

            var star = AddSystem(root.transform, "Star", m.Star, local: true);
            Burst(star, 1, 1);
            SetMain(star, lifetime: (0.04f, 0.06f), size: (0.18f * scale, 0.24f * scale), randomRotation: true);
            star.transform.localPosition = new Vector3(0f, 0f, 0.02f);

            var sparks = AddSystem(root.transform, "Sparks", m.Spark, local: false);
            Burst(sparks, Mathf.Max(1, sparkCount - 2), sparkCount);
            SetMain(sparks, lifetime: (0.15f, 0.35f), size: (0.012f * scale, 0.022f * scale), speed: (2f * scale, 5f * scale));
            var sparkMain = sparks.main;
            sparkMain.gravityModifier = 1.5f;
            Cone(sparks, 45f, 0.01f);
            Stretch(sparks, 1f, 0.02f);

            var dust = AddSystem(root.transform, "Dust", m.Smoke, local: false);
            Burst(dust, 2, 3);
            SetMain(dust, lifetime: (0.4f, 0.7f), size: (0.08f * scale, 0.12f * scale), speed: (0.3f, 0.8f), randomRotation: true);
            Cone(dust, 30f, 0.02f);
            Grow(dust, 3f);
            Fade(dust, 0.6f, fadeIn: true);

            return SavePrefab(root, name);
        }

        /// <summary>로켓 뒤 불꽃(로켓에 붙음) + 연기 줄기(월드에 남음) + 불빛</summary>
        private static ProjectileTrail BuildRocketTrail(Materials m)
        {
            var root = new GameObject("Trail_Rocket");

            var flame = AddSystem(root.transform, "Flame", m.Glow, local: true, loop: true);
            Rate(flame, 60f, 0f);
            SetMain(flame, lifetime: (0.04f, 0.07f), size: (0.25f, 0.4f), randomRotation: true);
            flame.transform.localPosition = new Vector3(0f, 0f, -0.3f);

            var core = AddSystem(root.transform, "Core", m.Core, local: true, loop: true);
            Rate(core, 40f, 0f);
            SetMain(core, lifetime: (0.04f, 0.06f), size: (0.12f, 0.18f));
            core.transform.localPosition = new Vector3(0f, 0f, -0.3f);

            var smoke = AddSystem(root.transform, "Smoke", m.Smoke, local: false, loop: true);
            Rate(smoke, 0f, 10f);
            SetMain(smoke, lifetime: (1f, 1.5f), size: (0.25f, 0.35f), randomRotation: true);
            Grow(smoke, 4f);
            Fade(smoke, 0.6f, fadeIn: false);
            smoke.transform.localPosition = new Vector3(0f, 0f, -0.35f);

            AddLight(root.transform, 3f, 2f).transform.localPosition = new Vector3(0f, 0f, -0.4f);

            root.AddComponent<ProjectileTrail>();
            return SavePrefab(root, "Trail_Rocket").GetComponent<ProjectileTrail>();
        }

        /// <summary>유탄: 신관 불씨 + 가는 연기</summary>
        private static ProjectileTrail BuildGrenadeTrail(Materials m)
        {
            var root = new GameObject("Trail_Grenade");

            var ember = AddSystem(root.transform, "Ember", m.Spark, local: true, loop: true);
            Rate(ember, 30f, 0f);
            SetMain(ember, lifetime: (0.04f, 0.06f), size: (0.1f, 0.14f));

            var smoke = AddSystem(root.transform, "Smoke", m.Smoke, local: false, loop: true);
            Rate(smoke, 0f, 6f);
            SetMain(smoke, lifetime: (0.5f, 0.7f), size: (0.06f, 0.1f), randomRotation: true);
            Grow(smoke, 3f);
            Fade(smoke, 0.45f, fadeIn: false);

            root.AddComponent<ProjectileTrail>();
            return SavePrefab(root, "Trail_Grenade").GetComponent<ProjectileTrail>();
        }

        private static GameObject SavePrefab(GameObject root, string name)
        {
            var asset = PrefabUtility.SaveAsPrefabAsset(root, $"{Root}/{name}.prefab");
            Object.DestroyImmediate(root);
            return asset;
        }
        #endregion

        #region 파티클 도구
        private static ParticleSystem AddSystem(Transform parent, string name, Material material, bool local, bool loop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = loop;
            main.duration = loop ? 1f : 0.2f;
            main.playOnAwake = true;
            // 총 모델 스케일과 상관없이 월드 크기(m) 그대로
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.startColor = Color.white;
            main.maxParticles = 100;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.enabled = false;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static void SetMain(ParticleSystem ps, (float min, float max) lifetime, (float min, float max) size,
                                    (float min, float max) speed = default, bool randomRotation = false)
        {
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.min, lifetime.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            if (randomRotation) main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        private static void Burst(ParticleSystem ps, int min, int max)
        {
            ps.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)min, (short)max) });
        }

        private static void Rate(ParticleSystem ps, float perSecond, float perMeter)
        {
            var emission = ps.emission;
            emission.rateOverTime = perSecond;
            emission.rateOverDistance = perMeter;
        }

        /// <summary>로컬 +Z(총구 방향) 원뿔</summary>
        private static void Cone(ParticleSystem ps, float angle, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
        }

        private static void Stretch(ParticleSystem ps, float lengthScale, float velocityScale)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = lengthScale;
            renderer.velocityScale = velocityScale;
        }

        /// <summary>수명 동안 크기가 1 → endScale 배</summary>
        private static void Grow(ParticleSystem ps, float endScale)
        {
            float max = Mathf.Max(1f, endScale);
            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(max,
                new AnimationCurve(new Keyframe(0f, 1f / max), new Keyframe(1f, endScale / max)));
        }

        /// <summary>최대 불투명도 peak 에서 서서히 사라진다. fadeIn 이면 처음에 잠깐 나타나는 구간을 둔다</summary>
        private static void Fade(ParticleSystem ps, float peak, bool fadeIn)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                fadeIn
                    ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(peak, 0.1f), new GradientAlphaKey(0f, 1f) }
                    : new[] { new GradientAlphaKey(peak, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;
        }

        private static Gradient MakeGradient(Color from, Color to, float fromAlpha, float toAlpha)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(fromAlpha, 0f), new GradientAlphaKey(toAlpha, 1f) });
            return gradient;
        }

        private static Light AddLight(Transform parent, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LightColor;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }
        #endregion

        #region QFX 폭발 색 바꾸기
        /// <summary>QFX 폭발을 복사해 파란 계열 색을 모두 주황 계열로. 텍스처는 흑백이라 색 값만 바꾸면 된다</summary>
        private static GameObject BuildOrangeExplosion(string sourceName, string targetName)
        {
            string sourcePath = $"{QfxPrefabs}/{sourceName}.prefab";
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null)
            {
                EditorUtility.DisplayDialog("무기 이펙트 설정", $"QFX 폭발 프리팹이 없습니다:\n{sourcePath}", "확인");
                return null;
            }

            var go = Object.Instantiate(source);
            go.name = targetName;
            var recolored = new Dictionary<Material, Material>();
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null) materials[i] = OrangeMaterial(materials[i], recolored);
                renderer.sharedMaterials = materials;
            }
            // 파티클 시작 색·수명 색, 불빛 색, 선·꼬리 색, QFX 불빛 애니메이터 등 직렬화된 색을 모두 바꾼다
            foreach (var component in go.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform) continue;
                RecolorSerialized(component);
            }
            FitToDamageRadius(go);
            return SavePrefab(go, targetName);
        }

        /// <summary>
        /// 피해 반경에 맞춰 키울 수 있게 만든다 (GunExplosion 이 ExplosionVisual.Fit 을 부른다).
        ///  - 하위 파티클도 루트 크기를 따르도록 Hierarchy, 위쪽 반구로만 퍼지던 불꽃·스파크는 구로 (공중 폭발도 둥글게)
        ///  - 기준 반경 = 루트 구(Sphere 메시 반경 1m) 크기. 충격파 원판(Disk 메시 반경 약 1.09m)은 그 구와 같은 반경으로
        /// </summary>
        private static void FitToDamageRadius(GameObject go)
        {
            const float diskMeshRadius = 1.0928f;
            float baseRadius = go.GetComponent<ParticleSystem>().main.startSize.constant;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var shape = ps.shape;
                if (shape.shapeType == ParticleSystemShapeType.Hemisphere) shape.shapeType = ParticleSystemShapeType.Sphere;
                if (ps.name == "PS_ShockWave") main.startSize = baseRadius / diskMeshRadius;
            }
            var visual = new SerializedObject(go.AddComponent<ExplosionVisual>());
            visual.FindProperty("baseRadius").floatValue = baseRadius;
            visual.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Material OrangeMaterial(Material source, Dictionary<Material, Material> cache)
        {
            if (cache.TryGetValue(source, out var cached)) return cached;
            var material = new Material(source);
            var shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Color) continue;
                int id = shader.GetPropertyNameId(i);
                material.SetColor(id, ToOrange(material.GetColor(id)));
            }
            EnsureFolder(MaterialFolder + "/Explosion");
            var saved = SaveMaterial($"{MaterialFolder}/Explosion/{source.name}_Orange.mat", material);
            cache[source] = saved;
            return saved;
        }

        private static void RecolorSerialized(Object target)
        {
            var so = new SerializedObject(target);
            var prop = so.GetIterator();
            bool enterChildren = true;
            while (prop.Next(enterChildren))
            {
                enterChildren = true;
                if (prop.propertyType == SerializedPropertyType.Color)
                {
                    prop.colorValue = ToOrange(prop.colorValue);
                }
                else if (prop.propertyType == SerializedPropertyType.Gradient)
                {
                    var gradient = prop.gradientValue;
                    var keys = gradient.colorKeys;
                    for (int i = 0; i < keys.Length; i++) keys[i].color = ToOrange(keys[i].color);
                    gradient.SetKeys(keys, gradient.alphaKeys);
                    prop.gradientValue = gradient;
                    enterChildren = false; // 그라데이션 안의 키 색을 한 번 더 바꾸지 않게
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 채도가 있는 색만 색상(Hue)을 바꾸고 밝기·채도·HDR 세기·알파는 유지 (흰색·회색 연기는 그대로).
        /// 하늘색일수록 노란 주황, 진한 파랑일수록 붉은 주황 → 원본의 색 차이(중심·가장자리)가 남는다
        /// </summary>
        private static Color ToOrange(Color c)
        {
            float intensity = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (intensity <= 0.0001f) return c;
            float scale = Mathf.Max(1f, intensity);
            Color.RGBToHSV(new Color(c.r / scale, c.g / scale, c.b / scale), out float h, out float s, out float v);
            if (s < 0.15f) return c;
            float hue = Mathf.Lerp(0.12f, 0.02f, Mathf.InverseLerp(0.5f, 0.67f, h));
            Color result = Color.HSVToRGB(hue, s, v) * scale;
            result.a = c.a;
            return result;
        }
        #endregion

        #region 연결
        private static int AssignPlayerPrefab(VfxSet set)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                Debug.LogWarning($"[WeaponVfxSetup] Player 프리팹이 없어 건너뜀: {PlayerPrefabPath}");
                return 0;
            }
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            int count = AssignGuns(root, set);
            AssignGrenades(root, set);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            return count;
        }

        /// <summary>PlayerTest 씬이 열려 있지 않으면 추가로 열어 연결·저장한 뒤 닫는다 (지금 작업 중인 씬은 그대로)</summary>
        private static int AssignScene(VfxSet set)
        {
            if (File.Exists(ScenePath) == false) return 0;
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool opened = false;
            if (scene.isLoaded == false)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                opened = true;
            }

            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                count += AssignGuns(root, set);
                AssignGrenades(root, set);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene, true);
            return count;
        }

        private static int AssignGuns(GameObject root, VfxSet set)
        {
            int count = 0;
            foreach (var gun in root.GetComponentsInChildren<Gun>(true))
            {
                var so = new SerializedObject(gun);
                switch (gun)
                {
                    case Pistol _:
                    case SMG _:
                        Set(so, "muzzleFlashPrefab", set.FlashSmall);
                        Set(so, "tracerPrefab", set.TracerStandard);
                        Set(so, "impactEffectPrefab", set.ImpactBullet);
                        break;
                    case AssaultRifle _:
                    case Rifle _:
                        Set(so, "muzzleFlashPrefab", set.FlashMedium);
                        Set(so, "tracerPrefab", set.TracerStandard);
                        Set(so, "impactEffectPrefab", set.ImpactBullet);
                        break;
                    case ShotGun _:
                        Set(so, "muzzleFlashPrefab", set.FlashLarge);
                        Set(so, "tracerPrefab", set.TracerPellet);
                        Set(so, "impactEffectPrefab", set.ImpactPellet);
                        break;
                    case SniperRifle _:
                        Set(so, "muzzleFlashPrefab", set.FlashLarge);
                        Set(so, "tracerPrefab", set.TracerSniper);
                        Set(so, "impactEffectPrefab", set.ImpactSniper);
                        break;
                    case RocketLauncher _:
                        Set(so, "muzzleFlashPrefab", set.FlashLauncher);
                        Set(so, "explosion.effectPrefab", set.ExplosionRocket);
                        Set(so, "rocketTrail", set.RocketTrail);
                        break;
                    case GrenadeLauncher _:
                        Set(so, "muzzleFlashPrefab", set.FlashLauncher);
                        Set(so, "explosion.effectPrefab", set.ExplosionGrenade);
                        Set(so, "grenadeTrail", set.GrenadeTrail);
                        break;
                    default:
                        Debug.LogWarning($"[WeaponVfxSetup] 이펙트 규칙이 없는 총: {gun.GetType().Name}", gun);
                        continue;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                count++;
            }
            return count;
        }

        /// <summary>PlayerItems 가 던지는 수류탄 프리팹(에셋)에 폭발 이펙트 연결</summary>
        private static void AssignGrenades(GameObject root, VfxSet set)
        {
            foreach (var items in root.GetComponentsInChildren<PlayerItems>(true))
            {
                var grenade = new SerializedObject(items).FindProperty("grenadePrefab").objectReferenceValue as Grenade;
                if (grenade == null || EditorUtility.IsPersistent(grenade) == false) continue;
                var so = new SerializedObject(grenade);
                Set(so, "explosionEffectPrefab", set.ExplosionGrenade);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(grenade);
            }
        }

        private static void Set(SerializedObject so, string path, Object value)
        {
            var prop = so.FindProperty(path);
            if (prop == null)
            {
                Debug.LogWarning($"[WeaponVfxSetup] {so.targetObject.GetType().Name} 에 {path} 가 없습니다", so.targetObject);
                return;
            }
            prop.objectReferenceValue = value;
        }
        #endregion

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
