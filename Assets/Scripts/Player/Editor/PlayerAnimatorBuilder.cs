using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using RootMotion;
using RootMotion.FinalIK;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// Player_AC 를 설계(Assets/Scenes/MS/Docs/AnimationDesign.md)대로 처음부터 다시 만든다. 여러 번 눌러도 같은 결과.
    /// Tools/HybridShooter/Build Player Animator
    ///
    /// 현재 단계(3-5): 대기 · 8방향 이동 · 전력질주 · 점프 · 낙하 · 착지 · 상체 자세(들고 다니기 ↔ 조준) · AimIK · 재장전 · 꺼내기 · 사격 반동 · 구르기 · 피격 · 사망
    ///  1. 라이플 1회성 클립(재장전·구르기·피격·수류탄)의 루프 설정 끄기
    ///  2. 이동 클립 속도를 루트모션(_RM) 판으로 측정 (제자리 클립은 속도가 0 으로 나온다)
    ///  3. Player_AC 비우고 새로 생성: Base Layer = Locomotion(2D Freeform Directional, 대기 + 걷기 8 + 달리기 8) ↔ Sprint, 점프 시작 → 낙하 → 착지 3종,
    ///     Upper Pose 레이어(팔·손가락·머리 마스크) = 1D[Aim](들고 다니기, 조준) → 이동·점프 중에도 상체는 이 자세
    ///  4. PlayerAnimationProfile 에셋에 측정한 속도 저장
    ///  5. PlayerTest 씬: Player 에 드라이버·디버그 HUD·PlayerAimIK 연결, 몸 Animator 연결, 걷기 속도 3.0,
    ///     몸에 Final IK AimIK(척추 3개) + 오른손 아래 AimTransform(조준 자세에서 총구 방향으로 맞춤),
    ///     FullBodyBipedIK (사격 반동 PlayerGunRecoil·1인칭 손·발 IK),
    ///     왼팔 LimbIK(AimIK 뒤 왼손을 총에) + GrounderFBBIK(발 IK) + PlayerBodyIK
    ///  총 위치·손 위치는 Tools/HybridShooter/IK Pose Lab 창에서 맞춘다 (빌더는 건드리지 않는다)
    ///  수류탄 던지기만 추가: Tools/HybridShooter/Add Throw Action (컨트롤러를 다시 만들지 않고 Upper Action 레이어에 Throw 상태만 더한다)
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        private const string Title = "플레이어 애니메이터 빌드";
        private const string ControllerPath = "Assets/Scenes/MS/Animations/Player_AC.controller";
        private const string ProfilePath = "Assets/Scenes/MS/Animations/PlayerAnimationProfile.asset";
        private const string ArmsMaskPath = "Assets/Scenes/MS/Animations/Player_ArmsMask.mask";
        private const string HitMaskPath = "Assets/Scenes/MS/Animations/Player_HitMask.mask";
        private const string AimTransformName = "AimTransform";
        private const string ScenePath = "Assets/Scenes/MS/Scenes/PlayerTest.unity";
        private const string ClipFolder = "Assets/ExternalAssets/PROTOFACTOR/Ultimate Animation Collection/Animations/Assault Rifle Animset";
        private const string Suffix = "AssaultRifle";
        private const float TargetWalkSpeed = 3f;
        private const float SprintTransition = 0.2f;

        /// <summary>8방향: (클립 이름 속 방향, 몸 기준 단위 방향 x = 오른쪽, y = 앞)</summary>
        private static readonly (string name, Vector2 dir)[] Directions =
        {
            ("Forward", new Vector2(0f, 1f)),
            ("ForwardRight", new Vector2(0.7071f, 0.7071f)),
            ("Right", new Vector2(1f, 0f)),
            ("BackwardsRight", new Vector2(0.7071f, -0.7071f)),
            ("Backwards", new Vector2(0f, -1f)),
            ("BackwardsLeft", new Vector2(-0.7071f, -0.7071f)),
            ("Left", new Vector2(-1f, 0f)),
            ("ForwardLeft", new Vector2(-0.7071f, 0.7071f)),
        };

        /// <summary>1회성인데 루프가 켜진 클립 (조사 리포트에서 확인)</summary>
        private static readonly string[] OneShotClips =
        {
            "Reload" + Suffix,
            "DodgeForward" + Suffix, "DodgeBackwards" + Suffix, "DodgeLeft" + Suffix, "DodgeRight" + Suffix,
            "GetHitFrontLight" + Suffix, "GetHitBackLight" + Suffix, "GetHitLeftLight" + Suffix, "GetHitRightLight" + Suffix,
            "GetHitFrontHeavy" + Suffix, "GetHitLeftHeavy" + Suffix, "GetHitRightHeavy" + Suffix,
            "ThrowGrenade1Standing" + Suffix, "ThrowGrenade2Standing" + Suffix,
            "ThrowGrenade1Crouching" + Suffix, "ThrowGrenade2Crouching" + Suffix,
        };

        private static Dictionary<string, AnimationClip> clips;
        private static readonly List<string> log = new List<string>();
        private static readonly List<string> missing = new List<string>();

        [MenuItem("Tools/HybridShooter/Build Player Animator")]
        private static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog(Title, "플레이 모드를 끄고 실행하세요.", "확인");
                return;
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                EditorUtility.DisplayDialog(Title, $"애니메이터 컨트롤러가 없습니다:\n{ControllerPath}", "확인");
                return;
            }
            if (EditorUtility.DisplayDialog(Title, "Player_AC 의 내용을 모두 지우고 설계대로 다시 만듭니다.\n(이전 버전은 Player_AC_Old 에 있습니다)", "만들기", "취소") == false)
                return;

            log.Clear();
            missing.Clear();

            LoadClips();
            FixOneShotLoops();
            LoadClips(); // 다시 임포트한 클립을 새로 읽는다

            PlayerAnimationProfile profile = LoadOrCreateProfile();
            PlayerAnimationProfile.WeaponSet rifle = profile.DefaultSet ?? new PlayerAnimationProfile.WeaponSet();
            if (profile.sets.Count == 0) profile.sets.Add(rifle);

            ClearController(controller);
            AddParameters(controller);
            BuildBaseLayer(controller, rifle);
            BuildUpperPoseLayer(controller);
            BuildUpperActionLayer(controller, rifle);
            BuildHitReactionLayer(controller);

            rifle.name = "Rifle";
            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) SetupScene(controller, profile);
            else log.Add("씬 저장을 취소해서 씬 연결은 건너뜀");

            if (missing.Count > 0) Debug.LogWarning("[PlayerAnimatorBuilder] 찾지 못한 클립:\n" + string.Join("\n", missing.Distinct()));
            Debug.Log("[PlayerAnimatorBuilder] 완료\n - " + string.Join("\n - ", log), controller);
            EditorGUIUtility.PingObject(controller);
        }

        #region 클립
        private static void LoadClips()
        {
            clips = new Dictionary<string, AnimationClip>();
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string key = Key(path);
                if (clips.ContainsKey(key)) continue;
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name.StartsWith("__preview__") == false);
                if (clip != null) clips[key] = clip;
            }
        }

        /// <summary>"…/Humanoid@RunForwardAssaultRifle.FBX" → "RunForwardAssaultRifle"</summary>
        private static string Key(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path);
            int at = file.IndexOf('@');
            return at >= 0 ? file.Substring(at + 1) : file;
        }

        private static AnimationClip Clip(string key)
        {
            if (clips.TryGetValue(key, out AnimationClip clip)) return clip;
            missing.Add(key);
            return null;
        }

        private static void FixOneShotLoops()
        {
            int count = 0;
            foreach (string key in OneShotClips)
            {
                if (clips.TryGetValue(key, out AnimationClip clip) == false) { missing.Add(key); continue; }
                string path = AssetDatabase.GetAssetPath(clip);
                if (AssetImporter.GetAtPath(path) is ModelImporter importer == false) continue;

                ModelImporterClipAnimation[] takes = importer.clipAnimations;
                if (takes == null || takes.Length == 0) takes = importer.defaultClipAnimations;
                bool changed = false;
                foreach (ModelImporterClipAnimation take in takes)
                {
                    if (take.loopTime == false) continue;
                    take.loopTime = false;
                    changed = true;
                }
                if (changed == false) continue;
                importer.clipAnimations = takes;
                importer.SaveAndReimport();
                count++;
            }
            log.Add($"1회성 클립 루프 끔: {count}개");
        }

        /// <summary>
        /// 이동 클립 속도(m/s). 제자리 클립은 0 이라 같은 동작의 루트모션(_RM) 판으로 잰다.
        /// 블렌드 좌표는 설계한 방향 × 잰 크기 (루트모션 방향이 조금 틀어져 있어도 8방향이 고르게 놓이도록)
        /// </summary>
        private static float MeasureSpeed(string key, float fallback)
        {
            AnimationClip clip = clips.TryGetValue(key + "_RM", out AnimationClip rm) ? rm : Clip(key);
            if (clip == null) return fallback;
            Vector3 v = clip.averageSpeed;
            float speed = new Vector2(v.x, v.z).magnitude;
            if (speed > 0.05f) return speed;
            log.Add($"속도 측정 실패 → 기본값 {fallback:0.00}: {key}");
            return fallback;
        }
        #endregion

        #region 컨트롤러
        /// <summary>레이어·파라미터와 안에 든 상태·전이·블렌드트리를 모두 지운다 (에셋 자체와 GUID 는 유지 → 씬 연결 유지)</summary>
        private static void ClearController(AnimatorController controller)
        {
            controller.layers = new AnimatorControllerLayer[0];
            controller.parameters = new AnimatorControllerParameter[0];
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (sub != null && sub != controller) Object.DestroyImmediate(sub, true);
        }

        private static void AddParameters(AnimatorController controller)
        {
            controller.AddParameter(PlayerAnimNames.Params.MoveX, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimNames.Params.MoveY, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = PlayerAnimNames.Params.MoveSpeedScale,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            controller.AddParameter(PlayerAnimNames.Params.Moving, AnimatorControllerParameterType.Bool);
            controller.AddParameter(PlayerAnimNames.Params.Sprint, AnimatorControllerParameterType.Bool);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = PlayerAnimNames.Params.Grounded,
                type = AnimatorControllerParameterType.Bool,
                defaultBool = true,
            });
            controller.AddParameter(PlayerAnimNames.Params.Falling, AnimatorControllerParameterType.Bool);
            controller.AddParameter(PlayerAnimNames.Params.Jump, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PlayerAnimNames.Params.LandType, AnimatorControllerParameterType.Int);
            controller.AddParameter(PlayerAnimNames.Params.Aim, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimNames.Params.Reload, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PlayerAnimNames.Params.Draw, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PlayerAnimNames.Params.Throw, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PlayerAnimNames.Params.ActionEnd, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = PlayerAnimNames.Params.ActionSpeed,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            controller.AddParameter(PlayerAnimNames.Params.Dodge, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PlayerAnimNames.Params.DodgeX, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimNames.Params.DodgeY, AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = PlayerAnimNames.Params.DodgeSpeed,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            controller.AddParameter(PlayerAnimNames.Params.Rolling, AnimatorControllerParameterType.Bool);
            controller.AddParameter(PlayerAnimNames.Params.Hit, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PlayerAnimNames.Params.HitX, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimNames.Params.HitY, AnimatorControllerParameterType.Float);
            controller.AddParameter(PlayerAnimNames.Params.Die, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(PlayerAnimNames.Params.Dead, AnimatorControllerParameterType.Bool);
            controller.AddParameter(PlayerAnimNames.Params.DeathDir, AnimatorControllerParameterType.Int);
        }

        private static void BuildBaseLayer(AnimatorController controller, PlayerAnimationProfile.WeaponSet speeds)
        {
            controller.AddLayer(PlayerAnimNames.Layers.Base);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            // 대기 + 걷기 8 + 달리기 8. 걷기·달리기 크기는 방향마다 따로 잰다
            var tree = new BlendTree
            {
                name = PlayerAnimNames.States.Locomotion,
                blendType = BlendTreeType.FreeformDirectional2D,
                blendParameter = PlayerAnimNames.Params.MoveX,
                blendParameterY = PlayerAnimNames.Params.MoveY,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);

            AnimationClip idle = Clip("IdleHold" + Suffix);
            if (idle != null) tree.AddChild(idle, Vector2.zero);

            var walkSpeeds = new List<float>();
            var runSpeeds = new List<float>();
            foreach (var (dir, unit) in Directions)
            {
                string walkKey = $"Walk{dir}Aiming{Suffix}";
                string runKey = $"Run{dir}Aiming{Suffix}";
                float walk = MeasureSpeed(walkKey, 0.77f);
                float run = MeasureSpeed(runKey, 2.5f);
                AnimationClip walkClip = Clip(walkKey);
                AnimationClip runClip = Clip(runKey);
                if (walkClip != null) tree.AddChild(walkClip, unit * walk);
                if (runClip != null) tree.AddChild(runClip, unit * run);
                walkSpeeds.Add(walk);
                runSpeeds.Add(run);
            }

            AnimatorState locomotion = machine.AddState(PlayerAnimNames.States.Locomotion, new Vector3(300f, 100f, 0f));
            locomotion.motion = tree;
            locomotion.speedParameter = PlayerAnimNames.Params.MoveSpeedScale;
            locomotion.speedParameterActive = true;
            machine.defaultState = locomotion;

            AnimatorState sprint = machine.AddState(PlayerAnimNames.States.Sprint, new Vector3(300f, 220f, 0f));
            sprint.motion = Clip("SprintForward" + Suffix);

            AnimatorStateTransition toSprint = locomotion.AddTransition(sprint);
            toSprint.hasExitTime = false;
            toSprint.duration = SprintTransition;
            toSprint.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Sprint);
            // 달리기 ↔ 전력질주로 섞이는 0.2초 사이에 점프하면, 그 전환을 끊고 바로 점프로 (출발 상태의 전이가 끼어들 수 있게)
            toSprint.interruptionSource = TransitionInterruptionSource.Source;

            AnimatorStateTransition toLocomotion = sprint.AddTransition(locomotion);
            toLocomotion.hasExitTime = false;
            toLocomotion.duration = SprintTransition;
            toLocomotion.AddCondition(AnimatorConditionMode.IfNot, 0f, PlayerAnimNames.Params.Sprint);
            toLocomotion.interruptionSource = TransitionInterruptionSource.Source;

            // 정면 값을 프로필에 (드라이버는 정면 달리기보다 빠르면 재생 속도를 올린다)
            speeds.walkSpeed = walkSpeeds[0];
            speeds.runSpeed = runSpeeds[0];
            speeds.sprintSpeed = MeasureSpeed("SprintForward" + Suffix, 4.8f);
            log.Add($"이동 클립 속도 (m/s): 걷기 {string.Join(" / ", walkSpeeds.Select(s => s.ToString("0.00")))}");
            log.Add($"                   달리기 {string.Join(" / ", runSpeeds.Select(s => s.ToString("0.00")))}  (정면부터 시계방향)");
            log.Add($"전력질주 {speeds.sprintSpeed:0.00} m/s, Base Layer: Locomotion(대기+걷기 8+달리기 8) ↔ Sprint, 전환 {SprintTransition}초");

            BuildAirborne(machine, locomotion, sprint);
            BuildDodgeAndDeath(controller, machine, locomotion, speeds);
        }

        /// <summary>
        /// 점프 시작 → 낙하(루프) → 착지(가볍게·중간·무겁게) → 이동.
        ///  - 이동·전력질주 → 점프 시작 [Jump], 이동·전력질주 → 낙하 [Falling] (턱에서 떨어질 때)
        ///  - 점프 시작 → 낙하: [Falling] 일 때만 (제자리·달리기 점프처럼 같은 높이로 내려오면 낙하 동작 없이 바로 착지)
        ///  - 점프 시작·낙하 → 착지 [Grounded && LandType]
        ///  - 착지 → 이동: 움직이면 바로 [Moving] (발이 미끄러지지 않게), 아니면 동작이 끝나갈 때. 착지 중 다시 [Jump] 가능
        /// </summary>
        private static void BuildAirborne(AnimatorStateMachine machine, AnimatorState locomotion, AnimatorState sprint)
        {
            AnimatorState jump = machine.AddState(PlayerAnimNames.States.JumpStart, new Vector3(600f, 40f, 0f));
            jump.motion = Clip("JumpToApex" + Suffix);
            AnimatorState fall = machine.AddState(PlayerAnimNames.States.Fall, new Vector3(600f, 160f, 0f));
            fall.motion = Clip("Falling" + Suffix);

            var lands = new[]
            {
                (state: machine.AddState(PlayerAnimNames.States.LandLight, new Vector3(900f, 40f, 0f)), clip: "LandingLight", exitTime: 0.8f),
                (state: machine.AddState(PlayerAnimNames.States.LandMedium, new Vector3(900f, 160f, 0f)), clip: "LandingMedium", exitTime: 0.75f),
                (state: machine.AddState(PlayerAnimNames.States.LandHeavy, new Vector3(900f, 280f, 0f)), clip: "LandingHeavy", exitTime: 0.8f),
            };

            foreach (AnimatorState ground in new[] { locomotion, sprint })
            {
                Transition(ground, jump, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Jump);
                Transition(ground, fall, 0.2f).AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Falling);
            }

            // 점프한 높이만큼 내려오는 동안은 점프 동작(마지막 자세 유지) 그대로 착지한다.
            // 절벽 아래로 뛰어내리는 등 '정말 떨어질 때'(Falling: 빠른 낙하 속도 + 먼 땅)만 낙하 동작으로
            Transition(jump, fall, 0.2f).AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Falling);

            for (int i = 0; i < lands.Length; i++)
            {
                var (land, clip, exitTime) = lands[i];
                land.motion = Clip(clip + Suffix);
                foreach (AnimatorState air in new[] { jump, fall })
                {
                    AnimatorStateTransition toLand = Transition(air, land, 0.1f);
                    toLand.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Grounded);
                    toLand.AddCondition(AnimatorConditionMode.Equals, i, PlayerAnimNames.Params.LandType);
                }

                Transition(land, jump, 0.1f).AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Jump);
                Transition(land, locomotion, 0.2f).AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Moving);
                AnimatorStateTransition done = Transition(land, locomotion, 0.25f);
                done.hasExitTime = true;
                done.exitTime = exitTime;
            }
            // 전이는 목록 순서대로 검사한다. 점프하는 프레임에는 전력질주가 꺼지면서(공중) Sprint → Locomotion 조건도 같이 맞는데,
            // 그게 먼저면 달리기 동작을 0.2초 지나서야 점프로 넘어간다 → 점프·낙하 전이를 맨 앞으로
            foreach (AnimatorState ground in new[] { locomotion, sprint })
            {
                ground.transitions = ground.transitions
                    .OrderBy(t => t.destinationState == jump ? 0 : t.destinationState == fall ? 1 : 2)
                    .ToArray();
            }
            log.Add("점프: Jump Start → Fall → Land Light/Medium/Heavy → Locomotion (움직이면 착지 동작을 바로 끊음, 점프 전이 우선)");
        }

        /// <summary>
        /// 구르기·사망 (Base Layer, Any State 에서).
        ///  - Dodge: 2D(DodgeX, DodgeY) 4방향, 재생 속도 DodgeSpeed. [Dodge && !Dead] 로 들어가고,
        ///    물리 구르기가 끝났고(!Rolling) 움직이면 바로 이동으로, 아니면 끝나갈 때 이동으로. 높은 데서 굴러 떨어지면 낙하로
        ///  - Death Front/Back/Left/Right: [Die && DeathDir] 로 들어가 마지막 자세로 멈춘다 (나가는 전이 없음)
        /// </summary>
        private static void BuildDodgeAndDeath(AnimatorController controller, AnimatorStateMachine machine, AnimatorState locomotion, PlayerAnimationProfile.WeaponSet set)
        {
            AnimatorState fall = machine.states.Select(s => s.state).First(s => s.name == PlayerAnimNames.States.Fall);

            var tree = new BlendTree
            {
                name = PlayerAnimNames.States.Dodge,
                blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = PlayerAnimNames.Params.DodgeX,
                blendParameterY = PlayerAnimNames.Params.DodgeY,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            var lengths = new List<float>();
            foreach (var (dir, pos) in new[] { ("Forward", Vector2.up), ("Backwards", Vector2.down), ("Left", Vector2.left), ("Right", Vector2.right) })
            {
                AnimationClip clip = Clip($"Dodge{dir}{Suffix}");
                if (clip == null) continue;
                tree.AddChild(clip, pos);
                lengths.Add(clip.length);
            }
            if (lengths.Count > 0) set.dodgeClipLength = lengths.Average();

            AnimatorState dodge = machine.AddState(PlayerAnimNames.States.Dodge, new Vector3(300f, 400f, 0f));
            dodge.motion = tree;
            dodge.speedParameter = PlayerAnimNames.Params.DodgeSpeed;
            dodge.speedParameterActive = true;

            AnimatorStateTransition enter = machine.AddAnyStateTransition(dodge);
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
            enter.duration = 0.1f;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Dodge);
            enter.AddCondition(AnimatorConditionMode.IfNot, 0f, PlayerAnimNames.Params.Dead);

            AnimatorStateTransition toFall = Transition(dodge, fall, 0.2f);
            toFall.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Falling);
            AnimatorStateTransition moveOn = Transition(dodge, locomotion, 0.2f);
            moveOn.AddCondition(AnimatorConditionMode.IfNot, 0f, PlayerAnimNames.Params.Rolling);
            moveOn.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Moving);
            AnimatorStateTransition done = Transition(dodge, locomotion, 0.25f);
            done.hasExitTime = true;
            done.exitTime = 0.85f;

            var deaths = new[]
            {
                (PlayerAnimNames.States.DeathFront, "DeathFront", 0),
                (PlayerAnimNames.States.DeathBack, "DeathBack", 1),
                (PlayerAnimNames.States.DeathLeft, "DeathLeft", 2),
                (PlayerAnimNames.States.DeathRight, "DeathRight", 3),
            };
            foreach (var (name, clip, dir) in deaths)
            {
                AnimatorState death = machine.AddState(name, new Vector3(900f, 400f + 60f * dir, 0f));
                death.motion = Clip(clip + Suffix);
                AnimatorStateTransition die = machine.AddAnyStateTransition(death);
                die.hasExitTime = false;
                die.hasFixedDuration = true;
                die.duration = 0.15f;
                die.canTransitionToSelf = false;
                die.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Die);
                die.AddCondition(AnimatorConditionMode.Equals, dir, PlayerAnimNames.Params.DeathDir);
            }
            log.Add($"구르기: 4방향({set.dodgeClipLength:0.00}s 평균), 사망: 4방향 (마지막 자세 유지)");
        }

        /// <summary>
        /// Hit Reaction 레이어 (Additive, 몸통·팔·머리 마스크): 맞은 방향 2D(HitX, HitY) 가벼운 피격 4방향.
        /// 클립 첫 프레임 대비 변화량(움찔함)만 더하므로 걷기·조준·점프 중에도 멈추지 않는다
        /// </summary>
        private static void BuildHitReactionLayer(AnimatorController controller)
        {
            AvatarMask mask = CreateMask(HitMaskPath, HitParts);
            controller.AddLayer(PlayerAnimNames.Layers.HitReaction);
            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layers.Length - 1];
            layer.avatarMask = mask;
            layer.blendingMode = AnimatorLayerBlendingMode.Additive;
            layer.defaultWeight = 1f;
            controller.layers = layers;
            AnimatorStateMachine machine = layer.stateMachine;

            AnimatorState empty = machine.AddState(PlayerAnimNames.States.Empty, new Vector3(300f, 100f, 0f));
            machine.defaultState = empty;

            var tree = new BlendTree
            {
                name = PlayerAnimNames.States.Hit,
                blendType = BlendTreeType.SimpleDirectional2D,
                blendParameter = PlayerAnimNames.Params.HitX,
                blendParameterY = PlayerAnimNames.Params.HitY,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            foreach (var (dir, pos) in new[] { ("Front", Vector2.up), ("Back", Vector2.down), ("Left", Vector2.left), ("Right", Vector2.right) })
            {
                AnimationClip clip = Clip($"GetHit{dir}Light{Suffix}");
                if (clip != null) tree.AddChild(clip, pos);
            }

            AnimatorState hit = machine.AddState(PlayerAnimNames.States.Hit, new Vector3(600f, 100f, 0f));
            hit.motion = tree;

            AnimatorStateTransition enter = machine.AddAnyStateTransition(hit);
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
            enter.duration = 0.05f;
            enter.canTransitionToSelf = true; // 연달아 맞으면 처음부터 다시 움찔
            enter.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.Hit);
            enter.AddCondition(AnimatorConditionMode.IfNot, 0f, PlayerAnimNames.Params.Dead);
            AnimatorStateTransition done = Transition(hit, empty, 0.15f);
            done.hasExitTime = true;
            done.exitTime = 0.9f;
            log.Add("Hit Reaction 레이어: Additive, 몸통·팔·머리, 가벼운 피격 4방향");
        }

        /// <summary>조건 없이 즉시 넘어가는(exit time 없음) 고정 시간 전이. 조건·exit time 은 호출하는 쪽에서 붙인다</summary>
        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            AnimatorStateTransition t = from.AddTransition(to);
            t.hasExitTime = false;
            t.hasFixedDuration = true;
            t.duration = duration;
            return t;
        }

        /// <summary>
        /// Upper Pose 레이어: 팔·손가락·머리만 덮는다 (Body 는 골반까지 묶여 있어 넣으면 하체가 끌려간다 → 이전 어기적거림의 원인).
        /// 상태 하나 = 1D[Aim]( IdleHold 들고 다니기, IdleAim 조준 ). 점프·낙하·착지 중에도 팔과 총은 이 자세 (점프 동작은 다리·몸통만)
        /// </summary>
        private static void BuildUpperPoseLayer(AnimatorController controller)
        {
            AvatarMask mask = CreateMask(ArmsMaskPath, ArmParts);
            controller.AddLayer(PlayerAnimNames.Layers.UpperPose);
            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layers.Length - 1];
            layer.avatarMask = mask;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 1f;
            controller.layers = layers;

            var tree = new BlendTree
            {
                name = PlayerAnimNames.States.Pose,
                blendType = BlendTreeType.Simple1D,
                blendParameter = PlayerAnimNames.Params.Aim,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            AnimationClip hold = Clip("IdleHold" + Suffix);
            AnimationClip aim = Clip("IdleAim" + Suffix);
            if (hold != null) tree.AddChild(hold, 0f);
            if (aim != null) tree.AddChild(aim, 1f);

            AnimatorStateMachine machine = layer.stateMachine;
            AnimatorState pose = machine.AddState(PlayerAnimNames.States.Pose, new Vector3(300f, 100f, 0f));
            pose.motion = tree;
            machine.defaultState = pose;
            log.Add("Upper Pose 레이어: 팔·손가락·머리 마스크, 1D[Aim](IdleHold ↔ IdleAim)");
        }

        /// <summary>
        /// Upper Action 레이어(팔·손가락·머리, Upper Pose 위): Empty(기본, 동작 없음) ↔ 재장전 · 꺼내기 · 수류탄 던지기.
        ///  - Any State → Reload [Reload] / Draw [Draw] / Throw [Throw], 재생 속도 = ActionSpeed (드라이버가 실제 시간에 맞춤)
        ///  - 끝나갈 때 Empty 로. [ActionEnd] 면 바로 Empty 로 (장전 취소·완료, 조준·사격으로 꺼내기 끊기)
        /// 클립 길이·던지는 손·놓는 순간은 프로필에 저장 (드라이버가 재생 속도·수류탄 놓기에 쓴다)
        /// </summary>
        private static void BuildUpperActionLayer(AnimatorController controller, PlayerAnimationProfile.WeaponSet set)
        {
            controller.AddLayer(PlayerAnimNames.Layers.UpperAction);
            AnimatorControllerLayer[] layers = controller.layers;
            AnimatorControllerLayer layer = layers[layers.Length - 1];
            layer.avatarMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(ArmsMaskPath);
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 1f;
            controller.layers = layers;
            AnimatorStateMachine machine = layer.stateMachine;

            AnimatorState empty = machine.AddState(PlayerAnimNames.States.Empty, new Vector3(300f, 100f, 0f));
            machine.defaultState = empty;

            AnimationClip reloadClip = Clip("Reload" + Suffix);
            AnimationClip drawClip = Clip("Draw" + Suffix);
            if (reloadClip != null) set.reloadClipLength = reloadClip.length;
            if (drawClip != null) set.drawClipLength = drawClip.length;

            AddActionState(machine, empty, PlayerAnimNames.States.Reload, reloadClip, PlayerAnimNames.Params.Reload, 0.92f, 40f);
            AddActionState(machine, empty, PlayerAnimNames.States.Draw, drawClip, PlayerAnimNames.Params.Draw, 0.85f, 160f);
            AddThrowState(machine, empty, set);
            log.Add($"Upper Action 레이어: 재장전({set.reloadClipLength:0.00}s)·꺼내기({set.drawClipLength:0.00}s)·던지기({set.throwClipLength:0.00}s), 재생 속도 = ActionSpeed");
        }

        /// <summary>Any State → 동작 [trigger], exitTime 에 Empty 로, [ActionEnd] 면 바로 Empty 로</summary>
        private static void AddActionState(AnimatorStateMachine machine, AnimatorState empty, string name, AnimationClip clip, string trigger, float exitTime, float y)
        {
            AnimatorState state = machine.AddState(name, new Vector3(600f, y, 0f));
            state.motion = clip;
            state.speedParameter = PlayerAnimNames.Params.ActionSpeed;
            state.speedParameterActive = true;

            AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
            enter.hasExitTime = false;
            enter.hasFixedDuration = true;
            enter.duration = 0.15f;
            enter.canTransitionToSelf = false;
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

            AnimatorStateTransition cancel = Transition(state, empty, 0.2f);
            cancel.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimNames.Params.ActionEnd);
            AnimatorStateTransition done = Transition(state, empty, 0.2f);
            done.hasExitTime = true;
            done.exitTime = exitTime;
        }

        /// <summary>수류탄 던지기 상태 + 클립 분석 (던지는 손, 놓는 순간)</summary>
        private static void AddThrowState(AnimatorStateMachine machine, AnimatorState empty, PlayerAnimationProfile.WeaponSet set)
        {
            AnimationClip clip = Clip("ThrowGrenade1Standing" + Suffix);
            if (clip == null) { set.throwClipLength = 0f; return; }
            AddActionState(machine, empty, PlayerAnimNames.States.Throw, clip, PlayerAnimNames.Params.Throw, 0.9f, 280f);
            set.throwClipLength = clip.length;
            AnalyzeThrow(clip, set);
        }

        /// <summary>
        /// 씬의 몸에 던지기 클립을 프레임마다 입혀 두 손의 속도를 잰다 → 더 빨리 움직이는 손 = 던지는 손,
        /// 그 손이 가장 빠른 순간 = 수류탄이 손을 떠나는 순간 (0~1)
        /// </summary>
        private static void AnalyzeThrow(AnimationClip clip, PlayerAnimationProfile.WeaponSet set)
        {
            var player = Object.FindFirstObjectByType<PlayerController>();
            Animator animator = player != null ? new SerializedObject(player).FindProperty("bodyAnimator").objectReferenceValue as Animator : null;
            if (animator == null && player != null) animator = player.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.isHuman == false) { log.Add("던지기 분석: 씬에서 몸 Animator 를 찾지 못해 기본값 (왼손, 0.5)"); return; }
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform right = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform root = animator.transform;

            const int Samples = 60;
            var leftPos = new Vector3[Samples + 1];
            var rightPos = new Vector3[Samples + 1];
            AnimationMode.StartAnimationMode();
            for (int i = 0; i <= Samples; i++)
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, clip, clip.length * i / Samples);
                AnimationMode.EndSampling();
                leftPos[i] = root.InverseTransformPoint(left.position);
                rightPos[i] = root.InverseTransformPoint(right.position);
            }
            AnimationMode.StopAnimationMode();

            float leftMax = 0f, rightMax = 0f;
            int leftAt = 0, rightAt = 0;
            for (int i = 1; i <= Samples; i++)
            {
                float l = (leftPos[i] - leftPos[i - 1]).magnitude;
                float r = (rightPos[i] - rightPos[i - 1]).magnitude;
                if (l > leftMax) { leftMax = l; leftAt = i; }
                if (r > rightMax) { rightMax = r; rightAt = i; }
            }
            set.throwWithLeftHand = leftMax >= rightMax;
            set.throwRelease = (float)(set.throwWithLeftHand ? leftAt : rightAt) / Samples;
            log.Add($"던지기 분석: {(set.throwWithLeftHand ? "왼손" : "오른손")}, 놓는 순간 {set.throwRelease:0.00} (클립 {clip.length:0.00}s)");
        }

        /// <summary>컨트롤러를 다시 만들지 않고 Upper Action 레이어에 Throw 상태·파라미터만 더한다 (이미 있으면 클립 분석만 다시)</summary>
        [MenuItem("Tools/HybridShooter/Add Throw Action")]
        private static void AddThrowAction()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog(Title, "플레이 모드를 끄고 실행하세요.", "확인"); return; }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            PlayerAnimationProfile profile = AssetDatabase.LoadAssetAtPath<PlayerAnimationProfile>(ProfilePath);
            if (controller == null || profile == null || profile.DefaultSet == null)
            {
                EditorUtility.DisplayDialog(Title, "Player_AC 또는 프로필이 없습니다. Build Player Animator 를 먼저 실행하세요.", "확인");
                return;
            }

            log.Clear();
            missing.Clear();
            LoadClips();
            FixOneShotLoops();
            LoadClips();

            AnimatorControllerLayer layer = controller.layers.FirstOrDefault(l => l.name == PlayerAnimNames.Layers.UpperAction);
            AnimatorState empty = layer?.stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == PlayerAnimNames.States.Empty);
            if (layer == null || empty == null)
            {
                EditorUtility.DisplayDialog(Title, "Upper Action 레이어가 없습니다. Build Player Animator 를 먼저 실행하세요.", "확인");
                return;
            }

            Undo.RecordObject(profile, Title);
            if (controller.parameters.All(p => p.name != PlayerAnimNames.Params.Throw))
                controller.AddParameter(PlayerAnimNames.Params.Throw, AnimatorControllerParameterType.Trigger);
            AnimatorStateMachine machine = layer.stateMachine;
            if (machine.states.Any(s => s.state.name == PlayerAnimNames.States.Throw))
            {
                AnimationClip clip = Clip("ThrowGrenade1Standing" + Suffix);
                if (clip != null) { profile.DefaultSet.throwClipLength = clip.length; AnalyzeThrow(clip, profile.DefaultSet); }
            }
            else AddThrowState(machine, empty, profile.DefaultSet);

            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            if (missing.Count > 0) Debug.LogWarning("[PlayerAnimatorBuilder] 찾지 못한 클립:\n" + string.Join("\n", missing.Distinct()));
            Debug.Log("[PlayerAnimatorBuilder] 던지기 추가 완료\n - " + string.Join("\n - ", log), controller);
        }

        /// <summary>상체 자세·동작용: 머리·팔·손가락·손 IK 만 (Body 는 골반까지 묶여 있어 뺀다)</summary>
        private static readonly AvatarMaskBodyPart[] ArmParts =
        {
            AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
            AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK,
        };

        /// <summary>피격 반응(additive)용: 몸통까지. 변화량만 더하므로 골반이 포함돼도 하체를 덮어쓰지 않는다</summary>
        private static readonly AvatarMaskBodyPart[] HitParts =
        {
            AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
            AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
        };

        /// <summary>휴머노이드 마스크: parts 만 켜고 나머지(Root·다리 등)는 끈다</summary>
        private static AvatarMask CreateMask(string path, AvatarMaskBodyPart[] parts)
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, path);
            }
            var on = new HashSet<AvatarMaskBodyPart>(parts);
            for (var part = (AvatarMaskBodyPart)0; part < AvatarMaskBodyPart.LastBodyPart; part++)
                mask.SetHumanoidBodyPartActive(part, on.Contains(part));
            mask.transformCount = 0;
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static PlayerAnimationProfile LoadOrCreateProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<PlayerAnimationProfile>(ProfilePath);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<PlayerAnimationProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            log.Add($"프로필 생성: {ProfilePath}");
            return profile;
        }
        #endregion

        #region 씬
        private static void SetupScene(AnimatorController controller, PlayerAnimationProfile profile)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var player = Object.FindFirstObjectByType<PlayerController>();
            if (player == null) { log.Add("씬: PlayerController 를 찾지 못함"); return; }

            var playerSo = new SerializedObject(player);
            var animator = playerSo.FindProperty("bodyAnimator").objectReferenceValue as Animator;
            if (animator == null) animator = player.GetComponentInChildren<Animator>(true);
            if (animator == null) { log.Add("씬: 몸 Animator 를 찾지 못함"); return; }

            playerSo.FindProperty("walkSpeed").floatValue = TargetWalkSpeed;
            playerSo.ApplyModifiedProperties();

            Undo.RecordObject(animator, Title);
            animator.enabled = true;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

            var driver = player.GetComponent<PlayerAnimationDriver>();
            if (driver == null) driver = Undo.AddComponent<PlayerAnimationDriver>(player.gameObject);
            var driverSo = new SerializedObject(driver);
            driverSo.FindProperty("player").objectReferenceValue = player;
            driverSo.FindProperty("animator").objectReferenceValue = animator;
            driverSo.FindProperty("profile").objectReferenceValue = profile;
            driverSo.ApplyModifiedProperties();
            if (player.GetComponent<PlayerAnimationDebugHUD>() == null) Undo.AddComponent<PlayerAnimationDebugHUD>(player.gameObject);

            AimIK aimIK = SetupAimIK(animator);
            if (aimIK != null)
            {
                var aim = player.GetComponent<PlayerAimIK>();
                if (aim == null) aim = Undo.AddComponent<PlayerAimIK>(player.gameObject);
                var aimSo = new SerializedObject(aim);
                aimSo.FindProperty("player").objectReferenceValue = player;
                aimSo.FindProperty("driver").objectReferenceValue = driver;
                aimSo.FindProperty("aimIK").objectReferenceValue = aimIK;
                aimSo.ApplyModifiedProperties();
                SetupFullBodyIK(animator);

                SetupBodyIK(player, driver, animator, aimIK);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            log.Add($"씬: {animator.name} Animator 연결, Player 에 드라이버·디버그 HUD·PlayerAimIK, 걷기 속도 {TargetWalkSpeed}");
        }

        /// <summary>
        /// 몸에 Final IK AimIK: 척추 3개(spine_01~03)를 돌려 AimTransform 의 앞쪽(총구 방향)이 조준점을 향하게 한다.
        /// AimTransform 은 오른손 아래 빈 오브젝트. 조준 자세(IdleAim) 첫 프레임을 몸에 입혀 놓고, 그때 몸 정면 = 총구 방향으로 맞춘다
        /// (총 소켓 각도가 아직 맞지 않아도 조준 방향은 애니메이션 기준으로 정확하게)
        /// </summary>
        private static AimIK SetupAimIK(Animator animator)
        {
            if (animator.isHuman == false) { log.Add("AimIK: 휴머노이드가 아니라 건너뜀"); return null; }
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform[] spine = new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest }
                .Select(animator.GetBoneTransform).Where(b => b != null).ToArray();
            AnimationClip aimClip = Clip("IdleAim" + Suffix);
            if (hand == null || spine.Length == 0 || aimClip == null) { log.Add("AimIK: 오른손·척추 뼈 또는 IdleAim 클립이 없어 건너뜀"); return null; }

            Transform aimTransform = hand.Find(AimTransformName);
            if (aimTransform == null)
            {
                aimTransform = new GameObject(AimTransformName).transform;
                Undo.RegisterCreatedObjectUndo(aimTransform.gameObject, Title);
                aimTransform.SetParent(hand, false);
            }

            // 조준 자세를 입혀 놓고 총구 방향(= 몸 정면)을 오른손 기준으로 기록
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(animator.gameObject, aimClip, 0f);
            AnimationMode.EndSampling();
            Vector3 forward = animator.transform.forward;
            Vector3 localPos = hand.InverseTransformPoint(hand.position + forward * 0.4f);
            Quaternion localRot = Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(forward, animator.transform.up);
            AnimationMode.StopAnimationMode();

            Undo.RecordObject(aimTransform, Title);
            aimTransform.localPosition = localPos;
            aimTransform.localRotation = localRot;

            AimIK aimIK = animator.GetComponent<AimIK>();
            if (aimIK == null) aimIK = Undo.AddComponent<AimIK>(animator.gameObject);
            Undo.RecordObject(aimIK, Title);
            aimIK.solver.SetChain(spine, animator.transform);
            float[] weights = { 0.3f, 0.6f, 1f };
            for (int i = 0; i < aimIK.solver.bones.Length; i++) aimIK.solver.bones[i].weight = weights[Mathf.Min(i, weights.Length - 1)];
            aimIK.solver.transform = aimTransform;
            aimIK.solver.axis = Vector3.forward;
            aimIK.solver.poleWeight = 0f;
            aimIK.solver.clampWeight = 0.1f;
            aimIK.solver.maxIterations = 4;
            aimIK.solver.tolerance = 0f;
            aimIK.solver.useRotationLimits = false;
            aimIK.solver.IKPositionWeight = 0f; // 가중치는 PlayerAimIK 가 조준 정도에 맞춘다
            PrefabUtility.RecordPrefabInstancePropertyModifications(aimIK);
            log.Add($"AimIK: 척추 {spine.Length}개, AimTransform = {hand.name}/{AimTransformName} (IdleAim 자세의 몸 정면)");
            return aimIK;
        }

        /// <summary>
        /// 왼손·발 IK: 몸에 왼팔 LimbIK(상완 → 전완 → 손, 꺼 둠: PlayerBodyIK 가 AimIK 직후에 직접 푼다)과
        /// GrounderFBBIK(바닥 레이어 = PlayerController.GroundMask), Player 에 PlayerBodyIK 연결 · PlayerFirstPersonIK · PlayerItemIK 추가
        /// </summary>
        private static void SetupBodyIK(PlayerController player, PlayerAnimationDriver driver, Animator animator, AimIK aimIK)
        {
            Transform upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (upper == null || lower == null || hand == null) { log.Add("왼손 IK: 왼팔 뼈를 찾지 못해 건너뜀"); return; }

            LimbIK leftArm = animator.GetComponent<LimbIK>();
            if (leftArm == null) leftArm = Undo.AddComponent<LimbIK>(animator.gameObject);
            Undo.RecordObject(leftArm, Title);
            leftArm.solver.SetChain(upper, lower, hand, animator.transform);
            leftArm.solver.goal = AvatarIKGoal.LeftHand;
            leftArm.solver.bendModifier = IKSolverLimb.BendModifier.Animation;
            leftArm.solver.IKPositionWeight = 0f;
            leftArm.solver.IKRotationWeight = 0f;
            leftArm.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(leftArm);

            GrounderFBBIK grounder = null;
            FullBodyBipedIK fbbik = animator.GetComponent<FullBodyBipedIK>();
            if (fbbik != null)
            {
                grounder = animator.GetComponent<GrounderFBBIK>();
                if (grounder == null) grounder = Undo.AddComponent<GrounderFBBIK>(animator.gameObject);
                Undo.RecordObject(grounder, Title);
                grounder.ik = fbbik;
                grounder.weight = 0f; // PlayerBodyIK 가 지상일 때 올린다
                grounder.solver.layers = player.GroundMask;
                grounder.solver.maxStep = 0.4f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(grounder);
            }
            else log.Add("발 IK: FullBodyBipedIK 가 없어 건너뜀");

            var bodyIK = player.GetComponent<PlayerBodyIK>();
            if (bodyIK == null) bodyIK = Undo.AddComponent<PlayerBodyIK>(player.gameObject);
            var so = new SerializedObject(bodyIK);
            so.FindProperty("player").objectReferenceValue = player;
            so.FindProperty("driver").objectReferenceValue = driver;
            so.FindProperty("gunSlot").objectReferenceValue = player.GetComponent<HybridShooter.Weapon.GunSlot>();
            so.FindProperty("aimIK").objectReferenceValue = aimIK;
            so.FindProperty("leftArmIK").objectReferenceValue = leftArm;
            so.FindProperty("grounder").objectReferenceValue = grounder;
            so.ApplyModifiedProperties();

            // 1인칭 무기 자리 (FBBIK 손 효과기)
            if (fbbik != null && player.GetComponent<PlayerFirstPersonIK>() == null) Undo.AddComponent<PlayerFirstPersonIK>(player.gameObject);
            // 아이템 사용 모습 (손에 든 수류탄, 회복 때 왼손)
            if (player.GetComponent<PlayerItemIK>() == null) Undo.AddComponent<PlayerItemIK>(player.gameObject);
            // 사격 반동 (총 기준, FBBIK 손 효과기)
            if (fbbik != null && player.GetComponent<PlayerGunRecoil>() == null) Undo.AddComponent<PlayerGunRecoil>(player.gameObject);
            // 저격 스코프·견착 조준 화면
            if (player.GetComponent<PlayerAimView>() == null) Undo.AddComponent<PlayerAimView>(player.gameObject);
            log.Add($"왼손 IK: LimbIK(왼팔) / 발 IK: {(grounder != null ? "GrounderFBBIK, 최대 계단 0.4m" : "없음")} / PlayerBodyIK 연결");
        }

        /// <summary>
        /// 몸에 FullBodyBipedIK (효과기 가중치 0 → 평소엔 자세를 바꾸지 않음). 반동(PlayerGunRecoil)·1인칭 손·발 IK 가 쓴다.
        /// 예전 Final IK Recoil 은 손 뼈 축 기준이라 총구가 내려가서 지운다. 실행 순서: AimIK(9997) → FullBodyBipedIK(9999)
        /// </summary>
        private static void SetupFullBodyIK(Animator animator)
        {
            FullBodyBipedIK fbbik = animator.GetComponent<FullBodyBipedIK>();
            if (fbbik == null)
            {
                var references = new BipedReferences();
                if (BipedReferences.AutoDetectReferences(ref references, animator.transform, BipedReferences.AutoDetectParams.Default) == false)
                {
                    log.Add("FullBodyBipedIK: 뼈 자동 인식 실패 → 반동·1인칭 손 건너뜀");
                    return;
                }
                string error = "";
                if (BipedReferences.SetupError(references, ref error))
                {
                    log.Add($"FullBodyBipedIK: 설정 오류 ({error}) → 반동·1인칭 손 건너뜀");
                    return;
                }
                fbbik = Undo.AddComponent<FullBodyBipedIK>(animator.gameObject);
                fbbik.SetReferences(references, IKSolverFullBodyBiped.DetectRootNodeBone(references));
            }
            Undo.RecordObject(fbbik, Title);
            fbbik.solver.IKPositionWeight = 1f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(fbbik);

            Recoil old = animator.GetComponent<Recoil>();
            if (old != null) Undo.DestroyObjectImmediate(old);
            log.Add("FullBodyBipedIK 연결" + (old != null ? ", 예전 Final IK Recoil 지움 (반동은 PlayerGunRecoil)" : ""));
        }
        #endregion
    }
}
