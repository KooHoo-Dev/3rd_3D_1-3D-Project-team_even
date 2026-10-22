using System.Reflection;
using UnityEngine;
using HybridShooter.Monster.Movement;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>실험실·테스트용 MovementProfile 런타임 생성. 프로덕션 MovementProfile 에는 테스트 전용 코드를 두지 않는다.</summary>
    public static class MovementLabProfiles
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        /// <summary>기본값 + 정책별 대표 값</summary>
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
                    Set(p, "moveSpeed", 5f);
                    break;
                case MovementPolicyKind.KeepDistance:
                    Set(p, "usesEngageToken", false);
                    Set(p, "moveSpeed", 2.5f);
                    Set(p, "attackRange", 18f);
                    break;
            }
            return p;
        }

        // MovementProfile 필드 이름이 바뀌면 여기서 즉시 예외로 드러난다.
        private static void Set(MovementProfile profile, string field, object value) =>
            typeof(MovementProfile).GetField(field, Private).SetValue(profile, value);
    }
}
