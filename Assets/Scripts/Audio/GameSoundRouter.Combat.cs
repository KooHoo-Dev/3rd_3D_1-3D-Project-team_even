using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.Player;
using UnityEngine;

namespace HybridShooter.Audio
{
    /// <summary>전투: 몬스터 생성 시·플레이어 시작 시 이미터 부착, 총기 피격·처치 피드백. 몬스터 사망음은 MonsterSoundEmitter, 플레이어 피격 목소리는 PlayerSoundEmitter</summary>
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

        /// <summary>
        /// 씬의 플레이어에 PlayerSoundEmitter 자동 부착. 시작 시 1번.
        /// 플레이어 프리팹에 이미터가 빠져 있어도 몸 소리(발소리·피격 등)가 나게 한다 (이미 있으면 그대로 둔다)
        /// </summary>
        private void AttachPlayerEmitters()
        {
            foreach (var player in FindObjectsByType<PlayerController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (player.GetComponent<PlayerSoundEmitter>() == null) player.gameObject.AddComponent<PlayerSoundEmitter>();
        }

        /// <summary>자식 포함 이미터를 찾고, 없으면 소리 출처(IMonsterSoundSource)가 있는 오브젝트에 붙인다. 근접 울음 설정을 넣는다</summary>
        private MonsterSoundEmitter EnsureEmitter(GameObject monster)
        {
            var emitter = monster.GetComponentInChildren<MonsterSoundEmitter>(true);
            if (emitter == null) emitter = monster.AddComponent<MonsterSoundEmitter>();
            emitter.SetNearIdle(monsterNearDistance, monsterNearIdleInterval);
            return emitter;
        }

        /// <summary>
        /// 플레이어가 준 피해만. 타격음은 총알(Bullet)만 (폭발은 폭발음이 이미 덮음), 히트마커는 처치가 아닐 때만.
        /// 처치에는 따로 UI 소리를 두지 않는다 — 몬스터 사망음(이미터)만 나고 처치·재화 획득은 UI 로 표시 (팀 결정 2026-10-28).
        /// 한 프레임 여러 발(샷건·폭발 범위)은 SoundTable 의 Cooldown·MaxInstances 가 걸러준다
        /// </summary>
        private void OnDamageDealt(DamageDealtEvent e)
        {
            if (e.Info.AttackerFaction != Faction.Player) return;
            if (e.Info.Kind == DamageKind.Bullet) service.PlaySfx(hitImpactKey, e.Info.HitPoint);
            if (e.IsKill == false) service.PlaySfx(hitMarkerKey);
        }
    }
}
