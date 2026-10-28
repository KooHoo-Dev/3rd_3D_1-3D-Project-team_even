using System;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using UnityEngine;

namespace HybridShooter.Core.Contracts
{
    // 몬스터(담당: 몬스터)가 "구현"하고 다른 파트가 "사용"하는 계약.
    // 사운드는 MonsterController 같은 구체 클래스를 직접 참조하지 않는다 (몬스터는 별도 어셈블리).

    /// <summary>
    /// 일반·중간체 몬스터의 소리 시점 알림. 구현: MonsterController. 사용: MonsterSoundEmitter (같은 오브젝트 또는 부모에서 찾음).
    /// 보스는 구조가 달라 BossController 이벤트로 따로 받는다 (설계 R11).
    /// </summary>
    public interface IMonsterSoundSource
    {
        /// <summary>지금 몬스터 종류. 종류가 바뀌면(날개 파괴 1003 → 1002) 새 값</summary>
        MonsterDef Def { get; }

        /// <summary>
        /// 공격 단계 변경. attack = 테이블 공격 Def (모르면 null). Windup 시작이 예고음 시점 (MonsterAttackDef.Sfx).
        /// 돌진처럼 Attack 애니메이션이 없는 공격도 같은 시점에 알린다
        /// </summary>
        event Action<MonsterAttackDef, SkillPhase> AttackPhaseChanged;

        /// <summary>기절 시작. seconds = 기절 시간. 지금은 돌진 벽 충돌(WallHit)에서만 발생</summary>
        event Action<float> Stunned;

        /// <summary>
        /// 공격 몸 이동이 땅에 닿음 (도약 착지). position = 착지 지점.
        /// 도약이 취소(이륙 실패)되면 발행하지 않는다 → 뛰지 않았는데 착지음이 나는 일이 없다
        /// </summary>
        event Action<MonsterAttackDef, Vector3> AttackLanded;

        /// <summary>사망 확정 (MonsterDiedEvent 직전). 울음·루프를 끄고 사망음을 낸다 — 자기 몸 소리는 전역 이벤트가 아니라 여기서</summary>
        event Action Died;
    }
}
