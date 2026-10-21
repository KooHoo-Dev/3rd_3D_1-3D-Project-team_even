using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using P = HybridShooter.Player.PlayerAnimParams;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 플레이어 애니메이터 컨트롤러(Player_AC)를 코드로 통째로 만든다. 메뉴: Tools > Player > 2. Build Animator Controller
    /// 손으로 수백 개의 상태·전이를 잇는 대신, 규칙(PlayerAnimParams)대로 자동 생성 → 클립을 바꾸거나 규칙을 고친 뒤 다시 누르면 된다.
    /// 기존 파일의 GUID 는 유지하므로 프리팹·씬의 연결은 끊기지 않는다. (컨트롤러를 손으로 고친 내용은 덮어써진다)
    ///
    /// 만들어지는 구조
    ///  [Base Layer] 온몸
    ///    ├ Unarmed / Rifle / Shotgun / RocketLauncher / Minigun   ← 무기별 하위 상태 머신 (WeaponType 으로 전환)
    ///    │    Locomotion(2D 블렌드 트리: MoveX·MoveY, 가운데는 1D 블렌드 트리: 내린 자세↔조준 자세)
    ///    │    Sprint · Jump · Fall · Land
    ///    ├ Dodge   ← 무기별 구르기 (2D 블렌드 트리: RollX·RollY → 앞/뒤/좌/우)
    ///    ├ Vault   ← 무기별 × 4종 벽넘기 (루트모션 클립)
    ///    └ Death   ← 무기별 × 4방향 사망
    ///  [Upper Body] 상체만 (아바타 마스크) — 무기별 하위 상태 머신: 사격 · 재장전 · 꺼내기 · 투척 · 회복
    ///  [Hit Reaction] 상체만 — 무기별 피격 (2D 블렌드 트리: HitX·HitY)
    /// </summary>
    public static class PlayerAnimatorBuilder
    {
        public const string ControllerPath = "Assets/Scenes/MS/Player_AC.controller";
        public const string UpperBodyMaskPath = "Assets/Scenes/MS/Player_UpperBodyMask.mask";
        private const string AnimRoot = "Assets/ExternalAssets/PROTOFACTOR/Ultimate Animation Collection/Animations";

        private static readonly string[] AnimFolders =
        {
            "Assault Rifle Animset", "Shotgun Animset", "Bazooka Animset", "Minigun Animset",
            "Basic Locomotion Animset", "Climbing Animset",
        };

        // 8방향 이동 클립 이름의 방향 부분과 블렌드 트리 위의 방향
        private static readonly (string name, Vector2 dir)[] MoveDirections =
        {
            ("Forward", new Vector2(0f, 1f)),
            ("ForwardLeft", new Vector2(-0.7071f, 0.7071f)),
            ("ForwardRight", new Vector2(0.7071f, 0.7071f)),
            ("Left", new Vector2(-1f, 0f)),
            ("Right", new Vector2(1f, 0f)),
            ("Backwards", new Vector2(0f, -1f)),
            ("BackwardsLeft", new Vector2(-0.7071f, -0.7071f)),
            ("BackwardsRight", new Vector2(0.7071f, -0.7071f)),
        };

        // 구르기·피격 4방향: 클립 이름 → 방향
        private static readonly (string dodge, string hit, Vector2 dir)[] FourDirections =
        {
            ("Forward", "Front", new Vector2(0f, 1f)),
            ("Backwards", "Back", new Vector2(0f, -1f)),
            ("Left", "Left", new Vector2(-1f, 0f)),
            ("Right", "Right", new Vector2(1f, 0f)),
        };

        private const float DefaultWalkSpeed = 1.5f;
        private const float DefaultRunSpeed = 3.8f;

        private static Dictionary<string, string> clipPaths;
        private static readonly Dictionary<string, AnimationClip> clipCache = new Dictionary<string, AnimationClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> missing = new List<string>();
        private static AnimatorStateMachine baseRoot, upperRoot, hitRoot;

        [MenuItem("Tools/Player/1. Fix Death Clip Loop Settings", priority = 1)]
        public static void FixDeathClipLoops()
        {
            IndexClips();
            int changed = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var pair in clipPaths)
                {
                    // 사망 모션이 반복되면 쓰러졌다 일어나기를 되풀이한다 → 반복 끄기
                    if (pair.Key.StartsWith("Death", StringComparison.OrdinalIgnoreCase) == false) continue;
                    if (AssetImporter.GetAtPath(pair.Value) is ModelImporter importer && SetLoop(importer, false)) changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            clipCache.Clear();
            Debug.Log($"[PlayerAnimatorBuilder] 사망 클립 반복 끄기: {changed}개 파일 수정");
        }

        private static bool SetLoop(ModelImporter importer, bool loop)
        {
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            bool dirty = false;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i].loopTime == loop) continue;
                clips[i].loopTime = loop;
                dirty = true;
            }
            if (dirty == false) return false;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return true;
        }

        [MenuItem("Tools/Player/2. Build Animator Controller", priority = 2)]
        public static void Build()
        {
            FixDeathClipLoops();
            missing.Clear();
            clipCache.Clear();
            IndexClips();

            AvatarMask upperMask = CreateUpperBodyMask();

            // 임시 파일에 만든 뒤 원래 파일 내용만 바꿔치기 → GUID 유지
            string tempPath = ControllerPath.Replace(".controller", "_Build.controller");
            AssetDatabase.DeleteAsset(tempPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(tempPath);

            AddParameters(controller);

            controller.AddLayer(P.UpperBodyLayer);
            controller.AddLayer(P.HitLayer);
            var layers = controller.layers;
            layers[0].name = P.BaseLayer;
            for (int i = 1; i < layers.Length; i++)
            {
                layers[i].avatarMask = upperMask;
                layers[i].blendingMode = AnimatorLayerBlendingMode.Override;
                layers[i].defaultWeight = 1f;
            }
            controller.layers = layers;

            baseRoot = controller.layers[0].stateMachine;
            upperRoot = controller.layers[1].stateMachine;
            hitRoot = controller.layers[2].stateMachine;

            BuildBaseLayer(controller);
            BuildUpperBodyLayer(controller);
            BuildHitLayer(controller);

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            ReplaceControllerFile(tempPath);

            if (missing.Count > 0)
                Debug.LogWarning("[PlayerAnimatorBuilder] 찾지 못한 클립 (해당 상태는 비어 있음):\n" + string.Join("\n", missing.Distinct()));
            Debug.Log($"[PlayerAnimatorBuilder] 완료: {ControllerPath}");
        }

        // ───────────────────────── 파라미터 ─────────────────────────

        private static void AddParameters(AnimatorController c)
        {
            foreach (var name in new[] { P.MoveX, P.MoveY, P.AimBlend, P.VerticalSpeed, P.RollX, P.RollY, P.RollSpeed, P.HitX, P.HitY, P.ActionSpeed, P.MoveAnimSpeed })
                c.AddParameter(name, AnimatorControllerParameterType.Float);
            foreach (var name in new[] { P.WeaponType, P.VaultType, P.DeathDirection })
                c.AddParameter(name, AnimatorControllerParameterType.Int);
            foreach (var name in new[] { P.Grounded, P.FreeFall, P.Sprinting, P.Firing, P.Dead })
                c.AddParameter(name, AnimatorControllerParameterType.Bool);
            foreach (var name in new[] { P.Jump, P.Roll, P.Fire, P.Reload, P.Draw, P.Throw, P.Heal, P.Hit, P.Die, P.Vault })
                c.AddParameter(name, AnimatorControllerParameterType.Trigger);

            // 기본값: 배속 1, 처음 무기 = 라이플, 처음엔 땅 위
            var ps = c.parameters;
            foreach (var p in ps)
            {
                if (p.name == P.RollSpeed || p.name == P.ActionSpeed || p.name == P.MoveAnimSpeed) p.defaultFloat = 1f;
                if (p.name == P.WeaponType) p.defaultInt = 1;
                if (p.name == P.Grounded) p.defaultBool = true;
            }
            c.parameters = ps;
        }

        // ───────────────────────── Base Layer ─────────────────────────

        private static void BuildBaseLayer(AnimatorController c)
        {
            var weaponMachines = new AnimatorStateMachine[P.WeaponTypeCount];
            for (int w = 0; w < P.WeaponTypeCount; w++)
                weaponMachines[w] = BuildWeaponLocomotion(c, w, new Vector3(300f, 60f + w * 70f));

            // 처음 들어올 때: WeaponType 에 맞는 무기 상태 머신으로 (기본 = 라이플)
            baseRoot.defaultState = weaponMachines[1].defaultState;
            for (int w = 0; w < P.WeaponTypeCount; w++)
                baseRoot.AddEntryTransition(weaponMachines[w]).AddCondition(AnimatorConditionMode.Equals, w, P.WeaponType);

            var dodge = baseRoot.AddStateMachine("Dodge", new Vector3(650f, 60f));
            var vault = baseRoot.AddStateMachine("Vault", new Vector3(650f, 140f));
            var death = baseRoot.AddStateMachine("Death", new Vector3(650f, 220f));

            for (int w = 0; w < P.WeaponTypeCount; w++)
            {
                string wn = P.WeaponName(w);

                // 구르기: 방향 4개를 2D 블렌드 트리로
                var dodgeTree = NewTree(c, "Dodge " + wn, BlendTreeType.SimpleDirectional2D, P.RollX, P.RollY);
                foreach (var d in FourDirections) AddChild(dodgeTree, Clip(P.DodgeClip(w, d.dodge)), d.dir);
                var dodgeState = AddState(dodge, "Dodge " + wn, dodgeTree, Grid(w, 0), P.TagNoAim, P.RollSpeed);
                var toDodge = AnyState(baseRoot, dodgeState, 0.05f);
                toDodge.AddCondition(AnimatorConditionMode.If, 0, P.Roll);
                AddWeaponCondition(toDodge, w);
                ExitAt(dodgeState, 0.85f, 0.15f);

                // 벽넘기: 종류 4개
                for (int t = 0; t < 4; t++)
                {
                    var clip = Clip(P.VaultClip(w, t), P.VaultClip(1, t));
                    var vaultState = AddState(vault, $"Vault {wn} {t}", clip, Grid(w, t), P.TagVault);
                    var toVault = AnyState(baseRoot, vaultState, 0.1f);
                    toVault.AddCondition(AnimatorConditionMode.If, 0, P.Vault);
                    toVault.AddCondition(AnimatorConditionMode.Equals, t, P.VaultType);
                    AddWeaponCondition(toVault, w);
                    ExitAt(vaultState, 0.95f, 0.1f);
                }

                // 사망: 방향 4개. 끝 자세에서 멈춰 있다가 Dead 가 꺼지면(부활) 빠져나간다
                for (int d = 0; d < 4; d++)
                {
                    var deathState = AddState(death, $"Death {wn} {P.DirectionNames[d]}", Clip(P.DeathClip(w, P.DirectionNames[d])), Grid(w, d), P.TagNoIK);
                    var toDeath = AnyState(baseRoot, deathState, 0.1f);
                    toDeath.AddCondition(AnimatorConditionMode.If, 0, P.Die);
                    toDeath.AddCondition(AnimatorConditionMode.Equals, d, P.DeathDirection);
                    AddWeaponCondition(toDeath, w, requireAlive: false);
                    var revive = deathState.AddExitTransition();
                    Configure(revive, 0.3f, -1f);
                    revive.AddCondition(AnimatorConditionMode.IfNot, 0, P.Dead);
                }
            }

            // 하위 상태 머신에서 빠져나오면 → 지금 무기의 상태 머신으로
            var sources = new List<AnimatorStateMachine>(weaponMachines) { dodge, vault, death };
            foreach (var src in sources)
                for (int w = 0; w < P.WeaponTypeCount; w++)
                {
                    if (src == weaponMachines[w]) continue;
                    baseRoot.AddStateMachineTransition(src, weaponMachines[w]).AddCondition(AnimatorConditionMode.Equals, w, P.WeaponType);
                }
        }

        /// <summary>무기 하나의 이동 상태 머신: Locomotion · Sprint · Jump · Fall · Land</summary>
        private static AnimatorStateMachine BuildWeaponLocomotion(AnimatorController c, int w, Vector3 position)
        {
            string wn = P.WeaponName(w);
            string s = P.ClipSuffix(w);
            bool unarmed = w == 0;
            var sm = baseRoot.AddStateMachine(wn, position);

            // ── Locomotion 블렌드 트리 ──
            // 위치 = 방향 × 그 클립의 실제 이동 속도(m/s). MoveX/MoveY 에 실제 속도를 넣으면 발 속도가 맞는 클립이 섞인다
            var tree = NewTree(c, wn + " Locomotion", BlendTreeType.FreeformDirectional2D, P.MoveX, P.MoveY);

            // 가운데(정지): 총을 내린 자세 ↔ 조준 자세 (AimBlend) — 블렌드 트리 안의 블렌드 트리
            var idle = tree.CreateBlendTreeChild(Vector2.zero);
            idle.name = wn + " Idle";
            idle.blendType = BlendTreeType.Simple1D;
            idle.blendParameter = P.AimBlend;
            idle.useAutomaticThresholds = false;
            idle.AddChild(Clip(unarmed ? "IdleUnarmed" : "IdleHold" + s), 0f);
            idle.AddChild(Clip(unarmed ? "IdleUnarmed" : "IdleAim" + s), 1f);

            foreach (var gait in new[] { "Walk", "Run" })
            {
                float fallback = gait == "Walk" ? DefaultWalkSpeed : DefaultRunSpeed;
                foreach (var d in MoveDirections)
                {
                    string name = unarmed ? $"{gait}{d.name}Unarmed" : $"{gait}{d.name}Aiming{s}";
                    var clip = Clip(name);
                    if (clip == null) continue;
                    AddChild(tree, clip, d.dir * ClipSpeed(name, fallback));
                }
            }

            var loco = AddState(sm, "Locomotion", tree, new Vector3(300f, 0f), speedParameter: P.MoveAnimSpeed);
            sm.defaultState = loco;
            var sprint = AddState(sm, "Sprint", Clip("SprintForward" + s), new Vector3(300f, 100f), P.TagNoAim);
            var jump = AddState(sm, "Jump", Clip("JumpToApex" + s), new Vector3(560f, 0f));
            var fall = AddState(sm, "Fall", Clip("Falling" + s), new Vector3(560f, 100f));
            var land = AddState(sm, "Land", Clip("LandingLight" + s), new Vector3(560f, 200f));

            // 달리기: 앞으로 빠르게 + Sprinting
            var toSprint = To(loco, sprint, 0.2f);
            toSprint.AddCondition(AnimatorConditionMode.If, 0, P.Sprinting);
            toSprint.AddCondition(AnimatorConditionMode.Greater, 1f, P.MoveY);
            To(sprint, loco, 0.2f).AddCondition(AnimatorConditionMode.IfNot, 0, P.Sprinting);
            To(sprint, loco, 0.2f).AddCondition(AnimatorConditionMode.Less, 0.5f, P.MoveY);

            // 점프 → 정점 → 낙하 → 착지
            foreach (var from in new[] { loco, sprint })
            {
                To(from, jump, 0.1f).AddCondition(AnimatorConditionMode.If, 0, P.Jump);
                To(from, fall, 0.2f).AddCondition(AnimatorConditionMode.If, 0, P.FreeFall);
            }
            To(jump, fall, 0.2f, 0.8f);
            To(jump, land, 0.1f).AddCondition(AnimatorConditionMode.If, 0, P.Grounded);
            To(fall, land, 0.1f).AddCondition(AnimatorConditionMode.If, 0, P.Grounded);
            To(land, loco, 0.2f, 0.5f);

            // 무기가 바뀌면 이 상태 머신을 빠져나간다 (다음 무기 상태 머신은 Base Layer 의 전이가 고름)
            foreach (var state in new[] { loco, sprint, jump, fall, land })
            {
                var exit = state.AddExitTransition();
                Configure(exit, 0.2f, -1f);
                exit.AddCondition(AnimatorConditionMode.NotEqual, w, P.WeaponType);
            }
            return sm;
        }

        // ───────────────────────── Upper Body Layer ─────────────────────────

        private static void BuildUpperBodyLayer(AnimatorController c)
        {
            var empty = upperRoot.AddState("Empty", new Vector3(300f, 0f));
            upperRoot.defaultState = empty;

            for (int w = 0; w < P.WeaponTypeCount; w++)
            {
                string wn = P.WeaponName(w);
                var sm = upperRoot.AddStateMachine(wn + " Actions", new Vector3(300f, 100f + w * 70f));

                // 사격
                if (w != 0)
                {
                    if (P.IsLoopFireWeapon(w))
                    {
                        // 라이플·미니건: Firing 이 켜져 있는 동안 반복
                        var fire = AddState(sm, "Fire", Clip(P.ShootClip(w)), Grid(0, 0));
                        var toFire = AnyState(upperRoot, fire, 0.1f);
                        toFire.AddCondition(AnimatorConditionMode.If, 0, P.Firing);
                        AddWeaponCondition(toFire, w);
                        var stop = fire.AddExitTransition();
                        Configure(stop, 0.15f, -1f);
                        stop.AddCondition(AnimatorConditionMode.IfNot, 0, P.Firing);
                    }
                    else
                    {
                        // 샷건·로켓: 한 발마다 한 번
                        var fire = AddState(sm, "Fire", Clip(P.ShootClip(w)), Grid(0, 0));
                        var toFire = AnyState(upperRoot, fire, 0.05f, canTransitionToSelf: true);
                        toFire.AddCondition(AnimatorConditionMode.If, 0, P.Fire);
                        AddWeaponCondition(toFire, w);
                        ExitAt(fire, 0.85f, 0.15f);
                    }
                }

                AddAction(sm, w, "Reload", P.Reload, Clip(P.ReloadClip(w)), Grid(1, 0));
                AddAction(sm, w, "Draw", P.Draw, Clip(P.DrawClip(w)), Grid(1, 1));
                AddAction(sm, w, "Throw", P.Throw, Clip(P.ThrowClip(w)), Grid(1, 2));

                // 회복: 팔 들기 → 들고 있기(일부만) → 끝
                var raise = AddState(sm, "Heal Raise", Clip(P.HealRaiseClip(w)), Grid(2, 0), P.TagNoIK, P.ActionSpeed);
                var hold = AddState(sm, "Heal Hold", Clip(P.HealHoldClip(w)), Grid(2, 1), P.TagNoIK, P.ActionSpeed);
                var toHeal = AnyState(upperRoot, raise, 0.15f);
                toHeal.AddCondition(AnimatorConditionMode.If, 0, P.Heal);
                AddWeaponCondition(toHeal, w);
                To(raise, hold, 0.1f, 0.95f);
                ExitAt(hold, P.HealHoldPortion, 0.2f);

                // 이 상태 머신이 끝나면 Empty 로
                upperRoot.AddStateMachineTransition(sm, empty);
            }
        }

        /// <summary>트리거 하나로 시작해서 끝나면 빠져나가는 상체 동작 (손 IK 끔, 배속 = ActionSpeed)</summary>
        private static void AddAction(AnimatorStateMachine sm, int w, string name, string trigger, Motion clip, Vector3 pos)
        {
            var state = AddState(sm, name, clip, pos, P.TagNoIK, P.ActionSpeed);
            var to = AnyState(upperRoot, state, 0.15f);
            to.AddCondition(AnimatorConditionMode.If, 0, trigger);
            AddWeaponCondition(to, w);
            ExitAt(state, P.ActionExitTime, 0.15f);
        }

        // ───────────────────────── Hit Reaction Layer ─────────────────────────

        private static void BuildHitLayer(AnimatorController c)
        {
            var empty = hitRoot.AddState("Empty", new Vector3(300f, 0f));
            hitRoot.defaultState = empty;

            for (int w = 0; w < P.WeaponTypeCount; w++)
            {
                string wn = P.WeaponName(w);
                var tree = NewTree(c, "Hit " + wn, BlendTreeType.SimpleDirectional2D, P.HitX, P.HitY);
                foreach (var d in FourDirections) AddChild(tree, Clip(P.HitClip(w, d.hit)), d.dir);

                var hit = AddState(hitRoot, "Hit " + wn, tree, new Vector3(300f, 100f + w * 70f));
                var to = AnyState(hitRoot, hit, 0.05f, canTransitionToSelf: true);
                to.AddCondition(AnimatorConditionMode.If, 0, P.Hit);
                AddWeaponCondition(to, w);
                To(hit, empty, 0.25f, 0.75f);
            }
        }

        // ───────────────────────── 아바타 마스크 ─────────────────────────

        private static AvatarMask CreateUpperBodyMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
            bool isNew = mask == null;
            if (isNew) mask = new AvatarMask();

            var upper = new HashSet<AvatarMaskBodyPart>
            {
                AvatarMaskBodyPart.Body, AvatarMaskBodyPart.Head,
                AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers,
                AvatarMaskBodyPart.LeftHandIK, AvatarMaskBodyPart.RightHandIK,
            };
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, upper.Contains((AvatarMaskBodyPart)i));

            if (isNew) AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
            else EditorUtility.SetDirty(mask);
            return mask;
        }

        // ───────────────────────── 클립 찾기 ─────────────────────────

        private static void IndexClips()
        {
            clipPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in AnimFolders)
            {
                string dir = Path.Combine(AnimRoot, folder);
                if (Directory.Exists(dir) == false) { Debug.LogWarning($"[PlayerAnimatorBuilder] 폴더 없음: {dir}"); continue; }
                foreach (var file in Directory.GetFiles(dir, "Humanoid@*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) == false) continue;
                    // 파일 이름 = "Humanoid@" + 클립 이름
                    string clipName = Path.GetFileNameWithoutExtension(file).Substring("Humanoid@".Length);
                    if (clipPaths.ContainsKey(clipName) == false) clipPaths.Add(clipName, file.Replace('\\', '/'));
                }
            }
        }

        /// <summary>다른 에디터 도구용 (총 자동 정렬이 조준 자세 클립을 가져갈 때)</summary>
        public static AnimationClip FindClip(params string[] names)
        {
            if (clipPaths == null) IndexClips();
            return Clip(names);
        }

        /// <summary>이름 후보를 순서대로 찾아 첫 번째로 있는 클립. 하나도 없으면 null + 경고 목록에 기록</summary>
        private static AnimationClip Clip(params string[] names)
        {
            foreach (var name in names)
            {
                if (clipCache.TryGetValue(name, out var cached)) return cached;
                if (clipPaths.TryGetValue(name, out var path) == false) continue;
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(a => a.name.StartsWith("__preview__", StringComparison.Ordinal) == false);
                if (clip == null) continue;
                clipCache[name] = clip;
                return clip;
            }
            missing.Add(string.Join(" / ", names));
            return null;
        }

        /// <summary>같은 동작의 루트모션(_RM) 클립에서 실제 이동 속도(m/s)를 읽는다. 블렌드 트리 위치로 쓴다</summary>
        private static float ClipSpeed(string clipName, float fallback)
        {
            if (clipPaths.ContainsKey(clipName + "_RM") == false) return fallback;
            var rm = Clip(clipName + "_RM");
            if (rm == null) return fallback;
            Vector3 v = rm.averageSpeed;
            float speed = new Vector2(v.x, v.z).magnitude;
            return speed > 0.2f ? speed : fallback;
        }

        // ───────────────────────── 상태·전이 도우미 ─────────────────────────

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, Vector3 pos,
                                              string tag = null, string speedParameter = null)
        {
            var state = sm.AddState(name, pos);
            state.motion = motion;
            if (tag != null) state.tag = tag;
            if (speedParameter != null)
            {
                state.speedParameterActive = true;
                state.speedParameter = speedParameter;
            }
            return state;
        }

        private static BlendTree NewTree(AnimatorController c, string name, BlendTreeType type, string x, string y)
        {
            var tree = new BlendTree
            {
                name = name,
                blendType = type,
                blendParameter = x,
                blendParameterY = y,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, c);
            return tree;
        }

        private static void AddChild(BlendTree tree, Motion motion, Vector2 position)
        {
            if (motion != null) tree.AddChild(motion, position);
        }

        private static AnimatorStateTransition To(AnimatorState from, AnimatorState to, float duration, float exitTime = -1f)
        {
            var t = from.AddTransition(to);
            Configure(t, duration, exitTime);
            return t;
        }

        private static AnimatorStateTransition AnyState(AnimatorStateMachine root, AnimatorState to, float duration, bool canTransitionToSelf = false)
        {
            var t = root.AddAnyStateTransition(to);
            Configure(t, duration, -1f);
            t.canTransitionToSelf = canTransitionToSelf;
            return t;
        }

        /// <summary>클립의 exitTime 지점에서 상태 머신 밖으로</summary>
        private static void ExitAt(AnimatorState state, float exitTime, float duration)
        {
            var t = state.AddExitTransition();
            Configure(t, duration, exitTime);
        }

        private static void Configure(AnimatorStateTransition t, float duration, float exitTime)
        {
            t.hasFixedDuration = true;
            t.duration = duration;
            t.hasExitTime = exitTime >= 0f;
            if (t.hasExitTime) t.exitTime = exitTime;
        }

        private static void AddWeaponCondition(AnimatorStateTransition t, int w, bool requireAlive = true)
        {
            t.AddCondition(AnimatorConditionMode.Equals, w, P.WeaponType);
            if (requireAlive) t.AddCondition(AnimatorConditionMode.IfNot, 0, P.Dead);
        }

        private static Vector3 Grid(int column, int row) => new Vector3(300f + column * 250f, row * 70f, 0f);

        // ───────────────────────── 파일 교체 (GUID 유지) ─────────────────────────

        private static void ReplaceControllerFile(string tempPath)
        {
            if (File.Exists(ControllerPath) == false)
            {
                AssetDatabase.MoveAsset(tempPath, ControllerPath);
                return;
            }

            // Animator 창이 옛 컨트롤러를 보고 있으면, 파일을 바꾼 뒤 사라진 상태 머신을 그리려다 MissingReferenceException 이 난다.
            // → 바꾸기 전에 창을 닫고, 끝난 뒤 새 컨트롤러를 선택해서 다시 연다.
            bool reopenAnimatorWindow = CloseAnimatorWindows();
            if (Selection.activeObject is AnimatorController || Selection.activeObject is AnimatorStateMachine
                || Selection.activeObject is AnimatorState || Selection.activeObject is AnimatorTransitionBase)
                Selection.activeObject = null;

            File.Copy(tempPath, ControllerPath, overwrite: true);
            AssetDatabase.DeleteAsset(tempPath);
            AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);

            if (reopenAnimatorWindow)
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                EditorApplication.delayCall += () => EditorApplication.ExecuteMenuItem("Window/Animation/Animator");
            }
        }

        /// <summary>열려 있는 Animator 창(내부 타입 AnimatorControllerTool)을 모두 닫는다. 하나라도 닫았으면 true</summary>
        private static bool CloseAnimatorWindows()
        {
            bool closed = false;
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window.GetType().Name != "AnimatorControllerTool") continue;
                window.Close();
                closed = true;
            }
            return closed;
        }
    }
}
