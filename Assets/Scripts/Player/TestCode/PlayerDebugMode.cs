using HybridShooter.Core.Contracts;
using HybridShooter.Weapon;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HybridShooter.Player.Tester
{
    /// <summary>
    /// 테스트용 디버그 모드. 플레이어 프리팹에 달려 있고, 인스펙터에서 켜고 끈다 (Play 중에도 바로 반영).
    ///  - 무적 / 무한 스태미나 / 무한 탄약 / 목숨 무한 / 최종 사망 막기
    ///  - Play 중 인스펙터 버튼(또는 컴포넌트 ⋮ 메뉴): 부활(연출)·즉시 부활, 체력·스태미나·탄약 채우기, 예비 탄창 0/+1, 자해, 목숨 API
    /// 제거할 때는 이 파일과 프리팹의 컴포넌트만 지우면 된다
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDebugMode : MonoBehaviour
    {
        [Header("상시")]
        [SerializeField] private bool invincible;
        [SerializeField] private bool infiniteStamina;
        [Tooltip("모든 슬롯의 탄창과 예비 탄창을 항상 가득 채운다 (재장전 없음)")]
        [SerializeField] private bool infiniteAmmo;

        [Header("사망")]
        [Tooltip("죽으면 목숨을 쓰지 않고 항상 자동 부활 (PlayerController 의 목숨 자동 부활 경로)")]
        [SerializeField] private bool infiniteLives;
        [Tooltip("마지막 목숨으로 쓰러져도 최종 사망(PlayerFinalDeathEvent → 게임오버)으로 넘어가지 않는다")]
        [SerializeField] private bool holdFinalDeath;

        [Header("버튼")]
        [SerializeField, Min(1)] private int selfDamageAmount = 20;
        [Tooltip("[최대 목숨 설정] 버튼이 SetMaxLives 에 넘기는 값 (난이도 흉내)")]
        [SerializeField, Min(1)] private int maxLivesToSet = 5;
        [Tooltip("켜져 있는 디버그 기능을 화면 오른쪽 위에 표시 (켜 둔 걸 잊지 않게)")]
        [SerializeField] private bool showStatus = true;

        private PlayerController player;
        private GunSlot gunSlot;
        private GUIStyle style;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            gunSlot = GetComponent<GunSlot>();
        }

        private void OnDisable()
        {
            if (player == null) return;
            player.DebugInvincible = false;
            player.DebugHoldFinalDeath = false;
            player.DebugInfiniteLives = false;
        }

        private void Update()
        {
            if (player == null) return;
            player.DebugInvincible = invincible;
            player.DebugInfiniteLives = infiniteLives;
            player.DebugHoldFinalDeath = holdFinalDeath;
            if (infiniteStamina && player.CurrentStamina < player.MaxStamina) player.RestoreStamina();
            if (infiniteAmmo) RefillAmmo();
        }

        /// <summary>부활 연출과 함께 (목숨을 쓰지 않는 외부 부활)</summary>
        [ContextMenu("부활")]
        public void Revive()
        {
            if (player != null) player.Revive();
        }

        /// <summary>연출 없이 바로 (목숨을 쓰지 않는 외부 부활)</summary>
        [ContextMenu("즉시 부활")]
        public void ReviveImmediate()
        {
            if (player != null) player.Revive(0f);
        }

        [ContextMenu("체력 가득")]
        public void FillHealth()
        {
            if (player != null) player.Heal(player.MaxHealth);
        }

        [ContextMenu("스태미나 가득")]
        public void FillStamina()
        {
            if (player != null) player.RestoreStamina();
        }

        /// <summary>모든 슬롯의 탄창 + 예비 탄창을 가득</summary>
        [ContextMenu("탄약 가득")]
        public void RefillAmmo()
        {
            if (gunSlot == null) return;
            for (int i = 0; i < GunSlot.SlotCount; i++)
                if (gunSlot.GetGun(i) != null) gunSlot.GetGun(i).RefillAmmo();
        }

        /// <summary>지금 든 총의 예비 탄창을 0 으로 (탄이 떨어지는 상황 테스트)</summary>
        [ContextMenu("예비 탄창 0")]
        public void EmptySpareMagazines()
        {
            if (gunSlot != null && gunSlot.CurrentGun != null) gunSlot.CurrentGun.SetSpareMagazines(0);
        }

        /// <summary>IPlayerAmmo.AddMagazines(1) — 보급 획득 흉내</summary>
        [ContextMenu("예비 탄창 +1 (AddMagazines)")]
        public void AddSpareMagazine()
        {
            if (gunSlot != null) Debug.Log($"[PlayerDebugMode] AddMagazines(1) → {gunSlot.AddMagazines(1)}", this);
        }

        /// <summary>정면에서 맞은 것처럼 피해를 준다 (무적이면 무시된다)</summary>
        [ContextMenu("자해")]
        public void DamageSelf()
        {
            if (player == null) return;
            player.TakeDamage(new DamageInfo(selfDamageAmount, DamageKind.MonsterAttack, Faction.Neutral, null,
                                             transform.position, -transform.forward));
        }

        [ContextMenu("목숨 +1 (AddLives)")]
        public void AddLife()
        {
            if (player != null) Debug.Log($"[PlayerDebugMode] AddLives(1) → {player.AddLives(1)}", this);
        }

        [ContextMenu("최대 목숨 +1 (AddMaxLives, 스테이지 클리어)")]
        public void AddMaxLife()
        {
            if (player != null) player.AddMaxLives(1, true);
        }

        [ContextMenu("최대 목숨 설정 (SetMaxLives, 난이도)")]
        public void SetMaxLives()
        {
            if (player != null) player.SetMaxLives(maxLivesToSet, true);
        }

        private void OnGUI()
        {
            if (showStatus == false || player == null) return;
            string text = "DEBUG";
            if (invincible) text += " · 무적";
            if (infiniteStamina) text += " · 무한 스태미나";
            if (infiniteAmmo) text += " · 무한 탄약";
            if (infiniteLives) text += " · 목숨 무한";
            if (holdFinalDeath) text += " · 최종 사망 막음";
            if (text == "DEBUG") return;

            style ??= new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperRight, normal = { textColor = Color.yellow } };
            GUI.Label(new Rect(Screen.width - 410f, 10f, 400f, 24f), text, style);
        }

