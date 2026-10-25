using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 원거리 몬스터의 독 포탄. PoisonAreaAttack 이 발사(Launch)하면 포물선으로 날아가 착탄점에 독장판을 깔고 자신을 반납한다.
    /// 예고 표식도 넘겨받아 착탄 때 함께 반납한다. 몬스터가 죽어도 자기 수명(비행 시간)까지 날아간다.
    /// </summary>
    /// <remarks>벽·지형 충돌은 검사하지 않는다. RoundCleanupEvent 를 받으면 즉시 반납한다.</remarks>
    public sealed class PoisonProjectile : MonoBehaviour
    {
        private PoisonAreaSettings settings;
        private Vector3 start;
        private Vector3 end;
        private float flightSeconds;   // 착탄까지 (s) = 수평 거리 ÷ 속도
        private float elapsedSeconds;
        private Transform marker;      // 착탄 때 반납할 예고 표식. null = 없음

        // ── 착탄 때 장판에 넘길 값 ──
        private float zoneRadius;      // m
        private StatusEffectDef effect;
        private IPlayerTarget target;
        private Func<IPlayerTarget, IDamageable> findDamageable;
        private int sourceDefId;

        /// <summary>
        /// 비행을 시작한다. PoisonAreaAttack.OnActive 가 풀에서 꺼낸 직후 1번 부른다.
        /// </summary>
        /// <param name="settings">풀과 포물선 높이를 가진 몬스터 설정</param>
        /// <param name="start">발사점</param>
        /// <param name="end">착탄점 (Terrain 표면)</param>
        /// <param name="speed">수평 속도 (m/s). 0 초과</param>
        /// <param name="marker">착탄 때 반납할 예고 표식. null = 없음</param>
        /// <param name="target">장판이 피해를 줄 대상</param>
        /// <param name="zoneRadius">장판 반경 (m)</param>
        /// <param name="effect">장판의 틱 피해·간격·지속</param>
        /// <param name="findDamageable">대상에서 IDamageable 을 찾는 방법</param>
        /// <param name="sourceDefId">MonsterAttackTable Id. 피해 정보에 남긴다</param>
        /// <exception cref="ArgumentOutOfRangeException">speed 가 0 이하일 때</exception>
        public void Launch(PoisonAreaSettings settings, Vector3 start, Vector3 end, float speed, Transform marker,
                           IPlayerTarget target, float zoneRadius, StatusEffectDef effect,
                           Func<IPlayerTarget, IDamageable> findDamageable, int sourceDefId)
        {
            if (speed <= 0f) throw new ArgumentOutOfRangeException(nameof(speed), speed, "투사체 속도는 0 초과");

            this.settings = settings;
            this.start = start;
            this.end = end;
            this.marker = marker;
            this.target = target;
            this.zoneRadius = zoneRadius;
            this.effect = effect;
            this.findDamageable = findDamageable;
            this.sourceDefId = sourceDefId;

            Vector3 horizontal = end - start;
            horizontal.y = 0f;
            flightSeconds = horizontal.magnitude / speed;
            elapsedSeconds = 0f;
            transform.position = start;
        }

        /// <summary>
        /// 포물선 위치 계산. 수평·높이를 진행률 t 로 직선 보간하고, 그 위로 4·arcHeight·t(1−t) 만큼 올린다.
        /// t = 0.5 에서 직선보다 arcHeight 만큼 높다.
        /// </summary>
        /// <param name="start">발사점</param>
        /// <param name="end">착탄점</param>
        /// <param name="arcHeight">최고 높이 (m)</param>
        /// <param name="t">진행률. 0 = 발사점, 1 = 착탄점. 범위 밖은 0~1 로 자른다</param>
        /// <returns>t 에서의 월드 위치</returns>
        public static Vector3 ArcPosition(Vector3 start, Vector3 end, float arcHeight, float t)
        {
            float progress = Mathf.Clamp01(t);
            Vector3 straightPoint = Vector3.Lerp(start, end, progress);
            float lift = 4f * arcHeight * progress * (1f - progress);
            return straightPoint + Vector3.up * lift;
        }

        /// <summary>RoundCleanupEvent 구독. 풀에서 꺼내 켜질 때마다 호출된다</summary>
        private void OnEnable() => EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);

        /// <summary>RoundCleanupEvent 구독 해제. 반납으로 꺼질 때 호출된다</summary>
        private void OnDisable() => EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);

        /// <summary>
        /// 매 프레임 진행률 t = 경과 ÷ 비행 시간으로 위치를 정한다. t ≥ 1 이면 착탄한다.
        /// 진행률로 계산하므로 프레임 간격이 커도 착탄점을 지나치지 않는다.
        /// </summary>
        private void Update()
        {
            elapsedSeconds += Time.deltaTime;
            float progress = 1f;
            if (flightSeconds > 0f) progress = elapsedSeconds / flightSeconds;

            transform.position = ArcPosition(start, end, settings.ArcHeight, progress);
            if (progress >= 1f) Arrive();
        }

        /// <summary>착탄: 표식을 반납하고, 착탄점에 독장판을 깔고, 자신을 반납한다</summary>
        private void Arrive()
        {
            ReturnMarker();
            PoisonZone zone = settings.ZonePool.Get(end);
            zone.Begin(settings, end, zoneRadius, effect, target, findDamageable, sourceDefId);
            settings.ProjectilePool.Return(this);
        }

        /// <summary>라운드 정리: 장판 없이 표식과 자신을 즉시 반납한다</summary>
        private void OnRoundCleanup(RoundCleanupEvent e)
        {
            ReturnMarker();
            settings.ProjectilePool.Return(this);
        }

        /// <summary>넘겨받은 예고 표식이 있으면 반납한다</summary>
        private void ReturnMarker()
        {
            if (marker == null) return;
            settings.MarkerPool.Return(marker);
            marker = null;
        }
    }
}
