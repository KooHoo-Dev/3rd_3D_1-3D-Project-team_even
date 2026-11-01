using HybridShooter.Core.Contracts;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace HybridShooter.Monster.MovementLab
{
    /// <summary>Scene에 독립적으로 붙여 실제 core snapshot과 이동 의도를 그린다.</summary>
    public sealed class MovementLabGizmos : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private CrowdController coordinator;
        [SerializeField] private MonoBehaviour targetSource;
        [SerializeField] private Transform optionalMonsterRoot;

        [Header("Visibility")]
        [SerializeField] private bool showApproach = true;
        [SerializeField] private bool showWaiting = true;
        [SerializeField] private bool showFree = true;
        [SerializeField] private bool showAssigned = true;
        [SerializeField] private bool showInvalid = true;
        [SerializeField] private bool showLines = true;
        [SerializeField] private bool showMonsterIds = true;
        [SerializeField] private bool showSlotIds = true;
        [SerializeField] private bool showLabels = true;
        [SerializeField, Min(1)] private int maxDetailedMonsters = 32;
        [SerializeField, Min(0.05f)] private float slotSize = 0.28f;

        [Header("Colors")]
        [SerializeField] private Color approachColor = new Color(.25f, .75f, 1f);
        [SerializeField] private Color waitingColor = new Color(1f, .7f, .2f);
        [SerializeField] private Color freeColor = Color.white;
        [SerializeField] private Color assignedColor = new Color(.2f, 1f, .35f);
        [SerializeField] private Color invalidColor = Color.red;
        [SerializeField] private Color reservationLineColor = new Color(1f, .8f, .1f);
        [SerializeField] private Color intentLineColor = new Color(1f, .35f, .15f);

        /// <summary>Scene 뷰 몬스터 머리 위 텍스트 표시 여부. MovementLabController 의 GUI 토글이 설정한다</summary>
        public bool ShowMonsterIds { get => showMonsterIds; set => showMonsterIds = value; }

        /// <summary>Scene 뷰 링(슬롯) 위 텍스트 표시 여부. MovementLabController 의 GUI 토글이 설정한다</summary>
        public bool ShowSlotIds { get => showSlotIds; set => showSlotIds = value; }

        public void Configure(CrowdController source, Transform monsterRoot = null)
        {
            coordinator = source;
            optionalMonsterRoot = monsterRoot;
            targetSource = source != null ? source.Target as MonoBehaviour : null;
        }

        private void OnDrawGizmos()
        {
            if (!enabled) return;
            var target = coordinator != null ? coordinator.Target : targetSource as IPlayerTarget;
            if (coordinator == null) return;

            if (target != null)
            {
                var settings = coordinator.Settings;
                // 편집 모드에선 PlayerTrackingInfo 가 갱신되지 않으므로 transform 을 직접 읽는다
                Vector3 targetPosition = target.Root.position;
                Gizmos.color = new Color(.2f, .8f, 1f, .7f);
                DrawRing(targetPosition, settings.NearRadius);
                Gizmos.color = new Color(1f, .25f, .2f, .5f);
                DrawRing(targetPosition, settings.ReleaseRadius);
                if (settings.UseSlots && showApproach)
                {
                    Gizmos.color = approachColor;
                    DrawRing(targetPosition, settings.Slots.ApproachRadius);
                }
                if (showWaiting)
                {
                    Gizmos.color = waitingColor;
                    if (!settings.UseSlots)
                        DrawRing(targetPosition, settings.Slots.WaitingRadius);
                    else
                    {
                        int lastRing = -1;
                        for (int i = 0; i < coordinator.SlotCount; i++)
                        {
                            SlotSnapshot slot = coordinator.GetSlotSnapshot(i);
                            if (slot.RingIndex == 0 || slot.RingIndex == lastRing) continue;
                            DrawRing(targetPosition, (slot.CandidatePosition - targetPosition).magnitude);
                            lastRing = slot.RingIndex;
                        }
                    }
                }
            }

            int detailed = 0;
            var agents = coordinator.Agents;
            for (int i = 0; i < agents.Count; i++)
                if (agents[i] != null) DrawMonster(agents[i], ref detailed);

            if (optionalMonsterRoot != null)
            {
                for (int i = 0; i < optionalMonsterRoot.childCount; i++)
                    DrawDirectMonsters(optionalMonsterRoot.GetChild(i), agents, ref detailed);
            }

            for (int i = 0; coordinator.Settings.UseSlots && i < coordinator.SlotCount; i++)
            {
                var slot = coordinator.GetSlotSnapshot(i);
                if (slot.RingIndex == 0 && !showApproach || slot.RingIndex != 0 && !showWaiting) continue;
                if (slot.Validity != SlotValidity.Valid && !showInvalid) continue;
                if (slot.Validity == SlotValidity.Valid && slot.Use == SlotUse.Free && !showFree) continue;
                if (slot.Validity == SlotValidity.Valid && slot.Use != SlotUse.Free && !showAssigned) continue;

                Color kindColor = slot.RingIndex == 0 ? approachColor : waitingColor;
                Color color = slot.Validity != SlotValidity.Valid ? invalidColor
                    : slot.Use == SlotUse.Free ? Color.Lerp(kindColor, freeColor, .5f) : assignedColor;
                if (slot.Congested) color = Color.Lerp(color, invalidColor, .65f);
                Gizmos.color = color;
                if (slot.Validity == SlotValidity.Valid)
                {
                    if (slot.RingIndex == 0)
                        Gizmos.DrawWireSphere(slot.Position + Vector3.up * .08f, slotSize);
                    else
                        Gizmos.DrawWireCube(slot.Position + Vector3.up * .08f, Vector3.one * slotSize * 2f);
                    if (slot.Use == SlotUse.Occupied)
                        Gizmos.DrawSphere(slot.Position + Vector3.up * .08f, slotSize * 0.45f);
                    if (slot.Congested) Gizmos.DrawWireSphere(slot.Position + Vector3.up * .1f, slotSize * 1.1f);
                    if (slot.OwnerId != 0 && showLines && detailed < maxDetailedMonsters && FindAgent(slot.OwnerId) is MonsterMovement owner)
                        DrawLink(owner.transform.position, slot.Position, reservationLineColor);
                }
                else
                    DrawX(slot.CandidatePosition, slotSize * .75f);

                if (showLabels && showSlotIds)
                {
                    string label = $"slot {slot.Id} ring:{slot.RingIndex} {slot.Use}";
                    if (slot.OwnerId != 0) label += $" owner:{slot.OwnerId}";
                    if (slot.Congested) label += " CONGESTED";
                    if (slot.Validity != SlotValidity.Valid) label += $" {slot.Validity}";
                    DrawLabel(slot.Validity == SlotValidity.Valid ? slot.Position : slot.CandidatePosition, label);
                }
            }
        }

        private void DrawDirectMonsters(Transform node, System.Collections.Generic.IReadOnlyList<MonsterMovement> agents, ref int detailed)
        {
            var movement = node.GetComponent<MonsterMovement>();
            if (movement != null)
            {
                // 몬스터 프리팹 내부(본 계층)는 탐색하지 않는다. 사망(SetActive false)한 몬스터는 그리지 않는다
                bool registered = false;
                for (int i = 0; i < agents.Count; i++) if (agents[i] == movement) { registered = true; break; }
                if (!registered && movement.isActiveAndEnabled) DrawMonster(movement, ref detailed);
                return;
            }
            for (int i = 0; i < node.childCount; i++) DrawDirectMonsters(node.GetChild(i), agents, ref detailed);
        }

        private void DrawMonster(MonsterMovement movement, ref int detailed)
        {
            Gizmos.color = movement.ExecutionState == MoveExecutionState.OffNavMesh ? invalidColor : assignedColor;
            Gizmos.DrawWireSphere(movement.transform.position + Vector3.up * .1f, movement.Radius);
            if (showLines && detailed < maxDetailedMonsters)
            {
                if (movement.Directive.HasSlotPosition && movement.Directive.Kind == DirectiveKind.Engage)
                    DrawLink(movement.transform.position, movement.Directive.SlotPosition, reservationLineColor);
                if (movement.LastIntent.HasDestination)
                {
                    DrawLink(movement.transform.position, movement.LastIntent.Destination, intentLineColor);
                    Gizmos.DrawWireSphere(movement.LastIntent.Destination, slotSize * .8f);
                }
            }
            if (showLabels && showMonsterIds && detailed < maxDetailedMonsters)
            {
                string label = $"monster {movement.GetInstanceID()} {movement.Profile.PolicyKind} {movement.State}";
                if (movement.Directive.HasSlotPosition) label += $" slot:{movement.Directive.SlotId}";
                label += $" {movement.ExecutionState}";
                DrawLabel(movement.transform.position + Vector3.up * 2f, label);
            }
            detailed++;
        }

        private MonsterMovement FindAgent(int instanceId)
        {
            var agents = coordinator.Agents;
            for (int i = 0; i < agents.Count; i++)
                if (agents[i] != null && agents[i].GetInstanceID() == instanceId) return agents[i];
            return FindDirect(instanceId, optionalMonsterRoot);
        }

        private static MonsterMovement FindDirect(int instanceId, Transform root)
        {
            if (root == null) return null;
            var movement = root.GetComponent<MonsterMovement>();
            if (movement != null) return movement.GetInstanceID() == instanceId ? movement : null;
            for (int i = 0; i < root.childCount; i++)
            {
                movement = FindDirect(instanceId, root.GetChild(i));
                if (movement != null) return movement;
            }
            return null;
        }

        private static void DrawLink(Vector3 from, Vector3 to, Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(from + Vector3.up * .15f, to + Vector3.up * .15f);
        }

        private static void DrawX(Vector3 center, float size)
        {
            Vector3 x = Vector3.right * size;
            Vector3 z = Vector3.forward * size;
            Gizmos.DrawLine(center - x - z, center + x + z);
            Gizmos.DrawLine(center - x + z, center + x - z);
        }

        private static void DrawRing(Vector3 center, float radius)
        {
            const int segments = 48;
            Vector3 previous = center + Vector3.forward * radius;
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Sin(a) * radius, 0f, Mathf.Cos(a) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        private static void DrawLabel(Vector3 position, string label)
        {
#if UNITY_EDITOR
            UnityEditor.Handles.Label(position, label);
#endif
        }
    }
}
