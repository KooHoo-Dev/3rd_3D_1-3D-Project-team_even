using HybridShooter.Core.Data;
using HybridShooter.Core.Events;
using HybridShooter.Supply;
using UnityEngine;

namespace HybridShooter.Atmosphere
{
    /// <summary>
    /// 보급 빛기둥을 안개를 무시하는 HDR 재질로 바꾼다 (SupplyCrate 코드는 건드리지 않음).
    ///
    /// SupplyCrate 가 만드는 기본 빛기둥은 URP Unlit 이라 짙은 안개 속에서 같이 사라진다.
    /// SupplyDroppedEvent 는 SupplyDropper 가 상자를 목록에 넣고 Launch(빛기둥 생성)한 직후 발행되므로,
    /// 그 시점에 목록의 마지막 상자에서 "Beacon" 자식을 찾아 재질·크기를 바꾼다.
    /// 이 컴포넌트가 없는 씬에서는 원래 빛기둥 그대로다.
    /// </summary>
    public sealed class SupplyBeaconOverride : MonoBehaviour
    {
        /// <summary>SupplyCrate.CreateBeacon 이 붙이는 자식 이름</summary>
        private const string BeaconName = "Beacon";

        [Tooltip("HybridShooter/Atmosphere/Supply Beacon (No Fog) 셰이더 재질")]
        [SerializeField] private Material beaconMaterial;
        [SerializeField, ColorUsage(false, true)] private Color grenadeColor = new Color(4f, 1.8f, 0.4f);
        [Tooltip("청회색 안개와 겹치지 않도록 초록보다 청록-흰색 쪽")]
        [SerializeField, ColorUsage(false, true)] private Color healColor = new Color(1.2f, 3.6f, 3f);
        [Tooltip("빛기둥 높이 (m). 0 이면 SupplyCrate 설정 그대로")]
        [SerializeField, Min(0f)] private float beaconHeight = 60f;
        [Tooltip("빛기둥 굵기 (m). 멀리서는 셰이더가 화면상 최소 굵기를 보장한다")]
        [SerializeField, Min(0.05f)] private float beaconWidth = 0.6f;

        private Material grenadeMaterial;
        private Material healMaterial;
        private SupplyDropper dropper;

        /// <summary>종류별 재질 인스턴스를 만든다 (빛기둥마다 만들지 않고 공유)</summary>
        private void Awake()
        {
            if (beaconMaterial == null) return;
            grenadeMaterial = new Material(beaconMaterial) { name = beaconMaterial.name + "_Grenade" };
            grenadeMaterial.SetColor("_Color", grenadeColor);
            healMaterial = new Material(beaconMaterial) { name = beaconMaterial.name + "_Heal" };
            healMaterial.SetColor("_Color", healColor);
        }

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
            if (grenadeMaterial != null) Destroy(grenadeMaterial);
            if (healMaterial != null) Destroy(healMaterial);
        }

        /// <summary>방금 투하된 상자(목록의 마지막)의 빛기둥을 교체</summary>
        private void OnSupplyDropped(SupplyDroppedEvent evt)
        {
            if (grenadeMaterial == null || evt.Def == null) return;
            if (dropper == null) dropper = FindAnyObjectByType<SupplyDropper>();
            if (dropper == null || dropper.ActiveCrates.Count == 0) return;

            var crate = dropper.ActiveCrates[dropper.ActiveCrates.Count - 1];
            var beacon = crate != null ? crate.transform.Find(BeaconName) : null;
            if (beacon == null) return;

            beacon.GetComponent<Renderer>().sharedMaterial =
                evt.Def.SupplyType == SupplyType.Grenade ? grenadeMaterial : healMaterial;

            if (beaconHeight > 0f)
            {
                // 유니티 기본 실린더 높이 2m 기준 → y 스케일 = 높이/2, 바닥이 상자 피벗에 오도록
                beacon.localPosition = Vector3.up * (beaconHeight * 0.5f);
                beacon.localScale = new Vector3(beaconWidth, beaconHeight * 0.5f, beaconWidth);
            }
        }
    }
}
