using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 수류탄 보유 개수와 던지기. 보급 물자는 IGrenadeReceiver 로 개수를 채운다.
    ///
    ///  TryThrow ─→ 개수 -1, 던지기 애니 시작, 손에 수류탄 모형을 쥐어 줌
    ///           ─→ releaseTime 초 뒤(팔을 휘두르는 순간) 모형을 지우고 진짜 수류탄(ThrownGrenade)을 조준 방향으로 날림
    ///           ─→ throwDuration 초 뒤 던지기 끝 (그동안 사격·재장전 불가)
    ///
    /// 능력치는 GrenadeTable(PlayerTable.GrenadeId), 개수는 PlayerTable.StartGrenades/MaxGrenades + 강화(GrenadeCapacity).
    /// </summary>
    [RequireComponent(typeof(PlayerAnimator))]
    public class PlayerGrenadeThrower : MonoBehaviour, IGrenadeReceiver
    {
        [Header("프리팹")]
        [Tooltip("GrenadeTable 의 프리팹이 없을 때 쓸 수류탄 (SciFiGrenade01_1 등)")]
        [SerializeField] private GameObject grenadePrefab;
        [SerializeField] private GameObject explosionEffect;

        [Header("던지기 타이밍")]
        [SerializeField] private float throwDuration = 1.2f;
        [Tooltip("던지기 시작 후 손을 떠나는 시간(초). 애니메이션의 팔 휘두르는 순간에 맞춘다")]
        [SerializeField] private float releaseTime = 0.55f;
        [Tooltip("조준 방향보다 이만큼(도) 위로 던진다 (포물선)")]
        [SerializeField] private float upwardAngle = 12f;
        [Tooltip("던지는 손. 라이플 투척 애니메이션은 왼손으로 던진다")]
        [SerializeField] private bool leftHand = true;
        [SerializeField] private Vector3 propLocalPosition = new Vector3(0f, 0.04f, 0.05f);
        [SerializeField] private Vector3 propLocalEuler;

        [Header("테이블이 없을 때")]
        [SerializeField] private int fallbackDamage = 80;
        [SerializeField] private float fallbackRadius = 6f;
        [SerializeField] private float fallbackFuseTime = 2f;
        [SerializeField] private float fallbackThrowSpeed = 18f;
        [SerializeField] private int fallbackStartGrenades = 3;
        [SerializeField] private int fallbackMaxGrenades = 5;

        private PlayerAnimator anim;
        private GrenadeDef def;
        private int grenades;
        private int baseMax;
        private bool throwing;
        private bool released;
        private float throwStartTime;
        private Vector3 aimDirection = Vector3.forward;
        private GameObject heldProp;

        public int Grenades => grenades;
        public int MaxGrenades
        {
            get
            {
                int max = baseMax;
                if (ServiceLocator.TryGet<IUpgradeState>(out var upgrades))
                    max += Mathf.RoundToInt(upgrades.GetTotalValue(UpgradeType.GrenadeCapacity));
                return max;
            }
        }
        public bool IsThrowing => throwing;

        private void Awake()
        {
            anim = GetComponent<PlayerAnimator>();
            var player = DataManager.Player;
            def = player?.Grenade;
            baseMax = player != null ? player.MaxGrenades : fallbackMaxGrenades;
            grenades = player != null ? player.StartGrenades : fallbackStartGrenades;
        }

        private void Start() => PublishCount();

        /// <summary>매 프레임 조준 방향 갱신 (PlayerController). 손을 떠나는 순간의 방향으로 날아간다</summary>
        public void SetAimDirection(Vector3 direction)
        {
            if (direction.sqrMagnitude > 0.0001f) aimDirection = direction.normalized;
        }

        public bool TryThrow()
        {
            if (throwing || grenades <= 0) return false;

            grenades--;
            PublishCount();
            throwing = true;
            released = false;
            throwStartTime = Time.time;
            anim.PlayThrow(throwDuration);
            heldProp = HandProp.Attach(GrenadePrefab, Hand, propLocalPosition, propLocalEuler);
            return true;
        }

        private void Update()
        {
            if (throwing == false) return;
            float t = Time.time - throwStartTime;
            if (released == false && t >= releaseTime) Release();
            if (t >= throwDuration) throwing = false;
        }

        private void Release()
        {
            released = true;
            Transform hand = Hand;
            Vector3 spawn = heldProp != null ? heldProp.transform.position
                : hand != null ? hand.position : transform.position + Vector3.up * 1.5f;
            HandProp.Release(ref heldProp);

            var prefab = GrenadePrefab;
            if (prefab == null)
            {
                Debug.LogWarning("[PlayerGrenadeThrower] 수류탄 프리팹이 없습니다. grenadePrefab 을 연결하세요.", this);
                return;
            }

            // 조준 방향을 오른쪽 축 기준으로 위로 살짝 들어 포물선을 만든다
            Vector3 right = Vector3.Cross(Vector3.up, aimDirection);
            Vector3 dir = right.sqrMagnitude > 0.0001f ? Quaternion.AngleAxis(-upwardAngle, right.normalized) * aimDirection : aimDirection;
            float speed = def != null ? def.ThrowSpeed : fallbackThrowSpeed;

            var go = Instantiate(prefab, spawn, Quaternion.LookRotation(dir));
            var grenade = go.GetComponent<ThrownGrenade>();
            if (grenade == null) grenade = go.AddComponent<ThrownGrenade>();
            if (explosionEffect != null) grenade.SetExplosionEffect(explosionEffect);
            grenade.Throw(dir * speed,
                def != null ? def.Damage : fallbackDamage,
                def != null ? def.Radius : fallbackRadius,
                def != null ? def.FuseTime : fallbackFuseTime,
                def, gameObject);
        }

        /// <summary>던지는 도중 취소 (사망 등). 손에 든 모형만 지운다 — 이미 쓴 개수는 돌려주지 않는다</summary>
        public void Cancel()
        {
            throwing = false;
            HandProp.Release(ref heldProp);
        }

        public bool AddGrenades(int amount)
        {
            if (amount <= 0 || grenades >= MaxGrenades) return false;
            grenades = Mathf.Min(MaxGrenades, grenades + amount);
            PublishCount();
            return true;
        }

        public void ResetCount()
        {
            Cancel();
            var player = DataManager.Player;
            grenades = player != null ? player.StartGrenades : fallbackStartGrenades;
            PublishCount();
        }

        private GameObject GrenadePrefab => def != null && def.Prefab != null ? def.Prefab : grenadePrefab;

        private Transform Hand => anim.HasAnimator && anim.Animator.isHuman
            ? anim.Animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand)
            : null;

        private void PublishCount() => EventBus<GrenadeCountChangedEvent>.Publish(new GrenadeCountChangedEvent(grenades, MaxGrenades));
    }
}
