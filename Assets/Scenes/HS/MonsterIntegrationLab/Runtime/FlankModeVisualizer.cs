using System.Collections.Generic;
using HybridShooter.Monster.Behaviour;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace Scenes.HS.MonsterIntegrationLab.Runtime
{
    /// <summary>
    /// 테스트 전용. Flank 정책 몬스터의 원거리(Far)/근거리(Near) 모드와 판단 기준(링·전환 경계·목적지·시야·사거리·궤적)을 Scene 뷰 Gizmos 로 그린다.
    /// MonsterMovement·MonsterController 의 공개 값만 읽고 아무 것도 쓰지 않는다.
    /// FlankPolicy 의 실제 모드는 private 이라서, 같은 거리 히스테리시스 규칙으로 모드를 다시 계산해 추정한다 (Think 주기만큼 어긋날 수 있음).
    /// MonsterMovement·MonsterController 와 같은 오브젝트에 붙인다.
    /// </summary>
    public sealed class FlankModeVisualizer : MonoBehaviour
    {
        private enum EstimatedFlankMode { Far, Near }

        private struct TrailPoint
        {
            public Vector3 Position;
            public EstimatedFlankMode Mode;
            public float Time; // s
        }

        // 시야 반각 바깥 선택 여유 (도). FlankPolicy.BearingMarginDeg (FlankPolicy.cs) 복제 — 원본이 바뀌면 같이 고칠 것
        private const float ViewChooseMarginDeg = 30f;

        [Header("표시 항목")]
        [Tooltip("1. 플레이어 중심 flankRadius 원")]
        [SerializeField] private bool showFlankRing = true;
        [Tooltip("2. 전환 경계 2겹 (Near 진입 / Far 복귀)")]
        [SerializeField] private bool showSwitchBoundary = true;
        [Tooltip("3. 몬스터 → 실제 목적지 선 + 구")]
        [SerializeField] private bool showDestination = true;
        [Tooltip("4. 머리 위 모드·거리·상태 라벨")]
        [SerializeField] private bool showLabel = true;
        [Tooltip("5. 카메라 시야 반각 선 + 반각+30° 선")]
        [SerializeField] private bool showViewCone = true;
        [Tooltip("7. 플레이어 중심 공격 사거리 원")]
        [SerializeField] private bool showAttackRange = true;
        [Tooltip("8. 최근 이동 궤적 (모드 색)")]
        [SerializeField] private bool showTrail = true;

        [Header("궤적")]
        [Tooltip("궤적 보관 시간 (s)")]
        [SerializeField, Min(0f)] private float trailSeconds = 3f;
        [Tooltip("궤적 기록 간격 (s)")]
        [SerializeField, Min(0.01f)] private float trailSampleInterval = 0.1f;

        [Header("색")]
        [SerializeField] private Color farColor = new Color(1f, 0.55f, 0f);
        [SerializeField] private Color nearColor = Color.red;
        [SerializeField] private Color flankRingColor = new Color(0.4f, 0.8f, 1f);
        [SerializeField] private Color boundaryColor = Color.yellow;
        [SerializeField] private Color viewConeColor = Color.white;
        [SerializeField] private Color attackRangeColor = Color.green;

        // ── 참조 ──
        private MonsterMovement movement;
        private MonsterController controller;

        // ── 추정 상태·궤적 ──
        private EstimatedFlankMode estimatedMode = EstimatedFlankMode.Far; // FlankPolicy 시작값과 맞춤
        private readonly List<TrailPoint> trailPoints = new List<TrailPoint>();
        private float nextTrailSampleTime; // s

        private void Awake()
        {
            movement = GetComponent<MonsterMovement>();
            controller = GetComponent<MonsterController>();
        }

        /// <summary>Play 중 매 프레임 모드를 추정하고 궤적을 기록한다. 그리기는 OnDrawGizmos 가 맡는다</summary>
        private void Update()
        {
            if (HasTarget() == false) return;

            UpdateEstimatedMode();
            RecordTrail();
        }

        /// <summary>Scene 뷰에 표시 항목을 순서대로 그린다. 편집 모드에서는 Target·StateMachine 이 없어 그리지 않는다</summary>
        private void OnDrawGizmos()
        {
            if (Application.isPlaying == false) return;
            if (HasTarget() == false) return;

            DrawFlankRing();
            DrawSwitchBoundary();
            DrawDestination();
            DrawViewCone();
            DrawAttackRange();
            DrawTrail();
            DrawLabel();
        }

        /// <summary>추적 대상(플레이어)이 있는가. Update·OnDrawGizmos 의 공통 가드</summary>
        /// <returns>movement.Target 이 있으면 true</returns>
        private bool HasTarget() => movement.Target != null;

        /// <summary>몬스터와 플레이어 사이 수평 거리. 모드 추정과 라벨이 쓴다</summary>
        /// <returns>높이를 무시한 거리 (m)</returns>
        private float HorizontalDistanceToTarget()
        {
            Vector3 self = transform.position;
            Vector3 target = movement.Target.PlayerTrackingInfo.Position;
            self.y = 0f;
            target.y = 0f;
            return Vector3.Distance(self, target);
        }

        /// <summary>
        /// FlankPolicy 와 같은 히스테리시스 규칙으로 모드를 추정한다.
        /// 거리 ≤ 전환 거리면 Near, 거리 > 전환 거리 + 폭이면 Far, 사이는 이전 모드 유지.
        /// </summary>
        private void UpdateEstimatedMode()
        {
            MovementTuning tuning = movement.Profile.ToTuning();
            float distance = HorizontalDistanceToTarget();

            if (distance <= tuning.FlankSwitchDistance)
                estimatedMode = EstimatedFlankMode.Near;
            else if (distance > tuning.FlankSwitchDistance + tuning.FlankSwitchBand)
                estimatedMode = EstimatedFlankMode.Far;
        }

        /// <summary>기록 간격마다 (위치, 모드, 시각)을 추가하고 trailSeconds 보다 오래된 점을 지운다</summary>
        private void RecordTrail()
        {
            if (Time.time < nextTrailSampleTime) return;

            trailPoints.Add(new TrailPoint { Position = transform.position, Mode = estimatedMode, Time = Time.time });
            nextTrailSampleTime = Time.time + trailSampleInterval;

            float oldestAllowedTime = Time.time - trailSeconds;
            trailPoints.RemoveAll(point => point.Time < oldestAllowedTime);
        }

        /// <summary>1. 원거리 우회 목적지가 놓이는 플레이어 중심 flankRadius 원</summary>
        private void DrawFlankRing()
        {
            if (showFlankRing == false) return;

            Gizmos.color = flankRingColor;
            DrawRing(movement.Target.PlayerTrackingInfo.Position, movement.Profile.ToTuning().FlankRadius);
        }

        /// <summary>2. Near 진입 원(전환 거리)과 Far 복귀 원(전환 거리 + 폭)</summary>
        private void DrawSwitchBoundary()
        {
            if (showSwitchBoundary == false) return;

            MovementTuning tuning = movement.Profile.ToTuning();
            Vector3 center = movement.Target.PlayerTrackingInfo.Position;
            Gizmos.color = boundaryColor;
            DrawRing(center, tuning.FlankSwitchDistance);
            DrawRing(center, tuning.FlankSwitchDistance + tuning.FlankSwitchBand);
        }

        /// <summary>3. 몬스터 → 마지막 판단 목적지(LastIntent.Destination) 선 + 작은 구. 추정 모드 색</summary>
        private void DrawDestination()
        {
            if (showDestination == false) return;

            Vector3 destination = movement.LastIntent.Destination;
            Gizmos.color = ModeColor(estimatedMode);
            Gizmos.DrawLine(transform.position, destination);
            Gizmos.DrawSphere(destination, 0.3f);
        }

        /// <summary>
        /// 5. 플레이어 카메라 위치에서 수평 정면 기준 ±반각 선(시야 경계)과 ±(반각 + 30°) 선(선택 가능 경계).
        /// 길이는 Far 복귀 거리(전환 거리 + 폭).
        /// </summary>
        private void DrawViewCone()
        {
            if (showViewCone == false) return;

            MovementTuning tuning = movement.Profile.ToTuning();
            Transform viewPoint = movement.Target.ViewPoint;
            float halfAngleDeg = HorizontalHalfFovDeg(viewPoint.GetComponent<Camera>());
            float length = tuning.FlankSwitchDistance + tuning.FlankSwitchBand;
            Vector3 origin = viewPoint.position;
            Vector3 forward = viewPoint.forward;
            forward.y = 0f;
            forward.Normalize();

            Gizmos.color = viewConeColor;
            DrawBearingLine(origin, forward, halfAngleDeg, length);
            DrawBearingLine(origin, forward, -halfAngleDeg, length);

            Color marginColor = viewConeColor;
            marginColor.a = 0.4f;
            Gizmos.color = marginColor;
            DrawBearingLine(origin, forward, halfAngleDeg + ViewChooseMarginDeg, length);
            DrawBearingLine(origin, forward, -(halfAngleDeg + ViewChooseMarginDeg), length);
        }

        /// <summary>DrawViewCone 이 쓰는 한 방향 선. 수평 정면에서 Y 축으로 돌린 방향으로 length 만큼 긋는다</summary>
        /// <param name="origin">선 시작점 (카메라 위치)</param>
        /// <param name="forward">수평 정면 (정규화)</param>
        /// <param name="angleDeg">정면 기준 회전 각 (도, + = 오른쪽)</param>
        /// <param name="length">선 길이 (m)</param>
        private static void DrawBearingLine(Vector3 origin, Vector3 forward, float angleDeg, float length)
        {
            Vector3 direction = Quaternion.AngleAxis(angleDeg, Vector3.up) * forward;
            Gizmos.DrawLine(origin, origin + direction * length);
        }

        /// <summary>
        /// 카메라 FOV 의 수평 반각. MonsterMovement.HorizontalHalfFovDeg (MonsterMovement.cs, private) 와 같은 식 복제 — 원본이 바뀌면 같이 고칠 것.
        /// </summary>
        /// <param name="viewCamera">플레이어 ViewPoint 의 카메라</param>
        /// <returns>수평 반각 (도) = atan(tan(세로 FOV / 2) × aspect)</returns>
        private static float HorizontalHalfFovDeg(Camera viewCamera)
        {
            float verticalHalfRad = viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float horizontalHalfRad = Mathf.Atan(Mathf.Tan(verticalHalfRad) * viewCamera.aspect);
            return horizontalHalfRad * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 7. 플레이어 중심 실제 이동 사거리 원 (첫 공격 사거리, MonsterController 가 SetCombatTuning 에 넘기는 값).
        /// 사거리 안 + 공격 허가면 진하게, 아니면 반투명.
        /// </summary>
        private void DrawAttackRange()
        {
            if (showAttackRange == false) return;

            bool isReadyToAttack = movement.IsInAttackRange && movement.CanAttack;
            Color color = attackRangeColor;
            if (isReadyToAttack == false)
                color.a = 0.3f;

            Gizmos.color = color;
            DrawRing(movement.Target.PlayerTrackingInfo.Position, controller.StateMachine.Attacks[0].Range);
        }

        /// <summary>8. 기록된 궤적 점을 차례로 이어 그린다. 각 선분은 뒤 점의 모드 색</summary>
        private void DrawTrail()
        {
            if (showTrail == false) return;

            for (int i = 1; i < trailPoints.Count; i++)
            {
                Gizmos.color = ModeColor(trailPoints[i].Mode);
                Gizmos.DrawLine(trailPoints[i - 1].Position, trailPoints[i].Position);
            }
        }

        /// <summary>4. 머리 위 라벨. 예: "Far 16.2m / Approach / 사거리 O / 허가 X"</summary>
        private void DrawLabel()
        {
            if (showLabel == false) return;

#if UNITY_EDITOR
            string inRangeMark = movement.IsInAttackRange ? "O" : "X";
            string canAttackMark = movement.CanAttack ? "O" : "X";
            string label = $"{estimatedMode} {HorizontalDistanceToTarget():F1}m / {movement.State} / 사거리 {inRangeMark} / 허가 {canAttackMark}";
            UnityEditor.Handles.Label(transform.position + Vector3.up * 2.5f, label);
#endif
        }

        /// <summary>모드별 표시 색</summary>
        /// <param name="mode">추정 모드</param>
        /// <returns>Far = farColor, Near = nearColor</returns>
        private Color ModeColor(EstimatedFlankMode mode)
        {
            if (mode == EstimatedFlankMode.Near)
                return nearColor;
            return farColor;
        }

        /// <summary>수평 원을 48분할 선으로 그린다 (MovementLabGizmos.DrawRing 과 같은 방식). 색은 호출 전 Gizmos.color</summary>
        /// <param name="center">원 중심 (월드)</param>
        /// <param name="radius">반지름 (m)</param>
        private static void DrawRing(Vector3 center, float radius)
        {
            const int segments = 48;
            Vector3 previous = center + Vector3.forward * radius;
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }
    }
}
