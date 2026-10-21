using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// Animator 파라미터 · 상태 태그 · 클립 이름 규칙을 한 곳에 모은 표.
    /// 런타임(PlayerAnimator)과 컨트롤러 생성기(Editor/PlayerAnimatorBuilder)가 같은 이름을 쓰도록 둘 다 여기만 본다.
    /// 이름을 바꾸면 "Tools > Player > 2. Build Animator Controller" 로 컨트롤러를 다시 만들 것.
    /// </summary>
    public static class PlayerAnimParams
    {
        // ── float ──
        /// <summary>몸 기준 좌우 속도(m/s). 블렌드 트리 X</summary>
        public const string MoveX = "MoveX";
        /// <summary>몸 기준 앞뒤 속도(m/s). 블렌드 트리 Y</summary>
        public const string MoveY = "MoveY";
        /// <summary>0 = 총을 내린 자세, 1 = 조준 자세</summary>
        public const string AimBlend = "AimBlend";
        public const string VerticalSpeed = "VerticalSpeed";
        /// <summary>구르기 방향(몸 기준, -1~1)</summary>
        public const string RollX = "RollX";
        public const string RollY = "RollY";
        /// <summary>구르기 애니 재생 배속 (모터의 구르기 시간에 맞춤)</summary>
        public const string RollSpeed = "RollSpeed";
        /// <summary>맞은 방향(몸 기준, 공격이 날아온 쪽)</summary>
        public const string HitX = "HitX";
        public const string HitY = "HitY";
        /// <summary>재장전·투척·회복·꺼내기 애니 재생 배속 (무기 재장전 시간 등에 맞춤)</summary>
        public const string ActionSpeed = "ActionSpeed";
        /// <summary>이동 애니 재생 배속. 클립보다 빨리 달리면 1 보다 커져 발이 미끄러지지 않게 한다</summary>
        public const string MoveAnimSpeed = "MoveAnimSpeed";

        // ── int ──
        /// <summary>0 맨손 · 1 라이플 · 2 샷건 · 3 로켓런처 · 4 미니건 (WeaponType 열거형과 같은 값)</summary>
        public const string WeaponType = "WeaponType";
        /// <summary>0 넘기 · 1 낮은 벽 오르기 · 2 1m 오르기 · 3 2m 오르기</summary>
        public const string VaultType = "VaultType";
        /// <summary>0 앞 · 1 뒤 · 2 왼쪽 · 3 오른쪽 (맞은 방향)</summary>
        public const string DeathDirection = "DeathDirection";

        // ── bool ──
        public const string Grounded = "Grounded";
        public const string FreeFall = "FreeFall";
        public const string Sprinting = "Sprinting";
        /// <summary>연사 무기(라이플·미니건) 사격 중</summary>
        public const string Firing = "Firing";
        public const string Dead = "Dead";

        // ── trigger ──
        public const string Jump = "Jump";
        public const string Roll = "Roll";
        /// <summary>단발 무기(샷건·로켓) 한 발</summary>
        public const string Fire = "Fire";
        public const string Reload = "Reload";
        public const string Draw = "Draw";
        public const string Throw = "Throw";
        public const string Heal = "Heal";
        public const string Hit = "Hit";
        public const string Die = "Die";
        public const string Vault = "Vault";

        // ── 상태 태그 (IK 를 켤지 판단) ──
        /// <summary>손 IK · 조준 IK 모두 끔 (재장전·투척·회복·꺼내기·벽넘기·사망)</summary>
        public const string TagNoIK = "NoIK";
        /// <summary>조준 IK 만 끔 (달리기·구르기). 왼손은 계속 총을 잡는다</summary>
        public const string TagNoAim = "NoAim";
        /// <summary>벽넘기 상태 (PlayerVault 가 끝났는지 확인)</summary>
        public const string TagVault = "Vault";

        // ── 레이어 ──
        public const string BaseLayer = "Base Layer";
        public const string UpperBodyLayer = "Upper Body";
        public const string HitLayer = "Hit Reaction";

        // ── 해시 (런타임용) ──
        public static readonly int MoveXId = Animator.StringToHash(MoveX);
        public static readonly int MoveYId = Animator.StringToHash(MoveY);
        public static readonly int AimBlendId = Animator.StringToHash(AimBlend);
        public static readonly int VerticalSpeedId = Animator.StringToHash(VerticalSpeed);
        public static readonly int RollXId = Animator.StringToHash(RollX);
        public static readonly int RollYId = Animator.StringToHash(RollY);
        public static readonly int RollSpeedId = Animator.StringToHash(RollSpeed);
        public static readonly int HitXId = Animator.StringToHash(HitX);
        public static readonly int HitYId = Animator.StringToHash(HitY);
        public static readonly int ActionSpeedId = Animator.StringToHash(ActionSpeed);
        public static readonly int MoveAnimSpeedId = Animator.StringToHash(MoveAnimSpeed);
        public static readonly int WeaponTypeId = Animator.StringToHash(WeaponType);
        public static readonly int VaultTypeId = Animator.StringToHash(VaultType);
        public static readonly int DeathDirectionId = Animator.StringToHash(DeathDirection);
        public static readonly int GroundedId = Animator.StringToHash(Grounded);
        public static readonly int FreeFallId = Animator.StringToHash(FreeFall);
        public static readonly int SprintingId = Animator.StringToHash(Sprinting);
        public static readonly int FiringId = Animator.StringToHash(Firing);
        public static readonly int DeadId = Animator.StringToHash(Dead);
        public static readonly int JumpId = Animator.StringToHash(Jump);
        public static readonly int RollId = Animator.StringToHash(Roll);
        public static readonly int FireId = Animator.StringToHash(Fire);
        public static readonly int ReloadId = Animator.StringToHash(Reload);
        public static readonly int DrawId = Animator.StringToHash(Draw);
        public static readonly int ThrowId = Animator.StringToHash(Throw);
        public static readonly int HealId = Animator.StringToHash(Heal);
        public static readonly int HitId = Animator.StringToHash(Hit);
        public static readonly int DieId = Animator.StringToHash(Die);
        public static readonly int VaultId = Animator.StringToHash(Vault);

        // ───────────────────────── 클립 이름 규칙 ─────────────────────────
        // PROTOFACTOR 팩의 클립 이름은 "동작 + 무기 접미사" 형태다. 예) ReloadAssaultRifle, DodgeLeftShotgun

        public const int WeaponTypeCount = 5;

        /// <summary>무기 번호 → 클립 이름 끝에 붙는 접미사</summary>
        public static string ClipSuffix(int weaponType)
        {
            switch (weaponType)
            {
                case 1: return "AssaultRifle";
                case 2: return "Shotgun";
                case 3: return "Bazooka";
                case 4: return "Minigun";
                default: return "Unarmed";
            }
        }

        /// <summary>상태 머신 이름에 쓰는 무기 이름</summary>
        public static string WeaponName(int weaponType)
        {
            switch (weaponType)
            {
                case 1: return "Rifle";
                case 2: return "Shotgun";
                case 3: return "RocketLauncher";
                case 4: return "Minigun";
                default: return "Unarmed";
            }
        }

        /// <summary>연사 무기는 사격 상태를 "누르는 동안 반복", 단발 무기는 "한 번 재생"</summary>
        public static bool IsLoopFireWeapon(int weaponType) => weaponType == 1 || weaponType == 4;

        // 맨손에는 전용 클립이 없는 동작이 있어 라이플 클립으로 대신한다
        private static string ArmedSuffix(int weaponType) => weaponType == 0 ? ClipSuffix(1) : ClipSuffix(weaponType);

        public static string ReloadClip(int w) => "Reload" + ArmedSuffix(w);
        public static string DrawClip(int w) => "Draw" + ArmedSuffix(w);
        public static string ThrowClip(int w) => w == 0 ? "ThrowGrenadeUnarmed1" : "ThrowGrenade1Standing" + ClipSuffix(w);
        /// <summary>회복: 팔을 드는 동작 → 들고 있는 동작 (주사기 전용 클립이 없어 수류탄 조준 동작으로 대체)</summary>
        public static string HealRaiseClip(int w) => "GoToAimGrenade" + ClipSuffix(w);
        public static string HealHoldClip(int w) => "IdleAimGrenade" + ClipSuffix(w);
        public static string DodgeClip(int w, string dir) => "Dodge" + dir + ArmedSuffix(w);
        public static string ShootClip(int w) => w == 1 ? "ShootPrimaryAssaultRifle" : "Shoot" + ArmedSuffix(w);
        public static string HitClip(int w, string dir) => "GetHit" + dir + "Light" + ArmedSuffix(w);
        public static string DeathClip(int w, string dir) => "Death" + dir + ArmedSuffix(w);

        /// <summary>벽넘기 종류별 루트모션 클립 (이동은 애니메이션이 직접 한다)</summary>
        public static string VaultClip(int w, int vaultType)
        {
            string s = w == 0 ? "Unarmed" : ClipSuffix(w);
            switch (vaultType)
            {
                case 0: return "Pass1MeterObstacleLeft" + s + "_RM";
                case 1: return "ClimbUpHalfMeterObstacleLeft" + s + "_RM";
                case 2: return "ClimbUp1MeterObstacle" + s + "_RM";
                default: return "ClimbUp2MetersObstacle" + s + "_RM";
            }
        }

        /// <summary>벽넘기 클립이 기준으로 삼는 장애물 높이(m). 실제 높이와 비율을 맞춰 위아래 이동을 늘이거나 줄인다</summary>
        public static float VaultClipHeight(int vaultType)
        {
            switch (vaultType)
            {
                case 0: return 1f;
                case 1: return 0.5f;
                case 2: return 1f;
                default: return 2f;
            }
        }

        public static readonly string[] DirectionNames = { "Front", "Back", "Left", "Right" };

        /// <summary>회복 "들고 있는 동작" 클립 중 이만큼(비율)만 재생하고 끝낸다</summary>
        public const float HealHoldPortion = 0.6f;
        /// <summary>재장전·투척·꺼내기 상태가 끝나는 지점(클립 비율). 끝부분에서 다음 동작으로 섞인다</summary>
        public const float ActionExitTime = 0.9f;
    }
}
