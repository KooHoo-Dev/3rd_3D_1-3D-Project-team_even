using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridShooter.Boss.Tyranopode;
using HybridShooter.Core.Contracts;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace HybridShooter.Boss.TyranopodeEditor
{
    /// <summary>
    /// Tyranopode 보스 프리팹을 에셋 팩 원본에서 만든다 (원본은 건드리지 않는다).
    ///  - Resources/Boss/Tyranopode/ 에 애니메이터 컨트롤러·머티리얼·침/알/포자 프리팹·보스 프리팹 생성
    ///  - 다시 실행해도 인스펙터에서 손본 값(패턴 가중치·타격 시점·히트박스 크기·이동 값 등)은 이전 프리팹에서 옮겨 온다
    ///  - 모델 구조: 몸통 뼈 BODY_01(아래)→05(위), 맨 위 돔 둘레에 턱 꽃잎 6장(JAW_*), 촉수 6개(앞 F·중간 M·뒤 R × 좌우)
    ///  - 히트박스: 몸통(기본 레이어, 플레이어를 막음) / 머리·주머니·촉수(BossHitbox 레이어, 총알 판정만)
    ///  - 앞 촉수(F) 쪽이 루트 +Z(정면)가 되도록 모델을 돌린다 (ROOT_ 에 회전 애니메이션이 없을 때만)
    ///  - 기어가기 기준 속도는 CrawlForward_RM 클립의 이동량으로 잰다 (발 미끄러짐 방지)
    /// </summary>
    public static class TyranopodePrefabBuilder
    {
        private const string PackRoot = "Assets/ExternalAssets/Monsters Full Pack Vol 1/Monsters Pack Vol 1/Tyranopode";
        private const string SourcePrefab = PackRoot + "/Prefabs/Tyranopode.prefab";
        private const string FbxFolder = PackRoot + "/FBX Files";
        private const string OutFolder = "Assets/Resources/Boss/Tyranopode";
        private const string PrefabPath = OutFolder + "/Tyranopode.prefab";
        private const string ControllerPath = OutFolder + "/Tyranopode_Boss.controller";
        private const string BonePrefix = "TYRANOPODE_";
        private const string ModelRootBone = "ROOT_";
        /// <summary>NotoSansKR-Regular SDF (HudFactory.TextFontGuid 와 같은 글꼴)</summary>
        private const string KoreanFontGuid = "659ceb8947c683b4a9e1216df0a67515";
        /// <summary>Aldrich-Regular SDF (HudFactory.NumberFontGuid 와 같은 글꼴) — 분석 화면 영문·숫자</summary>
        private const string TechFontGuid = "4d0bb7337f597634b94b9005947e8e28";
        /// <summary>
        /// 모델 크기 배율 (원본 = 1, 높이 약 5m). 키우면 TyranopodeTable 의 거리 값도 같이 키워야 한다.
        /// 뼈로 만드는 것(히트박스·막·입)과 NavMeshAgent(트랜스폼 배율을 따른다)는 따라 커지고,
        /// 크기에 묶인 이동 값(SetupScaledValues)은 다시 만들 때마다 이 배율로 계산한다. 연출 크기는 런타임에 BodyScale 로 맞춘다
        /// </summary>
        private const float ModelScale = 1.6f;

        private static readonly string[] StateClips =
        {
            "Idle", "CrawlForward", "CrawlBackwards", "CrawlLeft", "CrawlRight",
            "Bite", "JumpBite", "JumpTentaclesSmashAttack",
            "TentacleAttackLeft", "TentacleAttackRight", "Tentacles2HitCombo", "Tentacles3HitCombo", "TentaclesSmashAttack",
            "Spit", "RoarExhaleGas", "GetHit", "Death",
        };

        private static readonly Color Glow = new Color(1f, 0.55f, 0.1f);

        /// <summary>인스펙터 값을 이전 프리팹에서 옮겨 올 컴포넌트 (부위 컴포넌트는 뼈 참조가 있어 다시 만든다)</summary>
        private static readonly System.Type[] PreservedTypes =
        {
            typeof(TyranopodeAttack), typeof(TyranopodeView), typeof(TyranopodeLocomotion), typeof(TyranopodeController),
            typeof(BossView), typeof(NavMeshAgent), typeof(CapsuleCollider), typeof(SphereCollider),
        };

        [MenuItem("Tools/HybridShooter/Boss/Tyranopode 프리팹 만들기")]
        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefab);
            if (source == null)
            {
                Debug.LogError($"[TyranopodeBuilder] 원본 프리팹이 없습니다: {SourcePrefab}");
                return;
            }
            EnsureFolder(OutFolder);
            int hitboxLayer = EnsureLayer(TyranopodeController.HitboxLayerName);
            var previous = Capture(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var vfx = TyranopodeVfxBuilder.BuildAll();

            var controller = BuildAnimator();
            var telegraphMat = SaveMaterial(CreateTransparentMaterial(), "M_Telegraph");
            var glowMat = SaveMaterial(CreateGlowMaterial(Glow, 6f), "M_Glow");
            var eggMat = SaveMaterial(CreateGlowMaterial(new Color(0.9f, 0.75f, 0.45f), 1.5f), "M_Egg");

            var spit = BuildSpit(glowMat, vfx);
            var egg = BuildEgg(eggMat, vfx);
            var beacon = BuildBeacon(glowMat, vfx);

            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                root.name = "Tyranopode";
                root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                root.transform.localScale = Vector3.one * ModelScale;

                var bones = root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith(BonePrefix))
                    .ToDictionary(t => t.name.Substring(BonePrefix.Length));
                AlignFront(root, bones);

                SetupRoot(root, controller);
                var mouth = BuildHitboxes(root, bones, hitboxLayer, vfx.Stump, out float tentacleLength);

                Restore(root, previous);
                SetupReferences(root, mouth, telegraphMat, spit, egg, beacon, vfx);
                SetupScaledValues(root, tentacleLength);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool saved);
                if (saved) Debug.Log($"[TyranopodeBuilder] 완료: {PrefabPath} (인스펙터 값 {previous.Count}개 유지)", AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                else Debug.LogError("[TyranopodeBuilder] 프리팹 저장 실패");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tools/HybridShooter/Boss/Tyranopode 프리팹 처음부터 다시 만들기 (조정값 버림)")]
        public static void Rebuild()
        {
            if (EditorUtility.DisplayDialog("Tyranopode", "인스펙터에서 조정한 값(가중치·타격 시점·히트박스 크기 등)을 모두 버리고 다시 만듭니다.\n씬에 이미 놓은 보스는 프리팹 연결이 끊기니 다시 배치해주세요.", "다시 만들기", "취소") == false)
                return;
            AssetDatabase.DeleteAsset(PrefabPath);
            Build();
        }

        [MenuItem("Tools/HybridShooter/Boss/Tyranopode 현재 씬에 배치 (테스트)")]
        public static void PlaceInScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError("[TyranopodeBuilder] 먼저 'Tyranopode 프리팹 만들기'를 실행해주세요.");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var spawn = GameObject.Find("BossSpawnPoint");
            Vector3 position = spawn != null ? spawn.transform.position
                : SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            instance.transform.position = position;

            // 씬의 플레이어를 연결 (라운드 없이 바로 싸우기)
            var player = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).FirstOrDefault(m => m is IPlayerTarget);
            var so = new SerializedObject(instance.GetComponent<TyranopodeController>());
            so.FindProperty("playerSource").objectReferenceValue = player;
            so.ApplyModifiedPropertiesWithoutUndo();

            Undo.RegisterCreatedObjectUndo(instance, "Place Tyranopode");
            Selection.activeGameObject = instance;
            if (player == null) Debug.LogWarning("[TyranopodeBuilder] 씬에 플레이어(IPlayerTarget)가 없습니다. Play 시 ServiceLocator 에서 찾습니다.");
            Debug.Log("[TyranopodeBuilder] 배치 완료. 라운드 흐름(RunFlow)이 있는 씬이면 라운드 보스와 겹치니 테스트 씬에서 쓰세요.", instance);
        }

        // ───────────────────────── 레이어 ─────────────────────────

        /// <summary>레이어가 없으면 빈 칸(8번 이후)에 만든다. 반환 = 레이어 번호 (실패 시 0 = Default)</summary>
        private static int EnsureLayer(string name)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) return i;

            for (int i = 8; i < layers.arraySize; i++)
            {
                var slot = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(slot.stringValue) == false) continue;
                slot.stringValue = name;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log($"[TyranopodeBuilder] 레이어 '{name}' 를 {i}번에 추가했습니다 (ProjectSettings/TagManager.asset — 팀 공유 설정).");
                return i;
            }
            Debug.LogError($"[TyranopodeBuilder] 빈 레이어 칸이 없어 '{name}' 를 만들지 못했습니다. 히트박스가 플레이어와 부딪칠 수 있습니다.");
            return 0;
        }

        // ───────────────────────── 애니메이터 ─────────────────────────

        private static AnimatorController BuildAnimator()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool); // BossView 가 쓴다
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("PlaySpeed", AnimatorControllerParameterType.Float);
            var parameters = controller.parameters;
            foreach (var p in parameters)
                if (p.name == "PlaySpeed") p.defaultFloat = 1f;
            controller.parameters = parameters;

            var machine = controller.layers[0].stateMachine;
            var clips = new Dictionary<string, AnimationClip>();
            AnimatorState idle = null;
            foreach (var name in StateClips)
            {
                var clip = LoadClip(name);
                if (clip == null) continue;
                clips[name] = clip;
                var state = machine.AddState(name);
                state.motion = clip;
                state.speedParameter = "PlaySpeed";
                state.speedParameterActive = true;
                if (name == "Idle") idle = state;
            }

            var locomotion = controller.CreateBlendTreeInController("Locomotion", out var tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveZ";
            AddChild(tree, clips, "Idle", Vector2.zero);
            AddChild(tree, clips, "CrawlForward", Vector2.up);
            AddChild(tree, clips, "CrawlBackwards", Vector2.down);
            AddChild(tree, clips, "CrawlLeft", Vector2.left);
            AddChild(tree, clips, "CrawlRight", Vector2.right);
            locomotion.speedParameter = "PlaySpeed";
            locomotion.speedParameterActive = true;

            if (idle != null) machine.defaultState = idle;
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddChild(BlendTree tree, Dictionary<string, AnimationClip> clips, string name, Vector2 position)
        {
            if (clips.TryGetValue(name, out var clip)) tree.AddChild(clip, position);
        }

        private static AnimationClip LoadClip(string name)
        {
            string path = $"{FbxFolder}/Tyranopode@{name}.FBX";
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
            if (clip == null) Debug.LogWarning($"[TyranopodeBuilder] 클립 없음: {path}");
            return clip;
        }

        /// <summary>
        /// 기어가기 1배속의 실제 이동 속도(m/s). 루트 모션 클립의 평균 속도, 없으면 가장 위 뼈의 위치 곡선 이동량으로 잰다
        /// </summary>
        private static float MeasureCrawlSpeed()
        {
            var clip = LoadClip("CrawlForward_RM");
            if (clip == null || clip.length <= 0f) return 0f;
            if (clip.averageSpeed.magnitude > 0.05f) return clip.averageSpeed.magnitude;

            var bindings = AnimationUtility.GetCurveBindings(clip)
                .Where(b => b.propertyName.StartsWith("m_LocalPosition"))
                .GroupBy(b => b.path)
                .OrderBy(g => g.Key.Count(c => c == '/'))
                .FirstOrDefault();
            if (bindings == null) return 0f;

            Vector3 Sample(float time)
            {
                Vector3 v = Vector3.zero;
                foreach (var binding in bindings)
                {
                    float value = AnimationUtility.GetEditorCurve(clip, binding).Evaluate(time);
                    if (binding.propertyName.EndsWith(".x")) v.x = value;
                    else if (binding.propertyName.EndsWith(".y")) v.y = value;
                    else v.z = value;
                }
                return v;
            }
            return (Sample(clip.length) - Sample(0f)).magnitude / clip.length;
        }

        // ───────────────────────── 루트·컴포넌트 ─────────────────────────

        /// <summary>앞 촉수 뿌리 쪽이 루트 +Z 를 보도록 ROOT_ 를 돌린다</summary>
        private static void AlignFront(GameObject root, Dictionary<string, Transform> bones)
        {
            var modelRoot = root.transform.Find(ModelRootBone);
            var fl = Bone(bones, "TENTACLE_F_L_001");
            var fr = Bone(bones, "TENTACLE_F_R_001");
            var rl = Bone(bones, "TENTACLE_R_L_001");
            var rr = Bone(bones, "TENTACLE_R_R_001");
            if (modelRoot == null || fl == null || fr == null || rl == null || rr == null) return;

            Vector3 front = (fl.position + fr.position) * 0.5f - (rl.position + rr.position) * 0.5f;
            front.y = 0f;
            float angle = Vector3.SignedAngle(front, Vector3.forward, Vector3.up);
            if (Mathf.Abs(angle) < 1f) return;

            if (HasRootRotationCurves())
            {
                Debug.LogWarning($"[TyranopodeBuilder] ROOT_ 에 회전 애니메이션이 있어 정면을 돌리지 못했습니다 (앞 촉수가 +Z 에서 {angle:0}° 벗어남). 직접 확인해주세요.");
                return;
            }
            modelRoot.RotateAround(root.transform.position, Vector3.up, angle);
            Debug.Log($"[TyranopodeBuilder] 앞 촉수가 정면(+Z)을 보도록 모델을 {angle:0}° 돌렸습니다.");
        }

        private static bool HasRootRotationCurves()
        {
            foreach (var name in StateClips)
            {
                var clip = LoadClip(name);
                if (clip == null) continue;
                if (AnimationUtility.GetCurveBindings(clip).Any(b => b.path == ModelRootBone && b.propertyName.Contains("Rotation")))
                    return true;
            }
            return false;
        }

        private static void SetupRoot(GameObject root, AnimatorController controller)
        {
            var animator = root.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            // BossController 가 BossMovement(NavMeshAgent·NavMeshMoveExecutor)·BossView 를 함께 붙인다
            root.AddComponent<BossController>();
            var agent = root.GetComponent<NavMeshAgent>();
            agent.radius = 3f;
            agent.height = 5f;
            agent.speed = 8f;
            agent.angularSpeed = 0f;
            agent.acceleration = 40f;
            agent.stoppingDistance = 0f;
            agent.autoBraking = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;

            var bossView = new SerializedObject(root.GetComponent<BossView>());
            bossView.FindProperty("idleState").stringValue = "Base Layer.Idle";
            bossView.FindProperty("hitState").stringValue = "Base Layer.GetHit";
            bossView.FindProperty("deathState").stringValue = "Base Layer.Death";
            bossView.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<TyranopodeView>();
            root.AddComponent<TyranopodeLocomotion>();
            root.AddComponent<TyranopodeController>();

            // 패턴과 기본 가중치 (근·중·원). 숫자는 인스펙터에서 조정 (다시 빌드해도 유지)
            AddAttack<TentacleSwipeAttack>(root, 5f, 0.5f, 0f, false);
            AddAttack<BiteAttack>(root, 3f, 2.5f, 0f, false);
            AddAttack<JumpSmashAttack>(root, 0f, 2f, 3f, true);
            AddAttack<ChargeAttack>(root, 0.5f, 2f, 4f, true);
            AddAttack<SpitAttack>(root, 0f, 2f, 4f, false);
            AddAttack<EggThrowAttack>(root, 1f, 1.5f, 1.5f, false);
            AddAttack<TremorAttack>(root, 2f, 2f, 1f, false);
            AddAttack<SummonRoarAttack>(root, 0f, 0f, 0f, false);
            AddAttack<TentacleStormAttack>(root, 0f, 0f, 0f, true);
        }

        private static void AddAttack<T>(GameObject root, float near, float mid, float far, bool heavy) where T : TyranopodeAttack
        {
            var so = new SerializedObject(root.AddComponent<T>());
            so.FindProperty("nearWeight").floatValue = near;
            so.FindProperty("midWeight").floatValue = mid;
            so.FindProperty("farWeight").floatValue = far;
            so.FindProperty("heavy").boolValue = heavy;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>빌더가 만드는 참조·연출은 매번 다시 연결한다 (연출을 바꾸려면 TyranopodeVfxBuilder 를 고친다)</summary>
        private static void SetupReferences(GameObject root, Transform mouth, Material telegraph,
            TyranopodeSpit spit, TyranopodeEgg egg, SummonBeacon beacon, TyranopodeVfxBuilder.Set vfx)
        {
            var so = new SerializedObject(root.GetComponent<TyranopodeController>());
            so.FindProperty("playerSource").objectReferenceValue = null;
            so.FindProperty("arenaCenter").objectReferenceValue = null;
            so.FindProperty("mouth").objectReferenceValue = mouth;
            so.FindProperty("telegraphMaterial").objectReferenceValue = telegraph;
            so.FindProperty("spitPrefab").objectReferenceValue = spit;
            so.FindProperty("eggPrefab").objectReferenceValue = egg;
            so.FindProperty("beaconPrefab").objectReferenceValue = beacon;
            so.FindProperty("roarVfx").objectReferenceValue = vfx.Roar;
            so.FindProperty("shockwaveVfx").objectReferenceValue = vfx.Shockwave;
            so.FindProperty("impactVfx").objectReferenceValue = vfx.Impact;
            so.FindProperty("partBreakVfx").objectReferenceValue = vfx.PartBreak;
            so.FindProperty("waveVfx").objectReferenceValue = vfx.Wave;
            so.FindProperty("beaconFailVfx").objectReferenceValue = vfx.BeaconFail;
            so.FindProperty("minionSpawnVfx").objectReferenceValue = vfx.MinionSpawn;
            so.FindProperty("minionDeathVfx").objectReferenceValue = vfx.MinionDeath;
            so.FindProperty("groggyVfx").objectReferenceValue = AttachVfx(vfx.Groggy, mouth, mouth.position, "GroggyVfx");
            so.FindProperty("shield").objectReferenceValue = BuildShield(root, vfx);
            so.FindProperty("energyOrbVfx").objectReferenceValue = vfx.EnergyOrb;
            so.FindProperty("energyAbsorbVfx").objectReferenceValue = vfx.EnergyAbsorb;
            so.FindProperty("immuneCue").objectReferenceValue = BuildImmuneCue(root, vfx);
            so.FindProperty("ricochetVfx").objectReferenceValue = vfx.Ricochet;
            so.FindProperty("introVeinsVfx").objectReferenceValue = vfx.IntroVeins;
            so.FindProperty("techFont").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(TechFontGuid));
            so.FindProperty("finaleAssets.GroundEnergyVfx").objectReferenceValue = vfx.FinaleGroundEnergy;
            so.FindProperty("finaleAssets.StrikeVfx").objectReferenceValue = vfx.OrbitalStrike;
            so.FindProperty("finaleAssets.BeamMaterial").objectReferenceValue = vfx.Beam;
            so.FindProperty("finaleAssets.Font").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(KoreanFontGuid));
            so.ApplyModifiedPropertiesWithoutUndo();

            var view = new SerializedObject(root.GetComponent<TyranopodeView>());
            view.FindProperty("bodyRenderer").objectReferenceValue = root.GetComponentInChildren<SkinnedMeshRenderer>();
            view.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 보호막 구: 몸통 가운데 뼈(BODY_03)를 따라다니며 몸통·머리를 감싼다. 콜라이더 없음 (총알은 안쪽 부위를 맞히고, 양만 보호막이 먼저 받는다)
        /// </summary>
        private static TyranopodeShield BuildShield(GameObject root, TyranopodeVfxBuilder.Set vfx)
        {
            var bones = root.GetComponentsInChildren<Transform>(true);
            var center = bones.First(t => t.name == BonePrefix + "BODY_03");
            var bottom = bones.First(t => t.name == BonePrefix + "BODY_01");
            var top = bones.First(t => t.name == BonePrefix + "BODY_05");
            float diameter = Vector3.Distance(bottom.position, top.position) * 2.6f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Shield";
            go.transform.SetParent(center, false);
            go.transform.position = center.position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * (diameter / Mathf.Max(0.0001f, center.lossyScale.x));
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = vfx.Shield;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            var shield = go.AddComponent<TyranopodeShield>();
            var so = new SerializedObject(shield);
            so.FindProperty("bubble").objectReferenceValue = renderer;
            so.FindProperty("hitVfx").objectReferenceValue = vfx.ShieldHit;
            so.FindProperty("crackVfx").objectReferenceValue = vfx.ShieldCrack;
            so.FindProperty("shatterVfx").objectReferenceValue = vfx.ShieldShatter;
            so.ApplyModifiedPropertiesWithoutUndo();
            return shield;
        }

        /// <summary>
        /// 포효 소환 중 무적 표시 막: 보호막과 같은 자리·셰이더, 조금 더 크게. 색은 TyranopodeImmuneCue 가 차갑게 바꾼다
        /// </summary>
        private static TyranopodeImmuneCue BuildImmuneCue(GameObject root, TyranopodeVfxBuilder.Set vfx)
        {
            var bones = root.GetComponentsInChildren<Transform>(true);
            var center = bones.First(t => t.name == BonePrefix + "BODY_03");
            var bottom = bones.First(t => t.name == BonePrefix + "BODY_01");
            var top = bones.First(t => t.name == BonePrefix + "BODY_05");
            float diameter = Vector3.Distance(bottom.position, top.position) * 2.9f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "ImmuneShell";
            go.transform.SetParent(center, false);
            go.transform.position = center.position;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * (diameter / Mathf.Max(0.0001f, center.lossyScale.x));
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = vfx.Shield;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            var cue = go.AddComponent<TyranopodeImmuneCue>();
            var so = new SerializedObject(cue);
            so.FindProperty("shell").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            return cue;
        }

        /// <summary>연출 프리팹을 parent 아래 position 에 꺼 둔 채 붙인다 (상처·그로기처럼 켜고 끄는 연출)</summary>
        private static GameObject AttachVfx(GameObject prefab, Transform parent, Vector3 position, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            instance.transform.position = position;
            instance.SetActive(false);
            return instance;
        }

        /// <summary>처음 만들 때만: 모델에서 잰 값 (이후에는 인스펙터 값을 존중)</summary>
        /// <summary>
        /// 모델 크기에 묶인 값: 다시 만들 때마다 계산해 덮어쓴다 (크기를 바꾸면 이전 값이 맞지 않으므로 보존하지 않는다)
        /// </summary>
        private static void SetupScaledValues(GameObject root, float tentacleLength)
        {
            float crawl = MeasureCrawlSpeed() * ModelScale;
            var view = new SerializedObject(root.GetComponent<TyranopodeView>());
            if (crawl > 0.1f) view.FindProperty("crawlReferenceSpeed").floatValue = crawl;
            view.ApplyModifiedPropertiesWithoutUndo();

            var locomotion = new SerializedObject(root.GetComponent<TyranopodeLocomotion>());
            if (tentacleLength > 0f) locomotion.FindProperty("wallMargin").floatValue = tentacleLength * 0.8f;
            // 돌진 벽 감지: 몸통 절반 높이·몸통 폭, 몸 중심에서 앞 촉수 절반쯤까지
            // NavMeshAgent 반지름·높이는 로컬 값이라 배율을 곱해야 월드 크기
            var agent = root.GetComponent<UnityEngine.AI.NavMeshAgent>();
            locomotion.FindProperty("wallProbeHeight").floatValue = agent.height * ModelScale * 0.5f;
            locomotion.FindProperty("wallProbeRadius").floatValue = agent.radius * ModelScale * 0.7f;
            if (tentacleLength > 0f) locomotion.FindProperty("bodyReach").floatValue = tentacleLength * 0.4f;
            locomotion.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log($"[TyranopodeBuilder] 기어가기 1배속 = {crawl:0.##} m/s (TyranopodeTable.CircleSpeed 가 이보다 크면 빨리 재생해 맞춘다), 촉수 길이 = {tentacleLength:0.#} m");
        }

        // ───────────────────────── 부위 히트박스 ─────────────────────────

        /// <summary>뼈 위치로 히트박스를 만들고 입(머리 돔 꼭대기) 위치를 돌려준다</summary>
        private static Transform BuildHitboxes(GameObject root, Dictionary<string, Transform> bones, int hitboxLayer, GameObject stumpVfx, out float tentacleLength)
        {
            tentacleLength = 0f;
            var body = Enumerable.Range(1, 5).Select(i => Bone(bones, $"BODY_{i:00}")).ToList();
            var jaws = bones.Where(p => p.Key.StartsWith("JAW_")).Select(p => p.Value).ToList();
            var tentacles = new List<(string row, List<Transform> chain)>();
            foreach (var row in new[] { "F", "M", "R" })
            foreach (var side in new[] { "L", "R" })
            {
                var chain = Enumerable.Range(1, 13).Select(i => Bone(bones, $"TENTACLE_{row}_{side}_{i:000}")).ToList();
                if (chain.All(b => b != null)) tentacles.Add((row, chain));
            }
            if (body.Any(b => b == null) || tentacles.Count == 0)
            {
                Debug.LogError("[TyranopodeBuilder] 몸통·촉수 뼈를 찾지 못했습니다. 히트박스 없이 만듭니다.");
                return null;
            }

            // 몸통 축 (아래 → 위)과 촉수 뿌리 둘레 반지름
            Vector3 bottom = body[0].position;
            Vector3 top = body[4].position;
            Vector3 axis = (top - bottom).normalized;
            float ringRadius = tentacles.Average(t => Vector3.ProjectOnPlane(t.chain[0].position - bottom, axis).magnitude);
            float bodyRadius = Mathf.Max(0.5f, ringRadius * 0.9f);

            // 몸통: 기본 레이어 → 총알도 맞고 플레이어도 막는다
            var bodyHit = AddCapsule(body[0], top, bodyRadius, TyranopodePartType.Body, null, null, "Hit_Body");
            bodyHit.layer = 0;

            // 머리: 돔 구 + 턱 꽃잎마다 작은 구. 머리가 부서지면 꽃잎이 오그라든다
            var dome = NewHitbox(body[4], "Hit_Head", top + axis * bodyRadius * 0.3f);
            dome.gameObject.AddComponent<SphereCollider>().radius = WorldToLocal(dome, bodyRadius * 0.9f);
            var headStump = AttachVfx(stumpVfx, body[4], dome.position, "Stump_Head");
            SetupPart(dome.gameObject, TyranopodePartType.Head, null, headStump, 1f, hitboxLayer);
            foreach (var jaw in jaws)
            {
                var petal = NewHitbox(jaw, $"Hit_{jaw.name}", jaw.position);
                petal.gameObject.AddComponent<SphereCollider>().radius = WorldToLocal(petal, Mathf.Max(0.3f, bodyRadius * 0.35f));
                SetupPart(petal.gameObject, TyranopodePartType.Head, jaw, headStump, 0.4f, hitboxLayer);
            }

            // 발광 주머니: 촉수 뿌리 사이마다 하나, 몸통 아래쪽(BODY_02) 높이에 둥글게
            Vector3 sacLevel = body[1].position;
            var rootDirs = tentacles
                .Select(t => Vector3.ProjectOnPlane(t.chain[0].position - bottom, axis).normalized)
                .OrderBy(d => Vector3.SignedAngle(Vector3.forward, d, axis))
                .ToList();
            for (int i = 0; i < rootDirs.Count; i++)
            {
                Vector3 between = (rootDirs[i] + rootDirs[(i + 1) % rootDirs.Count]).normalized;
                if (between.sqrMagnitude < 0.01f) continue;
                var sac = NewHitbox(body[1], $"Hit_Sac_{i}", sacLevel + between * ringRadius);
                sac.gameObject.AddComponent<SphereCollider>().radius = WorldToLocal(sac, ringRadius * 0.4f);
                var sacStump = AttachVfx(stumpVfx, body[1], sac.position, $"Stump_Sac_{i}");
                SetupPart(sac.gameObject, TyranopodePartType.Sac, null, sacStump, 1f, hitboxLayer);
            }

            // 촉수: 3마디 캡슐, 끝으로 갈수록 가늘게. 앞 촉수는 가운데(007)부터, 다리 촉수는 끝마디(010)만 잘린다
            int[] joints = { 0, 4, 8, 12 };
            foreach (var (row, chain) in tentacles)
            {
                var type = row == "F" ? TyranopodePartType.FrontTentacle : TyranopodePartType.LegTentacle;
                float length = 0f;
                for (int i = 1; i < chain.Count; i++) length += Vector3.Distance(chain[i - 1].position, chain[i].position);
                tentacleLength = Mathf.Max(tentacleLength, length);
                float baseRadius = Mathf.Clamp(length * 0.07f, 0.25f, 1.2f);
                Transform sever = type == TyranopodePartType.FrontTentacle ? chain[6] : chain[9];
                // 잘린 자리: 줄어드는 뼈 바로 위 마디에 붙여야 같이 작아지지 않는다
                var stump = AttachVfx(stumpVfx, sever.parent, sever.position, $"Stump_{chain[0].name}");
                for (int i = 0; i < 3; i++)
                {
                    var hit = AddCapsule(chain[joints[i]], chain[joints[i + 1]].position, baseRadius * (1f - i * 0.25f), type, sever, stump, $"Hit_{chain[joints[i]].name}");
                    hit.layer = hitboxLayer;
                }
            }

            // 입: 턱 꽃잎 가운데에서 조금 위 (포효·침·알·포자가 나온다)
            Vector3 mouthPosition = jaws.Count > 0 ? jaws.Aggregate(Vector3.zero, (sum, j) => sum + j.position) / jaws.Count : top;
            var mouth = new GameObject("Mouth").transform;
            mouth.SetParent(body[4], false);
            mouth.position = mouthPosition + axis * bodyRadius * 0.5f;
            return mouth;
        }

        private static Transform Bone(Dictionary<string, Transform> bones, string key)
            => bones.TryGetValue(key, out var bone) ? bone : null;

        private static Transform NewHitbox(Transform parent, string name, Vector3 position)
        {
            var hit = new GameObject(name).transform;
            hit.SetParent(parent, false);
            hit.position = position;
            return hit;
        }

        /// <summary>뼈 스케일을 상쇄해 월드 크기를 로컬 값으로</summary>
        private static float WorldToLocal(Transform t, float worldSize) => worldSize / Mathf.Max(0.0001f, t.lossyScale.x);

        private static GameObject AddCapsule(Transform from, Vector3 to, float radius, TyranopodePartType type, Transform sever, GameObject stump, string name)
        {
            Vector3 axis = to - from.position;
            var hit = NewHitbox(from, name, from.position + axis * 0.5f);
            if (axis.sqrMagnitude > 0.0001f) hit.rotation = Quaternion.FromToRotation(Vector3.up, axis);

            float scale = Mathf.Max(0.0001f, hit.lossyScale.y);
            var capsule = hit.gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius / scale;
            capsule.height = (axis.magnitude + radius * 2f) / scale;
            SetupPart(hit.gameObject, type, sever, stump, 0.05f, hit.gameObject.layer);
            return hit.gameObject;
        }

        private static void SetupPart(GameObject go, TyranopodePartType type, Transform sever, GameObject stump, float severScale, int layer)
        {
            go.layer = layer;
            var so = new SerializedObject(go.AddComponent<TyranopodePart>());
            so.FindProperty("type").enumValueIndex = (int)type;
            so.FindProperty("severBone").objectReferenceValue = sever;
            so.FindProperty("severScale").floatValue = severScale;
            so.FindProperty("stumpVfx").objectReferenceValue = stump;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── 이전 값 유지 ─────────────────────────

        /// <summary>이전 프리팹의 인스펙터 값 (키 = 계층 경로 | 컴포넌트 형식)</summary>
        private static Dictionary<string, string> Capture(GameObject prefab)
        {
            var saved = new Dictionary<string, string>();
            if (prefab == null) return saved;
            foreach (var component in prefab.GetComponentsInChildren<Component>(true))
            {
                if (component == null || PreservedTypes.Any(t => t.IsInstanceOfType(component)) == false) continue;
                saved[Key(prefab.transform, component)] = EditorJsonUtility.ToJson(component);
            }
            return saved;
        }

        private static void Restore(GameObject root, Dictionary<string, string> saved)
        {
            if (saved.Count == 0) return;
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (component == null || saved.TryGetValue(Key(root.transform, component), out var json) == false) continue;
                EditorJsonUtility.FromJsonOverwrite(json, component);
            }
        }

        private static string Key(Transform root, Component component)
        {
            var names = new List<string>();
            for (var t = component.transform; t != null && t != root; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names) + "|" + component.GetType().FullName;
        }

        // ───────────────────────── 침·알·포자 ─────────────────────────

        private static TyranopodeSpit BuildSpit(Material glow, TyranopodeVfxBuilder.Set vfx)
        {
            var go = Primitive("Tyranopode_Spit", glow, new Vector3(0.7f, 0.7f, 0.7f), keepCollider: false);
            var trail = (GameObject)PrefabUtility.InstantiatePrefab(vfx.SpitTrail, go.transform);
            trail.transform.localScale = Vector3.one / 0.7f;
            var spit = go.AddComponent<TyranopodeSpit>();
            var so = new SerializedObject(spit);
            so.FindProperty("burstVfx").objectReferenceValue = vfx.AcidSplash;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(go, "Tyranopode_Spit").GetComponent<TyranopodeSpit>();
        }

        private static TyranopodeEgg BuildEgg(Material material, TyranopodeVfxBuilder.Set vfx)
        {
            var go = Primitive("Tyranopode_Egg", material, new Vector3(1.2f, 1.6f, 1.2f), keepCollider: true);
            var so = new SerializedObject(go.AddComponent<TyranopodeEgg>());
            so.FindProperty("breakVfx").objectReferenceValue = vfx.EggBreak;
            so.FindProperty("hatchVfx").objectReferenceValue = vfx.EggHatch;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(go, "Tyranopode_Egg").GetComponent<TyranopodeEgg>();
        }

        private static SummonBeacon BuildBeacon(Material glow, TyranopodeVfxBuilder.Set vfx)
        {
            var go = Primitive("Tyranopode_Beacon", glow, Vector3.one * 2.5f, keepCollider: true);
            go.GetComponent<SphereCollider>().radius = 0.6f;

            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(go.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = Glow;
            light.range = 35f;
            light.intensity = 8f;

            var so = new SerializedObject(go.AddComponent<SummonBeacon>());
            so.FindProperty("glow").objectReferenceValue = light;
            so.FindProperty("pulseVfx").objectReferenceValue = vfx.BeaconPulse;
            so.FindProperty("breakVfx").objectReferenceValue = vfx.BeaconBreak;
            so.ApplyModifiedPropertiesWithoutUndo();
            return SavePrefab(go, "Tyranopode_Beacon").GetComponent<SummonBeacon>();
        }

        private static GameObject Primitive(string name, Material material, Vector3 scale, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            if (keepCollider == false) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject SavePrefab(GameObject go, string name)
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{OutFolder}/{name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        // ───────────────────────── 머티리얼·도구 ─────────────────────────

        private static Material CreateTransparentMaterial()
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetColor("_BaseColor", Color.white);
            return material;
        }

        private static Material CreateGlowMaterial(Color color, float emission)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return material;
        }

        private static Material SaveMaterial(Material material, string name)
        {
            string path = $"{OutFolder}/{name}.mat";
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


        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent) == false) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
