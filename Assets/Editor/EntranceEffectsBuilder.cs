using HybridShooter.Boss.Clypeosaurus;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.EditorTools
{
    /// <summary>
    /// 보스 등장 공격의 Shadow·ShockWave 프리팹 내용을 만들고, Clypeosaurus 프리팹의 EntranceAttack 에 연결한다.
    /// 파티클 설정을 YAML 로 손으로 쓰기엔 너무 길어서 에디터 API 로 구성한다.
    /// 에디터가 스크립트를 다시 불러올 때 한 번 확인해서 아직 안 만들어졌으면 만든다.
    /// 다시 만들려면 Tools/HybridShooter/Rebuild Entrance Effects (자식 오브젝트를 지우고 새로 구성한다).
    /// </summary>
    [InitializeOnLoad]
    internal static class EntranceEffectsBuilder
    {
        private const string ShadowPath = "Assets/Resources/Projectile/Shadow.prefab";
        private const string ShockwavePath = "Assets/Resources/Projectile/ShockWave.prefab";
        private const string BossPath = "Assets/Resources/Boss/Clypeosaurus/Prefab/Clypeosaurus.prefab";

        private const string ShadowMaterialPath = "Assets/Prefabs/Effects/Materials/M_EntranceShadow.mat";
        private const string GroundMaterialPath = "Assets/Prefabs/Effects/Materials/M_EntranceImpactGround.mat";
        private const string DebrisMaterialPath = "Assets/Prefabs/Effects/Materials/M_EntranceDebris.mat";
        private const string SmokeMaterialPath = "Assets/Prefabs/Effects/Materials/M_Smoke.mat";
        private const string SparkMaterialPath = "Assets/Prefabs/Effects/Materials/M_Spark.mat";

        static EntranceEffectsBuilder()
        {
            EditorApplication.delayCall += () => Run(false);
        }

        [MenuItem("Tools/HybridShooter/Rebuild Entrance Effects")]
        private static void RebuildFromMenu()
        {
            Run(true);
        }

        private static void Run(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            var shadowAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ShadowPath);
            var shockwaveAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ShockwavePath);
            if (shadowAsset == null || shockwaveAsset == null) return;

            if (force || shadowAsset.GetComponent<EntranceShadowTelegraph>() == null)
            {
                EditPrefab(ShadowPath, BuildShadow);
                Debug.Log("[EntranceEffectsBuilder] Shadow 프리팹을 구성했습니다.");
            }

            if (force || shockwaveAsset.GetComponent<EntranceShockwave>() == null)
            {
                EditPrefab(ShockwavePath, BuildShockwave);
                Debug.Log("[EntranceEffectsBuilder] ShockWave 프리팹을 구성했습니다.");
            }

            LinkBoss(AssetDatabase.LoadAssetAtPath<GameObject>(ShadowPath), AssetDatabase.LoadAssetAtPath<GameObject>(ShockwavePath));
        }

        private static void EditPrefab(string path, System.Action<GameObject> build)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--)
                {
                    Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                }
                build(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ───────────────────────── Shadow ─────────────────────────

        private static void BuildShadow(GameObject root)
        {
            // 루트 스케일은 EntranceAttack 이 (지름, 1, 지름) 으로 덮어쓴다 → 자식 상자는 가로 1, 세로는 투사 깊이
            var projector = CreateProjectorBox(root.transform, "Projector", LoadMaterial(ShadowMaterialPath));
            projector.transform.localScale = new Vector3(1f, 14f, 1f);

            var telegraph = GetOrAdd<EntranceShadowTelegraph>(root);
            var so = new SerializedObject(telegraph);
            so.FindProperty("projector").objectReferenceValue = projector.GetComponent<MeshRenderer>();
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── ShockWave ─────────────────────────

        private static void BuildShockwave(GameObject root)
        {
            var ground = CreateProjectorBox(root.transform, "GroundWave", LoadMaterial(GroundMaterialPath));

            var flashObject = new GameObject("Flash");
            flashObject.transform.SetParent(root.transform, false);
            flashObject.transform.localPosition = new Vector3(0f, 3f, 0f);
            var flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, 0.55f, 0.2f);
            flash.range = 40f;
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;

            BuildDustWall(root.transform);
            BuildDustColumn(root.transform);
            BuildDebris(root.transform);
            BuildSparks(root.transform);

            var shockwave = GetOrAdd<EntranceShockwave>(root);
            var so = new SerializedObject(shockwave);
            so.FindProperty("groundWave").objectReferenceValue = ground.GetComponent<MeshRenderer>();
            so.FindProperty("flash").objectReferenceValue = flash;
            so.ApplyModifiedPropertiesWithoutUndo();
            shockwave.ApplyRadius();
        }

        /// <summary>땅을 따라 바깥으로 밀려나는 흙먼지 벽 (피해 반경 20m 근처에서 멈춘다)</summary>
        private static void BuildDustWall(Transform parent)
        {
            var ps = CreateParticles(parent, "DustWall", LoadMaterial(SmokeMaterialPath), new Vector3(0f, 0.8f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(34f, 44f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.43f, 0.36f, 0.85f), new Color(0.36f, 0.31f, 0.27f, 0.85f));
            main.maxParticles = 150;

            SetBurst(ps, 90);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 2f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.drag = 2f;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;

            SetSizeGrowth(ps, 0.6f, 1.6f);
            SetFadeInOut(ps, 0.05f, 0.5f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        }

        /// <summary>착지 지점에서 피어오르는 큰 먼지 덩어리</summary>
        private static void BuildDustColumn(Transform parent)
        {
            var ps = CreateParticles(parent, "DustColumn", LoadMaterial(SmokeMaterialPath), new Vector3(0f, 1f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 10f);
            main.startSize = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.42f, 0.36f, 0.31f, 0.9f), new Color(0.3f, 0.26f, 0.23f, 0.9f));
            main.gravityModifier = -0.05f;
            main.maxParticles = 60;

            SetBurst(ps, 30);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 4f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.drag = 1.2f;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;

            SetSizeGrowth(ps, 0.7f, 1.5f);
            SetFadeInOut(ps, 0.05f, 0.45f);
        }

        /// <summary>튀어 오른 뒤 떨어져 땅에 튕기는 바위 파편</summary>
        private static void BuildDebris(Transform parent)
        {
            var ps = CreateParticles(parent, "Debris", LoadMaterial(DebrisMaterialPath), new Vector3(0f, 0.5f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 20f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.3f, 1.1f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.3f, 1.0f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = Color.white;
            main.gravityModifier = 2.5f;
            main.maxParticles = 60;

            SetBurst(ps, 40);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = 3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rotation.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = 0.45f;
            collision.bounce = 0.3f;
            collision.lifetimeLoss = 0f;

            // 사라질 때 툭 끊기지 않게 마지막에 작아진다
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        /// <summary>착지 순간 튀는 불꽃</summary>
        private static void BuildSparks(Transform parent)
        {
            var ps = CreateParticles(parent, "Sparks", LoadMaterial(SparkMaterialPath), new Vector3(0f, 0.5f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(12f, 28f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.55f, 0.15f, 1f), new Color(1f, 0.3f, 0.05f, 1f));
            main.gravityModifier = 1.2f;
            main.maxParticles = 150;

            SetBurst(ps, 100);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 65f;
            shape.radius = 2f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            SetFadeInOut(ps, 0f, 0.3f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1.5f;
        }

        // ───────────────────────── 공통 ─────────────────────────

        private static GameObject CreateProjectorBox(Transform parent, string name, Material material)
        {
            var box = new GameObject(name);
            box.transform.SetParent(parent, false);
            box.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var renderer = box.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return box;
        }

        private static ParticleSystem CreateParticles(Transform parent, string name, Material material, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.stopAction = ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        private static void SetBurst(ParticleSystem ps, short count)
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void SetSizeGrowth(ParticleSystem ps, float from, float to)
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, from, 0f, 2f), new Keyframe(1f, to)));
        }

        private static void SetFadeInOut(ParticleSystem ps, float fadeIn, float fadeOutStart)
        {
            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(fadeIn > 0f ? 0f : 1f, 0f),
                    new GradientAlphaKey(1f, Mathf.Max(fadeIn, 0.001f)),
                    new GradientAlphaKey(1f, fadeOutStart),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static Material LoadMaterial(string path)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) Debug.LogWarning($"[EntranceEffectsBuilder] 머티리얼을 찾지 못했습니다: {path}");
            return material;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        /// <summary>보스 프리팹의 EntranceAttack 에 비어 있는 슬롯만 채운다 (이미 다른 걸 넣어 뒀으면 건드리지 않는다)</summary>
        private static void LinkBoss(GameObject shadow, GameObject shockwave)
        {
            var bossAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BossPath);
            if (bossAsset == null || shadow == null || shockwave == null) return;

            var entrance = bossAsset.GetComponentInChildren<EntranceAttack>(true);
            if (entrance == null) return;

            var check = new SerializedObject(entrance);
            bool needsShadow = check.FindProperty("shadowPrefab").objectReferenceValue == null;
            bool needsShockwave = check.FindProperty("shockwavePrefab").objectReferenceValue == null;
            if (needsShadow == false && needsShockwave == false) return;

            GameObject root = PrefabUtility.LoadPrefabContents(BossPath);
            try
            {
                var so = new SerializedObject(root.GetComponentInChildren<EntranceAttack>(true));
                if (needsShadow) so.FindProperty("shadowPrefab").objectReferenceValue = shadow.transform;
                if (needsShockwave) so.FindProperty("shockwavePrefab").objectReferenceValue = shockwave;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, BossPath);
                Debug.Log("[EntranceEffectsBuilder] Clypeosaurus 의 EntranceAttack 에 Shadow·ShockWave 를 연결했습니다.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