#if UNITY_EDITOR
        [CustomEditor(typeof(PlayerDebugMode))]
        private sealed class PlayerDebugModeEditor : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();

                var mode = (PlayerDebugMode)target;
                EditorGUILayout.Space();
                using (new EditorGUI.DisabledScope(Application.isPlaying == false))
                {
                    EditorGUILayout.LabelField("Play 중에만", EditorStyles.miniLabel);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("부활")) mode.Revive();
                        if (GUILayout.Button("즉시 부활")) mode.ReviveImmediate();
                        if (GUILayout.Button($"자해 {mode.selfDamageAmount}")) mode.DamageSelf();
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("체력 가득")) mode.FillHealth();
                        if (GUILayout.Button("스태미나 가득")) mode.FillStamina();
                        if (GUILayout.Button("탄약 가득")) mode.RefillAmmo();
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("예비 탄창 0")) mode.EmptySpareMagazines();
                        if (GUILayout.Button("예비 탄창 +1")) mode.AddSpareMagazine();
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("목숨 +1")) mode.AddLife();
                        if (GUILayout.Button("최대 목숨 +1")) mode.AddMaxLife();
                        if (GUILayout.Button($"최대 목숨 = {mode.maxLivesToSet}")) mode.SetMaxLives();
                    }
                }
                if (Application.isPlaying && mode.player != null)
                {
                    PlayerController p = mode.player;
                    EditorGUILayout.LabelField($"체력 {p.CurrentHealth} / {p.MaxHealth}   스태미나 {p.CurrentStamina:0} / {p.MaxStamina:0}   목숨 {p.Lives} / {p.MaxLives}");
                    string state = (p.IsReviving ? $"부활 중 {p.ReviveProgress:P0}   " : "") +
                                   (p.ReviveWindowRemaining > 0f ? $"최종 사망까지 {p.ReviveWindowRemaining:0.0}초   " : "") +
                                   (p.IsFinallyDead ? "최종 사망   " : "") +
                                   (p.IsReviveBuffActive ? $"보호 {p.ReviveBuffRemainingTime:0.0}초" : "");
                    if (state.Length > 0) EditorGUILayout.LabelField(state);
                    Gun gun = mode.gunSlot != null ? mode.gunSlot.CurrentGun : null;
                    if (gun != null)
                        EditorGUILayout.LabelField($"{gun.DisplayName}  탄창 {gun.Ammo} / {gun.MagazineSize}   예비 탄창 " +
                                                   (gun.InfiniteMagazines ? "무한" : $"{gun.SpareMagazines} / {gun.MaxMagazines}") +
                                                   (gun.IsReloading ? "   장전 중" : ""));
                }
            }

            public override bool RequiresConstantRepaint() => Application.isPlaying;
        }
#endif
    }
}
