using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridShooter.Monster.Behaviour;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace HybridShooter.Monster.EditorTools
{
    /// <summary>
    /// 몬스터 피격·사망 표현 세팅 (메뉴 한 번). 다시 실행하면 같은 경로 에셋을 덮어쓰고 이미 붙은 컴포넌트는 그대로 쓴다.
    ///
    ///  1. 피 이펙트 3종(맞은 곳·약점·사망 터짐)을 파티클로 만들어 PooledEffect 와 함께 프리팹으로 저장
    ///  2. Monster Base Controller 에 Additive "Hit" 레이어(Empty + HitFront/Back/Left/Right) 추가
    ///  3. 몬스터별 Override Controller 의 Hit 슬롯에 GetHit 클립 연결 (방향 클립이 없으면 Front 로 대신)
    ///  4. 몬스터 프리팹 9종에 MonsterDamageFeedback(피 이펙트 연결)·MonsterCorpse 추가. 강화 개체는 변형 프리팹이라 따라온다
    ///  5. GiantSlug 등 분비샘 뼈 2곳에 약점 콜라이더(MonsterWeakPoint, PartKey Gland) 추가
    /// </summary>
    public static class MonsterHitFeedbackSetupMenu
    {
        private const string EffectsFolder = "Assets/Prefabs/Effects";
        private const string BloodMaterialFolder = EffectsFolder + "/Materials/Blood";
        private const string GlowTexturePath = EffectsFolder + "/Textures/T_ExplosionGlow.png";
        private const string PuffSheetTexturePath = EffectsFolder + "/Textures/T_ExplosionPuffSheet.png";
        private const string HitBloodPath = EffectsFolder + "/Blood_Hit.prefab";
        private const string WeakPointBloodPath = EffectsFolder + "/Blood_WeakPoint.prefab";
        private const string DeathBloodPath = EffectsFolder + "/Blood_Death.prefab";
        private const string ParticleShader = "QFX/SFX/Particle/Particle HDR";

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
            Shader shader = Shader.Find(ParticleShader);
            if (shader == null) throw new InvalidOperationException($"셰이더 {ParticleShader} 를 찾지 못했다. QFX 에셋이 있는지 확인");

            BloodEffects blood = BuildBloodEffects(shader);
            AnimationClip[] placeholderClips = SaveHitPlaceholderClips();
            AddHitLayer(placeholderClips);
            AssignHitClips(placeholderClips);
            foreach (string prefabName in MonsterPrefabNames) SetupMonsterPrefab($"{MonsterFolder}/{prefabName}.prefab", blood);
            AddGiantSlugWeakPoints();

            AssetDatabase.SaveAssets();
            Debug.Log("[MonsterHitFeedback] 피 이펙트·Hit 레이어·Override 클립·몬스터 프리팹 세팅 완료");
        }

        /// <summary>FBX 경로 앞부분 + 맞은 쪽별 접미사 4개 → 전체 경로 4개</summary>
        private static string[] Sides(string prefix, string front, string back, string left, string right)
        {
            return new[] { front, back, left, right }.Select(suffix => $"{ClipRoot}/{prefix}{suffix}.FBX").ToArray();
        }

        #region 피 이펙트
        /// <summary>재질 2개(방울·안개)를 만들고 피 이펙트 프리팹 3개를 저장한다</summary>
        private static BloodEffects BuildBloodEffects(Shader shader)
        {
            EnsureFolder(BloodMaterialFolder);
            Material dropletMaterial = SaveMaterial("M_Blood_Droplet", shader, AssetDatabase.LoadAssetAtPath<Texture2D>(GlowTexturePath));
            Material mistMaterial = SaveMaterial("M_Blood_Mist", shader, AssetDatabase.LoadAssetAtPath<Texture2D>(PuffSheetTexturePath));

            var darkBlood = new Color(0.45f, 0.02f, 0.02f, 1f);
            var brightBlood = new Color(0.8f, 0.06f, 0.03f, 1f);

            // 맞은 곳: 공격자 쪽으로 좁게 튀는 방울 + 작은 안개
            var hit = new GameObject("Blood_Hit");
            AddDroplets(hit.transform, dropletMaterial, darkBlood, count: (10, 16), speed: (2.5f, 5f), size: (0.05f, 0.11f), coneAngle: 30f);
            AddMist(hit.transform, mistMaterial, darkBlood, count: (2, 3), size: (0.3f, 0.6f));

            // 약점: 더 많고 밝은 방울 + 큰 안개 → 일반 피격과 구별
            var weakPoint = new GameObject("Blood_WeakPoint");
            AddDroplets(weakPoint.transform, dropletMaterial, brightBlood, count: (24, 34), speed: (3.5f, 7f), size: (0.08f, 0.16f), coneAngle: 45f);
            AddMist(weakPoint.transform, mistMaterial, brightBlood, count: (4, 6), size: (0.6f, 1.1f));

            // 사망: 몸통 중심에서 사방으로 터지고 바닥에 튄다
            var death = new GameObject("Blood_Death");
            ParticleSystem burst = AddDroplets(death.transform, dropletMaterial, darkBlood, count: (60, 90), speed: (3f, 8f), size: (0.1f, 0.24f), coneAngle: 0f);
            Sphere(burst, 0.4f);
            Bounce(burst);
            ParticleSystem cloud = AddMist(death.transform, mistMaterial, darkBlood, count: (6, 10), size: (1f, 2f));
            Sphere(cloud, 0.6f);

            return new BloodEffects
            {
                Hit = SaveEffectPrefab(hit, HitBloodPath, lifetime: 1f),
                WeakPoint = SaveEffectPrefab(weakPoint, WeakPointBloodPath, lifetime: 1.2f),
                Death = SaveEffectPrefab(death, DeathBloodPath, lifetime: 2.5f),
            };
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

        /// <summary>재질을 만들어 저장한다. Tint 0.5 = 파티클 색 그대로 (QFX 셰이더 = 2 × 파티클 색 × Tint × 텍스처)</summary>
        private static Material SaveMaterial(string name, Shader shader, Texture2D texture)
        {
            string path = $"{BloodMaterialFolder}/{name}.mat";
            var material = new Material(shader);
            material.SetTexture("_MainTex", texture);
            material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));

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

        /// <summary>루트에 PooledEffect(수명)를 붙여 프리팹으로 저장한다</summary>
        /// <returns>저장된 프리팹의 PooledEffect</returns>
        private static PooledEffect SaveEffectPrefab(GameObject root, string path, float lifetime)
        {
            var pooledEffect = root.AddComponent<PooledEffect>();
            var serialized = new SerializedObject(pooledEffect);
            serialized.FindProperty("lifetime").floatValue = lifetime;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<PooledEffect>();
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

        /// <summary>GiantSlug 등 분비샘 뼈마다 구 콜라이더 + MonsterWeakPoint(Gland) 자식을 단다. 이미 있으면 그대로 둔다</summary>
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
