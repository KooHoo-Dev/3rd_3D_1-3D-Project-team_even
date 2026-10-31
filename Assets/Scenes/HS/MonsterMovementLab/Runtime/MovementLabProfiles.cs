using System.Reflection;
using UnityEngine;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>실험실·테스트용 MovementProfile 런타임 생성. 프로덕션 MovementProfile 에는 테스트 전용 코드를 두지 않는다.</summary>
    public static class MovementLabProfiles
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>
        /// 기본값 + 정책별 대표 값. 이동 속도·사거리는 프로필에 없다 (Lab 몬스터 프리팹의 MonsterController 가 SetCombatTuning 으로 테이블 값을 넣는다).
        /// </summary>
        /// <param name="kind">만들 이동 정책 종류</param>
        /// <returns>런타임 생성 profile. 호출자가 파괴한다</returns>
        public static MovementProfile CreateRuntime(MovementPolicyKind kind)
        {
            var p = ScriptableObject.CreateInstance<MovementProfile>();
            p.name = $"Runtime_{kind}";
            Set(p, "policyKind", kind);
            switch (kind)
            {
                case MovementPolicyKind.Melee:
                    break;
                case MovementPolicyKind.Flank:
                    break;
                case MovementPolicyKind.KeepDistance:
                    Set(p, "usesEngageToken", false);
                    break;
            }
            return p;
        }

        // MovementProfile 필드 이름이 바뀌면 여기서 즉시 예외로 드러난다.
        private static void Set(MovementProfile profile, string field, object value) =>
            typeof(MovementProfile).GetField(field, Private).SetValue(profile, value);
    }
}
