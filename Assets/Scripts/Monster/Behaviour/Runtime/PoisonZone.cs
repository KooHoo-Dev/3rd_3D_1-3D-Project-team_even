using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.Events;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour
{
    /// <summary>
    /// 독장판. PoisonProjectile 이 착탄점에 깔면(Begin) 지속 시간 동안 TickInterval 마다 대상의 위치를 검사해 피해를 준다.
    /// 첫 틱은 깔리는 순간이다. 다른 장판과 겹쳐도 각자 따로 피해를 준다. 몬스터가 죽어도 지속 시간까지 남는다.
    /// </summary>
    /// <remarks>RoundCleanupEvent 를 받으면 즉시 반납한다.</remarks>
    public sealed class PoisonZone : MonoBehaviour
    {
        private PoisonAreaSettings settings;
        private Vector3 center;
        private float radius;            // m
        private StatusEffectDef effect;
        private IPlayerTarget target;
        private Func<IPlayerTarget, IDamageable> findDamageable;
        private int sourceDefId;
        private float nextTickTime;      // 다음 피해 검사 시각 (s, Time.time)
        private float endTime;           // 반납 시각 (s, Time.time)
        private SoundHandle loopSound;   // 장판 루프 소리 (StatusEffectTable.LoopSfxKey)

        /// <summary>
        /// 장판을 깐다. PoisonProjectile 이 착탄 때 풀에서 꺼낸 직후 1번 부른다. XZ 크기를 지름(2·radius)으로 맞춘다.
        /// </summary>
        /// <param name="settings">풀과 피해 높이 허용값을 가진 몬스터 설정</param>
        /// <param name="center">장판 중심 (Terrain 표면)</param>
        /// <param name="radius">반경 (m)</param>
        /// <param name="effect">틱 피해(Value)·간격(TickInterval)·지속(Duration)</param>
        /// <param name="target">피해를 줄 대상</param>
        /// <param name="findDamageable">대상에서 IDamageable 을 찾는 방법</param>
        /// <param name="sourceDefId">MonsterAttackTable Id. 피해 정보에 남긴다</param>
        public void Begin(PoisonAreaSettings settings, Vector3 center, float radius, StatusEffectDef effect,
                          IPlayerTarget target, Func<IPlayerTarget, IDamageable> findDamageable, int sourceDefId)
        {
            this.settings = settings;
            this.center = center;
            this.radius = radius;
            this.effect = effect;
            this.target = target;
            this.findDamageable = findDamageable;
            this.sourceDefId = sourceDefId;

            float diameter = radius * 2f;
            transform.position = center;
            transform.localScale = new Vector3(diameter, transform.localScale.y, diameter);
            nextTickTime = Time.time;
            endTime = Time.time + effect.Duration;

            StopLoopSound(); // 풀에서 바로 다시 꺼낸 경우 대비
            if (effect.LoopSfx != null && ServiceLocator.TryGet<ISoundService>(out var sound))
                loopSound = sound.PlayLoop(effect.LoopSfx, transform); // 여러 장판이 겹쳐도 가까운 몇 개만 들린다
        }

        /// <summary>
        /// 장판 피해 조건. 대상이 장판 중심의 구 안이고, Terrain 표면 가까이(±tolerance) 서 있을 때만 true.
        /// 언덕 위 Terrain 은 맞고, 바위·다리 메시 위는 높이 차이로 맞지 않는다.
        /// </summary>
        /// <param name="center">장판 중심</param>
        /// <param name="radius">반경 (m)</param>
        /// <param name="playerPosition">대상 위치</param>
        /// <param name="groundHeight">대상 XZ 의 Terrain 표면 높이 (m)</param>
        /// <param name="tolerance">대상 Y 와 표면 높이 차이 허용값 ± (m)</param>
        /// <returns>피해를 줘야 하면 true</returns>
        public static bool IsInsideZone(Vector3 center, float radius, Vector3 playerPosition, float groundHeight, float tolerance)
        {
            bool isInsideSphere = Vector3.Distance(playerPosition, center) <= radius;
            bool isOnGround = Mathf.Abs(playerPosition.y - groundHeight) <= tolerance;
            return isInsideSphere && isOnGround;
        }

        /// <summary>RoundCleanupEvent 구독. 풀에서 꺼내 켜질 때마다 호출된다</summary>
        private void OnEnable() => EventBus<RoundCleanupEvent>.Subscribe(OnRoundCleanup);

        /// <summary>RoundCleanupEvent 구독 해제 + 루프 소리 정지. 반납으로 꺼질 때 호출된다</summary>
        private void OnDisable()
        {
            EventBus<RoundCleanupEvent>.Unsubscribe(OnRoundCleanup);
            StopLoopSound();
        }

        private void StopLoopSound()
        {
            if (loopSound.IsValid && ServiceLocator.TryGet<ISoundService>(out var sound)) sound.StopLoop(loopSound);
            loopSound = default;
        }

        /// <summary>매 프레임: 틱 시각이면 피해 검사 → 지속 시간이 끝났으면 반납</summary>
        private void Update()
        {
            if (Time.time >= nextTickTime) TickDamage();
            if (Time.time >= endTime) settings.ZonePool.Return(this);
        }

        /// <summary>틱 1회: 대상이 장판 안이면 피해를 주고, 다음 틱 시각을 정한다</summary>
        private void TickDamage()
        {
            nextTickTime += effect.TickInterval;
            Vector3 playerPosition = target.PlayerTrackingInfo.Position;
            float groundHeight = TerrainGround.HeightAt(playerPosition);
            if (IsInsideZone(center, radius, playerPosition, groundHeight, settings.ZoneHeightTolerance) == false) return;

            DealDamage(playerPosition);
        }

        /// <summary>대상에게 틱 피해(effect.Value) 1회. 대상이 이 진영의 피해를 받지 않으면(사망 등) 주지 않는다</summary>
        /// <param name="playerPosition">피해 지점</param>
        /// <exception cref="InvalidOperationException">대상에서 IDamageable 을 찾지 못했을 때</exception>
        private void DealDamage(Vector3 playerPosition)
        {
            var damageable = findDamageable(target);
            if (damageable == null) throw new InvalidOperationException("독장판 대상에서 IDamageable 을 찾지 못했다");
            if (damageable.CanBeHitBy(Faction.Monster) == false) return;

            // 지속 피해 → Status (사운드는 틱마다 피격음 대신 기침, R10)
            var info = new DamageInfo(Mathf.RoundToInt(effect.Value), DamageKind.Status, Faction.Monster, null,
                                      playerPosition, Vector3.zero, sourceDefId);
            damageable.TakeDamage(in info);
        }

        /// <summary>라운드 정리: 즉시 반납한다</summary>
        private void OnRoundCleanup(RoundCleanupEvent e) => settings.ZonePool.Return(this);
    }
}
