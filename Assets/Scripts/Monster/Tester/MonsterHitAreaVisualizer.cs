using HybridShooter.Core.Data;
using HybridShooter.Core.Data.Defs;
using HybridShooter.Core.GameFlow;
using HybridShooter.Monster.Behaviour;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace HybridShooter.Monster.Tester
{
    /// <summary>
    /// 몬스터 공격 판정 범위(IHitArea)를 Scene 뷰에 그리는 디버그 표시. MonsterController 와 같은 GameObject 에 붙인다.
    /// 근접 공격의 두 사거리를 함께 보여준다:
    /// 흰 외곽선 = 진입 사거리(테이블 Range, 이 안에서만 예고가 시작된다),
    /// 연한 채움 = 허용 사거리(Range × HitRangeRatio, 판정 순간 이 안이면 맞는다).
    /// 에디터에서 몬스터를 선택하면 테이블 값과 프리팹의 hitRangeRatio 로, 플레이 중에는 예고·공격 구간의 범위와
    /// MonsterAttack.MonsterHitEvent 가 알린 판정 순간의 범위(맞음 = 빨강, 빗나감 = 노랑)를 보여준다.
    /// </summary>
    /// <remarks>
    /// Game 뷰에서 보려면 Gizmos 버튼을 켠다. 풀에서 몬스터가 다시 조립되면(StateMachine 교체) 새 공격에 다시 구독한다.
    /// </remarks>
    [RequireComponent(typeof(MonsterController))]
    public sealed class MonsterHitAreaVisualizer : MonoBehaviour
    {
        [Header("표시")]
        [Tooltip("판정 순간 범위를 진하게 남겨 두는 시간 (s). 판정은 1프레임이라 눈에 보이도록 길게 둔다")]
        [SerializeField, Min(0.05f)] private float hitFlashSeconds = 0.3f;
        [Tooltip("선택했을 때(에디터)·예고/공격 구간의 범위 색")]
        [SerializeField] private Color previewColor = new Color(0.2f, 0.8f, 1f, 0.25f);
        [Tooltip("판정 순간 대상이 범위 안이었을 때 색")]
        [SerializeField] private Color hitColor = new Color(1f, 0.15f, 0.1f, 0.6f);
        [Tooltip("판정 순간 대상이 범위 밖이었을 때 색")]
        [SerializeField] private Color missColor = new Color(1f, 0.9f, 0.1f, 0.5f);

        private const float DrawHeight = 0.05f; // 바닥에 묻히지 않게 띄우는 높이 (m)

        private MonsterController controller;
        private MonsterStateMachine subscribedMachine;

        // ── 마지막 판정 순간 ──
        private IHitArea flashArea;
        private Vector3 flashOrigin;
        private Vector3 flashForward;
        private bool flashDidHit;
        private float flashEndTime = -1f; // s (Time.time 기준). 지났으면 그리지 않는다

        /// <summary>같은 GameObject 의 MonsterController 를 잡는다</summary>
        private void Awake()
        {
            controller = GetComponent<MonsterController>();
        }

        /// <summary>
        /// 상태 머신이 새로 만들어졌으면(Start 직후, 풀 Respawn) 새 공격들에 다시 구독한다.
        /// 조립은 MonsterController.Start 에서 일어나므로 매 프레임 참조를 비교한다.
        /// </summary>
        private void Update()
        {
            if (controller.StateMachine == subscribedMachine) return;
            Unsubscribe();
            Subscribe();
        }

        /// <summary>꺼질 때 구독을 해제한다</summary>
        private void OnDisable()
        {
            Unsubscribe();
        }

        /// <summary>현재 상태 머신의 모든 공격이 알리는 판정 순간을 구독한다</summary>
        private void Subscribe()
        {
            subscribedMachine = controller.StateMachine;
            if (subscribedMachine == null) return;
            foreach (MonsterAttack attack in subscribedMachine.Attacks)
                attack.MonsterHitEvent += OnMonsterHit;
        }

        /// <summary>구독 중인 공격들에서 해제한다</summary>
        private void Unsubscribe()
        {
            if (subscribedMachine == null) return;
            foreach (MonsterAttack attack in subscribedMachine.Attacks)
                attack.MonsterHitEvent -= OnMonsterHit;
            subscribedMachine = null;
        }

        /// <summary>
        /// 판정 순간을 기억한다. 그리기는 OnDrawGizmos 가 hitFlashSeconds 동안 한다.
        /// </summary>
        /// <param name="attack">판정한 공격. 쓰지 않는다</param>
        /// <param name="area">판정에 쓴 범위</param>
        /// <param name="origin">판정 기준 위치</param>
        /// <param name="forward">판정 기준 정면</param>
        /// <param name="didHit">대상이 범위 안이었으면 true</param>
        private void OnMonsterHit(MonsterAttack attack, IHitArea area, Vector3 origin, Vector3 forward, bool didHit)
        {
            flashArea = area;
            flashOrigin = origin;
            flashForward = forward;
            flashDidHit = didHit;
            flashEndTime = Time.time + hitFlashSeconds;
        }

#if UNITY_EDITOR
        /// <summary>플레이 중 예고·공격 구간의 범위와 판정 순간의 범위를 그린다</summary>
        private void OnDrawGizmos()
        {
            if (Application.isPlaying == false) return;
            DrawWindupAndActiveAreas();
            DrawHitFlash();
        }

        /// <summary>에디터에서 선택했을 때 테이블 값으로 만든 범위를 그린다 (플레이 중에는 OnDrawGizmos 가 맡는다)</summary>
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying) return;
            DrawTablePreview();
        }

        /// <summary>예고·공격 중인 StrikeAttack 의 허용 사거리(Range × HitRangeRatio)와 진입 사거리(Range)를 몸 위치에 그린다</summary>
        private void DrawWindupAndActiveAreas()
        {
            if (controller.StateMachine == null) return;
            foreach (MonsterAttack attack in controller.StateMachine.Attacks)
            {
                bool isStrike = attack is StrikeAttack;
                bool isWindupOrActive = attack.Phase == SkillPhase.Windup || attack.Phase == SkillPhase.Active;
                if (isStrike == false) continue;
                if (isWindupOrActive == false) continue;

                var allowedArea = new MeleeArea(attack.Spec.Range * attack.HitRangeRatio);
                DrawArea(allowedArea, transform.position, transform.forward, previewColor);
                DrawEntryRing(attack.Spec.Range, transform.position);
            }
        }

        /// <summary>마지막 판정 순간의 범위를 맞음 = 빨강, 빗나감 = 노랑으로 hitFlashSeconds 동안 그린다</summary>
        private void DrawHitFlash()
        {
            if (flashArea == null) return;
            if (Time.time > flashEndTime) return;

            Color color = flashDidHit ? hitColor : missColor;
            DrawArea(flashArea, flashOrigin, flashForward, color);
        }

        /// <summary>
        /// 테이블의 기본 공격 값으로 범위를 그린다. Melee 역할 = 허용 사거리(Range × 프리팹 hitRangeRatio) + 진입 사거리(Range), Infiltrator = 최대 도약 지점의 착지 원.
        /// 다른 역할은 IHitArea 를 쓰지 않아 그리지 않는다.
        /// </summary>
        private void DrawTablePreview()
        {
            var monsterController = GetComponent<MonsterController>();
            MonsterDef def = DataManager.Monsters.Get(monsterController.MonsterId);
            float hitRangeRatio = new SerializedObject(monsterController).FindProperty("hitRangeRatio").floatValue;

            foreach (MonsterAttackDef attackDef in def.BasicAttacks)
            {
                if (def.Role == MonsterRole.Melee)
                {
                    DrawArea(new MeleeArea(attackDef.Range * hitRangeRatio), transform.position, transform.forward, previewColor);
                    DrawEntryRing(attackDef.Range, transform.position);
                }
                if (def.Role == MonsterRole.Infiltrator)
                {
                    Vector3 flatForward = FlatDirection(transform.forward);
                    Vector3 farthestLanding = transform.position + flatForward * attackDef.Range;
                    DrawArea(new CircleArea(0f, attackDef.Radius), farthestLanding, transform.forward, previewColor);
                }
            }
        }

        /// <summary>
        /// 범위 모양에 맞는 채운 도형과 외곽선을 수평으로 그린다.
        /// </summary>
        /// <param name="area">그릴 범위 (Melee / Circle / Cone / Line)</param>
        /// <param name="origin">범위 기준 위치</param>
        /// <param name="forward">범위 기준 정면 (수평 성분만 사용)</param>
        /// <param name="fillColor">채움 색. 외곽선은 같은 색을 불투명하게 쓴다</param>
        /// <exception cref="System.NotSupportedException">그릴 수 없는 범위 클래스일 때</exception>
        private static void DrawArea(IHitArea area, Vector3 origin, Vector3 forward, Color fillColor)
        {
            Vector3 flatOrigin = new Vector3(origin.x, origin.y + DrawHeight, origin.z);
            Vector3 flatForward = FlatDirection(forward);
            Color lineColor = new Color(fillColor.r, fillColor.g, fillColor.b, 1f);

            if (area is MeleeArea melee)
                DrawDisc(flatOrigin, melee.Range, fillColor, lineColor);
            else if (area is CircleArea circle)
                DrawDisc(flatOrigin + flatForward * circle.Offset, circle.Radius, fillColor, lineColor);
            else if (area is ConeArea cone)
                DrawSector(flatOrigin, flatForward, cone.Range, cone.Angle, fillColor, lineColor);
            else if (area is LineArea line)
                DrawRectangle(flatOrigin, flatForward, line.Length, line.Width, fillColor, lineColor);
            else
                throw new System.NotSupportedException($"{area.GetType().Name} 은 그릴 수 없다");
        }

        /// <summary>
        /// 공격 진입 거리(Spec.Range)를 초록 외곽선 원으로 그린다. 채운 원(허용 거리 = Range × HitRangeRatio)과 비교하려는 용도다.
        /// </summary>
        /// <param name="entryRange">진입 거리 (m). 예고를 시작할 수 있는 수평 거리</param>
        /// <param name="center">몸 위치</param>
        private static void DrawEntryRing(float entryRange, Vector3 center)
        {
            Handles.color = Color.green;
            Handles.DrawWireDisc(new Vector3(center.x, center.y + DrawHeight, center.z), Vector3.up, entryRange);
        }

        /// <summary>수평 원판 (Melee, Circle)</summary>
        private static void DrawDisc(Vector3 center, float radius, Color fillColor, Color lineColor)
        {
            Handles.color = fillColor;
            Handles.DrawSolidDisc(center, Vector3.up, radius);
            Handles.color = lineColor;
            Handles.DrawWireDisc(center, Vector3.up, radius);
        }

        /// <summary>정면 기준 부채꼴 (Cone). fullAngle = 전체 각도 (도)</summary>
        private static void DrawSector(Vector3 center, Vector3 flatForward, float radius, float fullAngle, Color fillColor, Color lineColor)
        {
            Vector3 startDirection = Quaternion.AngleAxis(-fullAngle * 0.5f, Vector3.up) * flatForward;
            Handles.color = fillColor;
            Handles.DrawSolidArc(center, Vector3.up, startDirection, fullAngle, radius);
            Handles.color = lineColor;
            Handles.DrawWireArc(center, Vector3.up, startDirection, fullAngle, radius);
        }

        /// <summary>정면으로 뻗은 직사각형 (Line). 폭은 정면 축 양쪽으로 절반씩</summary>
        private static void DrawRectangle(Vector3 start, Vector3 flatForward, float length, float width, Color fillColor, Color lineColor)
        {
            Vector3 side = Vector3.Cross(Vector3.up, flatForward) * (width * 0.5f);
            Vector3 end = start + flatForward * length;
            var corners = new[] { start - side, start + side, end + side, end - side };
            Handles.DrawSolidRectangleWithOutline(corners, fillColor, lineColor);
        }

        /// <summary>높이를 버리고 수평 단위 방향으로 만든다. 판정(HitAreas.FlatDirection)과 같은 기준</summary>
        private static Vector3 FlatDirection(Vector3 forward)
        {
            return new Vector3(forward.x, 0f, forward.z).normalized;
        }
#endif
    }
}
