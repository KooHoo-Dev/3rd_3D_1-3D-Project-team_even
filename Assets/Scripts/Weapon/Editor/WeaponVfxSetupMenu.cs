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
    ///  4. QFX 폭발(유탄·로켓)을 복사해 파란색 → 주황색으로 바꾼다 (재질 색 + 파티클·불빛 색). 피해 반경에 맞춰 커지도록 ExplosionVisual 을 붙인다.
    ///     유탄 폭발은 어두운 맵에 맞게 섬광·불빛·위로 솟는 폭풍·먼지 링·불빛 받은 연기·불씨·파편을 더하고, 피해 지점 이펙트(Impact_Explosion)를 단다
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
        private const string HeatDistortionShader = "HybridShooter/Effects/Explosion Heat Distortion";

        private static readonly Color LightColor = new Color(1f, 0.6f, 0.25f);

        private sealed class Materials
        {
            public Material Star, Core, Glow, Spark, Smoke, Tracer, Dust, FireLitSmoke;
        }

        /// <summary>유탄 폭발을 어두운 맵에 맞게 꾸밀 재료 (AddGrenadeBlast)</summary>
        private sealed class GrenadeBlast
        {
            public Materials Materials;
            public Shader HeatDistortion;
            /// <summary>폭발 피해를 받은 지점에 재생 (ExplosionVisual.PlayHit)</summary>
            public GameObject HitEffect;
        }

        private sealed class VfxSet
        {
            public GameObject FlashSmall, FlashMedium, FlashLarge, FlashLauncher;
            public BulletTracer TracerStandard, TracerPellet, TracerSniper;
            public GameObject ImpactBullet, ImpactPellet, ImpactSniper;
            public GameObject ExplosionGrenade, ExplosionRocket, ExplosionFuelCell;
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
            if (TryFindShaders(out Shader shader, out Shader heatShader) == false) return;

            EnsureFolder(Root);
            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);

            MakeTextures(out var star, out var glow, out var smoke, out var line);
            VfxSet set = BuildAll(MakeMaterials(shader, star, glow, smoke, line), heatShader);
            if (set.ExplosionGrenade == null || set.ExplosionRocket == null || set.ExplosionFuelCell == null) return;

            int guns = AssignPlayerPrefab(set) + AssignScene(set);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WeaponVfxSetup] 이펙트 생성 완료 ({Root}), 총 {guns}개에 연결");
            EditorUtility.DisplayDialog("무기 이펙트 설정", $"이펙트를 만들고 총 {guns}개에 연결했습니다.\n{Root}", "확인");
        }

        /// <summary>
        /// 연료 셀 폭발(Explosion_FuelCell)만 다시 만든다. 다른 무기 이펙트·Player 프리팹·PlayerTest 씬은 건드리지 않는다.
        /// 재질·피해 지점 이펙트(Impact_Explosion)가 이미 있으면 그대로 쓴다 (없을 때만 만든다)
        /// </summary>
        [MenuItem("Tools/HybridShooter/Map/연료 셀/0. 연료 셀 폭발 이펙트만 만들기")]
        private static void BuildFuelCellExplosionMenu()
        {
            var prefab = BuildFuelCellExplosion();
            if (prefab == null) return;
            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;
            Debug.Log($"[WeaponVfxSetup] 연료 셀 폭발 이펙트 생성: {FuelCellExplosionPath}");
        }

        public const string FuelCellExplosionPath = Root + "/Explosion_FuelCell.prefab";

        /// <summary>연료 셀 폭발만 만든다 (FuelCellSceneTool 이 프리팹이 없을 때 부른다). 실패하면 null</summary>
        internal static GameObject BuildFuelCellExplosion()
        {
            if (TryFindShaders(out Shader shader, out Shader heatShader) == false) return null;
            EnsureFolder(Root);
            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);

            Materials m = LoadMaterials();
            if (m == null)
            {
                MakeTextures(out var star, out var glow, out var smoke, out var line);
                m = MakeMaterials(shader, star, glow, smoke, line);
            }
            var hit = AssetDatabase.LoadAssetAtPath<GameObject>($"{Root}/Impact_Explosion.prefab") ?? BuildExplosionHit(m);
            return BuildOrangeExplosion("Grenade/PS_Explosion_Grenade", "Explosion_FuelCell",
                new GrenadeBlast { Materials = m, HeatDistortion = heatShader, HitEffect = hit }, fuel: true);
        }

        private static bool TryFindShaders(out Shader shader, out Shader heatShader)
        {
            shader = Shader.Find(ParticleShader);
            heatShader = Shader.Find(HeatDistortionShader);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("무기 이펙트 설정", $"셰이더를 찾지 못했습니다:\n{ParticleShader}\n(QFX Sci-Fi VFX 에셋 필요)", "확인");
                return false;
            }
            if (heatShader == null)
            {
                EditorUtility.DisplayDialog("무기 이펙트 설정", $"셰이더를 찾지 못했습니다:\n{HeatDistortionShader}\n({Root}/Shaders/ExplosionHeatDistortion.shader)", "확인");
                return false;
            }
            return true;
        }

        /// <summary>이미 만든 재질을 읽는다. 하나라도 없으면 null (그때는 MakeMaterials 로 새로)</summary>
        private static Materials LoadMaterials()
        {
            Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
            var m = new Materials
            {
                Star = Load("M_MuzzleStar"), Core = Load("M_FlashCore"), Glow = Load("M_FlameGlow"), Spark = Load("M_Spark"),
                Smoke = Load("M_Smoke"), Tracer = Load("M_Tracer"), Dust = Load("M_ExplosionDust"), FireLitSmoke = Load("M_ExplosionSmoke"),
            };
            bool complete = m.Star && m.Core && m.Glow && m.Spark && m.Smoke && m.Tracer && m.Dust && m.FireLitSmoke;
            return complete ? m : null;
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

        /// <summary>
        /// 셰이더 색 = 2 × 파티클 색 × Tint × 텍스처. 파티클 색은 0~1 로 잘리므로 1 을 넘는 밝기(HDR)는 Tint 가 맡는다.
        ///  - Dust: 폭발 불빛을 받은 따뜻한 회색 먼지 (어두운 맵에서 묻히지 않게 밝게)
        ///  - FireLitSmoke: Tint 를 밝게 두고 색은 파티클 수명 색이 정한다 (불빛 주황 → 회색 → 청회색, FireLitSmokeColor)
        /// </summary>
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
                Dust = SaveMaterial("M_ExplosionDust", shader, smoke, new Color(0.375f, 0.325f, 0.275f, 0.5f)),
                FireLitSmoke = SaveMaterial("M_ExplosionSmoke", shader, smoke, new Color(1f, 1f, 1f, 0.5f)),
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
        private static VfxSet BuildAll(Materials m, Shader heatDistortion)
        {
            var explosionHit = BuildExplosionHit(m);
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
                ExplosionGrenade = BuildOrangeExplosion("Grenade/PS_Explosion_Grenade", "Explosion_Grenade", new GrenadeBlast
                {
                    Materials = m, HeatDistortion = heatDistortion, HitEffect = explosionHit,
                }),
                ExplosionRocket = BuildOrangeExplosion("Rocket/PS_Explosion_Rocket", "Explosion_Rocket", null),
                // 연료 셀(맵 폭발물): 유탄 폭발 위에 바닥을 휩쓰는 화염·불덩이·잔불을 더한다 (무기에는 연결하지 않는다)
                ExplosionFuelCell = BuildOrangeExplosion("Grenade/PS_Explosion_Grenade", "Explosion_FuelCell", new GrenadeBlast
                {
                    Materials = m, HeatDistortion = heatDistortion, HitEffect = explosionHit,
                }, fuel: true),
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

        /// <summary>
        /// 폭발 피해를 받은 지점: 불똥 + 그을음 연기 (0.3초). 피와 함께 "불에 맞았다"가 보이게.
        /// 로컬 +Z = 폭발 중심 → 대상 (ExplosionVisual.PlayHit 이 그 방향으로 만들고 1초 뒤 지운다)
        /// </summary>
        private static GameObject BuildExplosionHit(Materials m)
        {
            var root = new GameObject("Impact_Explosion");

            var sparks = AddSystem(root.transform, "Sparks", m.Spark, local: false);
            Burst(sparks, 8, 8);
            SetMain(sparks, lifetime: (0.15f, 0.3f), size: (0.03f, 0.05f), speed: (3f, 7f));
            var sparkMain = sparks.main;
            sparkMain.gravityModifier = 1f;
            Cone(sparks, 40f, 0.05f);
            Stretch(sparks, 1f, 0.02f);

            var smoke = AddSystem(root.transform, "Smoke", m.Smoke, local: false);
            Burst(smoke, 2, 2);
            SetMain(smoke, lifetime: (0.25f, 0.3f), size: (0.4f, 0.6f), speed: (0.5f, 1f), randomRotation: true);
            Cone(smoke, 30f, 0.05f);
            Grow(smoke, 2f);
            Fade(smoke, 0.6f, fadeIn: true);

            return SavePrefab(root, "Impact_Explosion");
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

        /// <summary>파티클 시작 색 (0~1. 셰이더가 2 × Tint 를 곱한다)</summary>
        private static void Tint(ParticleSystem ps, Color color)
        {
            var main = ps.main;
            main.startColor = color;
        }

        /// <summary>모양(원뿔·원)의 로컬 +Z 를 위(+Y)로 돌린다</summary>
        private static void PointUp(ParticleSystem ps)
        {
            var shape = ps.shape;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        /// <summary>처음 속도가 수명 비율 stopAt 까지 일정하게 줄어 멈춘다 → 한 번에 퍼진 뒤 그 자리에 머문다. 이동 거리 = 속도 × 수명 × stopAt / 2</summary>
        private static void Brake(ParticleSystem ps, float stopAt)
        {
            float slope = -1f / stopAt;
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.speedModifier = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f, 0f, slope), new Keyframe(stopAt, 0f, slope, 0f), new Keyframe(1f, 0f, 0f, 0f)));
        }

        /// <summary>
        /// 불빛을 받은 연기: 주황(밝기 약 1.5) → 따뜻한 회색(0.5) → 청회색(0.35, 맵 안개색 쪽). 최대 불투명도 0.5 (M_ExplosionSmoke 기준)
        /// 어두운 맵에서 검은 연기는 묻히므로 배경보다 밝게 식어 가고, 멀리서는 안개에 섞인다
        /// </summary>
        private static void FireLitSmokeColor(ParticleSystem ps)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.75f, 0.33f, 0.1f), 0f),
                    new GradientColorKey(new Color(0.25f, 0.23f, 0.21f), 0.15f),
                    new GradientColorKey(new Color(0.16f, 0.18f, 0.21f), 0.6f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.08f),
                    new GradientAlphaKey(0.35f, 0.5f), new GradientAlphaKey(0f, 1f),
                });
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
        /// <summary>
        /// QFX 폭발을 복사해 파란 계열 색을 모두 주황 계열로. 텍스처는 흑백이라 색 값만 바꾸면 된다.
        /// blast 가 있으면 어두운 맵에 맞게 층을 더한다 (null = 원본 그대로)
        /// </summary>
        private static GameObject BuildOrangeExplosion(string sourceName, string targetName, GrenadeBlast blast, bool fuel = false)
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
            // 주황으로 바뀐 뒤에 복제해야 새 파티클이 다시 색을 바꾸지 않고, FitToDamageRadius 보다 먼저여야 피해 반경을 따라 커진다
            if (blast != null) AddGrenadeBlast(go, blast);
            if (blast != null && fuel) AddFuelBlast(go, blast.Materials);
            FitToDamageRadius(go, blast?.HitEffect);
            return SavePrefab(go, targetName);
        }

        /// <summary>
        /// 유탄 폭발을 어두운 맵(환경광 0.2·청회색 안개·ACES)에 맞게 꾸민다. 범위를 선으로 그리지 않고,
        /// 섬광 → 불덩이·위로 솟는 폭풍 → 바닥 먼지 링 → 불빛 받은 연기·불씨 순으로 겹쳐 크기를 느끼게 한다.
        /// 수치는 루트 구 반경(5m) 기준이고 ExplosionVisual.Fit 이 피해 반경에 맞춰 키운다.
        ///  - 불빛: 어두운 맵에서 가장 잘 보이는 층. 범위 = 반경 × 1.3 → 밝혀진 바닥·공중몹 몸이 곧 범위로 읽힌다
        ///  - 아지랑이(원본 구): URP 화면 복사에 투명 파티클이 없어 불꽃보다 나중에 그리면 불꽃을 덮는다
        ///    → 깊이를 쓰지 않고 불꽃보다 먼저 그리는 복사본 셰이더로
        ///  - 폭발 오브젝트 수명 1 → 3초 (연기가 남도록. GunExplosion 은 5초에 지운다)
        /// </summary>
        private static void AddGrenadeBlast(GameObject go, GrenadeBlast blast)
        {
            Materials m = blast.Materials;
            float radius = RangeSphereRadius(go);

            var sphereRenderer = go.GetComponent<ParticleSystemRenderer>();
            var heat = new Material(sphereRenderer.sharedMaterial) { shader = blast.HeatDistortion };
            sphereRenderer.sharedMaterial = SaveMaterial($"{MaterialFolder}/Explosion/M_ExplosionHeatDistortion.mat", heat);

            go.GetComponent<QFX.SFX.SFX_SelfDestroyer>().LifeTime = 3f;

            // 불빛: 0초에 바로 최대 → 0.15초에 1/3 → 0.6초에 꺼짐. 1m 올려 공중몹 몸도 비춘다
            Transform lightTransform = RequireChild(go, "Point light");
            lightTransform.localPosition += Vector3.up;
            lightTransform.GetComponent<Light>().range = radius * 1.3f;
            var lightAnimator = lightTransform.GetComponent<QFX.SFX.SFX_LightAnimator>();
            lightAnimator.LightIntensity.AnimationCurve = new AnimationCurve(
                new Keyframe(0f, 12f), new Keyframe(0.15f, 4f), new Keyframe(0.6f, 0f));
            lightAnimator.LightIntensity.ValueMultiplier = 1f;
            lightAnimator.LightIntensity.TimeMultiplier = 1f;

            // 섬광: 0.06초 흰빛 노랑 (밝기 약 6 → 블룸)
            var flash = AddSystem(go.transform, "PS_Flash", m.Core, local: true);
            Burst(flash, 1, 1);
            SetMain(flash, lifetime: (0.06f, 0.06f), size: (3f, 3f));
            Tint(flash, new Color(0.5f, 0.5f, 0.5f, 1f));

            // 불덩이: 중심에 묶어 두던 속도 제한을 풀어 0.3초에 반경 40%(2m)까지
            var fire = RequireChild(go, "PS_Fire").GetComponent<ParticleSystem>();
            var fireMain = fire.main;
            fireMain.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7f);
            fireMain.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.45f);
            var fireLimit = fire.limitVelocityOverLifetime;
            fireLimit.enabled = false;

            // 위로 솟는 폭풍(공중몹 높이를 휩쓴다): 25° 원뿔로 0.2초에 반경 높이(5m)까지. 불꽃 12 + 연기 6
            var blastFire = Object.Instantiate(fire.gameObject, go.transform, false);
            blastFire.name = "PS_BlastFire";
            blastFire.transform.localPosition = Vector3.zero;
            var blastFirePs = blastFire.GetComponent<ParticleSystem>();
            var blastFireMain = blastFirePs.main;
            blastFireMain.startLifetime = 0.45f;
            blastFireMain.startSpeed = 37f;
            Cone(blastFirePs, 25f, 0.3f);
            PointUp(blastFirePs);
            var blastFireEmission = blastFirePs.emission;
            blastFireEmission.rateOverTime = 0f;
            Burst(blastFirePs, 12, 12);
            Brake(blastFirePs, 0.3f / 0.45f);

            var blastSmoke = AddSystem(go.transform, "PS_BlastSmoke", m.FireLitSmoke, local: false);
            Burst(blastSmoke, 6, 6);
            SetMain(blastSmoke, lifetime: (1.6f, 2f), size: (1.5f, 2f), speed: (37f, 37f), randomRotation: true);
            Cone(blastSmoke, 25f, 0.3f);
            PointUp(blastSmoke);
            Brake(blastSmoke, 0.3f / 1.8f);
            Grow(blastSmoke, 2.5f);
            FireLitSmokeColor(blastSmoke);
            BehindFire(blastSmoke);

            // 바닥 먼지 링: 0.3초에 반경까지 퍼지고 1초 동안 흐려진다 → 가로 범위
            var dust = AddSystem(go.transform, "PS_DustRing", m.Dust, local: false);
            Burst(dust, 30, 30);
            SetMain(dust, lifetime: (1.2f, 1.4f), size: (1.2f, 2f), speed: (28f, 33f), randomRotation: true);
            var dustShape = dust.shape;
            dustShape.enabled = true;
            dustShape.shapeType = ParticleSystemShapeType.Circle;
            dustShape.radius = 0.3f;
            dustShape.rotation = new Vector3(90f, 0f, 0f); // 원(로컬 XY 면)을 바닥(XZ 면)으로
            Brake(dust, 0.3f / 1.3f);
            Grow(dust, 2f);
            Fade(dust, 0.6f, fadeIn: false);
            BehindFire(dust);

            // 연기 기둥: 불빛 받은 주황 → 회색 → 청회색으로 식으며 솟는다 → 세로 크기감
            var smoke = AddSystem(go.transform, "PS_SmokeColumn", m.FireLitSmoke, local: false);
            Burst(smoke, 10, 10);
            SetMain(smoke, lifetime: (1.5f, 2.5f), size: (2f, 2f), speed: (1f, 2f), randomRotation: true);
            Cone(smoke, 15f, 0.5f);
            PointUp(smoke);
            Grow(smoke, 2.5f);
            FireLitSmokeColor(smoke);
            BehindFire(smoke);

            // 불씨: 어둠 속 하이라이트. 떠오르며 흔들린다
            var embers = AddSystem(go.transform, "PS_Embers", m.Spark, local: false);
            Burst(embers, 25, 25);
            SetMain(embers, lifetime: (0.8f, 2f), size: (0.04f, 0.08f), speed: (1f, 4f));
            Tint(embers, new Color(0.35f, 0.35f, 0.35f, 1f));
            var emberMain = embers.main;
            emberMain.gravityModifier = -0.1f;
            var emberShape = embers.shape;
            emberShape.enabled = true;
            emberShape.shapeType = ParticleSystemShapeType.Sphere;
            emberShape.radius = 1f;
            var emberNoise = embers.noise;
            emberNoise.enabled = true;
            emberNoise.strength = 0.6f;
            emberNoise.frequency = 1f;
            Fade(embers, 1f, fadeIn: false);

            // 파편: 끝이 달아오른 조각 (검은 조각은 어두운 맵에서 안 보인다)
            var debris = AddSystem(go.transform, "PS_Debris", m.Spark, local: false);
            Burst(debris, 15, 15);
            SetMain(debris, lifetime: (0.8f, 0.8f), size: (0.06f, 0.12f), speed: (8f, 14f));
            Tint(debris, new Color(0.125f, 0.125f, 0.125f, 1f));
            var debrisMain = debris.main;
            debrisMain.gravityModifier = 1.5f;
            Cone(debris, 60f, 0.3f);
            PointUp(debris);
            Stretch(debris, 1f, 0.02f);

            // 바닥 충격파 원판: 0.25초 만에 지나가는 링으로
            var shockMain = RequireChild(go, "PS_ShockWave").GetComponent<ParticleSystem>().main;
            shockMain.startLifetime = 0.25f;
        }

        /// <summary>
        /// 연료 셀 폭발: 유탄 폭발(AddGrenadeBlast) 위에 연료가 터지는 층을 더한다. 수치는 루트 구 반경(5m) 기준이고 피해 반경(9m)에 맞춰 커진다.
        ///  - 바닥을 휩쓰는 화염 띠: 0.3초 만에 반경 끝까지 밀려나간다 (피해는 폭발 순간에 들어가므로 빨리 퍼져야 어긋나 보이지 않는다)
        ///  - 굴러 오르는 불덩이 → 그을음 연기 기둥 (연료 폭발은 연기가 짙고 오래 남는다)
        ///  - 잔불·불티 5초 + 일렁이는 불빛 (피해 없음, 보기만)
        ///  - 달아오른 금속 파편 (꼬리를 끌며 높이 튄다)
        ///  - 불빛은 길게: 섬광 뒤 1.2초 동안 일렁이며 꺼진다. 오브젝트 수명 8초 (ExplosiveProp.effectLifetime 과 같게)
        /// 폭발 중심은 셀 가운데(지면 위 약 0.85m)라 바닥 층은 Ground 만큼 내려 놓는다 (배율 1.8 이 곱해진다)
        /// </summary>
        private static void AddFuelBlast(GameObject go, Materials m)
        {
            const float Ground = -0.45f;
            float radius = RangeSphereRadius(go);

            go.GetComponent<QFX.SFX.SFX_SelfDestroyer>().LifeTime = 8f;

            // 주 불빛: 더 밝고 넓게, 1.2초 동안 일렁이며 꺼진다
            Transform lightTransform = RequireChild(go, "Point light");
            lightTransform.GetComponent<Light>().range = radius * 1.6f;
            var lightAnimator = lightTransform.GetComponent<QFX.SFX.SFX_LightAnimator>();
            lightAnimator.LightIntensity.AnimationCurve = new AnimationCurve(
                new Keyframe(0f, 16f), new Keyframe(0.1f, 9f), new Keyframe(0.3f, 6f), new Keyframe(0.45f, 7f),
                new Keyframe(0.7f, 4f), new Keyframe(0.9f, 4.5f), new Keyframe(1.2f, 1.5f), new Keyframe(1.6f, 0f));

            // 잔불 불빛: 바닥 가까이에서 5초 동안 낮게 일렁인다 (QFX 불빛을 복사 → RunAtStart 로 스스로 켜진다)
            var burnLight = Object.Instantiate(lightTransform.gameObject, go.transform, false);
            burnLight.name = "Light_Burn";
            burnLight.transform.localPosition = new Vector3(0f, Ground + 0.4f, 0f);
            burnLight.GetComponent<Light>().range = radius * 0.8f;
            burnLight.GetComponent<QFX.SFX.SFX_LightAnimator>().LightIntensity.AnimationCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.4f, 3f), new Keyframe(0.8f, 2.2f), new Keyframe(1.3f, 2.8f),
                new Keyframe(2f, 2f), new Keyframe(2.7f, 2.5f), new Keyframe(3.5f, 1.6f), new Keyframe(4.3f, 1.8f),
                new Keyframe(5.2f, 0.6f), new Keyframe(6f, 0f));

            // 바닥을 휩쓰는 화염 띠: 바닥 원에서 바깥으로. 이동 거리 = 32 × 0.55 × 0.55 / 2 ≈ 4.8 (반경 끝)
            var wave = AddSystem(go.transform, "PS_FireWave", m.Glow, local: false);
            wave.transform.localPosition = new Vector3(0f, Ground, 0f);
            Burst(wave, 48, 48);
            SetMain(wave, lifetime: (0.5f, 0.6f), size: (1.4f, 2.2f), speed: (30f, 34f), randomRotation: true);
            var waveMain = wave.main;
            waveMain.gravityModifier = -0.3f;
            FlatCircle(wave, 0.3f);
            Brake(wave, 0.55f);
            Grow(wave, 1.6f);
            FlameColor(wave);

            // 화염 띠 뒤로 깔리는 그을음 (불빛 받은 주황 → 짙은 갈색 → 어두운 회색)
            var waveSmoke = AddSystem(go.transform, "PS_FireWaveSmoke", m.FireLitSmoke, local: false);
            waveSmoke.transform.localPosition = new Vector3(0f, Ground + 0.2f, 0f);
            Burst(waveSmoke, 24, 24);
            SetMain(waveSmoke, lifetime: (1.8f, 2.4f), size: (1.6f, 2.4f), speed: (24f, 28f), randomRotation: true);
            var waveSmokeMain = waveSmoke.main;
            waveSmokeMain.gravityModifier = -0.05f;
            FlatCircle(waveSmoke, 0.3f);
            Brake(waveSmoke, 0.35f / 2.1f);
            Grow(waveSmoke, 2.2f);
            SootColor(waveSmoke);
            BehindFire(waveSmoke);

            // 굴러 오르는 불덩이: 크게 부풀며 솟고 노랑 → 주황 → 검붉게 식는다
            var fireball = AddSystem(go.transform, "PS_Fireball", m.Glow, local: false);
            Burst(fireball, 16, 16);
            SetMain(fireball, lifetime: (0.9f, 1.3f), size: (2.2f, 3.2f), speed: (2f, 5f), randomRotation: true);
            var fireballMain = fireball.main;
            fireballMain.gravityModifier = -0.45f;
            var fireballShape = fireball.shape;
            fireballShape.enabled = true;
            fireballShape.shapeType = ParticleSystemShapeType.Sphere;
            fireballShape.radius = 0.6f;
            Grow(fireball, 1.8f);
            FlameColor(fireball);
            var roll = fireball.rotationOverLifetime;
            roll.enabled = true;
            roll.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            var fireballNoise = fireball.noise;
            fireballNoise.enabled = true;
            fireballNoise.strength = 0.8f;
            fireballNoise.frequency = 0.6f;

            // 그을음 연기 기둥: 4~6초 동안 굵게 솟는다
            var soot = AddSystem(go.transform, "PS_SootColumn", m.FireLitSmoke, local: false);
            Burst(soot, 16, 16);
            SetMain(soot, lifetime: (4f, 6f), size: (2.5f, 3.5f), speed: (1.5f, 3f), randomRotation: true);
            Cone(soot, 20f, 0.8f);
            PointUp(soot);
            Grow(soot, 3f);
            var sootMain = soot.main;
            sootMain.gravityModifier = -0.05f;
            SootColor(soot);
            BehindFire(soot);

            // 잔불: 5초 동안 바닥에서 낮게 타오르다 잦아든다
            var groundFire = AddSystem(go.transform, "PS_GroundFire", m.Glow, local: false);
            groundFire.transform.localPosition = new Vector3(0f, Ground, 0f);
            SustainedRate(groundFire, 5f, 26f);
            SetMain(groundFire, lifetime: (0.6f, 1f), size: (0.6f, 1.3f), speed: (0.5f, 1.5f), randomRotation: true);
            var groundFireMain = groundFire.main;
            groundFireMain.gravityModifier = -0.6f;
            FlatCircle(groundFire, 1.6f);
            FlameColor(groundFire);

            // 잔불 불티: 떠오르며 흔들린다
            var groundEmbers = AddSystem(go.transform, "PS_GroundEmbers", m.Spark, local: false);
            groundEmbers.transform.localPosition = new Vector3(0f, Ground, 0f);
            SustainedRate(groundEmbers, 5f, 14f);
            SetMain(groundEmbers, lifetime: (1f, 2f), size: (0.04f, 0.08f), speed: (1f, 3f));
            Tint(groundEmbers, new Color(0.35f, 0.35f, 0.35f, 1f));
            var groundEmberMain = groundEmbers.main;
            groundEmberMain.gravityModifier = -0.2f;
            FlatCircle(groundEmbers, 2f);
            var groundEmberNoise = groundEmbers.noise;
            groundEmberNoise.enabled = true;
            groundEmberNoise.strength = 0.6f;
            groundEmberNoise.frequency = 1f;
            Fade(groundEmbers, 1f, fadeIn: false);

            // 달아오른 금속 파편: 높이 튀며 불꽃 꼬리를 끈다
            var shrapnel = AddSystem(go.transform, "PS_Shrapnel", m.Spark, local: false);
            Burst(shrapnel, 30, 30);
            SetMain(shrapnel, lifetime: (1f, 1.6f), size: (0.08f, 0.16f), speed: (12f, 20f));
            Tint(shrapnel, new Color(0.25f, 0.25f, 0.25f, 1f));
            var shrapnelMain = shrapnel.main;
            shrapnelMain.gravityModifier = 1.6f;
            Cone(shrapnel, 70f, 0.3f);
            PointUp(shrapnel);
            var trails = shrapnel.trails;
            trails.enabled = true;
            trails.lifetime = 0.15f;
            trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            trails.dieWithParticles = true;
            shrapnel.GetComponent<ParticleSystemRenderer>().trailMaterial = m.Spark;
        }

        /// <summary>바닥(XZ 면)에 눕힌 원에서 바깥으로 퍼진다</summary>
        private static void FlatCircle(ParticleSystem ps, float radius)
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.rotation = new Vector3(90f, 0f, 0f); // 원(로컬 XY 면)을 바닥(XZ 면)으로
        }

        /// <summary>duration 초 동안 처음 perSecond 개/초로 내뿜다가 0 으로 줄어든다 (잦아드는 불)</summary>
        private static void SustainedRate(ParticleSystem ps, float duration, float perSecond)
        {
            var main = ps.main;
            main.duration = duration;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(perSecond,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.6f, 0.7f), new Keyframe(1f, 0f)));
        }

        /// <summary>불꽃: 흰빛 노랑 → 주황 → 검붉게 식으며 사라진다 (재질 M_FlameGlow 의 주황 Tint 에 곱해진다)</summary>
        private static void FlameColor(ParticleSystem ps)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f),
                    new GradientColorKey(new Color(1f, 0.6f, 0.3f), 0.3f),
                    new GradientColorKey(new Color(0.55f, 0.15f, 0.05f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;
        }

        /// <summary>
        /// 연료 그을음: 불빛 받은 주황 → 짙은 갈색 → 어두운 회색. 유탄 연기(FireLitSmokeColor)보다 어둡고 진하지만
        /// 완전히 검으면 어두운 맵에 묻히므로 안개색보다 조금 밝게 남긴다
        /// </summary>
        private static void SootColor(ParticleSystem ps)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.8f, 0.35f, 0.1f), 0f),
                    new GradientColorKey(new Color(0.18f, 0.13f, 0.1f), 0.12f),
                    new GradientColorKey(new Color(0.13f, 0.13f, 0.15f), 0.6f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.06f),
                    new GradientAlphaKey(0.45f, 0.5f), new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = gradient;
        }

        /// <summary>QFX 원본 구조(하위 오브젝트 이름)에 기대는 곳. 없으면 원본이 바뀐 것이라 바로 멈춘다</summary>
        private static Transform RequireChild(GameObject go, string name)
        {
            Transform child = go.transform.Find(name);
            if (child == null) throw new System.InvalidOperationException($"'{go.name}' 에 {name} 가 없습니다 (QFX 원본 구조가 바뀜)");
            return child;
        }

        /// <summary>반투명 연기·먼지가 불꽃 앞을 덮지 않게 정렬을 뒤로 민다 (Sorting Fudge 가 클수록 먼저 = 뒤에 그린다)</summary>
        private static void BehindFire(ParticleSystem ps)
        {
            ps.GetComponent<ParticleSystemRenderer>().sortingFudge = 50f;
        }

        /// <summary>루트 구(Sphere 메시 반경 1m)의 반경 = 시작 크기. 배율 1 일 때 보이는 범위</summary>
        private static float RangeSphereRadius(GameObject go) => go.GetComponent<ParticleSystem>().main.startSize.constant;

        /// <summary>
        /// 피해 반경에 맞춰 키울 수 있게 만든다 (GunExplosion 이 ExplosionVisual.Fit 을 부른다).
        ///  - 하위 파티클도 루트 크기를 따르도록 Hierarchy, 위쪽 반구로만 퍼지던 불꽃·스파크는 구로 (공중 폭발도 둥글게)
        ///  - 기준 반경 = 루트 구(Sphere 메시 반경 1m) 크기. 충격파 원판(Disk 메시 반경 약 1.09m)은 그 구와 같은 반경으로
        ///  - hitEffect: 폭발 피해를 받은 지점에 재생할 이펙트 (null = 없음)
        /// </summary>
        private static void FitToDamageRadius(GameObject go, GameObject hitEffect)
        {
            const float diskMeshRadius = 1.0928f;
            float baseRadius = RangeSphereRadius(go);
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
            visual.FindProperty("hitEffectPrefab").objectReferenceValue = hitEffect;
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
