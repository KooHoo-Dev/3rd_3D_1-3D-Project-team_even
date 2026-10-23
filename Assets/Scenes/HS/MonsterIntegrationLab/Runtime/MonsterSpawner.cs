using HybridShooter.Monster.Behaviour;
using HybridShooter.Monster.Movement;
using UnityEngine;

namespace Scenes.HS.MonsterIntegrationLab.Runtime
{
    public class MonsterSpawner : MonoBehaviour
    {
        [SerializeField] private MonsterController monsterPrefab;
        private Transform monsterRoot;

        [ContextMenu("SpawnMonsterOne")]
        public void SpawnMonsterOne()
        {
            MonsterController monster = Instantiate(monsterPrefab, monsterRoot);
        }
    }
}