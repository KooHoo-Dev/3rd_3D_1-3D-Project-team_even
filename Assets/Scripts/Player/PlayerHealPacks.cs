using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Weapon;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 회복 팩(힐 주사기) 보유 개수와 사용. 보급 물자는 IHealPackReceiver 로 개수를 채운다.
    ///
    ///  TryUse ─→ (HP 가 가득이면 실패) 개수 -1, 회복 애니 시작, 왼손에 주사기 모형
    ///         ─→ applyTime 초 뒤 HP 회복 (주사를 놓는 순간)
    ///         ─→ useDuration 초 뒤 끝 (그동안 사격·재장전 불가, 피격되어도 계속 진행)
    ///
    /// 회복 전용 애니메이션이 팩에 없어 "수류탄 조준" 동작(왼팔을 들어 올리는 모션)으로 대신한다.
    /// 전용 클립이 생기면 PlayerAnimParams.HealRaiseClip/HealHoldClip 이름만 바꾸고 컨트롤러를 다시 만들면 된다.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth), typeof(PlayerAnimator))]
    public class PlayerHealPacks : MonoBehaviour, IHealPackReceiver
    {
        [Header("개수")]
        [SerializeField] private int startHealPacks = 1;
        [SerializeField] private int maxHealPacks = 3;

        [Header("회복")]
        [SerializeField] private int healAmount = 40;
        [SerializeField] private float useDuration = 1.6f;
        [Tooltip("사용 시작 후 실제로 HP 가 차는 시간(초)")]
        [SerializeField] private float applyTime = 1.1f;

        [Header("손에 쥐는 주사기")]
        [Tooltip("SciFiMedInjector01_1 등. 비워 두면 모형 없이 회복만")]
        [SerializeField] private GameObject injectorPrefab;
        [SerializeField] private Vector3 propLocalPosition = new Vector3(0f, 0.03f, 0.06f);
        [SerializeField] private Vector3 propLocalEuler = new Vector3(0f, 0f, 90f);

        private PlayerHealth health;
        private PlayerAnimator anim;
        private int healPacks;
        private bool isUsing;
        private bool applied;
        private float startTime;
        private GameObject heldProp;

        public int HealPacks => healPacks;
        public int MaxHealPacks => maxHealPacks;
        public bool IsUsing => isUsing;

        private void Awake()
        {
            health = GetComponent<PlayerHealth>();
            anim = GetComponent<PlayerAnimator>();
            healPacks = Mathf.Clamp(startHealPacks, 0, maxHealPacks);
        }

        private void Start() => PublishCount();

        public bool TryUse()
        {
            if (isUsing || healPacks <= 0 || health.IsAlive == false || health.IsFull) return false;

            healPacks--;
            PublishCount();
            isUsing = true;
            applied = false;
            startTime = Time.time;
            anim.PlayHeal(useDuration);

            Transform hand = anim.HasAnimator && anim.Animator.isHuman ? anim.Animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            heldProp = HandProp.Attach(injectorPrefab, hand, propLocalPosition, propLocalEuler);
            return true;
        }

        private void Update()
        {
            if (isUsing == false) return;
            float t = Time.time - startTime;
            if (applied == false && t >= applyTime)
            {
                applied = true;
                health.Heal(healAmount);
            }
            if (t >= useDuration) Finish();
        }

        /// <summary>사망 등으로 중단. 이미 쓴 개수는 돌려주지 않는다</summary>
        public void Cancel() => Finish();

        private void Finish()
        {
            isUsing = false;
            HandProp.Release(ref heldProp);
        }

        public bool AddHealPacks(int amount)
        {
            if (amount <= 0 || healPacks >= maxHealPacks) return false;
            healPacks = Mathf.Min(maxHealPacks, healPacks + amount);
            PublishCount();
            return true;
        }

        public void ResetCount()
        {
            Cancel();
            healPacks = Mathf.Clamp(startHealPacks, 0, maxHealPacks);
            PublishCount();
        }

        private void PublishCount() => EventBus<HealPackCountChangedEvent>.Publish(new HealPackCountChangedEvent(healPacks, maxHealPacks));
    }
}
