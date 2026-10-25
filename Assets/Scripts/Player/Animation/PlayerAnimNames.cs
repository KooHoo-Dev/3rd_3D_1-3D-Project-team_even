using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// Player_AC 의 레이어·상태·파라미터 이름. 빌더(에디터)와 드라이버·디버그 HUD(런타임)가 같은 이름을 쓰도록 한곳에 둔다.
    /// 설계: Assets/Scenes/MS/Docs/AnimationDesign.md
    /// </summary>
    public static class PlayerAnimNames
    {
        public static class Layers
        {
            public const string Base = "Base Layer";
            /// <summary>팔·손가락·머리만 덮는 상체 자세 레이어 (들고 다니기 ↔ 조준)</summary>
            public const string UpperPose = "Upper Pose";
            /// <summary>재장전·꺼내기 등 팔로 하는 1회 동작 레이어 (Upper Pose 위를 덮는다)</summary>
            public const string UpperAction = "Upper Action";
            /// <summary>피격 반응을 원래 자세 위에 더하는(additive) 레이어</summary>
            public const string HitReaction = "Hit Reaction";
        }

        public static class States
        {
            public const string Locomotion = "Locomotion";
            public const string Sprint = "Sprint";
            public const string JumpStart = "Jump Start";
            public const string Fall = "Fall";
            public const string LandLight = "Land Light";
            public const string LandMedium = "Land Medium";
            public const string LandHeavy = "Land Heavy";
            public const string Pose = "Pose";
            public const string Empty = "Empty";
            public const string Reload = "Reload";
            public const string Draw = "Draw";
            public const string Throw = "Throw";
            public const string Dodge = "Dodge";
            public const string Hit = "Hit";
            public const string DeathFront = "Death Front";
            public const string DeathBack = "Death Back";
            public const string DeathLeft = "Death Left";
            public const string DeathRight = "Death Right";

            /// <summary>디버그 HUD 가 상태 해시 → 이름을 찾을 때 쓰는 목록</summary>
            public static readonly string[] All = { Locomotion, Sprint, JumpStart, Fall, LandLight, LandMedium, LandHeavy, Pose, Empty, Reload, Draw, Throw, Dodge, Hit, DeathFront, DeathBack, DeathLeft, DeathRight };
        }

        public static class Params
        {
            public const string MoveX = "MoveX";
            public const string MoveY = "MoveY";
            public const string MoveSpeedScale = "MoveSpeedScale";
            public const string Moving = "Moving";
            public const string Sprint = "Sprint";
            public const string Grounded = "Grounded";
            public const string Falling = "Falling";
            public const string Jump = "Jump";
            /// <summary>착지 세기: 0 = 가볍게, 1 = 중간, 2 = 무겁게 (떨어진 속도로 정한다)</summary>
            public const string LandType = "LandType";
            /// <summary>0 = 들고 다니기, 1 = 조준 (상체 자세)</summary>
            public const string Aim = "Aim";
            public const string Reload = "Reload";
            public const string Draw = "Draw";
            /// <summary>수류탄 던지기 (Upper Action 레이어)</summary>
            public const string Throw = "Throw";
            /// <summary>재장전·꺼내기를 중간에 끝낸다 (장전 취소·완료, 조준·사격으로 꺼내기 끊기)</summary>
            public const string ActionEnd = "ActionEnd";
            /// <summary>재장전·꺼내기 재생 속도 (실제 시간에 맞춤)</summary>
            public const string ActionSpeed = "ActionSpeed";
            public const string Dodge = "Dodge";
            public const string DodgeX = "DodgeX";
            public const string DodgeY = "DodgeY";
            /// <summary>구르기 재생 속도 (구르기 + 회복 시간에 맞춤)</summary>
            public const string DodgeSpeed = "DodgeSpeed";
            /// <summary>물리 구르기 중 (끝나면 움직일 때 바로 이동 동작으로)</summary>
            public const string Rolling = "Rolling";
            public const string Hit = "Hit";
            public const string HitX = "HitX";
            public const string HitY = "HitY";
            public const string Die = "Die";
            public const string Dead = "Dead";
            /// <summary>사망 방향: 0 = 앞에서 맞음, 1 = 뒤, 2 = 왼쪽, 3 = 오른쪽</summary>
            public const string DeathDir = "DeathDir";
        }

        public static class Hash
        {
            public static readonly int MoveX = Animator.StringToHash(Params.MoveX);
            public static readonly int MoveY = Animator.StringToHash(Params.MoveY);
            public static readonly int MoveSpeedScale = Animator.StringToHash(Params.MoveSpeedScale);
            public static readonly int Moving = Animator.StringToHash(Params.Moving);
            public static readonly int Sprint = Animator.StringToHash(Params.Sprint);
            public static readonly int Grounded = Animator.StringToHash(Params.Grounded);
            public static readonly int Falling = Animator.StringToHash(Params.Falling);
            public static readonly int Jump = Animator.StringToHash(Params.Jump);
            public static readonly int LandType = Animator.StringToHash(Params.LandType);
            public static readonly int Aim = Animator.StringToHash(Params.Aim);
            public static readonly int Reload = Animator.StringToHash(Params.Reload);
            public static readonly int Draw = Animator.StringToHash(Params.Draw);
            public static readonly int Throw = Animator.StringToHash(Params.Throw);
            public static readonly int ThrowState = Animator.StringToHash(States.Throw);
            public static readonly int ActionEnd = Animator.StringToHash(Params.ActionEnd);
            public static readonly int ActionSpeed = Animator.StringToHash(Params.ActionSpeed);
            public static readonly int EmptyState = Animator.StringToHash(States.Empty);
            public static readonly int Dodge = Animator.StringToHash(Params.Dodge);
            public static readonly int DodgeX = Animator.StringToHash(Params.DodgeX);
            public static readonly int DodgeY = Animator.StringToHash(Params.DodgeY);
            public static readonly int DodgeSpeed = Animator.StringToHash(Params.DodgeSpeed);
            public static readonly int Rolling = Animator.StringToHash(Params.Rolling);
            public static readonly int Hit = Animator.StringToHash(Params.Hit);
            public static readonly int HitX = Animator.StringToHash(Params.HitX);
            public static readonly int HitY = Animator.StringToHash(Params.HitY);
            public static readonly int Die = Animator.StringToHash(Params.Die);
            public static readonly int Dead = Animator.StringToHash(Params.Dead);
            public static readonly int DeathDir = Animator.StringToHash(Params.DeathDir);
        }
    }
}
