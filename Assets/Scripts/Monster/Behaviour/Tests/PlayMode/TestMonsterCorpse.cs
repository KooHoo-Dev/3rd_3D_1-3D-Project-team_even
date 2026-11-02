using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Data;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HybridShooter.Monster.Behaviour.Tests
{
    /// <summary>
    /// MonsterCorpse 시체 수명(누움 → 침강 → 회수)과 자폭형 처치 폭발을 실제 프리팹·테이블로 확인한다 (S2).
    /// 바닥은 BoxCollider 하나. NavMesh 가 없어 몬스터는 움직이지 않는다.
    /// </summary>
    public sealed class TestMonsterCorpse
    {
        private const int Bufomorph = 1001;
        private const int BufomorphSuicide = 1005;
        private const int Letalobrach = 2001;
        private const int GiantSlug = 2003;
        private const float LyingSeconds = 4f;      // MonsterCorpse 기본값
        private const float SinkSeconds = 1.5f;     // MonsterCorpse 기본값

        private MonsterPool pool;
        private GameObject floor;
        private readonly List<GameObject> spawnedMonsters = new List<GameObject>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(100f, 1f, 100f);
            pool = new GameObject("MonsterPool").AddComponent<MonsterPool>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject monster in spawnedMonsters.Distinct()) Object.DestroyImmediate(monster);
            spawnedMonsters.Clear();
            if (pool != null) Object.DestroyImmediate(pool.gameObject);
            Object.DestroyImmediate(floor);
        }

        private GameObject Spawn(int monsterId, Vector3 position)
        {
            GameObject monster = pool.Spawn(DataManager.Monsters.Get(monsterId), position, Quaternion.identity);
            spawnedMonsters.Add(monster);
            return monster;
        }

        private static void Kill(GameObject monster)
        {
            var controller = monster.GetComponent<MonsterController>();
            controller.TakeDamage(new DamageInfo(controller.CurrentHp, DamageKind.Bullet, Faction.Player, null,
                                                 monster.transform.position, Vector3.forward));
        }

        [UnityTest]
        public IEnumerator GiantSlugCorpse_StaysOnFloor_WithChildBodyColliderOnCorpseLayer()
        {
            GameObject slug = Spawn(GiantSlug, Vector3.zero);
            yield return null;
            Kill(slug);
            yield return new WaitForSeconds(1.5f);

            Collider hitCollider = slug.transform.Find("Hit Collider").GetComponent<Collider>();
            Assert.IsTrue(hitCollider.enabled, "자식 몸통 콜라이더가 시체 몸으로 남아야 한다");
            Assert.AreEqual(LayerMask.NameToLayer("MonsterCorpse"), hitCollider.gameObject.layer);
            Assert.Greater(slug.transform.position.y, -0.5f, "바닥을 뚫고 떨어지면 안 된다");
        }

        [UnityTest]
        public IEnumerator Corpse_LiesThenSinksKeepingRotation_ThenReturnsToPool()
        {
            GameObject monster = Spawn(Letalobrach, Vector3.zero);
            yield return null;
            Kill(monster);
            yield return new WaitForSeconds(LyingSeconds - 0.5f);
            Assert.IsTrue(monster.activeSelf, "누워 있는 동안은 회수되지 않는다");
            float lyingY = monster.transform.position.y;

            yield return new WaitForSeconds(0.5f + SinkSeconds * 0.5f);
            Quaternion sinkingRotation = monster.transform.rotation;
            Assert.Less(monster.transform.position.y, lyingY - 0.1f, "침강 중이면 아래로 내려간다");
            Assert.IsTrue(monster.GetComponent<Rigidbody>().isKinematic, "침강 중에는 물리를 멈춘다");
            yield return null;
            Assert.AreEqual(sinkingRotation, monster.transform.rotation, "침강 중 회전은 그대로");

            yield return new WaitForSeconds(SinkSeconds);
            Assert.IsFalse(monster.activeSelf, "침강이 끝나면 풀로 회수된다");

            GameObject reused = Spawn(Letalobrach, new Vector3(2f, 0f, 2f));
            Assert.AreSame(monster, reused);
            yield return null; // Restore 의 Destroy(Rigidbody) 는 프레임 끝에 반영된다
            Assert.IsFalse(reused.TryGetComponent(out Rigidbody _), "재사용하면 시체 Rigidbody 를 뗀다");
            Assert.AreNotEqual(LayerMask.NameToLayer("MonsterCorpse"), reused.layer);
        }

        [UnityTest]
        public IEnumerator SelfDestructKilledByPlayer_ExplodesAndDamagesNearbyMonster()
        {
            GameObject suicide = Spawn(BufomorphSuicide, Vector3.zero);
            GameObject neighbor = Spawn(Bufomorph, new Vector3(2f, 0f, 0f));
            yield return null;
            var neighborController = neighbor.GetComponent<MonsterController>();
            int neighborHpBefore = neighborController.CurrentHp;

            Kill(suicide);

            Assert.Less(neighborController.CurrentHp, neighborHpBefore, "처치 폭발이 반경 안 몬스터에게 중립 피해를 준다");
        }
    }
}
