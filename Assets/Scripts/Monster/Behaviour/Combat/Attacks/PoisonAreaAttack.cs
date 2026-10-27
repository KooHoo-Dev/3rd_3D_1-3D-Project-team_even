using System;
using HybridShooter.Core.Contracts;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 원거리 독장판 포격 (ZonePressure 역할). 예고 시작에 플레이어가 선 Terrain 지면점을 목표로 확정하고 표식을 켠다.
    /// 공격 순간 독 포탄을 1발 쏘고 동작을 끝낸다. 비행·장판·피해는 PoisonProjectile / PoisonZone 이 맡는다.
    /// </summary>
    public sealed class PoisonAreaAttack : MonsterAttack
    {
        private readonly PoisonAreaSettings settings;
        private readonly Func<IPlayerTarget, IDamageable> findDamageable;

        // ── 이번 주기 (예고 시작에 정하고 발사 때 투사체로 넘긴다) ──
        private Vector3 targetPoint;   // 착탄점 (Terrain 표면)
        private Transform marker;      // 발사 전 예고 표식. null = 없음 또는 투사체로 넘김

        // 예고 중 착탄점 쪽으로 돈다 (플레이어를 따라가지 않는다)
        protected override Vector3 WindupLookPoint => targetPoint;

        /// <summary>
        /// MonsterAttackFactory 가 ZonePressure 역할에 만든다.
        /// </summary>
        /// <param name="spec">공격 값 묶음. Radius = 장판 반경, ProjectileSpeed = 포탄 수평 속도</param>
        /// <param name="body">공격자 몸체. 발사 위치와 조준 방향</param>
        /// <param name="findDamageable">대상에서 피해를 줄 IDamageable 을 찾는 방법. 장판이 쓴다</param>
        /// <param name="settings">원거리 프리팹의 설정 (풀·발사 높이·장판 틱 피해·간격·지속·피해 높이 허용값)</param>
        /// <exception cref="ArgumentNullException">body, settings 중 하나가 null 일 때 (프리팹에 PoisonAreaSettings 가 없을 때 등). body 는 기반 생성자가 던진다</exception>
        public PoisonAreaAttack(in AttackSpec spec, MonsterBody body, Func<IPlayerTarget, IDamageable> findDamageable,
                                PoisonAreaSettings settings)
            : base(spec, body, findDamageable)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings), "원거리 몬스터 프리팹에 PoisonAreaSettings 가 없다");

            this.settings = settings;
            this.findDamageable = findDamageable;
        }

        /// <summary>예고 시작: 플레이어 발밑 Terrain 지면점을 목표로 확정하고 표식을 켠다. 그쪽으로 도는 것은 예고 중 Tick 이 한다</summary>
        protected override void OnWindup()
        {
            targetPoint = TerrainGround.PointUnder(Target.PlayerTrackingInfo.Position);

            float diameter = Spec.Radius * 2f;
            marker = settings.MarkerPool.Get(targetPoint);
            marker.localScale = new Vector3(diameter, marker.localScale.y, diameter);
        }

        /// <summary>공격 순간: 포탄을 1발 쏘고 표식을 포탄에 넘긴다</summary>
        /// <returns>항상 true (동작 완료)</returns>
        protected override bool OnActive(float now, float deltaTime)
        {
            Vector3 launchPoint = Body.Position + Vector3.up * settings.LaunchHeight;
            PoisonProjectile projectile = settings.ProjectilePool.Get(launchPoint);
            projectile.Launch(settings, launchPoint, targetPoint, Spec.ProjectileSpeed, marker,
                              Target, Spec.Radius, findDamageable, Spec.SourceDefId);
            marker = null;
            return true;
        }

        /// <summary>중간에 끊길 때: 아직 쏘지 않았으면 표식을 반납한다</summary>
        protected override void OnCancel()
        {
            if (marker == null) return;
            settings.MarkerPool.Return(marker);
            marker = null;
        }
    }
}
