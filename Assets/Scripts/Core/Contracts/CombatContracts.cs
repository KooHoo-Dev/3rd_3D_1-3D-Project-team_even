using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    public enum Faction { Player, Monster, Neutral }

    /// <summary>
    /// 피해 원인. 피격 반응/연출을 다르게 할 때 사용 (BL-007, BL-017).
    /// Status = 상태 효과의 지속 피해 (독장판 틱 등). 피격음 대신 기침처럼 가벼운 반응을 낸다. 새 값은 기존 값 뒤에만 추가
    /// </summary>
    public enum DamageKind { Bullet, Explosion, MonsterAttack, MonsterSkill, Status }

    /// <summary>
    /// 피해 한 번의 정보. 라이플 → 몬스터, 수류탄 → 범위 내 전부, 몬스터/보스 → 플레이어 모두 이 구조체 하나로 주고받는다.
    /// readonly struct + in 파라미터 → 복사/GC 없음.
    /// </summary>
    public readonly struct DamageInfo
    {
        public readonly int Amount;
        public readonly DamageKind Kind;
        public readonly Faction AttackerFaction;
        /// <summary>공격한 오브젝트 (없을 수 있음)</summary>
        public readonly GameObject Attacker;
        /// <summary>명중 위치 (피격 부위 반응용)</summary>
        public readonly Vector3 HitPoint;
        /// <summary>피해가 들어온 방향 (공격자 → 피격자). 피격 방향 표시용</summary>
        public readonly Vector3 Direction;
        /// <summary>원인 데이터 Id (WeaponTable/GrenadeTable/MonsterAttackTable 등, 0 = 알 수 없음)</summary>
        public readonly int SourceDefId;

        public DamageInfo(int amount, DamageKind kind, Faction attackerFaction, GameObject attacker,
                          Vector3 hitPoint, Vector3 direction, int sourceDefId = 0)
        {
            Amount = amount;
            Kind = kind;
            AttackerFaction = attackerFaction;
            Attacker = attacker;
            HitPoint = hitPoint;
            Direction = direction;
            SourceDefId = sourceDefId;
        }
    }

    /// <summary>
    /// 피해를 받을 수 있는 모든 것 (플레이어, 일반 몬스터, 보스).
    /// 공격하는 쪽은 상대 클래스를 몰라도 된다:
    ///   if (hit.collider.TryGetDamageable(out var target) &amp;&amp; target.CanBeHitBy(Faction.Player)) target.TakeDamage(info);
    /// </summary>
    public interface IDamageable
    {
        Faction Faction { get; }
        bool IsAlive { get; }
        void TakeDamage(in DamageInfo info);
    }

    /// <summary>
    /// 환경 폭발(연료 셀 등) 같은 큰 충격에 움찔(피격 경직)할 수 있는 대상 (보스).
    /// 경직을 받을지·쿨다운은 대상이 정한다. 폭발 쪽은 피해를 준 뒤 TryStagger 를 한 번 부른다
    /// </summary>
    public interface IStaggerable
    {
        /// <summary>등장·전환·최후 연출처럼 환경 충격을 받지 않는 중 (몸에 닿은 연료 셀은 터지지 않고 찌그러진다)</summary>
        bool IsInCinematic { get; }

        /// <summary>경직을 건다. 실제로 걸렸으면 true (무적·쿨다운·특수 패턴 중이면 false)</summary>
        /// <param name="source">충격 진원 (폭발 중심)</param>
        bool TryStagger(Vector3 source);
    }

    public static class DamageableExtensions
    {
        /// <summary>콜라이더가 자식(히트박스)에 있어도 찾도록 부모 방향으로 탐색</summary>
        public static bool TryGetDamageable(this Collider col, out IDamageable target)
        {
            target = col != null ? col.GetComponentInParent<IDamageable>() : null;
            return target != null;
        }

        /// <summary>같은 진영끼리는 맞지 않게</summary>
        public static bool CanBeHitBy(this IDamageable target, Faction attacker)
            => target.IsAlive && (target.Faction != attacker || attacker == Faction.Neutral);
    }
}
