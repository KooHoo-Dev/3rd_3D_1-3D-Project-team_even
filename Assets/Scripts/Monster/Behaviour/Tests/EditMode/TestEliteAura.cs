using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// 강화·자폭 변형종 프리팹이 몸 렌더러마다 오라 재질을 마지막 칸에 달고 있는지,
    /// 오라 셰이더가 에러 없이 컴파일되는지 확인한다. 프리팹은 MonsterTable.PrefabKey 경로(Resources)로 읽는다.
    /// </summary>
    public sealed class TestEliteAura
    {
        private const string AuraShaderName = "HybridShooter/Monster/EliteAura";
        private const string EliteMaterialName = "EliteAura_Elite";
        private const string SelfDestructMaterialName = "EliteAura_SelfDestruct";

        [TestCase("Prefabs/Monsters/Bufomorph_Elite", EliteMaterialName)]
        [TestCase("Prefabs/Monsters/Muscomorph_Ground_Elite", EliteMaterialName)]
        [TestCase("Prefabs/Monsters/Muscomorph_Air_Elite", EliteMaterialName)]
        [TestCase("Prefabs/Monsters/Densoptere_Elite", EliteMaterialName)]
        [TestCase("Prefabs/Monsters/Letalobrach_Elite", EliteMaterialName)]
        [TestCase("Prefabs/Monsters/Pardathrox_Elite", EliteMaterialName)]
        [TestCase("Prefabs/Monsters/GiantSlug_Elite", EliteMaterialName)]
        [TestCase("Prefabs/Monsters/Bufomorph_Suicide", SelfDestructMaterialName)]
        public void EveryBodyRendererEndsWithAura(string prefabPath, string auraMaterialName)
        {
            var prefab = Resources.Load<GameObject>(prefabPath);
            Assert.IsNotNull(prefab, prefabPath);
            var renderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Assert.IsNotEmpty(renderers);
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                Assert.AreEqual(2, materials.Length, renderer.name);
                Assert.IsNotNull(materials[0], renderer.name);
                Assert.AreEqual(auraMaterialName, materials[1].name, renderer.name);
                Assert.AreEqual(AuraShaderName, materials[1].shader.name, renderer.name);
            }
        }

        [TestCase("Prefabs/Monsters/Bufomorph")]
        [TestCase("Prefabs/Monsters/Muscomorph_Air")]
        public void OriginalPrefabsHaveNoAura(string prefabPath)
        {
            var prefab = Resources.Load<GameObject>(prefabPath);
            foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Assert.AreEqual(1, renderer.sharedMaterials.Length, renderer.name);
        }

        [Test]
        public void AuraShaderCompilesWithoutError()
        {
            var shader = Shader.Find(AuraShaderName);
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
            Assert.IsTrue(shader.isSupported);
        }
    }
}
