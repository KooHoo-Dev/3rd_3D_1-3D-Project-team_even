using System.Collections.Generic;
using HybridShooter.Weapon;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace HybridShooter.Player.EditorTools
{
    /// <summary>
    /// 플레이어 프리팹 자동 설정. 메뉴: Tools > Player > 3. Setup Player Prefab
    ///
    ///  1) 빠진 컴포넌트 붙이기 (체력·애니메이터 연결·무기·수류탄·회복·벽넘기·손 IK)
    ///  2) Animator 에 Player_AC 연결 + 루트모션 중계(AnimatorRootMotionRelay)
    ///  3) 오른손 뼈 아래에 WeaponSocket 을 만들고 총 4자루(라이플·샷건·로켓런처·미니건)를 붙인다
    ///  4) 총마다 WeaponItem 능력치 + 총구(Muzzle)·왼손 위치(LeftHandGrip)를 메시 모양으로 추정해서 만든다
    ///
    /// 이미 있는 총은 건드리지 않는다 (손으로 맞춘 위치를 지키기 위해).
    /// 위치를 다시 추정하고 싶으면 총의 WeaponItem 컴포넌트 ⋮ 메뉴 → "자동 정렬 다시 하기".
    /// </summary>
    public static class PlayerPrefabSetup
    {
        public const string PlayerPrefabPath = "Assets/Scenes/MS/Player.prefab";
        private const string WeaponFolder = "Assets/ExternalAssets/Sci-fi-Weapons/SciFiWorld/Weapons/Prefabs/";
        private const string MinigunModel = "Assets/ExternalAssets/PROTOFACTOR/Ultimate Animation Collection/Animations/Minigun Animset/Prop/FBX File/SM_Minigun.fbx";

        private struct WeaponPreset
        {
            public WeaponType Type;
            public string Path;
            public string Name;
            public ShotKind Shot;
            public int TableId;
            public bool Upgrades;
            /// <summary>메시 모양으로 추정한 앞뒤가 반대로 나오는 총 (SF 무기 프리팹은 추정이 뒤집혀 나온다)</summary>
            public bool Flip;
            public bool Auto;
            public int Damage, Magazine, Pellets;
            public float Rpm, Reload, Range, Spread, SpinUp, AimMove, CarryMove, AimFov;
        }

        private static readonly WeaponPreset[] Presets =
        {
            new WeaponPreset { Type = WeaponType.Rifle, Path = WeaponFolder + "SciFiRifle01_1.prefab", Name = "Rifle", Shot = ShotKind.Hitscan, Flip = true,
                TableId = 2001, Upgrades = true, Auto = true, Damage = 10, Magazine = 30, Pellets = 1,
                Rpm = 600, Reload = 2f, Range = 120, Spread = 1.2f, AimMove = 0.45f, CarryMove = 1f, AimFov = 0.7f },
            new WeaponPreset { Type = WeaponType.Shotgun, Path = WeaponFolder + "SciFiShotGun01_1.prefab", Name = "Shotgun", Shot = ShotKind.Hitscan, Flip = true,
                Auto = false, Damage = 9, Magazine = 6, Pellets = 8,
                Rpm = 75, Reload = 2.6f, Range = 35, Spread = 6f, AimMove = 0.45f, CarryMove = 0.95f, AimFov = 0.85f },
            new WeaponPreset { Type = WeaponType.RocketLauncher, Path = WeaponFolder + "SciFiRocketLauncher01_1.prefab", Name = "Rocket Launcher", Shot = ShotKind.Projectile, Flip = true,
                Auto = false, Damage = 120, Magazine = 1, Pellets = 1,
                Rpm = 40, Reload = 2.8f, Range = 200, Spread = 0.3f, AimMove = 0.4f, CarryMove = 0.85f, AimFov = 0.6f },
            new WeaponPreset { Type = WeaponType.Minigun, Path = MinigunModel, Name = "Minigun", Shot = ShotKind.Hitscan,
                Auto = true, Damage = 6, Magazine = 150, Pellets = 1,
                Rpm = 1400, Reload = 4f, Range = 100, Spread = 2.5f, SpinUp = 0.5f, AimMove = 0.35f, CarryMove = 0.75f, AimFov = 0.85f },
        };

        [MenuItem("Tools/Player/3. Setup Player Prefab", priority = 3)]
        public static void Setup()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                SetupComponents(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[PlayerPrefabSetup] 완료: {PlayerPrefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>프리팹 안의 모든 총을 조준 자세 기준으로 다시 정렬 (손으로 맞춘 위치는 덮어써진다)</summary>
        [MenuItem("Tools/Player/4. Realign All Weapons (aim pose)", priority = 4)]
        public static void RealignAll()
        {
            if (EditorUtility.DisplayDialog("총 위치 다시 맞추기",
                    "Player 프리팹의 모든 총 위치·왼손 자리·총구를 조준 자세 기준으로 다시 계산합니다.\n손으로 맞춘 값은 덮어써집니다.", "다시 맞추기", "취소") == false)
                return;

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var animator = root.GetComponentInChildren<Animator>();
                if (animator == null) { Debug.LogError("[PlayerPrefabSetup] 자식에 Animator 가 없습니다."); return; }
                int count = 0;
                foreach (var item in root.GetComponentsInChildren<WeaponItem>(true))
                {
                    WeaponAutoAlign.Align(item, animator, flip: PresetFlip(item.Type));
                    EnsureHandPose(item);
                    count++;
                }
                foreach (var item in root.GetComponentsInChildren<WeaponItem>(true)) ClearStaticFlags(item.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log($"[PlayerPrefabSetup] 총 {count}자루 다시 정렬 완료");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("Tools/Player/Run All (2 → 3)", priority = 20)]
        public static void RunAll()
        {
            PlayerAnimatorBuilder.Build();
            Setup();
        }

        private static void SetupComponents(GameObject root)
        {
            // 1) 컴포넌트 (RequireComponent 로 연결된 것도 함께 붙는다)
            Ensure<PlayerHealth>(root);
            Ensure<PlayerAnimator>(root);
            var weapons = Ensure<PlayerWeapons>(root);
            var grenades = Ensure<PlayerGrenadeThrower>(root);
            var heal = Ensure<PlayerHealPacks>(root);
            Ensure<PlayerVault>(root);
            Ensure<PlayerHandIK>(root);

            // 2) Animator
            var animator = root.GetComponentInChildren<Animator>();
            if (animator == null) { Debug.LogError("[PlayerPrefabSetup] 자식에 Animator 가 없습니다."); return; }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerAnimatorBuilder.ControllerPath);
            if (controller != null) animator.runtimeAnimatorController = controller;
            else Debug.LogWarning("[PlayerPrefabSetup] Player_AC 가 없습니다. 먼저 2. Build Animator Controller 를 실행하세요.");
            animator.applyRootMotion = false;
            Ensure<AnimatorRootMotionRelay>(animator.gameObject);

            // 3) 수류탄·주사기 프리팹
            SetObject(grenades, "grenadePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(WeaponFolder + "SciFiGrenade01_1.prefab"), onlyIfEmpty: true);
            SetObject(heal, "injectorPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(WeaponFolder + "SciFiMedInjector01_1.prefab"), onlyIfEmpty: true);

            // 4) 총
            Transform rightHand = FindBone(animator, HumanBodyBones.RightHand);
            if (rightHand == null) { Debug.LogError("[PlayerPrefabSetup] 오른손 뼈를 찾지 못했습니다 (Humanoid 아바타 확인)."); return; }
            Transform socket = rightHand.Find("WeaponSocket");
            if (socket == null)
            {
                socket = new GameObject("WeaponSocket").transform;
                socket.SetParent(rightHand, false);
            }

            var items = new List<WeaponItem>();
            foreach (var preset in Presets)
            {
                var item = EnsureWeapon(socket, preset, animator);
                if (item != null) items.Add(item);
            }

            ClearStaticFlags(socket.gameObject);

            var so = new SerializedObject(weapons);
            var slots = so.FindProperty("slots");
            slots.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) slots.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static WeaponItem EnsureWeapon(Transform socket, WeaponPreset preset, Animator animator)
        {
            string objectName = "Weapon_" + preset.Type;
            Transform existing = socket.Find(objectName);
            if (existing != null)
            {
                var found = existing.GetComponent<WeaponItem>();
                if (found != null) { FillZoomIfEmpty(found, preset.AimFov); EnsureHandPose(found); return found; }
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(preset.Path);
            if (source == null) { Debug.LogWarning($"[PlayerPrefabSetup] 총 프리팹 없음: {preset.Path}"); return null; }

            var go = existing != null ? existing.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(source, socket);
            go.name = objectName;
            HandProp.KeepWorldScale(go.transform, source.transform.localScale);
            // 손에 든 총이 플레이어 몸·바닥 판정에 걸리지 않게 총에 달린 물리를 끈다
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (var body in go.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            var item = go.GetComponent<WeaponItem>();
            if (item == null) item = go.AddComponent<WeaponItem>();

            var so = new SerializedObject(item);
            so.FindProperty("type").enumValueIndex = (int)preset.Type;
            so.FindProperty("shotKind").enumValueIndex = (int)preset.Shot;
            so.FindProperty("displayName").stringValue = preset.Name;
            so.FindProperty("tableId").intValue = preset.TableId;
            so.FindProperty("affectedByUpgrades").boolValue = preset.Upgrades;
            so.FindProperty("fireMode").enumValueIndex = preset.Auto ? 1 : 0;
            so.FindProperty("damage").intValue = preset.Damage;
            so.FindProperty("fireRate").floatValue = preset.Rpm;
            so.FindProperty("magazineSize").intValue = preset.Magazine;
            so.FindProperty("reloadTime").floatValue = preset.Reload;
            so.FindProperty("range").floatValue = preset.Range;
            so.FindProperty("spreadAngle").floatValue = preset.Spread;
            so.FindProperty("pelletsPerShot").intValue = preset.Pellets;
            so.FindProperty("spinUpTime").floatValue = preset.SpinUp;
            so.FindProperty("aimMoveSpeedMultiplier").floatValue = preset.AimMove;
            so.FindProperty("carryMoveSpeedMultiplier").floatValue = preset.CarryMove;
            so.FindProperty("aimFovMultiplier").floatValue = preset.AimFov;
            if (preset.Shot == ShotKind.Projectile)
                so.FindProperty("projectilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponFolder + "SciFiRocket01.prefab");
            so.ApplyModifiedPropertiesWithoutUndo();

            WeaponAutoAlign.Align(item, animator, flip: preset.Flip);
            EnsureHandPose(item);
            return item;
        }

        // ───────────────────────── 도우미 ─────────────────────────

        /// <summary>총마다 손 자세(손가락·팔꿈치) 컴포넌트</summary>
        private static void EnsureHandPose(WeaponItem item)
        {
            if (item.GetComponent<WeaponHandPose>() == null) item.gameObject.AddComponent<WeaponHandPose>();
        }

        /// <summary>이 무기 종류의 기본 앞뒤 뒤집기 값</summary>
        public static bool PresetFlip(WeaponType type)
        {
            foreach (var p in Presets) if (p.Type == type) return p.Flip;
            return false;
        }

        /// <summary>
        /// SF 무기 프리팹은 모든 오브젝트가 Static 으로 표시돼 있다. Static 이면 플레이 시작 때 메시가 합쳐져(정적 배칭)
        /// 부모(손)가 움직여도 처음 자리에 남는다 → 손에 든 물체는 Static 을 꺼야 한다. (원본 프리팹은 그대로, 플레이어 프리팹 안에서만)
        /// </summary>
        private static void ClearStaticFlags(GameObject root)
        {
            int cleared = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) == 0) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
                cleared++;
            }
            if (cleared > 0) Debug.Log($"[PlayerPrefabSetup] 손에 든 물체의 Static 해제: {cleared}개");
        }

        /// <summary>이전 버전에서 만든 총엔 조준 줌 값이 없으므로 채워 준다</summary>
        private static void FillZoomIfEmpty(WeaponItem item, float fov)
        {
            var so = new SerializedObject(item);
            var p = so.FindProperty("aimFovMultiplier");
            if (p.floatValue > 0f) return;
            p.floatValue = fov;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void SetObject(Object target, string property, Object value, bool onlyIfEmpty)
        {
            if (value == null) return;
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            if (p == null || (onlyIfEmpty && p.objectReferenceValue != null)) return;
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 휴머노이드 뼈 찾기. 편집 모드에서는 Animator.GetBoneTransform 이 비어 있을 수 있어
        /// 아바타의 뼈 이름표(humanDescription)로 한 번 더 찾는다.
        /// </summary>
        public static Transform FindBone(Animator animator, HumanBodyBones bone)
        {
            if (animator.isHuman)
            {
                Transform t = animator.GetBoneTransform(bone);
                if (t != null) return t;
            }
            if (animator.avatar == null) return null;
            string humanName = HumanTrait.BoneName[(int)bone];
            foreach (var hb in animator.avatar.humanDescription.human)
                if (hb.humanName == humanName) return FindDeep(animator.transform, hb.boneName);
            return null;
        }

        private static Transform FindDeep(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }

    /// <summary>
    /// 총 메시 모양을 보고 "총열 방향 · 위쪽 · 손잡이 · 왼손 · 총구" 를 추정해 오른손에 맞춰 놓는다.
    /// 완벽하지 않으니 결과를 씬 뷰에서 보고 총 오브젝트(Weapon_*)와 LeftHandGrip·Muzzle 을 손으로 다듬는 것을 전제로 한다.
    /// </summary>
    public static class WeaponAutoAlign
    {
        // 총 길이(총열 방향) 기준 비율: 0 = 개머리판 끝, 1 = 총구 끝 / 높이 기준 비율: 0 = 바닥, 1 = 꼭대기
        private const float RightGripAlong = 0.3f, RightGripHeight = 0.35f;
        private const float LeftGripAlong = 0.65f, LeftGripHeight = 0.3f;

        [MenuItem("CONTEXT/WeaponItem/자동 정렬 다시 하기")]
        private static void RealignMenu(MenuCommand cmd) => Realign((WeaponItem)cmd.context, PlayerPrefabSetup.PresetFlip(((WeaponItem)cmd.context).Type));

        [MenuItem("CONTEXT/WeaponItem/자동 정렬 다시 하기 (앞뒤 반대로)")]
        private static void RealignFlipMenu(MenuCommand cmd) => Realign((WeaponItem)cmd.context, !PlayerPrefabSetup.PresetFlip(((WeaponItem)cmd.context).Type));

        private static void Realign(WeaponItem item, bool flip)
        {
            var animator = item.GetComponentInParent<Animator>();
            if (animator == null) { Debug.LogWarning("[WeaponAutoAlign] 부모에 Animator 가 없습니다 (플레이어 손에 붙은 총에서 실행하세요)."); return; }
            Undo.RegisterFullObjectHierarchyUndo(item.gameObject, "Realign Weapon");
            Align(item, animator, flip);
            EditorUtility.SetDirty(item);
        }

        public static void Align(WeaponItem item, Animator animator, bool flip)
        {
            Transform weapon = item.transform;
            if (TryMeasure(weapon, out Vector3 min, out Vector3 max, out int barrelAxis, out float barrelSign, out int upAxis, out float upSign) == false)
            {
                Debug.LogWarning($"[WeaponAutoAlign] {weapon.name}: 메시를 읽지 못해 정렬을 건너뜁니다.", weapon);
                return;
            }
            if (flip) barrelSign = -barrelSign;

            Vector3 barrelLocal = Axis(barrelAxis) * barrelSign;
            Vector3 upLocal = Axis(upAxis) * upSign;

            Vector3 Point(float along, float height)
            {
                // along: 0 = 뒤쪽 끝, 1 = 앞쪽 끝 (barrelSign 반영), height: 0 = 바닥, 1 = 꼭대기 (upSign 반영)
                Vector3 p = (min + max) * 0.5f;
                p[barrelAxis] = barrelSign > 0 ? Mathf.Lerp(min[barrelAxis], max[barrelAxis], along) : Mathf.Lerp(max[barrelAxis], min[barrelAxis], along);
                p[upAxis] = upSign > 0 ? Mathf.Lerp(min[upAxis], max[upAxis], height) : Mathf.Lerp(max[upAxis], min[upAxis], height);
                return p;
            }

            Transform hand = PlayerPrefabSetup.FindBone(animator, HumanBodyBones.RightHand);
            if (hand == null) return;

            // 총 옆모습(길이 방향으로 잘라 본 아랫선)에서 손잡이·총열 받침 위치를 찾는다
            float muzzleHeight = MuzzleHeight(weapon, barrelAxis, barrelSign, upAxis, upSign, min, max);
            var profile = new GunProfile(CollectVertices(weapon), weapon, barrelAxis, barrelSign, upAxis, upSign, min, max, muzzleHeight);

            // ── 1) 캐릭터를 이 무기의 "조준 자세" 애니메이션으로 잠깐 세운다 ──
            //    T자세에서 맞추면 실제로 총을 드는 자세와 손 방향이 달라 총이 손에서 떠 보인다.
            //    애니메이션 자세에서 맞춰야 "애니메이션이 총을 쥐는 손 모양" 그대로 붙는다. 끝나면 뼈를 원래대로 돌려놓는다.
            var pose = new PoseSnapshot(animator.transform);
            bool posed = SampleAimPose(animator, (int)item.Type);
            if (posed == false)
                Debug.LogWarning($"[WeaponAutoAlign] {weapon.name}: 조준 자세 애니메이션을 적용하지 못해 기본 자세로 맞춥니다. 손으로 다듬어 주세요.", weapon);

            try
            {
                Transform root = animator.transform;
                PalmFrame(animator, true, out Vector3 rightPalm, out _, out _);

                // ── 2) 총: 총열 = 캐릭터 정면, 총 위쪽 = 위, 손잡이 = 오른손바닥 ──
                Quaternion rotation = Quaternion.LookRotation(root.forward, root.up) * Quaternion.Inverse(Quaternion.LookRotation(barrelLocal, upLocal));
                Vector3 gripLocal = Point(profile.GripAlong, profile.GripHeight);
                weapon.rotation = rotation;
                weapon.position = rightPalm - rotation * Vector3.Scale(gripLocal, weapon.lossyScale);

                // ── 3) 왼손 자리: 애니메이션의 왼손바닥 위치를 총 아랫면으로 옮긴 점, 방향은 애니메이션의 왼손 방향 그대로 ──
                var left = GetOrCreateChild(weapon, "LeftHandGrip");
                Transform leftHand = PlayerPrefabSetup.FindBone(animator, HumanBodyBones.LeftHand);
                if (posed && leftHand != null && PalmFrame(animator, false, out Vector3 leftPalm, out _, out _))
                {
                    Vector3 local = weapon.InverseTransformPoint(leftPalm);
                    // 손잡이보다 앞, 총구보다 뒤
                    float along = Mathf.Clamp(AlongRatio(local, barrelAxis, barrelSign, min, max), profile.GripAlong + 0.12f, 0.92f);
                    left.localPosition = Point(along, profile.SupportHeight(along));
                    left.rotation = leftHand.rotation; // 손 IK 의 "방향 맞추기"가 이 회전을 쓴다
                }
                else
                {
                    float along = Mathf.Max(LeftGripAlong, profile.GripAlong + 0.2f);
                    left.localPosition = Point(along, profile.SupportHeight(along));
                    left.localRotation = Quaternion.LookRotation(barrelLocal, upLocal);
                }
            }
            finally
            {
                pose.Restore();
            }

            // ── 총구 ──
            Quaternion localLook = Quaternion.LookRotation(barrelLocal, upLocal);
            var leftGrip = weapon.Find("LeftHandGrip");

            var muzzle = GetOrCreateChild(weapon, "Muzzle");
            muzzle.localPosition = Point(1f, muzzleHeight);
            muzzle.localRotation = localLook;

            // 오른손 자리: 총이 손 아래 붙어 있는 지금, 손목 위치·방향 그대로 (총 기준 모드에서 오른손 IK 목표)
            var rightGrip = GetOrCreateChild(weapon, "RightHandGrip");
            Transform wrist = PlayerPrefabSetup.FindBone(animator, HumanBodyBones.RightHand);
            if (wrist != null) rightGrip.SetPositionAndRotation(wrist.position, wrist.rotation);

            // 총과 손의 관계가 바뀌었으므로 저장된 "총 드는 위치"는 다시 계산하게 한다
            var handPose = item.GetComponent<WeaponHandPose>();
            if (handPose != null) handPose.hasHoldOffset = false;

            var so = new SerializedObject(item);
            so.FindProperty("leftHandGrip").objectReferenceValue = leftGrip;
            so.FindProperty("rightHandGrip").objectReferenceValue = rightGrip;
            so.FindProperty("muzzle").objectReferenceValue = muzzle;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ───────────────────────── 총 옆모습 분석 ─────────────────────────

        /// <summary>
        /// 총을 길이 방향으로 칸을 나눠 칸마다 "가장 아래 지점 높이"를 잰 옆모습.
        ///  - 총열선(총구 높이)보다 한참 아래로 튀어나온 칸 = 손잡이·탄창·개머리판 아래쪽
        ///  - 뒤 끝에 붙어 있는 튀어나옴은 개머리판으로 보고 건너뛰고, 그다음 처음 튀어나온 덩어리를 손잡이로 본다
        ///  (높이·위치는 모두 0~1 비율: 길이 0 = 뒤 끝, 1 = 총구 / 높이 0 = 바닥, 1 = 꼭대기)
        /// </summary>
        private sealed class GunProfile
        {
            private const int Bins = 24;
            private readonly float[] lowest = new float[Bins];
            private readonly float barrelHeight;

            public float GripAlong { get; } = RightGripAlong;
            public float GripHeight { get; } = RightGripHeight;

            public GunProfile(List<Vector3> points, Transform weapon, int barrelAxis, float barrelSign, int upAxis, float upSign,
                              Vector3 min, Vector3 max, float muzzleHeight)
            {
                barrelHeight = muzzleHeight;
                for (int i = 0; i < Bins; i++) lowest[i] = float.NaN;
                foreach (var p in points)
                {
                    float along = Mathf.InverseLerp(min[barrelAxis], max[barrelAxis], p[barrelAxis]);
                    if (barrelSign < 0) along = 1f - along;
                    float height = Mathf.InverseLerp(min[upAxis], max[upAxis], p[upAxis]);
                    if (upSign < 0) height = 1f - height;
                    int bin = Mathf.Clamp((int)(along * Bins), 0, Bins - 1);
                    if (float.IsNaN(lowest[bin]) || height < lowest[bin]) lowest[bin] = height;
                }
                for (int i = 0; i < Bins; i++) if (float.IsNaN(lowest[i])) lowest[i] = barrelHeight; // 빈 칸

                // 총열선과 바닥의 중간보다 낮으면 "아래로 튀어나왔다"
                float threshold = barrelHeight * 0.5f;
                var runs = new List<(int start, int end)>();
                for (int i = 0; i < Bins; i++)
                {
                    if (lowest[i] >= threshold) continue;
                    int start = i;
                    while (i + 1 < Bins && lowest[i + 1] < threshold) i++;
                    runs.Add((start, i));
                }
                if (runs.Count == 0) return; // 아래로 튀어나온 곳이 없음 → 기본 비율

                // 뒤 끝에 붙은 덩어리는 개머리판 (뒤에 다른 덩어리가 있을 때만)
                var grip = runs[0];
                if (grip.start == 0 && runs.Count > 1) grip = runs[1];
                // 덩어리가 길면(손잡이+탄창이 붙은 경우) 뒤쪽 3칸만 손잡이로
                int gripEnd = Mathf.Min(grip.end, grip.start + 2);

                float lowestInGrip = 1f;
                for (int i = grip.start; i <= gripEnd; i++) lowestInGrip = Mathf.Min(lowestInGrip, lowest[i]);

                GripAlong = (grip.start + gripEnd + 1) * 0.5f / Bins;
                // 손바닥은 손잡이 윗부분(총 몸통 바로 아래)을 쥔다
                GripHeight = Mathf.Lerp(lowestInGrip, barrelHeight, 0.6f);
            }

            /// <summary>왼손이 받칠 높이: 그 위치의 총 아랫면 바로 아래 (탄창처럼 깊이 튀어나온 곳이면 총열선 절반 높이)</summary>
            public float SupportHeight(float along)
            {
                int bin = Mathf.Clamp((int)(along * Bins), 0, Bins - 1);
                return Mathf.Max(lowest[bin], barrelHeight * 0.5f) - 0.03f;
            }
        }

        // ───────────────────────── 조준 자세 ─────────────────────────

        /// <summary>무기 종류의 조준 대기 클립(IdleAim…)을 모델에 한 프레임 적용. 손이 실제로 움직였으면 true</summary>
        internal static bool SampleAimPose(Animator animator, int weaponType)
        {
            string suffix = PlayerAnimParams.ClipSuffix(weaponType == 0 ? 1 : weaponType);
            var clip = PlayerAnimatorBuilder.FindClip("IdleAim" + suffix, "IdleAimAssaultRifle");
            Transform hand = PlayerPrefabSetup.FindBone(animator, HumanBodyBones.RightHand);
            if (clip == null || hand == null) return false;

            Vector3 before = hand.position;
            Quaternion beforeRot = hand.rotation;
            clip.SampleAnimation(animator.gameObject, clip.length * 0.25f);
            if (Moved(hand, before, beforeRot)) return true;

            // 일부 버전은 휴머노이드 클립을 SampleAnimation 으로 적용하지 못한다 → 애니메이션 모드로 한 번 더
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(animator.gameObject, clip, clip.length * 0.25f);
                AnimationMode.EndSampling();
                // 애니메이션 모드는 끝날 때 뼈를 되돌리므로, 그 전에 결과 자세를 복사해 둔다
                if (Moved(hand, before, beforeRot) == false) return false;
                var sampled = new PoseSnapshot(animator.transform);
                AnimationMode.StopAnimationMode();
                sampled.Restore();
                return true;
            }
            finally
            {
                if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
            }
        }

        private static bool Moved(Transform t, Vector3 position, Quaternion rotation)
            => (t.position - position).sqrMagnitude > 0.0001f || Quaternion.Angle(t.rotation, rotation) > 1f;

        /// <summary>
        /// 손바닥 중심과 손 방향을 뼈 위치로 계산 (모델마다 다른 뼈 축과 무관).
        /// length = 손목 → 가운데 손가락, thumb = 엄지 쪽, palm = 손바닥이 향하는 쪽
        /// </summary>
        private static bool PalmFrame(Animator animator, bool right, out Vector3 palm, out Vector3 length, out Vector3 thumbDir)
        {
            Transform hand = PlayerPrefabSetup.FindBone(animator, right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform lowerArm = PlayerPrefabSetup.FindBone(animator, right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            Transform middle = PlayerPrefabSetup.FindBone(animator, right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            Transform thumb = PlayerPrefabSetup.FindBone(animator, right ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            palm = Vector3.zero; length = Vector3.forward; thumbDir = Vector3.up;
            if (hand == null || lowerArm == null) return false;

            length = middle != null ? (middle.position - hand.position).normalized : (hand.position - lowerArm.position).normalized;
            thumbDir = thumb != null ? thumb.position - hand.position : animator.transform.forward;
            thumbDir = Vector3.ProjectOnPlane(thumbDir, length).normalized;
            if (thumbDir.sqrMagnitude < 0.001f) thumbDir = Vector3.ProjectOnPlane(Vector3.up, length).normalized;
            // 오른손: 길이 × 엄지 = 손바닥 방향, 왼손은 거울이라 순서가 반대
            Vector3 palmNormal = (right ? Vector3.Cross(length, thumbDir) : Vector3.Cross(thumbDir, length)).normalized;
            palm = hand.position + length * 0.07f + palmNormal * 0.02f;
            return true;
        }

        /// <summary>총 로컬 점이 총열 방향으로 어디쯤인지 (0 = 뒤 끝, 1 = 총구 끝)</summary>
        private static float AlongRatio(Vector3 local, int barrelAxis, float barrelSign, Vector3 min, Vector3 max)
        {
            float t = Mathf.InverseLerp(min[barrelAxis], max[barrelAxis], local[barrelAxis]);
            return barrelSign > 0 ? t : 1f - t;
        }

        /// <summary>모델 아래 모든 뼈의 로컬 위치·회전을 저장했다가 되돌린다 (자세 샘플링이 프리팹에 남지 않게)</summary>
        internal sealed class PoseSnapshot
        {
            private readonly List<(Transform t, Vector3 p, Quaternion r, Vector3 s)> items = new List<(Transform, Vector3, Quaternion, Vector3)>();

            public PoseSnapshot(Transform root)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.GetComponent<WeaponItem>() != null || t.GetComponentInParent<WeaponItem>() != null) continue; // 총은 저장 대상 아님
                    items.Add((t, t.localPosition, t.localRotation, t.localScale));
                }
            }

            public void Restore()
            {
                foreach (var (t, p, r, s) in items)
                {
                    if (t == null) continue;
                    t.localPosition = p;
                    t.localRotation = r;
                    t.localScale = s;
                }
            }
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        private static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>
        /// 총 로컬 공간의 정점들로 크기와 방향을 잰다.
        ///  - 가장 긴 축 = 총열 축, 두 번째로 긴 축 = 위아래 축
        ///  - 앞뒤: 양 끝 12% 구간의 위아래 두께를 비교해 더 가는 쪽이 총구
        ///  - 위아래: 총구 높이(총열선)에서 위쪽 끝보다 아래쪽 끝(손잡이·탄창)이 더 멀면 그쪽이 아래
        /// </summary>
        private static bool TryMeasure(Transform weapon, out Vector3 min, out Vector3 max,
                                       out int barrelAxis, out float barrelSign, out int upAxis, out float upSign)
        {
            var points = CollectVertices(weapon);
            min = Vector3.one * float.MaxValue;
            max = Vector3.one * float.MinValue;
            barrelAxis = 2; barrelSign = 1f; upAxis = 1; upSign = 1f;
            if (points.Count == 0) return false;

            foreach (var p in points) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            Vector3 size = max - min;

            int[] order = { 0, 1, 2 };
            System.Array.Sort(order, (a, b) => size[b].CompareTo(size[a]));
            barrelAxis = order[0];
            upAxis = order[1];

            float len = size[barrelAxis];
            float lowEnd = min[barrelAxis] + len * 0.12f, highEnd = max[barrelAxis] - len * 0.12f;
            float lowMin = float.MaxValue, lowMax = float.MinValue, highMin = float.MaxValue, highMax = float.MinValue;
            foreach (var p in points)
            {
                float u = p[upAxis];
                if (p[barrelAxis] <= lowEnd) { lowMin = Mathf.Min(lowMin, u); lowMax = Mathf.Max(lowMax, u); }
                else if (p[barrelAxis] >= highEnd) { highMin = Mathf.Min(highMin, u); highMax = Mathf.Max(highMax, u); }
            }
            float lowThickness = lowMax - lowMin, highThickness = highMax - highMin;
            barrelSign = highThickness <= lowThickness ? 1f : -1f;

            float barrelLine = barrelSign > 0 ? (highMin + highMax) * 0.5f : (lowMin + lowMax) * 0.5f;
            upSign = (max[upAxis] - barrelLine) <= (barrelLine - min[upAxis]) ? 1f : -1f;
            return true;
        }

        private static float MuzzleHeight(Transform weapon, int barrelAxis, float barrelSign, int upAxis, float upSign, Vector3 min, Vector3 max)
        {
            var points = CollectVertices(weapon);
            float len = max[barrelAxis] - min[barrelAxis];
            float edge = barrelSign > 0 ? max[barrelAxis] - len * 0.05f : min[barrelAxis] + len * 0.05f;
            float sum = 0f; int n = 0;
            foreach (var p in points)
                if (barrelSign > 0 ? p[barrelAxis] >= edge : p[barrelAxis] <= edge) { sum += p[upAxis]; n++; }
            if (n == 0) return 0.7f;
            float t = Mathf.InverseLerp(min[upAxis], max[upAxis], sum / n);
            return upSign > 0 ? t : 1f - t;
        }

        private static List<Vector3> CollectVertices(Transform weapon)
        {
            var list = new List<Vector3>();
            foreach (var filter in weapon.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || renderer.enabled == false || filter.sharedMesh == null) continue; // 충돌용(UCX) 메시 제외
                if (filter.name.StartsWith("UCX_")) continue;
                Vector3[] verts;
                try { verts = filter.sharedMesh.vertices; }
                catch { continue; }
                Matrix4x4 toWeapon = weapon.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                // 정점이 많으면 건너뛰며 샘플링
                int step = Mathf.Max(1, verts.Length / 4000);
                for (int i = 0; i < verts.Length; i += step) list.Add(toWeapon.MultiplyPoint3x4(verts[i]));
            }
            return list;
        }
    }
}
