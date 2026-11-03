using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>
    /// 플레이어 몸 소리. PlayerController 와 같은 오브젝트에 붙인다 (플레이어 코드 수정 없이 기존 이벤트·속성만 사용).
    /// 플레이어 자신의 소리는 전부 2D → 3인칭에서 리스너(카메라)가 몸 뒤로 빠져도 크기가 같다.
    ///
    ///   발소리   이동 거리 누적, 보폭(걷기/달리기)마다 1회 — 애니메이션과 무관하게 1인칭에서도 일정
    ///   점프     PlayerController.Jumped
    ///   착지     PlayerController.Landed (minAirTime 보다 짧게 뜬 경우·스폰 직후는 무시)
    ///   피격     PlayerDamagedEvent — 지속 피해(DamageKind.Status, 독장판 틱)는 기침, 그 외는 피격 목소리. 그 피해로 쓰러졌으면 생략 (사망 목소리와 겹치지 않게)
    ///   사망     PlayerController.Died
    ///   숨소리   PlayerStaminaChangedEvent — breathStartRatio 이하에서 루프 시작, breathStopRatio 이상에서 정지
    ///   투척     PlayerItems.ThrowStarted
    ///   회복 팩  PlayerItems.HealStarted (사용 성공 때만, 개수 차감과 함께)
    ///   무기 교체 GunSlot.GunChanged → WeaponTable.SwitchSfxKey (시작 장착은 제외)
    /// 바닥 재질은 흙 하나만 (금속 발소리는 맵 확정 후 추가).
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class PlayerSoundEmitter : MonoBehaviour
    {
        [Header("SoundTable 키")]
        [SerializeField] private string footstepKey = "sfx_player_footstep";
        [SerializeField] private string jumpKey = "sfx_player_jump";
        [SerializeField] private string landKey = "sfx_player_land";
        [SerializeField] private string hitKey = "sfx_player_hit";
        [Tooltip("지속 피해(DamageKind.Status, 독장판 틱) 때 피격음 대신")]
        [SerializeField] private string statusHitKey = "sfx_player_cough";
        [SerializeField] private string deathKey = "sfx_player_death";
        [SerializeField] private string breathLoopKey = "sfx_player_breath_loop";
        [SerializeField] private string throwKey = "sfx_grenade_throw";
        [SerializeField] private string healUseKey = "sfx_player_heal_use";

        [Header("발소리")]
        [Tooltip("걷기 보폭(m). 이 거리를 움직일 때마다 발소리 1회")]
        [SerializeField] private float walkStride = 1.8f;
        [Tooltip("달리기 보폭(m)")]
        [SerializeField] private float sprintStride = 2.4f;
        [Tooltip("이 속도(m/s) 미만이면 멈춘 것으로 본다")]
        [SerializeField] private float minMoveSpeed = 0.5f;

        [Header("착지")]
        [Tooltip("이 시간(초)보다 짧게 떠 있었으면 착지음 없음 (턱·경사 흔들림)")]
        [SerializeField] private float minAirTime = 0.25f;

        [Header("무기 교체")]
        [Tooltip("켜진 직후 이 시간(초) 동안의 교체는 소리 없음 (시작 장착)")]
        [SerializeField] private float switchMuteAfterSpawn = 0.5f;

        [Header("숨소리 (스태미나 비율)")]
        [Range(0f, 1f)][SerializeField] private float breathStartRatio = 0.25f;
        [Range(0f, 1f)][SerializeField] private float breathStopRatio = 0.6f;

        private PlayerController player;
        private PlayerItems items;
        private GunSlot gunSlot;
        private float enableTime;
        private float strideDistance;
        private float lastGroundedTime;
        private SoundHandle breathHandle;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            items = GetComponentInChildren<PlayerItems>(true);
            gunSlot = GetComponentInChildren<GunSlot>(true);
        }

        /// <summary>이벤트 구독 (OnDisable 과 짝)</summary>
        private void OnEnable()
        {
            lastGroundedTime = Time.time; // 스폰 직후 첫 접지를 착지로 치지 않음
            strideDistance = 0f;
            player.Jumped += OnJumped;
            player.Landed += OnLanded;
            player.Died += OnDied;
            if (items != null) items.ThrowStarted += OnThrowStarted;
            if (items != null) items.HealStarted += OnHealStarted;
            if (gunSlot != null) gunSlot.GunChanged += OnGunChanged;
            enableTime = Time.time;
            EventBus<PlayerStaminaChangedEvent>.Subscribe(OnStaminaChanged);
            EventBus<PlayerDamagedEvent>.Subscribe(OnDamaged);
        }

        /// <summary>구독 해제 + 숨소리 정지</summary>
        private void OnDisable()
        {
            player.Jumped -= OnJumped;
            player.Landed -= OnLanded;
            player.Died -= OnDied;
            if (items != null) items.ThrowStarted -= OnThrowStarted;
            if (items != null) items.HealStarted -= OnHealStarted;
            if (gunSlot != null) gunSlot.GunChanged -= OnGunChanged;
            EventBus<PlayerStaminaChangedEvent>.Unsubscribe(OnStaminaChanged);
            EventBus<PlayerDamagedEvent>.Unsubscribe(OnDamaged);
            StopBreath();
        }

        /// <summary>지상 이동 거리를 보폭 단위로 세어 발소리</summary>
        private void Update()
        {
            if (player.IsGrounded) lastGroundedTime = Time.time;

            float speed = player.HorizontalSpeed;
            if (player.IsOnGround == false || player.IsRolling || speed < minMoveSpeed)
            {
                strideDistance = 0f;
                return;
            }

            float stride = player.IsSprinting ? sprintStride : walkStride;
            // 움직이기 시작하면 반 보폭 만에 첫 발소리 (출발 직후 한참 조용하지 않게)
            if (strideDistance <= 0f) strideDistance = stride * 0.5f;
            strideDistance += speed * Time.deltaTime;
            if (strideDistance < stride) return;

            strideDistance -= stride;
            Play(footstepKey);
        }

        private void OnJumped() => Play(jumpKey);

        private void OnLanded()
        {
            if (Time.time - lastGroundedTime < minAirTime) return;
            strideDistance = 0f; // 착지음이 첫 발소리를 대신함
            Play(landKey);
        }

        /// <summary>쓰러뜨린 피해면 생략 — 같은 프레임의 Died 가 사망 목소리를 낸다</summary>
        private void OnDamaged(PlayerDamagedEvent e)
        {
            if (player.IsAlive == false) return;
            Play(e.Info.Kind == DamageKind.Status ? statusHitKey : hitKey);
        }

        private void OnDied()
        {
            StopBreath();
            Play(deathKey);
        }

        private void OnThrowStarted() => Play(throwKey);

        private void OnHealStarted() => Play(healUseKey);

        /// <summary>바꿔 든 무기의 교체음 (2D). 시작 장착(LoadLoadout)은 제외</summary>
        private void OnGunChanged(int slot, Gun gun)
        {
            if (Time.time - enableTime < switchMuteAfterSpawn || gun == null || gun.Def?.SwitchSfx == null) return;
            if (ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(gun.Def.SwitchSfx);
        }

        private void OnStaminaChanged(PlayerStaminaChangedEvent e)
        {
            if (breathHandle.IsValid == false && e.Ratio <= breathStartRatio) StartBreath();
            else if (breathHandle.IsValid && e.Ratio >= breathStopRatio) StopBreath();
        }

        private void StartBreath()
        {
            if (ServiceLocator.TryGet<ISoundService>(out var service)) breathHandle = service.PlayLoop(breathLoopKey, transform);
        }

        private void StopBreath()
        {
            if (breathHandle.IsValid && ServiceLocator.TryGet<ISoundService>(out var service)) service.StopLoop(breathHandle);
            breathHandle = default;
        }

        /// <summary>2D 재생 (위치 없음)</summary>
        private static void Play(string key)
        {
            if (ServiceLocator.TryGet<ISoundService>(out var service)) service.PlaySfx(key);
        }
    }
}
