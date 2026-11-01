using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 보스가 던진 알. 포물선으로 날아가 내려앉은 뒤 부화 시간이 지나면 잡몹이 나온다.
    /// 총·수류탄으로 체력을 깎거나 보스의 돌진·내려찍기에 휩쓸리면 깨진다. 부화가 가까울수록 빠르게 깜빡인다
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class TyranopodeEgg : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(0f)] private float arcHeight = 6f;
        [SerializeField] private GameObject breakVfx;
        [SerializeField] private GameObject hatchVfx;
        [Tooltip("부화 직전 커지는 비율")]
        [SerializeField, Min(0f)] private float pulseScale = 0.15f;

        private TyranopodeController owner;
        private Vector3 from;
        private Vector3 to;
        private float flightTime;
        private float launchTime;
        private float hatchTime;
        private int hp;
        private bool landed;
        private bool finished;
        private Vector3 baseScale;

        public Faction Faction => Faction.Monster;
        public bool IsAlive => finished == false;
        /// <summary>연출이 모두 연결됐는지 (TyranopodeController 가 생성 순간 검사)</summary>
        public bool HasReferences => breakVfx != null && hatchVfx != null;

        private void Awake() => baseScale = transform.localScale;

        public void Launch(TyranopodeController controller, Vector3 start, Vector3 target, float flight)
        {
            owner = controller;
            from = start;
            to = target;
            flightTime = Mathf.Max(0.05f, flight);
            launchTime = Time.time;
            hp = controller.Def.EggHp;
            transform.position = start;
            controller.RegisterEgg(this);
        }

        private void Update()
        {
            if (finished) return;
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }

            if (landed == false)
            {
                float t = Mathf.Clamp01((Time.time - launchTime) / flightTime);
                Vector3 position = Vector3.Lerp(from, to, t);
                position.y += arcHeight * 4f * t * (1f - t);
                transform.position = position;
                if (t < 1f) return;
                landed = true;
                hatchTime = Time.time + owner.Def.EggHatchTime;
            }

            float remain = hatchTime - Time.time;
            float speed = Mathf.Lerp(10f, 2f, Mathf.Clamp01(remain / owner.Def.EggHatchTime));
            transform.localScale = baseScale * (1f + pulseScale * Mathf.Abs(Mathf.Sin(Time.time * speed)));
            if (remain <= 0f) Hatch();
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (finished || this.CanBeHitBy(info.AttackerFaction) == false) return;
            hp -= info.Amount;
            bool broken = hp <= 0;
            EventBus<DamageDealtEvent>.Publish(new DamageDealtEvent(gameObject, info, broken));
            if (broken) Break();
        }

        /// <summary>깨진다 (잡몹 없음)</summary>
        public void Break()
        {
            if (finished) return;
            Finish(breakVfx);
        }

        private void Hatch()
        {
            owner.Minions.SpawnAround(owner.Def.EggMinionId, transform.position, owner.Def.EggMinionCount, owner.Target.position);
            Finish(hatchVfx);
        }

        private void Finish(GameObject vfx)
        {
            finished = true;
            owner.UnregisterEgg(this);
            Destroy(Instantiate(vfx, transform.position, Quaternion.identity), 4f);
            Destroy(gameObject);
        }
    }
}
