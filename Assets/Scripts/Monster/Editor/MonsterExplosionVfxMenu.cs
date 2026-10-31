using System.IO;
using HybridShooter.Monster.Behaviour;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.Monster.EditorTools
{
    /// <summary>
    /// 자폭 몬스터 폭발 이펙트 만들기 + Bufomorph_Suicide 프리팹에 연결 (메뉴 한 번).
    ///
    ///  1. 텍스처(불덩이·연기 덩어리 4x4 시트, 빛 덩어리, 파편 조각)를 코드로 그려 PNG 로 저장
    ///  2. QFX Particle HDR 셰이더로 재질을 만든다 (HDR 색이라 Bloom 에서 빛난다)
    ///  3. 섬광 · 불덩이 · 잔광(불빛) · 연기 · 불티 · 파편 · 바닥 먼지 고리 7층 파티클을 Explosion_SelfDestruct 프리팹으로 저장
    ///  4. Bufomorph_Suicide 프리팹에 SelfDestructSettings 를 붙이고 이펙트를 연결
    ///
    /// 다시 실행하면 같은 경로의 에셋을 덮어쓴다 (GUID 유지 → 연결은 그대로).
    /// 크기는 폭발 반경 약 3.5m 기준. 프리팹 루트 스케일로 전체 크기를 바꿀 수 있다 (Scaling Mode = Hierarchy).
    /// </summary>
    public static class MonsterExplosionVfxMenu
    {
        private const string Root = "Assets/Prefabs/Effects";
        private const string TextureFolder = Root + "/Textures";
        private const string MaterialFolder = Root + "/Materials/SelfDestruct";
        private const string ExplosionPrefabPath = Root + "/Explosion_SelfDestruct.prefab";
        private const string LightPrefabPath = Root + "/Explosion_SelfDestruct_Light.prefab";
        private const string SuicidePrefabPath = "Assets/Resources/Prefabs/Monsters/Bufomorph_Suicide.prefab";
        private const string ParticleShader = "QFX/SFX/Particle/Particle HDR";

        // 시트 한 변의 칸 수. 불덩이·연기는 16 칸 중 하나를 무작위로 골라 덩어리마다 모양이 다르다
        private const int SheetTiles = 4;
        // 불덩이·섬광 높이 (m). 이펙트는 몸 위치(발밑)에 생기므로 몸통 높이로 올린다
        private const float BlastHeight = 0.8f;

        private static readonly Color LightColor = new Color(1f, 0.55f, 0.2f);

        private sealed class Materials
        {
            public Material Fire, Smoke, Dust, Glow, Spark, Debris;
        }

        [MenuItem("Tools/HybridShooter/Setup Monster Explosion VFX (자폭)")]
        private static void Setup()
        {
            Shader shader = Shader.Find(ParticleShader);
            if (shader == null)
            {
                Debug.LogError($"[MonsterExplosionVfx] 셰이더 {ParticleShader} 를 찾지 못했다. QFX 에셋이 있는지 확인");
                return;
            }

            EnsureFolder(MaterialFolder);
            Materials materials = MakeMaterials(shader);
            Light lightTemplate = SaveLightPrefab();
            GameObject explosion = BuildExplosion(materials, lightTemplate);
            AssignToSuicidePrefab(explosion);

            AssetDatabase.SaveAssets();
            Selection.activeObject = explosion;
            Debug.Log($"[MonsterExplosionVfx] {ExplosionPrefabPath} 생성, {SuicidePrefabPath} 에 연결 완료");
        }

        #region 텍스처
        /// <summary>
        /// 연기 덩어리 1개의 색. 가장자리를 노이즈로 흐트러뜨린 둥근 덩어리에, 속은 밝고 가장자리는 어두운 뭉게구름 음영을 넣는다.
        /// </summary>
        /// <param name="u">칸 안 가로 위치 (0~1)</param>
        /// <param name="v">칸 안 세로 위치 (0~1)</param>
        /// <param name="seed">칸마다 다른 노이즈 위치</param>
        /// <returns>회색 밝기 + 알파. 실제 색은 재질 Tint 와 파티클 색이 곱해진다</returns>
        private static Color PuffColor(float u, float v, float seed)
        {
            float distanceFromCenter = Radius(u, v);
            float edgeNoise = FractalNoise(u * 3f + seed, v * 3f + seed * 0.7f, 4) - 0.5f;
            float distortedDistance = distanceFromCenter + edgeNoise * 0.45f;
            float shape = Mathf.Clamp01(Mathf.InverseLerp(0.95f, 0.3f, distortedDistance));

            float detail = FractalNoise(u * 7f + seed * 1.7f, v * 7f + seed * 2.3f, 5);
            float alpha = Mathf.Clamp01(shape * (0.55f + 0.9f * detail) - 0.08f);
            float brightness = Mathf.Lerp(0.4f, 1f, detail) * (1f - 0.35f * Mathf.Clamp01(distanceFromCenter));
            return new Color(brightness, brightness, brightness, alpha);
        }

        /// <summary>뾰족뾰족한 돌 조각. 노이즈로 일그러진 원을 단단한 가장자리로 자른다</summary>
        private static Color DebrisColor(float u, float v)
        {
            float distortedDistance = Radius(u, v) + (FractalNoise(u * 4f + 11f, v * 4f + 5f, 3) - 0.5f) * 0.7f;
            float alpha = Mathf.Clamp01((0.62f - distortedDistance) * 12f);
            float brightness = Mathf.Lerp(0.5f, 1f, FractalNoise(u * 10f, v * 10f, 3));
            return new Color(brightness, brightness, brightness, alpha);
        }

        /// <summary>가운데가 가장 밝고 바깥으로 부드럽게 사라지는 빛 덩어리</summary>
        private static Color GlowColor(float u, float v)
        {
            float alpha = Mathf.Pow(Mathf.Clamp01(1f - Radius(u, v)), 2.2f);
            return new Color(1f, 1f, 1f, alpha);
        }

        /// <summary>
        /// 4x4 칸에 서로 다른 연기 덩어리 16개를 그린다. 파티클이 칸 하나를 무작위로 골라 덩어리마다 모양이 달라진다.
        /// </summary>
        /// <param name="u">시트 전체 가로 위치 (0~1)</param>
        /// <param name="v">시트 전체 세로 위치 (0~1)</param>
        private static Color PuffSheetColor(float u, float v)
        {
            int column = Mathf.Min(SheetTiles - 1, (int)(u * SheetTiles));
            int row = Mathf.Min(SheetTiles - 1, (int)(v * SheetTiles));
            float cellU = u * SheetTiles - column;
            float cellV = v * SheetTiles - row;
            float seed = (row * SheetTiles + column) * 13.37f + 1f;
            return PuffColor(cellU, cellV, seed);
        }

        /// <summary>중심(0.5, 0.5)에서의 거리. 칸 가장자리 중앙 = 1</summary>
        private static float Radius(float u, float v)
        {
            float x = u * 2f - 1f;
            float y = v * 2f - 1f;
            return Mathf.Sqrt(x * x + y * y);
        }

        /// <summary>
        /// 여러 크기의 Perlin 노이즈를 겹친 프랙탈 노이즈. 굵은 덩어리 위에 잔무늬가 얹혀 연기·불처럼 보인다.
        /// </summary>
        /// <param name="x">노이즈 공간 가로 좌표</param>
        /// <param name="y">노이즈 공간 세로 좌표</param>
        /// <param name="octaves">겹칠 층 수. 많을수록 잔무늬가 촘촘하다</param>
        /// <returns>0~1</returns>
        private static float FractalNoise(float x, float y, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float frequency = 1f;
            float amplitudeTotal = 0f;
            for (int octave = 0; octave < octaves; octave++)
            {
                sum += Mathf.PerlinNoise(x * frequency + 17.1f * octave, y * frequency + 31.7f * octave) * amplitude;
                amplitudeTotal += amplitude;
                amplitude *= 0.5f;
                frequency *= 2f;
            }
            return sum / amplitudeTotal;
        }

        /// <summary>색 함수로 텍스처를 그려 PNG 로 저장하고 투명도·클램프 설정으로 다시 불러온다</summary>
        /// <param name="name">파일 이름 (확장자 제외)</param>
        /// <param name="size">한 변 픽셀 수</param>
        /// <param name="color">(u, v) → 색. u, v 는 0~1</param>
        /// <returns>불러온 텍스처 에셋</returns>
        private static Texture2D WriteTexture(string name, int size, System.Func<float, float, Color> color)
        {
            string path = $"{TextureFolder}/{name}.png";
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = color((x + 0.5f) / size, (y + 0.5f) / size);
            texture.SetPixels(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        #endregion

        #region 재질
        /// <summary>
        /// 텍스처를 그리고 층별 재질을 만든다. 셰이더 결과 = 2 × 파티클 색 × Tint × 텍스처 이므로
        /// Tint 0.5 = 파티클 색 그대로, 1 이상 = HDR(Bloom 에서 빛남).
        /// </summary>
        private static Materials MakeMaterials(Shader shader)
        {
            Texture2D puffSheet = WriteTexture("T_ExplosionPuffSheet", 512, PuffSheetColor);
            Texture2D glow = WriteTexture("T_ExplosionGlow", 64, GlowColor);
            Texture2D debris = WriteTexture("T_ExplosionDebris", 64, DebrisColor);

            return new Materials
            {
                Fire = SaveMaterial("M_Explosion_Fire", shader, puffSheet, new Color(2.2f, 1.5f, 1.1f, 1f)),
                Smoke = SaveMaterial("M_Explosion_Smoke", shader, puffSheet, new Color(0.5f, 0.5f, 0.5f, 0.5f)),
                Dust = SaveMaterial("M_Explosion_Dust", shader, puffSheet, new Color(0.5f, 0.5f, 0.5f, 0.5f)),
                Glow = SaveMaterial("M_Explosion_Glow", shader, glow, new Color(4f, 2.6f, 1.4f, 1f)),
                Spark = SaveMaterial("M_Explosion_Spark", shader, glow, new Color(6f, 3f, 1f, 1f)),
                Debris = SaveMaterial("M_Explosion_Debris", shader, debris, new Color(0.5f, 0.5f, 0.5f, 0.5f)),
            };
        }

        /// <summary>재질을 만들어 저장한다. 같은 경로에 있으면 내용만 덮어써 GUID(연결)를 유지한다</summary>
        private static Material SaveMaterial(string name, Shader shader, Texture2D texture, Color tint)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = new Material(shader);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_TintColor", tint);

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
        /// <summary>파티클 Lights 모듈이 복제할 주황 점광원. 모듈은 프리팹 에셋의 Light 를 본으로 쓴다</summary>
        private static Light SaveLightPrefab()
        {
            var lightObject = new GameObject("Explosion_SelfDestruct_Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = LightColor;
            light.range = 12f;
            light.intensity = 8f;
            light.shadows = LightShadows.None;

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(lightObject, LightPrefabPath);
            Object.DestroyImmediate(lightObject);
            return saved.GetComponent<Light>();
        }

        /// <summary>
        /// 7층 파티클을 한 루트 아래 만들어 프리팹으로 저장한다. 모두 한 번만 터지는(Burst) 비반복 파티클이다.
        /// </summary>
        /// <returns>저장된 프리팹 에셋</returns>
        private static GameObject BuildExplosion(Materials materials, Light lightTemplate)
        {
            var root = new GameObject("Explosion_SelfDestruct");
            AddFlash(root.transform, materials.Glow);
            AddFireball(root.transform, materials.Fire);
            AddAfterglow(root.transform, materials.Glow, lightTemplate);
            AddSmoke(root.transform, materials.Smoke);
            AddSparks(root.transform, materials.Spark);
            AddDebris(root.transform, materials.Debris);
            AddGroundDust(root.transform, materials.Dust);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, ExplosionPrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>터지는 순간 0.15초 동안 번쩍이는 큰 흰노랑 빛</summary>
        private static void AddFlash(Transform parent, Material material)
        {
            ParticleSystem particles = AddSystem(parent, "Flash", material, BlastHeight, -30f);
            SetMain(particles, (0.12f, 0.15f), (5.5f, 6.5f), (0f, 0f));
            Burst(particles, 0f, 1, 1);
            Grow(particles, 0.6f, 1.2f);
            SetColorOverLifetime(particles,
                new[] { Key(new Color(1f, 0.95f, 0.85f), 0f), Key(new Color(1f, 0.7f, 0.35f), 1f) },
                new[] { Alpha(1f, 0f), Alpha(0f, 1f) });
        }

        /// <summary>
        /// 폭발의 몸통. 연기 시트를 HDR 주황으로 칠한 덩어리들이 퍼지며 감속하고,
        /// 흰노랑 → 주황 → 검붉은색으로 식다가 사라진다.
        /// </summary>
        private static void AddFireball(Transform parent, Material material)
        {
            ParticleSystem particles = AddSystem(parent, "Fireball", material, BlastHeight, -20f);
            SetMain(particles, (0.6f, 1.1f), (1.4f, 2.6f), (2f, 6f));
            RandomRotation(particles, 90f);
            Burst(particles, 0f, 22, 28);
            Sphere(particles, 0.6f);
            Drag(particles, 3f);
            Rise(particles, 0.15f);
            Grow(particles, 0.7f, 1.8f);
            Turbulence(particles, 0.6f, 0.8f);
            RandomSheetCell(particles);
            SetColorOverLifetime(particles,
                new[]
                {
                    Key(new Color(1f, 0.95f, 0.8f), 0f),
                    Key(new Color(1f, 0.55f, 0.15f), 0.3f),
                    Key(new Color(0.4f, 0.1f, 0.03f), 0.75f),
                    Key(new Color(0.15f, 0.05f, 0.03f), 1f),
                },
                new[] { Alpha(1f, 0f), Alpha(1f, 0.5f), Alpha(0f, 1f) });
        }

        /// <summary>불덩이 뒤에 남는 은은한 열기 빛 + 주변을 비추는 점광원. 점광원 밝기는 파티클 투명도를 따라 0.5초 동안 꺼진다</summary>
        private static void AddAfterglow(Transform parent, Material material, Light lightTemplate)
        {
            ParticleSystem particles = AddSystem(parent, "Afterglow", material, BlastHeight, -10f);
            SetMain(particles, (0.5f, 0.5f), (4f, 4f), (0f, 0f));
            Burst(particles, 0f, 1, 1);
            SetColorOverLifetime(particles,
                new[] { Key(LightColor, 0f), Key(LightColor, 1f) },
                new[] { Alpha(0.35f, 0f), Alpha(0.15f, 0.4f), Alpha(0f, 1f) });

            var lights = particles.lights;
            lights.enabled = true;
            lights.light = lightTemplate;
            lights.ratio = 1f;
            lights.maxLights = 1;
            lights.useParticleColor = true;
            lights.alphaAffectsIntensity = true;
            lights.sizeAffectsRange = false;
            lights.intensityMultiplier = 3f; // 시작 알파 0.35 × 3 ≈ 프리팹 밝기
        }

        /// <summary>불덩이가 식은 뒤 남는 검은 연기. 조금 늦게 생겨 천천히 떠오르며 커지고 2~3.5초에 걸쳐 사라진다</summary>
        private static void AddSmoke(Transform parent, Material material)
        {
            ParticleSystem particles = AddSystem(parent, "Smoke", material, BlastHeight, 20f);
            SetMain(particles, (2f, 3.5f), (2f, 3.5f), (1f, 3.5f));
            RandomRotation(particles, 25f);
            Burst(particles, 0.08f, 16, 20);
            Sphere(particles, 1f);
            Drag(particles, 1.5f);
            Rise(particles, 0.12f);
            Grow(particles, 0.8f, 2.5f);
            Turbulence(particles, 0.4f, 0.5f);
            RandomSheetCell(particles);
            SetColorOverLifetime(particles,
                new[] { Key(new Color(0.12f, 0.1f, 0.09f), 0f), Key(new Color(0.3f, 0.29f, 0.28f), 1f) },
                new[] { Alpha(0f, 0f), Alpha(0.75f, 0.1f), Alpha(0.4f, 0.6f), Alpha(0f, 1f) });
        }

        /// <summary>사방으로 튀는 불티. 속도 방향으로 늘어나 보이고, 중력으로 떨어지며 바닥에 튕긴다</summary>
        private static void AddSparks(Transform parent, Material material)
        {
            ParticleSystem particles = AddSystem(parent, "Sparks", material, BlastHeight, -25f);
            SetMain(particles, (0.5f, 1.4f), (0.04f, 0.09f), (8f, 18f));
            Burst(particles, 0f, 50, 70);
            Sphere(particles, 0.3f);
            Drag(particles, 0.6f);
            Fall(particles, 1.2f);
            Bounce(particles, 0.3f);
            Stretch(particles, 0.04f);
            SetColorOverLifetime(particles,
                new[] { Key(new Color(1f, 0.9f, 0.6f), 0f), Key(new Color(1f, 0.45f, 0.1f), 1f) },
                new[] { Alpha(1f, 0f), Alpha(1f, 0.7f), Alpha(0f, 1f) });
        }

        /// <summary>위쪽으로 튀어 올랐다가 포물선으로 떨어져 바닥에 튕기는 어두운 돌·살점 조각</summary>
        private static void AddDebris(Transform parent, Material material)
        {
            ParticleSystem particles = AddSystem(parent, "Debris", material, 0.4f, 0f);
            SetMain(particles, (1f, 1.8f), (0.08f, 0.22f), (5f, 11f));
            RandomRotation(particles, 540f);
            Burst(particles, 0f, 15, 22);
            UpwardCone(particles, 60f, 0.3f);
            Fall(particles, 2f);
            Bounce(particles, 0.3f);
            SetColorOverLifetime(particles,
                new[] { Key(new Color(0.1f, 0.08f, 0.07f), 0f), Key(new Color(0.06f, 0.05f, 0.05f), 1f) },
                new[] { Alpha(1f, 0f), Alpha(1f, 0.8f), Alpha(0f, 1f) });
        }

        /// <summary>폭풍에 바닥 흙먼지가 고리 모양으로 바깥에 밀려나는 층. 빠르게 퍼졌다가 급히 멈추고 흩어진다</summary>
        private static void AddGroundDust(Transform parent, Material material)
        {
            ParticleSystem particles = AddSystem(parent, "GroundDust", material, 0.1f, 30f);
            SetMain(particles, (0.8f, 1.5f), (0.8f, 1.6f), (6f, 10f));
            RandomRotation(particles, 40f);
            Burst(particles, 0.02f, 30, 40);
            FlatRing(particles, 0.6f);
            Drag(particles, 3.5f);
            Grow(particles, 0.6f, 2.5f);
            RandomSheetCell(particles);
            SetColorOverLifetime(particles,
                new[] { Key(new Color(0.45f, 0.38f, 0.3f), 0f), Key(new Color(0.5f, 0.45f, 0.4f), 1f) },
                new[] { Alpha(0f, 0f), Alpha(0.6f, 0.1f), Alpha(0f, 1f) });
        }

        /// <summary>
        /// Bufomorph_Suicide 프리팹에 SelfDestructSettings 를 붙이고(이미 있으면 그대로) 폭발 이펙트를 넣는다.
        /// </summary>
        private static void AssignToSuicidePrefab(GameObject explosion)
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(SuicidePrefabPath);
            var settings = prefabRoot.GetComponent<SelfDestructSettings>();
            if (settings == null) settings = prefabRoot.AddComponent<SelfDestructSettings>();

            var serialized = new SerializedObject(settings);
            serialized.FindProperty("explosionEffectPrefab").objectReferenceValue = explosion;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, SuicidePrefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
        #endregion

        #region 파티클 도구
        /// <summary>
        /// 한 번만 터지는 월드 공간 파티클 1층을 만든다. 모듈은 이후 도구 메서드로 켠다.
        /// </summary>
        /// <param name="height">루트 기준 높이 (m)</param>
        /// <param name="sortingFudge">투명 정렬 보정. 낮을수록 다른 층 위에 그려진다 (불 &lt; 연기)</param>
        private static ParticleSystem AddSystem(Transform parent, string name, Material material, float height, float sortingFudge)
        {
            var systemObject = new GameObject(name);
            systemObject.transform.SetParent(parent, false);
            systemObject.transform.localPosition = new Vector3(0f, height, 0f);
            var particles = systemObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.loop = false;
            main.duration = 1f;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; // 루트 스케일로 전체 크기 조절
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            main.startColor = Color.white;
            main.maxParticles = 200;

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.enabled = false;

            var renderer = systemObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingFudge = sortingFudge;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        /// <summary>수명(s)·크기(m)·속도(m/s) 를 최솟값~최댓값 사이 무작위로</summary>
        private static void SetMain(ParticleSystem particles, (float min, float max) lifetime,
                                    (float min, float max) size, (float min, float max) speed)
        {
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.min, lifetime.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
        }

        /// <summary>시작 각도를 무작위로 하고 수명 동안 ±degreesPerSecond 로 돈다</summary>
        private static void RandomRotation(ParticleSystem particles, float degreesPerSecond)
        {
            var main = particles.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rotation = particles.rotationOverLifetime;
            rotation.enabled = true;
            float radiansPerSecond = degreesPerSecond * Mathf.Deg2Rad;
            rotation.z = new ParticleSystem.MinMaxCurve(-radiansPerSecond, radiansPerSecond);
        }

        /// <summary>delay 초에 min~max 개를 한 번에 뿜는다</summary>
        private static void Burst(ParticleSystem particles, float delay, int min, int max)
        {
            particles.emission.SetBursts(new[] { new ParticleSystem.Burst(delay, (short)min, (short)max) });
        }

        /// <summary>반지름 radius(m) 구 안에서 바깥 방향으로</summary>
        private static void Sphere(ParticleSystem particles, float radius)
        {
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        /// <summary>위(+Y)를 향한 원뿔. 기본 원뿔은 +Z 방향이라 X 축으로 -90° 돌린다</summary>
        private static void UpwardCone(ParticleSystem particles, float angle, float radius)
        {
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = radius;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        /// <summary>바닥에 눕힌 원 테두리에서 바깥으로. 기본 원은 XY 평면이라 X 축으로 90° 돌린다</summary>
        private static void FlatRing(ParticleSystem particles, float radius)
        {
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(90f, 0f, 0f);
        }

        /// <summary>공기 저항. 값이 클수록 빨리 멈춘다 (속도가 대략 e^(-drag × 시간) 로 줄어든다)</summary>
        private static void Drag(ParticleSystem particles, float drag)
        {
            var limit = particles.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.drag = drag;
        }

        /// <summary>뜨거운 공기처럼 위로 떠오른다 (음의 중력 배율)</summary>
        private static void Rise(ParticleSystem particles, float strength)
        {
            var main = particles.main;
            main.gravityModifier = -strength;
        }

        /// <summary>중력 배율만큼 떨어진다</summary>
        private static void Fall(ParticleSystem particles, float gravityScale)
        {
            var main = particles.main;
            main.gravityModifier = gravityScale;
        }

        /// <summary>월드 콜라이더(바닥·벽)에 부딪히면 튕긴다. bounce = 튕겨 나가는 속도 비율</summary>
        private static void Bounce(ParticleSystem particles, float bounce)
        {
            var collision = particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 0.5f;
            collision.bounce = bounce;
        }

        /// <summary>수명 동안 크기가 startScale → endScale 배. 처음에 빨리 커지고 끝에 느려진다</summary>
        private static void Grow(ParticleSystem particles, float startScale, float endScale)
        {
            float largest = Mathf.Max(startScale, endScale);
            float middleScale = Mathf.Lerp(startScale, endScale, 0.7f);
            var curve = new AnimationCurve(
                new Keyframe(0f, startScale / largest),
                new Keyframe(0.3f, middleScale / largest),
                new Keyframe(1f, endScale / largest));
            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(largest, curve);
        }

        /// <summary>덩어리가 일렁이며 흐트러지게 하는 노이즈. strength = 흔들림 세기, frequency = 무늬 촘촘함</summary>
        private static void Turbulence(ParticleSystem particles, float strength, float frequency)
        {
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 0.3f;
            noise.quality = ParticleSystemNoiseQuality.Low;
        }

        /// <summary>4x4 시트에서 파티클마다 칸 하나를 무작위로 골라 수명 내내 유지한다</summary>
        private static void RandomSheetCell(ParticleSystem particles)
        {
            var sheet = particles.textureSheetAnimation;
            sheet.enabled = true;
            sheet.mode = ParticleSystemAnimationMode.Grid;
            sheet.numTilesX = SheetTiles;
            sheet.numTilesY = SheetTiles;
            sheet.animation = ParticleSystemAnimationType.WholeSheet;
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f, 0.999f); // 두 상수 사이 무작위 = 칸 고정
            sheet.cycleCount = 1;
        }

        /// <summary>속도 방향으로 늘어진 선으로 그린다. velocityScale = 속도 1m/s 당 늘어나는 길이 배율</summary>
        private static void Stretch(ParticleSystem particles, float velocityScale)
        {
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1f;
            renderer.velocityScale = velocityScale;
        }

        private static void SetColorOverLifetime(ParticleSystem particles, GradientColorKey[] colors, GradientAlphaKey[] alphas)
        {
            var gradient = new Gradient();
            gradient.SetKeys(colors, alphas);
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = gradient;
        }

        private static GradientColorKey Key(Color color, float time) => new GradientColorKey(color, time);

        private static GradientAlphaKey Alpha(float alpha, float time) => new GradientAlphaKey(alpha, time);

        /// <summary>Assets/... 경로의 폴더가 없으면 한 단계씩 만든다</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        #endregion
    }
}
