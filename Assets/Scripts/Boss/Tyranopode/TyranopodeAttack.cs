using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// Tyranopode 패턴 공통 부분 (BossAttack 위). 패턴 하나 = 컴포넌트 하나.
    ///  - 선택: TyranopodeController 가 거리 구간별 가중치(GetWeight)로 고른다. 강공격(heavy)은 난이도 보정으로 덜 고른다
    ///  - 예고 중에도 매 프레임 할 일은 WindupTick (컨트롤러가 부른다. BossAttack 은 예고 중 틱이 없다)
    ///  - 끝낼 때는 Finish (예고 표시 정리 + 회복 단계로)
    /// 수치는 TyranopodeTable, 애니메이션 타이밍(타격 진행도)은 각 패턴 인스펙터
    /// </summary>
    [RequireComponent(typeof(TyranopodeController))]
    public abstract class TyranopodeAttack : BossAttack
    {
        protected const float PlayerRadius = 0.5f;
        protected static readonly Color DangerColor = new Color(1f, 0.3f, 0.05f, 0.55f);
        /// <summary>맞는 순간 범위 번쩍임 색</summary>
        protected static readonly Color HitColor = new Color(1f, 0.85f, 0.45f, 0.85f);

        [Header("선택 가중치 (근거리 · 중거리 · 원거리)")]
        [SerializeField, Min(0f)] private float nearWeight;
        [SerializeField, Min(0f)] private float midWeight;
        [SerializeField, Min(0f)] private float farWeight;
        [Tooltip("강공격: 플레이어가 힘들수록 덜 고른다")]
        [SerializeField] private bool heavy;

        private readonly List<GroundTelegraph> telegraphs = new List<GroundTelegraph>();
        private float readyTime;

        protected TyranopodeController Owner { get; private set; }
        protected TyranopodeView View { get; private set; }
        protected TyranopodeLocomotion Move { get; private set; }
        protected TyranopodeDef Data { get; private set; }

        public bool IsHeavy => heavy;

        protected abstract float WindupTime { get; }
        protected abstract float RecoveryTime { get; }
        /// <summary>시작한 뒤 다시 고를 수 있을 때까지(초)</summary>
        protected virtual float CooldownTime => 0f;

        protected override void Awake()
        {
            base.Awake();
            Owner = GetComponent<TyranopodeController>();
            View = GetComponent<TyranopodeView>();
            Move = GetComponent<TyranopodeLocomotion>();
        }

        /// <summary>초기화·페이즈 전환 때 컨트롤러가 부른다 (예고 시간 배율 적용)</summary>
        public void Configure(TyranopodeDef def, IPlayerTarget target, float windupScale)
        {
            Data = def;
            InitializeAttack(target, WindupTime * windupScale, RecoveryTime);
        }

        /// <summary>이번에 고를 가중치. 0 이면 고르지 않는다</summary>
        public virtual float GetWeight(float distance)
        {
            if (Data == null || Time.time < readyTime) return 0f;
            float weight = distance < Data.MeleeRange ? nearWeight : distance < Data.FarRange ? midWeight : farWeight;
            return weight * WeightMultiplier();
        }

        /// <summary>부위 파괴 등으로 가중치를 바꿀 때</summary>
        protected virtual float WeightMultiplier() => 1f;

        protected bool IsReady => Time.time >= readyTime;

        protected sealed override void OnWindup()
        {
            readyTime = Time.time + CooldownTime;
            Move.Stop();
            StartWindup();
        }

        protected abstract void StartWindup();

        /// <summary>컨트롤러가 매 프레임 부른다</summary>
        public void TickWindup(float deltaTime)
        {
            if (Phase == SkillPhase.Windup) WindupTick(deltaTime);
        }

        protected virtual void WindupTick(float deltaTime) { }

        protected sealed override void OnCancelled()
        {
            HideTelegraphs();
            Stopped();
        }

        /// <summary>취소(그로기·페이즈 전환·사망) 때 정리</summary>
        protected virtual void Stopped() { }

        /// <summary>공격 끝 → 회복 단계 (대기 자세)</summary>
        protected void Finish()
        {
            HideTelegraphs();
            CompleteActive();
            Stopped();
            if (Boss.IsAlive) View.Play(TyranopodeView.Idle, 1f, 0.25f);
        }

        // ── 도구 ──

        protected static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        protected Vector3 TargetPosition => TargetRoot.position;
        protected Vector3 TargetChest => TargetRoot.position + Vector3.up * 1.2f;
        protected float DistanceToTarget => Flat(TargetRoot.position - transform.position).magnitude;

        protected Vector3 DirectionToTarget
        {
            get
            {
                Vector3 dir = Flat(TargetRoot.position - transform.position);
                return dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
            }
        }

        protected GroundTelegraph Telegraph()
        {
            var telegraph = Owner.RentTelegraph();
            telegraphs.Add(telegraph);
            return telegraph;
        }

        /// <summary>예고 하나만 먼저 거둔다 (나머지는 그대로). 거둔 예고는 풀로 돌아가므로 목록에서도 뺀다</summary>
        protected void HideTelegraph(GroundTelegraph telegraph)
        {
            if (telegraph == null) return;
            telegraphs.Remove(telegraph);
            telegraph.Hide();
        }

        protected void HideTelegraphs()
        {
            foreach (var telegraph in telegraphs)
                if (telegraph != null) telegraph.Hide();
            telegraphs.Clear();
        }

        /// <summary>예고했던 범위가 맞는 순간: 같은 모양·크기로 번쩍이고 범위 안에 먼지가 인다 (예고 표시는 스스로 사라진다)</summary>
        protected void Detonate(GroundTelegraph telegraph) => Detonate(telegraph, HitColor, dust: true);

        protected void Detonate(GroundTelegraph telegraph, Color color, bool dust)
        {
            telegraphs.Remove(telegraph);
            telegraph.Detonate(color);
            if (dust) Owner.PlayAreaDust(telegraph);
        }

        /// <summary>예고 없이 이어지는 타격(연타 등)의 범위 번쩍임</summary>
        protected void FlashSector(Vector3 center, Vector3 forward, float radius, float angle)
        {
            var telegraph = Owner.RentTelegraph();
            telegraph.ShowSector(center, forward, radius, angle, 0.01f, HitColor);
            Detonate(telegraph);
        }

        protected void FlashCircle(Vector3 center, float radius) => FlashSector(center, Vector3.forward, radius, 360f);

        /// <summary>플레이어에게 피해 (페이즈 배율 적용). 구르기 무적은 플레이어가 막는다</summary>
        protected void HitPlayer(int damage, Vector3 from)
        {
            if (damage <= 0 || TargetDamageable.CanBeHitBy(Boss.Faction) == false) return;
            int amount = Mathf.Max(1, Mathf.RoundToInt(damage * Owner.DamageScale));
            Vector3 dir = Flat(TargetRoot.position - from);
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : transform.forward;
            TargetDamageable.TakeDamage(new DamageInfo(amount, DamageKind.MonsterSkill, Boss.Faction, Boss.gameObject, TargetRoot.position, dir));
        }

        protected bool PlayerInCircle(Vector3 center, float radius)
        {
            float r = radius + PlayerRadius;
            return Flat(TargetRoot.position - center).sqrMagnitude <= r * r;
        }

        protected bool PlayerInSector(Vector3 center, Vector3 forward, float radius, float angle)
        {
            Vector3 offset = Flat(TargetRoot.position - center);
            float r = radius + PlayerRadius;
            if (offset.sqrMagnitude > r * r) return false;
            if (angle >= 360f || offset.sqrMagnitude < 1f) return true;
            return Vector3.Angle(Flat(forward), offset) <= angle * 0.5f;
        }

        protected bool PlayerInRect(Vector3 start, Vector3 forward, float width, float length)
        {
            Vector3 f = Flat(forward).normalized;
            Vector3 offset = Flat(TargetRoot.position - start);
            float along = Vector3.Dot(offset, f);
            float side = Mathf.Abs(Vector3.Dot(offset, Vector3.Cross(Vector3.up, f)));
            return along >= -PlayerRadius && along <= length + PlayerRadius && side <= width * 0.5f + PlayerRadius;
        }

        protected static void Shake(in CameraShakeSettings settings, Vector3 source, float radius)
            => EventBus<CameraShakeRequestedEvent>.Publish(new CameraShakeRequestedEvent(settings.At(source, radius)));

        /// <summary>진행도 n 이 다음 타격 시점을 넘었으면 true 를 돌려주고 index 를 넘긴다</summary>
        protected static bool PassedHit(float normalizedTime, float[] hitTimes, ref int index)
        {
            if (hitTimes == null || index >= hitTimes.Length || normalizedTime < hitTimes[index]) return false;
            index++;
            return true;
        }
    }
}
