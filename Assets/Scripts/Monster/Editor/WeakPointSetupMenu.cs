using System.Linq;
using HybridShooter.Boss;
using HybridShooter.Monster.Behaviour;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Monster.EditorTools
{
    /// <summary>
    /// 몬스터·보스 프리팹의 뼈 아래에 약점 콜라이더(SphereCollider + 약점 컴포넌트)를 단다.
    /// 자식 이름은 "WeakPoint_부위키" 이고, 이미 있으면 건너뛴다 → 메뉴를 다시 실행해도 Inspector 에서 조정한 위치·크기가 그대로 남는다.
    /// 강화 개체 프리팹(_Elite)은 원본의 변종이라 따로 달지 않아도 약점을 물려받는다.
    /// </summary>
    public static class WeakPointSetupMenu
    {
        /// <summary>약점 콜라이더 하나의 배치 정보</summary>
        private readonly struct WeakPointPlacement
        {
            public readonly string PrefabPath;
            public readonly string BoneName;
            public readonly string PartKey;  // MonsterPartTable 의 PartKey
            public readonly float Radius;    // 뼈 로컬 단위 (뼈 스케일을 따라간다)
            public readonly bool IsBoss;     // true = BossWeakPoint, false = MonsterWeakPoint

            public WeakPointPlacement(string prefabPath, string boneName, string partKey, float radius, bool isBoss)
            {
                PrefabPath = prefabPath;
                BoneName = boneName;
                PartKey = partKey;
                Radius = radius;
                IsBoss = isBoss;
            }
        }

        private const string MonsterFolder = "Assets/Resources/Prefabs/Monsters";
        private const string ClypeosaurusPath = "Assets/Resources/Boss/Clypeosaurus/Prefab/Clypeosaurus.prefab";

        // ── 배치 목록 (부위를 늘리려면 여기에 한 줄 + MonsterPartTable 에 한 행) ──
        private static readonly WeakPointPlacement[] Placements =
        {
            new WeakPointPlacement($"{MonsterFolder}/Letalobrach.prefab", "Letalobrach_Jaw", "Mouth", 0.4f, false),
            new WeakPointPlacement($"{MonsterFolder}/Pardathrox.prefab", "Pardathrox_ Spine1", "Spine", 0.4f, false),
            new WeakPointPlacement($"{MonsterFolder}/GiantSlug.prefab", "GiantSlug_WeakPointLeft", "Gland", 0.5f, false),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Head", "Head", 1f, true),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Antenna_1", "Antenna", 0.3f, true),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Antenna_2", "Antenna", 0.3f, true),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Antenna_3", "Antenna", 0.3f, true),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Antenna_4", "Antenna", 0.3f, true),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Antenna_5", "Antenna", 0.3f, true),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Antenna_6", "Antenna", 0.3f, true),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Antenna_7", "Antenna", 0.3f, true),
            new WeakPointPlacement(ClypeosaurusPath, "Clypeosaurus_Antenna_8", "Antenna", 0.3f, true),
        };

        /// <summary>배치 목록을 프리팹별로 묶어 열고, 빠진 약점 콜라이더만 달고 저장한다</summary>
        [MenuItem("Tools/HybridShooter/Setup Weak Points (부위 피해 콜라이더)")]
        public static void Setup()
        {
            foreach (var placementsInPrefab in Placements.GroupBy(placement => placement.PrefabPath))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(placementsInPrefab.Key);
                foreach (WeakPointPlacement placement in placementsInPrefab) AddWeakPoint(root, placement);
                PrefabUtility.SaveAsPrefabAsset(root, placementsInPrefab.Key);
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("[WeakPointSetup] 약점 콜라이더 세팅 완료 (이미 있던 콜라이더는 그대로 둠)");
        }

        /// <summary>
        /// 뼈 아래에 WeakPoint_부위키 자식을 만들고 SphereCollider 와 약점 컴포넌트를 붙인다. 이미 있으면 건드리지 않는다.
        /// </summary>
        /// <param name="root">열어 둔 프리팹 루트</param>
        /// <param name="placement">달 뼈·부위키·반지름</param>
        /// <exception cref="System.InvalidOperationException">뼈 이름을 프리팹에서 찾지 못했을 때 (First)</exception>
        private static void AddWeakPoint(GameObject root, WeakPointPlacement placement)
        {
            string objectName = $"WeakPoint_{placement.PartKey}";
            Transform bone = root.GetComponentsInChildren<Transform>(true).First(t => t.name == placement.BoneName);
            if (bone.Find(objectName) != null) return;

            var weakPoint = new GameObject(objectName);
            weakPoint.layer = root.layer;
            weakPoint.transform.SetParent(bone, false);
            var sphere = weakPoint.AddComponent<SphereCollider>();
            sphere.radius = placement.Radius;

            Component part;
            if (placement.IsBoss) part = weakPoint.AddComponent<BossWeakPoint>();
            else part = weakPoint.AddComponent<MonsterWeakPoint>();
            var serialized = new SerializedObject(part);
            serialized.FindProperty("partKey").stringValue = placement.PartKey;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
