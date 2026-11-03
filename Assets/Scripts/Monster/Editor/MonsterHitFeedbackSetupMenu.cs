using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridShooter.Monster.Behaviour;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Rendering.Universal.ShaderGUI;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HybridShooter.Monster.EditorTools
{
    /// <summary>
    /// 몬스터 피격·사망 표현 세팅 (메뉴 한 번). 다시 실행하면 같은 경로 에셋을 덮어쓰고 이미 붙은 컴포넌트는 그대로 쓴다.
    ///
    ///  1. 피 이펙트 3종(맞은 곳·약점·사망 터짐)을 파티클로 만들어 PooledEffect 와 함께 프리팹으로 저장
    ///     (피만 다시 만들려면 "Rebuild Blood Effects" 메뉴. 기존 프리팹 안에서 다시 지어 참조가 유지된다)
    ///  2. Monster Base Controller 에 Additive "Hit" 레이어(Empty + HitFront/Back/Left/Right) 추가
    ///  3. 몬스터별 Override Controller 의 Hit 슬롯에 GetHit 클립 연결 (방향 클립이 없으면 Front 로 대신)
    ///  4. 몬스터 프리팹 9종에 MonsterDamageFeedback(피 이펙트 연결)·MonsterCorpse 추가. 강화 개체는 변형 프리팹이라 따라온다
    ///  5. GiantSlug 양쪽 등 분비샘 뼈 2곳에 약점 콜라이더(MonsterWeakPoint, PartKey Gland) 추가
    /// </summary>
    public static class MonsterHitFeedbackSetupMenu
    {
        private const string EffectsFolder = "Assets/Prefabs/Effects";
        private const string BloodMaterialFolder = EffectsFolder + "/Materials/Blood";
        private const string DropletTexturePath = EffectsFolder + "/Textures/T_Glow.png";
        private const string PuffSheetTexturePath = EffectsFolder + "/Textures/T_ExplosionPuffSheet.png";
        private const string HazeTexturePath = EffectsFolder + "/Textures/T_Smoke.png";
        private const string FlashTexturePath = EffectsFolder + "/Textures/T_Glow.png";
        private const string HitBloodPath = EffectsFolder + "/Blood_Hit.prefab";
        private const string WeakPointBloodPath = EffectsFolder + "/Blood_WeakPoint.prefab";
        private const string DeathBloodPath = EffectsFolder + "/Blood_Death.prefab";
        // 피 방울·분무·연무: 씬 조명을 받는 URP 파티클 셰이더
        private const string LitParticleShader = "Universal Render Pipeline/Particles/Lit";
        // 약점 번쩍임: 조명과 상관없이 항상 밝게 보이는 언릿 HDR 셰이더
        private const string FlashParticleShader = "QFX/SFX/Particle/Particle HDR";

        private const string AnimatorFolder = "Assets/Scenes/HS/MonsterIntegrationLab/Prefabs/MonsterAnimators";
        private const string BaseControllerPath = AnimatorFolder + "/Monster Base Controller.controller";
        private const string HitLayerName = "Hit";
        private const string ClipRoot = "Assets/ExternalAssets/Monsters Full Pack Vol 1";

        private const string MonsterFolder = "Assets/Resources/Prefabs/Monsters";
        private const string WeakPointObjectName = "WeakPoint_Gland";
        // 약점 콜라이더 반지름 (m, 뼈 기준). 씬에서 보고 조정
        private const float WeakPointRadius = 0.5f;

        private static readonly string[] MonsterPrefabNames =
        {
            "Bufomorph", "Bufomorph_Suicide", "Densoptere", "GiantSlug", "Letalobrach",
            "Muscomorph_Air", "Muscomorph_Ground", "Muscomorph_Heavy", "Pardathrox",
        };

        private static readonly string[] GiantSlugWeakPointBones = { "GiantSlug_WeakPointLeft", "GiantSlug_WeakPointRight" };

        // Override Controller 이름 → 맞은 쪽(HitSide 순서: Front, Back, Left, Right)별 GetHit FBX (ClipRoot 기준).
        // 방향 클립이 없는 몬스터는 Front, 방향 구분이 없는 몬스터는 같은 클립을 쓴다
        private static readonly Dictionary<string, string[]> HitClipsByController = new Dictionary<string, string[]>
        {
            ["Bufomorph Controller"] = Sides("Monsters Pack Vol 5/Bufomorph/FBX Files/Buformorph@GetHit", "Front", "Back", "Left", "Right"),
            ["Bufomorph_Elite Controller"] = Sides("Monsters Pack Vol 5/Bufomorph/FBX Files/Buformorph@GetHit", "Front", "Back", "Left", "Right"),
            ["Letalobrach Controller"] = Sides("Monsters Pack Vol 4/Letalobrach/FBX Files/Letalobrach@GetHit", "Front", "Back", "Left", "Right"),
            ["GiantSlug Controller"] = Sides("Monsters Pack Vol 3/Giant Slug/FBX Files/GiantSlug@GetHit", "Front", "Front", "Left", "Right"),
            ["Pardathrox Controller"] = Sides("Monsters Pack Vol 5/Pardathrox/FBX Files/Pardathrox@GetHit", "Front", "Front", "Left", "Right"),
            ["Muscomorph_Ground Controller"] = Sides("Monsters Pack Vol 2/Muscomorph/FBX Files/Muscomorph@GetHit", "1", "1", "2", "2"),
            ["Muscomorph_Air Controller"] = Sides("Monsters Pack Vol 2/Muscomorph/FBX Files/Muscomorph@FlyingGetHit", "", "", "", ""),
            ["Densoptere Controller"] = Sides("Monsters Pack Vol 6/Densoptere/FBX Files/Densoptere@FlyNormalGetHit", "", "", "", ""),
        };

        private sealed class BloodEffects
        {
            public PooledEffect Hit, WeakPoint, Death;
        }

        [MenuItem("Tools/HybridShooter/Setup Monster Hit Feedback (피격·사망)")]
        public static void Setup()
        {
            BloodEffects blood = BuildBloodEffects();
            AnimationClip[] placeholderClips = SaveHitPlaceholderClips();
            AddHitLayer(placeholderClips);
            AssignHitClips(placeholderClips);
            foreach (string prefabName in MonsterPrefabNames) SetupMonsterPrefab($"{MonsterFolder}/{prefabName}.prefab", blood);
            AddGiantSlugWeakPoints();

            AssetDatabase.SaveAssets();
            Debug.Log("[MonsterHitFeedback] 피 이펙트·Hit 레이어·Override 클립·몬스터 프리팹 세팅 완료");
        }

        /// <summary>
        /// 피 이펙트 3종과 재질만 다시 만든다. Animator·몬스터 프리팹은 건드리지 않는다.
        /// 기존 프리팹 안에서 다시 지으므로 몬스터·보스에 연결된 참조는 그대로 유지된다.
        /// </summary>
        [MenuItem("Tools/HybridShooter/Rebuild Blood Effects (피 이펙트만)")]
        public static void RebuildBloodEffects()
        {
            BuildBloodEffects();

            AssetDatabase.SaveAssets();
            Debug.Log("[MonsterHitFeedback] 피 이펙트 3종 다시 만들기 완료");
        }

        /// <summary>FBX 경로 앞부분 + 맞은 쪽별 접미사 4개 → 전체 경로 4개</summary>
        private static string[] Sides(string prefix, string front, string back, string left, string right)
        {
            return new[] { front, back, left, right }.Select(suffix => $"{ClipRoot}/{prefix}{suffix}.FBX").ToArray();
        }

        #region 피 이펙트
        /// <summary>
        /// 재질 4개(방울·분무·연무·번쩍임)를 만들고 피 이펙트 프리팹 3개를 저장한다.
        /// 이펙트별 색·개수·크기 같은 튜닝 값은 모두 여기 모여 있다.
        /// </summary>
        /// <returns>저장된 프리팹 3개의 PooledEffect (몬스터 프리팹 연결에 쓴다)</returns>
        private static BloodEffects BuildBloodEffects()
        {
            // 재질: 방울·분무·연무는 씬 조명을 받고, 번쩍임만 언릿.
            // 자체 발광(빨강)을 조금 넣어 어두운 곳에서도 회색이 아니라 피 색으로 보이게 한다
            EnsureFolder(BloodMaterialFolder);
            var bloodGlow = new Color(0.25f, 0.01f, 0.01f);
            var hazeGlow = new Color(0.15f, 0.01f, 0.01f);
            Material dropletMaterial = SaveLitMaterial("M_Blood_Droplet", DropletTexturePath, bloodGlow);
            Material mistMaterial = SaveLitMaterial("M_Blood_Mist", PuffSheetTexturePath, bloodGlow);
            Material hazeMaterial = SaveLitMaterial("M_Blood_Haze", HazeTexturePath, hazeGlow);
            Material flashMaterial = SaveFlashMaterial("M_Blood_Flash", FlashTexturePath);

            // 색: 확실한 피 빨강. 연무도 회색이 섞이지 않은 빨강 반투명
            var darkBlood = new Color(0.55f, 0.02f, 0.02f, 1f);
            var brightBlood = new Color(0.85f, 0.04f, 0.03f, 1f);
            var bloodHaze = new Color(0.45f, 0.03f, 0.03f, 0.3f);

            // 맞은 곳: 공격자 쪽으로 좁게 툭 튀는 방울 + 작은 분무 + 잠깐 남는 옅은 연무
            PooledEffect hit = SaveEffectPrefab(HitBloodPath, lifetime: 1.2f, root =>
            {
                AddDroplets(root, dropletMaterial, darkBlood, count: (10, 16), speed: (2.5f, 5f), size: (0.05f, 0.11f), coneAngle: 30f);
                AddMist(root, mistMaterial, darkBlood, count: (2, 3), size: (0.3f, 0.6f));
                AddHaze(root, hazeMaterial, bloodHaze, count: (1, 2), size: (0.4f, 1f), lifetime: (0.8f, 1f), riseSpeed: 0.1f);
            });

            // 약점: 번쩍임과 함께 넓게 확 터지는 밝은 피 + 큰 분무·연무 → 일반 피격과 한눈에 구별
            PooledEffect weakPoint = SaveEffectPrefab(WeakPointBloodPath, lifetime: 1.4f, root =>
            {
                AddFlash(root, flashMaterial, size: (0.5f, 0.9f), lifetime: 0.08f);
                AddDroplets(root, dropletMaterial, brightBlood, count: (24, 34), speed: (3.5f, 7f), size: (0.08f, 0.16f), coneAngle: 45f);
                AddMist(root, mistMaterial, brightBlood, count: (4, 6), size: (0.6f, 1.1f));
                AddHaze(root, hazeMaterial, bloodHaze, count: (2, 3), size: (0.7f, 1.6f), lifetime: (0.9f, 1.2f), riseSpeed: 0.15f);
            });

            // 사망: 몸통 중심에서 사방으로 터지고 바닥에 튄다. 큰 연무가 오래 남는다
            PooledEffect death = SaveEffectPrefab(DeathBloodPath, lifetime: 2.5f, root =>
            {
                ParticleSystem burst = AddDroplets(root, dropletMaterial, darkBlood, count: (60, 90), speed: (3f, 8f), size: (0.1f, 0.24f), coneAngle: 0f);
                Sphere(burst, 0.4f);
                Bounce(burst);
                ParticleSystem cloud = AddMist(root, mistMaterial, darkBlood, count: (6, 10), size: (1f, 2f));
                Sphere(cloud, 0.6f);
                ParticleSystem haze = AddHaze(root, hazeMaterial, bloodHaze, count: (5, 7), size: (1.5f, 3.5f), lifetime: (1.6f, 2.2f), riseSpeed: 0.3f);
                Sphere(haze, 0.6f);
            });

            return new BloodEffects { Hit = hit, WeakPoint = weakPoint, Death = death };
        }

        /// <summary>
        /// 피 방울 1층. 중력으로 떨어지고, 속도 방향으로 늘어나 튀는 느낌을 준다.
        /// coneAngle 0 = 모양 없음(사망 터짐은 이후 Sphere 로 바꾼다), 그 외 = +Z 방향 원뿔.
        /// </summary>
        private static ParticleSystem AddDroplets(Transform parent, Material material, Color color,
                                                  (int min, int max) count, (float min, float max) speed,
                                                  (float min, float max) size, float coneAngle)
        {
            ParticleSystem particles = AddSystem(parent, "Droplets", material);
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startColor = color;
            main.gravityModifier = 1.5f;
            Burst(particles, count.min, count.max);
            if (coneAngle > 0f) Cone(particles, coneAngle);
            FadeOut(particles);

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 1.5f;
            return particles;
        }

        /// <summary>피 안개 1층. 천천히 퍼지며 커지다 사라진다</summary>
        private static ParticleSystem AddMist(Transform parent, Material material, Color color,
                                              (int min, int max) count, (float min, float max) size)
        {
            ParticleSystem particles = AddSystem(parent, "Mist", material);
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(color.r, color.g, color.b, 0.6f);
            Burst(particles, count.min, count.max);
            FadeOut(particles);

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1f));

            var sheet = particles.textureSheetAnimation;
            sheet.enabled = true;
            sheet.numTilesX = 4;
            sheet.numTilesY = 4;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 15f);
            sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            return particles;
        }

        /// <summary>
        /// 피 연무 1층. 맞은 뒤 뿌옇게 남아 천천히 커지고 살짝 떠오르다 사라진다. 방울·분무 뒤에 그려진다.
        /// 모양은 작은 구(0.1 m)다. 사망 연무처럼 넓게 퍼뜨리려면 이후 Sphere 로 반지름을 바꾼다.
        /// </summary>
        /// <param name="parent">이펙트 루트</param>
        /// <param name="material">연무 재질 (Lit)</param>
        /// <param name="color">연무 색 (알파 = 진하기)</param>
        /// <param name="count">한 번에 뿜는 연무 개수 (최소, 최대)</param>
        /// <param name="size">처음 크기 → 사라질 때 크기 (m)</param>
        /// <param name="lifetime">연무 1개가 남는 시간 (s, 최소, 최대)</param>
        /// <param name="riseSpeed">위로 떠오르는 속도 (m/s, 월드 위쪽)</param>
        /// <returns>만든 파티클 시스템</returns>
        private static ParticleSystem AddHaze(Transform parent, Material material, Color color,
                                              (int min, int max) count, (float start, float end) size,
                                              (float min, float max) lifetime, float riseSpeed)
        {
            ParticleSystem particles = AddSystem(parent, "Haze", material);
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.min, lifetime.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            main.startSize = size.end;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = color;
            Burst(particles, count.min, count.max);
            Sphere(particles, 0.1f);
            FadeInOut(particles);

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, size.start / size.end, 1f, 1f));

            var velocityOverLifetime = particles.velocityOverLifetime;
            velocityOverLifetime.enabled = true;
            velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
            velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(0f);
            velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(riseSpeed);
            velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(0f);

            // 연무가 반듯하게 퍼지지 않고 살짝 일렁이게
            var noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.2f;

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sortingFudge = 10f;
            return particles;
        }

        /// <summary>
        /// 약점 명중 번쩍임 1층. 맞은 자리에서 아주 짧게 커지며 빛난다. 방울·분무보다 앞에 그려진다.
        /// </summary>
        /// <param name="parent">이펙트 루트</param>
        /// <param name="material">번쩍임 재질 (언릿 HDR)</param>
        /// <param name="size">처음 크기 → 사라질 때 크기 (m)</param>
        /// <param name="lifetime">번쩍이는 시간 (s)</param>
        /// <returns>만든 파티클 시스템</returns>
        private static ParticleSystem AddFlash(Transform parent, Material material, (float start, float end) size, float lifetime)
        {
            ParticleSystem particles = AddSystem(parent, "Flash", material);
            var main = particles.main;
            main.startLifetime = lifetime;
            main.startSpeed = 0f;
            main.startSize = size.end;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            Burst(particles, 1, 1);
            FadeOut(particles);

            var sizeOverLifetime = particles.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, size.start / size.end, 1f, 1f));

            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sortingFudge = -10f;
            return particles;
        }

        /// <summary>한 번만 터지는 월드 공간 파티클 1층</summary>
        private static ParticleSystem AddSystem(Transform parent, string name, Material material)
        {
            var systemObject = new GameObject(name);
            systemObject.transform.SetParent(parent, false);
            var particles = systemObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = particles.main;
            main.loop = false;
            main.duration = 0.5f;
            main.playOnAwake = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;

            var emission = particles.emission;
            emission.rateOverTime = 0f;
            var shape = particles.shape;
            shape.enabled = false;

            var renderer = systemObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        private static void Burst(ParticleSystem particles, int min, int max)
        {
            particles.emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)min, (short)max) });
        }

        /// <summary>+Z 방향 원뿔 (EffectPool.Play 의 rotation 이 튀는 방향)</summary>
        private static void Cone(ParticleSystem particles, float angle)
        {
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 0.05f;
        }

        private static void Sphere(ParticleSystem particles, float radius)
        {
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
        }

        /// <summary>바닥·벽에 닿으면 거의 튀지 않고 멈춘다 (피가 바닥에 묻는 느낌)</summary>
        private static void Bounce(ParticleSystem particles)
        {
            var collision = particles.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 0.8f;
            collision.bounce = 0.1f;
        }

        /// <summary>수명 끝으로 갈수록 투명해진다</summary>
        private static void FadeOut(ParticleSystem particles)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = gradient;
        }

        /// <summary>처음엔 서서히 나타나고 수명 끝으로 갈수록 투명해진다 (연무가 툭 튀어나오지 않게)</summary>
        private static void FadeInOut(ParticleSystem particles)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            var colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = gradient;
        }

        /// <summary>
        /// 씬 조명을 받는 반투명 피 재질(URP Particles/Lit, 알파 블렌드)을 만들거나 덮어쓴다.
        /// 재질 색은 흰색이고 실제 피 색은 파티클 색이 정한다. Soft Particles 로 몸·바닥과 겹치는 경계를 부드럽게 한다.
        /// </summary>
        /// <remarks>
        /// Preserve Specular 를 끄고 반사(Smoothness)를 0 으로 둔다. 켜 두면 반사광이 알파와 상관없이 더해져서
        /// 텍스처의 투명한 부분까지 회색 사각형으로 보이고 피 색이 회색으로 바랜다.
        /// </remarks>
        /// <param name="name">재질 이름 (BloodMaterialFolder 안 파일 이름)</param>
        /// <param name="texturePath">파티클 텍스처 경로</param>
        /// <param name="glowColor">자체 발광 색. 어두운 곳에서도 피 색이 남게 하는 최소 밝기 (검정 = 발광 없음)</param>
        /// <returns>저장된 재질 에셋</returns>
        private static Material SaveLitMaterial(string name, string texturePath, Color glowColor)
        {
            Material material = LoadOrCreateMaterial(name, FindShader(LitParticleShader));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f); // Transparent
            material.SetFloat("_Blend", 0f);   // Alpha
            material.SetFloat("_BlendModePreserveSpecular", 0f);
            material.SetFloat("_Smoothness", 0f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_ColorMode", 0f);
            material.SetFloat("_ReceiveShadows", 0f);
            material.SetColor("_EmissionColor", glowColor);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetFloat("_SoftParticlesEnabled", 1f);
            material.SetFloat("_SoftParticlesNearFadeDistance", 0f);
            material.SetFloat("_SoftParticlesFarFadeDistance", 0.5f);
            BaseShaderGUI.SetMaterialKeywords(material, LitGUI.SetMaterialKeywords, ParticleGUI.SetMaterialKeywords);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// 약점 번쩍임 재질(QFX 언릿 HDR)을 만들거나 덮어쓴다. 조명과 상관없이 밝게 보인다.
        /// QFX 셰이더 출력 = 2 × 파티클 색 × Tint × 텍스처 이므로, Tint 를 1 보다 크게 둬서 블룸이 걸리는 주황빛을 낸다.
        /// </summary>
        /// <param name="name">재질 이름 (BloodMaterialFolder 안 파일 이름)</param>
        /// <param name="texturePath">파티클 텍스처 경로</param>
        /// <returns>저장된 재질 에셋</returns>
        private static Material SaveFlashMaterial(string name, string texturePath)
        {
            Material material = LoadOrCreateMaterial(name, FindShader(FlashParticleShader));
            material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_TintColor", new Color(2f, 0.9f, 0.5f, 0.5f));
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>BloodMaterialFolder 의 재질을 불러와 셰이더를 바꾼다. 없으면 새로 만들어 저장한다</summary>
        /// <param name="name">재질 이름 (확장자 제외)</param>
        /// <param name="shader">재질에 쓸 셰이더</param>
        /// <returns>재질 에셋 (속성은 호출한 쪽이 채운다)</returns>
        private static Material LoadOrCreateMaterial(string name, Shader shader)
        {
            string path = $"{BloodMaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
                return material;
            }
            material.shader = shader;
            return material;
        }

        /// <summary>이름으로 셰이더를 찾는다</summary>
        /// <exception cref="InvalidOperationException">셰이더가 프로젝트에 없을 때 (URP·QFX 에셋 확인)</exception>
        private static Shader FindShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException($"셰이더 {shaderName} 를 찾지 못했다. URP·QFX 에셋이 있는지 확인");
            return shader;
        }

        /// <summary>
        /// 피 이펙트 프리팹을 (다시) 짓는다. 프리팹이 이미 있으면 그 안에서 하위 파티클만 지우고 다시 만든다.
        /// 루트의 PooledEffect 가 그대로 남으므로 몬스터·보스에 연결된 참조가 끊기지 않는다. 없으면 새로 만든다.
        /// </summary>
        /// <param name="path">프리팹 경로 (파일 이름 = 루트 이름)</param>
        /// <param name="lifetime">PooledEffect 반납까지 시간 (s). 가장 오래 남는 층보다 길게</param>
        /// <param name="addLayers">루트 아래에 파티클 층들을 붙이는 단계</param>
        /// <returns>저장된 프리팹의 PooledEffect</returns>
        private static PooledEffect SaveEffectPrefab(string path, float lifetime, Action<Transform> addLayers)
        {
            bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
            GameObject root;
            if (prefabExists) root = PrefabUtility.LoadPrefabContents(path);
            else root = new GameObject(Path.GetFileNameWithoutExtension(path));

            RemoveChildren(root.transform);
            addLayers(root.transform);
            var serialized = new SerializedObject(GetOrAdd<PooledEffect>(root));
            serialized.FindProperty("lifetime").floatValue = lifetime;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (prefabExists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
            return saved.GetComponent<PooledEffect>();
        }

        /// <summary>이전에 만든 파티클 층을 모두 지운다 (다시 지을 때 층이 겹치지 않게)</summary>
        private static void RemoveChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.GetChild(i).gameObject);
        }
        #endregion

        #region Animator
        /// <summary>
        /// Hit 레이어 4상태의 원본(자리표시) 클립을 만든다. 비어 있어서 Override 를 안 넣은 컨트롤러는 아무 동작도 하지 않는다.
        /// </summary>
        /// <returns>HitSide 순서(Front, Back, Left, Right)의 클립</returns>
        private static AnimationClip[] SaveHitPlaceholderClips()
        {
            var sides = (HitSide[])Enum.GetValues(typeof(HitSide));
            var clips = new AnimationClip[sides.Length];
            for (int i = 0; i < sides.Length; i++)
            {
                string path = $"{AnimatorFolder}/Placeholder_Hit{sides[i]}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                {
                    clip = new AnimationClip();
                    AssetDatabase.CreateAsset(clip, path);
                }
                clips[i] = clip;
            }
            return clips;
        }

        /// <summary>
        /// Base Controller 에 Additive "Hit" 레이어를 (다시) 만든다. 기본 상태 Empty, Hit 상태는 끝나기 직전 Empty 로 돌아간다.
        /// MonsterDamageFeedback 이 CrossFade 로 Hit 상태를 직접 재생하므로 파라미터·진입 전이는 없다.
        /// </summary>
        private static void AddHitLayer(AnimationClip[] placeholderClips)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BaseControllerPath);
            for (int i = controller.layers.Length - 1; i >= 0; i--)
                if (controller.layers[i].name == HitLayerName) controller.RemoveLayer(i);

            controller.AddLayer(HitLayerName);
            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer hitLayer = layers[layers.Length - 1];
            hitLayer.blendingMode = AnimatorLayerBlendingMode.Additive;
            hitLayer.defaultWeight = 1f;
            controller.layers = layers;

            AnimatorStateMachine stateMachine = hitLayer.stateMachine;
            AnimatorState empty = stateMachine.AddState("Empty");
            stateMachine.defaultState = empty;

            var sides = (HitSide[])Enum.GetValues(typeof(HitSide));
            for (int i = 0; i < sides.Length; i++)
            {
                AnimatorState state = stateMachine.AddState("Hit" + sides[i]);
                state.motion = placeholderClips[i];
                AnimatorStateTransition backToEmpty = state.AddTransition(empty);
                backToEmpty.hasExitTime = true;
                backToEmpty.exitTime = 0.85f;
                backToEmpty.duration = 0.15f;
            }
            EditorUtility.SetDirty(controller);
        }

        /// <summary>몬스터별 Override Controller 의 Hit 자리표시 클립에 그 몬스터의 GetHit 클립을 넣는다</summary>
        /// <exception cref="InvalidOperationException">표에 적힌 FBX 에 애니메이션 클립이 없을 때</exception>
        private static void AssignHitClips(AnimationClip[] placeholderClips)
        {
            foreach (KeyValuePair<string, string[]> entry in HitClipsByController)
            {
                string path = $"{AnimatorFolder}/{entry.Key}.overrideController";
                var overrideController = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
                for (int i = 0; i < placeholderClips.Length; i++)
                    overrideController[placeholderClips[i]] = LoadClip(entry.Value[i]);
                EditorUtility.SetDirty(overrideController);
            }
        }

        /// <summary>FBX 안의 애니메이션 클립 (미리보기 클립 제외)</summary>
        private static AnimationClip LoadClip(string fbxPath)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                                              .OfType<AnimationClip>()
                                              .FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
            if (clip == null) throw new InvalidOperationException($"{fbxPath} 에 애니메이션 클립이 없다");
            return clip;
        }
        #endregion

        #region 몬스터 프리팹
        /// <summary>몬스터 프리팹에 MonsterDamageFeedback(피 이펙트 3종 연결)과 MonsterCorpse 를 붙인다</summary>
        private static void SetupMonsterPrefab(string path, BloodEffects blood)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            var feedback = GetOrAdd<MonsterDamageFeedback>(root);
            GetOrAdd<MonsterCorpse>(root);

            var serialized = new SerializedObject(feedback);
            serialized.FindProperty("hitBloodPrefab").objectReferenceValue = blood.Hit;
            serialized.FindProperty("weakPointBloodPrefab").objectReferenceValue = blood.WeakPoint;
            serialized.FindProperty("deathBloodPrefab").objectReferenceValue = blood.Death;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        /// <summary>GiantSlug 약점 뼈마다 구 콜라이더 + MonsterWeakPoint(Gland) 자식을 단다. 이미 있으면 그대로 둔다</summary>
        private static void AddGiantSlugWeakPoints()
        {
            string path = $"{MonsterFolder}/GiantSlug.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            Transform[] bones = root.GetComponentsInChildren<Transform>(true);

            foreach (string boneName in GiantSlugWeakPointBones)
            {
                Transform bone = bones.First(t => t.name == boneName);
                if (bone.Find(WeakPointObjectName) != null) continue;

                var weakPoint = new GameObject(WeakPointObjectName);
                weakPoint.layer = root.layer;
                weakPoint.transform.SetParent(bone, false);
                var sphere = weakPoint.AddComponent<SphereCollider>();
                sphere.radius = WeakPointRadius;
                var part = weakPoint.AddComponent<MonsterWeakPoint>();
                var serialized = new SerializedObject(part);
                serialized.FindProperty("partKey").stringValue = "Gland";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            if (component == null) component = target.AddComponent<T>();
            return component;
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
