using HybridShooter.Boss.Clypeosaurus;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace HybridShooter.EditorTools
{
    /// <summary>
    /// 흡입 패턴 마무리 공격(ClawStrike 앞발 내려찍기)의 착지 이펙트 프리팹을 만들고 SuctionAttack 에 연결한다.
    ///  - 바닥: 흙빛 충격 링이 퍼지고, 찍은 자리에 어두운 금과 그을음이 남았다 사라진다 (EntranceImpactGround 셰이더 재사용)
    ///  - 흙먼지: 바닥을 따라 퍼지는 먼지 고리 + 가운데 먼지 덩어리
    ///  - 땅 조각: 납작한 흙판이 살짝 튀어 올랐다가 떨어져 땅에 닿고 가라앉는다
    ///  - 잔돌: 작은 돌 조각이 튄다
    ///  - 카메라 흔들림 (EntranceShockwave 컴포넌트 재사용)
    /// SuctionAttack 이 3초 뒤 Destroy 하므로 모든 연출은 3초 안에 끝난다.
    /// 프리팹이 없을 때 한 번 자동으로 만들고, 다시 만들려면 Tools/HybridShooter/Rebuild Claw Impact Effect.
    /// </summary>
    [InitializeOnLoad]
    internal static class ClawImpactEffectBuilder
    {
        private const string PrefabPath = "Assets/Resources/Projectile/ClawImpact.prefab";
        private const string BossPath = "Assets/Resources/Boss/Clypeosaurus/Prefab/Clypeosaurus.prefab";

        private const string GroundMaterialPath = "Assets/Prefabs/Effects/Materials/M_ClawImpactGround.mat";
        private const string ChunkMaterialPath = "Assets/Prefabs/Effects/Materials/M_GroundChunk.mat";
        private const string SmokeMaterialPath = "Assets/Prefabs/Effects/Materials/M_Smoke.mat";

        // ClypeosaurusTable 의 SuctionDamageRadius
        private const float ImpactRadius = 10f;

        static ClawImpactEffectBuilder()
        {
            EditorApplication.delayCall += () => Run(false);
        }

        [MenuItem("Tools/HybridShooter/Rebuild Claw Impact Effect")]
        private static void RebuildFromMenu()
        {
            Run(true);
        }

        private static void Run(bool force)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (force || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            {
                var root = new GameObject("ClawImpact");
                try
                {
                    Build(root);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                    Debug.Log($"[ClawImpactEffectBuilder] {PrefabPath} 를 만들었습니다.");
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            }

            LinkBoss(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        }

        private static void Build(GameObject root)
        {
            var ground = CreateProjectorBox(root.transform, "GroundWave", LoadMaterial(GroundMaterialPath));

            BuildDustRing(root.transform);
            BuildDustPuff(root.transform);
            BuildGroundChunks(root.transform);
            BuildPebbles(root.transform);

            // 바닥 데칼 시간 진행 + 카메라 흔들림은 등장 공격 충격파 컴포넌트를 그대로 쓴다 (섬광 없음)
            var shockwave = root.AddComponent<EntranceShockwave>();
            var so = new SerializedObject(shockwave);
            so.FindProperty("groundWave").objectReferenceValue = ground.GetComponent<MeshRenderer>();
            so.FindProperty("radius").floatValue = ImpactRadius;
            so.FindProperty("projectionHeight").floatValue = 8f;
            so.FindProperty("shakeRadius").floatValue = 45f;
            var shake = so.FindProperty("shake");
            shake.FindPropertyRelative("Intensity").floatValue = 0.5f;
            shake.FindPropertyRelative("Duration").floatValue = 0.45f;
            shake.FindPropertyRelative("Frequency").floatValue = 7f;
            shake.FindPropertyRelative("FadeIn").floatValue = 0.01f;
            shake.FindPropertyRelative("FadeOut").floatValue = 0.3f;
            so.ApplyModifiedPropertiesWithoutUndo();
            shockwave.ApplyRadius();
        }

        /// <summary>바닥을 따라 바깥으로 퍼지다 피해 반경(10m) 근처에서 멈추는 먼지 고리</summary>
        private static void BuildDustRing(Transform parent)
        {
            var ps = CreateParticles(parent, "DustRing", LoadMaterial(SmokeMaterialPath), new Vector3(0f, 0.4f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.3f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(18f, 24f);
            main.startSize = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.47f, 0.38f, 0.75f), new Color(0.4f, 0.34f, 0.28f, 0.75f));
            main.maxParticles = 100;

            SetBurst(ps, 60);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            SetDrag(ps, 2.2f);
            SetSizeGrowth(ps, 0.6f, 1.7f);
            SetFadeInOut(ps, 0.05f, 0.45f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        }

        /// <summary>찍은 자리에서 뭉게 피어오르는 먼지</summary>
        private static void BuildDustPuff(Transform parent)
        {
            var ps = CreateParticles(parent, "DustPuff", LoadMaterial(SmokeMaterialPath), new Vector3(0f, 0.6f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.5f, 0.43f, 0.35f, 0.8f), new Color(0.37f, 0.32f, 0.27f, 0.8f));
            main.gravityModifier = -0.04f;
            main.maxParticles = 40;

            SetBurst(ps, 20);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 2.5f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            SetDrag(ps, 1.2f);
            SetSizeGrowth(ps, 0.7f, 1.5f);
            SetFadeInOut(ps, 0.05f, 0.45f);
        }

        /// <summary>납작한 흙판이 바닥에서 살짝 튀어 올랐다가 떨어져 가라앉는다</summary>
        private static void BuildGroundChunks(Transform parent)
        {
            var ps = CreateParticles(parent, "GroundChunks", LoadMaterial(ChunkMaterialPath), new Vector3(0f, 0.1f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.7f, 2.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            main.startColor = Color.white;
            main.gravityModifier = 2f;
            main.maxParticles = 30;

            SetBurst(ps, 18);

            // 원판 전체에서 위로 곧게 솟는다 (가운데는 비워 둔다)
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 0f;
            shape.radius = 7f;
            shape.radiusThickness = 0.75f;
            shape.randomDirectionAmount = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
            rotation.y = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
            rotation.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);

            SetGroundCollision(ps, 0.7f, 0.1f);

            // 땅에 닿은 뒤 서서히 가라앉듯 작아진다
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.65f, 1f), new Keyframe(1f, 0f)));

            SetMeshRenderer(ps);
        }

        /// <summary>찍는 순간 튀는 작은 돌</summary>
        private static void BuildPebbles(Transform parent)
        {
            var ps = CreateParticles(parent, "Pebbles", LoadMaterial(ChunkMaterialPath), new Vector3(0f, 0.3f, 0f));

            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
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
            shape.angle = 40f;
            shape.radius = 2f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var rotation = ps.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rotation.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
            rotation.z = new ParticleSystem.MinMaxCurve(-8f, 8f);

            SetGroundCollision(ps, 0.5f, 0.3f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));

            SetMeshRenderer(ps);
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

        private static void SetMeshRenderer(ParticleSystem ps)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static void SetGroundCollision(ParticleSystem ps, float dampen, float bounce)
        {
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = ParticleSystemCollisionQuality.Medium;
            collision.dampen = dampen;
            collision.bounce = bounce;
            collision.lifetimeLoss = 0f;
        }

        private static void SetBurst(ParticleSystem ps, short count)
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });
        }

        private static void SetDrag(ParticleSystem ps, float drag)
        {
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1000f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;
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
            if (material == null) Debug.LogWarning($"[ClawImpactEffectBuilder] 머티리얼을 찾지 못했습니다: {path}");
            return material;
        }

        /// <summary>보스 프리팹 SuctionAttack 의 Attack Effect Prefab 이 비어 있을 때만 채운다</summary>
        private static void LinkBoss(GameObject effect)
        {
            var bossAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BossPath);
            if (bossAsset == null || effect == null) return;

            var suction = bossAsset.GetComponentInChildren<SuctionAttack>(true);
            if (suction == null) return;
            if (new SerializedObject(suction).FindProperty("attackEffectPrefab").objectReferenceValue != null) return;

            GameObject root = PrefabUtility.LoadPrefabContents(BossPath);
            try
            {
                var so = new SerializedObject(root.GetComponentInChildren<SuctionAttack>(true));
                so.FindProperty("attackEffectPrefab").objectReferenceValue = effect;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, BossPath);
                Debug.Log("[ClawImpactEffectBuilder] SuctionAttack 의 Attack Effect Prefab 에 ClawImpact 를 연결했습니다.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
