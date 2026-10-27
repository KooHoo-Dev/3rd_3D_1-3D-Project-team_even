using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>전투: 몬스터 생성·사망, 총기 피격·처치, 플레이어 피격</summary>
    public sealed partial class GameSoundRouter
    {
        /// <summary>
        /// 이미터가 없으면 붙이고(자동 부착 — 몬스터 프리팹 수정 불필요) Def 연결 → 울음·루프 시작.
        /// 보스는 구조가 달라 BossSoundEmitter 담당이므로 붙이지 않는다
        /// </summary>
        private void OnMonsterSpawned(MonsterSpawnedEvent e)
        {
            if (e.Instance == null || (e.Def != null && e.Def.IsBoss)) return;
            EnsureEmitter(e.Instance).Bind(e.Def);
        }

        /// <summary>씬에 직접 둔 몬스터(MonsterSpawnedEvent 없음)에도 이미터를 붙인다. 시작 시 1번</summary>
        private void AttachEmittersInScene()
        {
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (behaviour is IMonsterSoundSource) EnsureEmitter(behaviour.gameObject);
        }

        /// <summary>자식 포함 이미터를 찾고, 없으면 소리 출처(IMonsterSoundSource)가 있는 오브젝트에 붙인다</summary>
        private static MonsterSoundEmitter EnsureEmitter(GameObject monster)
        {
            var emitter = monster.GetComponentInChildren<MonsterSoundEmitter>(true);
            return emitter != null ? emitter : monster.AddComponent<MonsterSoundEmitter>();
        }

        /// <summary>
        /// 사망 위치에서 DeathSfx + 울음·루프 정지 (사망 연출 중에 울지 않게. 애니메이션 이벤트 없이도 동작).
        /// 보스는 BossDefeatedEvent 에서 처리 (보스가 MonsterDied 도 보내면 두 번 나므로)
        /// </summary>
        private void OnMonsterDied(MonsterDiedEvent e)
        {
            if (e.Instance != null)
            {
                var emitter = e.Instance.GetComponentInChildren<MonsterSoundEmitter>(true);
                if (emitter != null) emitter.Silence();
            }
            if (e.Def == null || e.Def.IsBoss) return;
            service.PlaySfx(e.Def.DeathSfx, e.Position);
        }

        /// <summary>
        /// 플레이어가 준 피해만. 타격음은 총알(Bullet)만 (폭발은 폭발음이 이미 덮음), 피드백은 처치면 처치 확인음 아니면 히트마커.
        /// 한 프레임 여러 발(샷건·폭발 범위)은 SoundTable 의 Cooldown·MaxInstances 가 걸러준다
        /// </summary>
        private void OnDamageDealt(DamageDealtEvent e)
        {
            if (e.Info.AttackerFaction != Faction.Player) return;
            if (e.Info.Kind == DamageKind.Bullet) service.PlaySfx(hitImpactKey, e.Info.HitPoint);
            service.PlaySfx(e.IsKill ? killConfirmKey : hitMarkerKey);
        }

        /// <summary>지속 피해(독장판 틱)는 기침 (쿨다운 1.5초), 그 외는 피격 목소리</summary>
        private void OnPlayerDamaged(PlayerDamagedEvent e) =>
            service.PlaySfx(e.Info.Kind == DamageKind.Status ? playerStatusHitKey : playerHitKey);
    }
}
