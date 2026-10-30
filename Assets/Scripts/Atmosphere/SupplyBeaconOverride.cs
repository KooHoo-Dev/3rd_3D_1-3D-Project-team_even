using System.Collections.Generic;
using HybridShooter.Core.Events;
using UnityEngine;

namespace HybridShooter.Atmosphere
{
    /// <summary>
    /// 보급 빛기둥을 안개를 무시하는 HDR 재질로 바꾼다 (SupplyCrate 코드는 건드리지 않음).
    ///
    /// SupplyCrate 가 만드는 기본 빛기둥은 URP Unlit 이라 짙은 안개 속에서 같이 사라진다.
    /// SupplyDroppedEvent 는 상자를 Launch(빛기둥 생성)한 직후 발행되므로, 이벤트의 상자(Crate)에서 "Beacon" 자식을 찾아 재질·크기를 바꾼다.
    /// 색 = SupplyTable.BeaconColor × hdrIntensity (종류가 늘어도 테이블에 행만 추가하면 된다).
    /// 이 컴포넌트가 없는 씬에서는 원래 빛기둥 그대로다.
    /// </summary>
    public sealed class SupplyBeaconOverride : MonoBehaviour
    {
        /// <summary>SupplyCrate.CreateBeacon 이 붙이는 자식 이름</summary>
        private const string BeaconName = "Beacon";

        [Tooltip("HybridShooter/Atmosphere/Supply Beacon (No Fog) 셰이더 재질")]
        [SerializeField] private Material beaconMaterial;
        [Tooltip("SupplyTable.BeaconColor 에 곱하는 HDR 밝기 (안개를 뚫고 보이도록 1 보다 크게)")]
        [SerializeField, Min(1f)] private float hdrIntensity = 3.8f;
        [Tooltip("빛기둥 높이 (m). 0 이면 SupplyCrate 설정 그대로")]
        [SerializeField, Min(0f)] private float beaconHeight = 60f;
        [Tooltip("빛기둥 굵기 (m). 멀리서는 셰이더가 화면상 최소 굵기를 보장한다")]
        [SerializeField, Min(0.05f)] private float beaconWidth = 0.6f;

        /// <summary>보급 Id 별 재질 인스턴스 (빛기둥마다 만들지 않고 공유)</summary>
        private readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();

        private void OnEnable()
        {
            EventBus<SupplyDroppedEvent>.Subscribe(OnSupplyDropped);
        }

        private void OnDisable()
        {
            EventBus<SupplyDroppedEvent>.Unsubscribe(OnSupplyDropped);
        }

        /// <summary>코드로 만든 재질 정리 (메모리 누수 방지)</summary>
        private void OnDestroy()
        {
            foreach (var material in materials.Values)
                if (material != null) Destroy(material);
            materials.Clear();
        }

        /// <summary>방금 투하된 상자의 빛기둥을 교체</summary>
        private void OnSupplyDropped(SupplyDroppedEvent evt)
        {
            if (beaconMaterial == null || evt.Def == null || evt.Crate == null) return;

            var beacon = evt.Crate.transform.Find(BeaconName);
            if (beacon == null) return;

            if (materials.TryGetValue(evt.Def.Id, out var material) == false)
            {
                material = new Material(beaconMaterial) { name = $"{beaconMaterial.name}_{evt.Def.Id}" };
                Color color = evt.Def.BeaconColor * hdrIntensity;
                color.a = 1f;
                material.SetColor("_Color", color);
                materials.Add(evt.Def.Id, material);
            }
            beacon.GetComponent<Renderer>().sharedMaterial = material;

            if (beaconHeight > 0f)
            {
                // 유니티 기본 실린더 높이 2m 기준 → y 스케일 = 높이/2, 바닥이 상자 피벗에 오도록
                beacon.localPosition = Vector3.up * (beaconHeight * 0.5f);
                beacon.localScale = new Vector3(beaconWidth, beaconHeight * 0.5f, beaconWidth);
            }
        }
    }
}
