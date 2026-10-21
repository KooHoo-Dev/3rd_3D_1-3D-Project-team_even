using System.Collections;
using HybridShooter.Monster.Movement;
using HybridShooter.Monster.MovementLab;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace HybridShooter.Monster.Movement.Lab.Tests
{
    public class LaboratoryIntegrationTests
    {
        private const string ScenePath = "Assets/MonsterMovementLab/Scenes/MonsterMovementLaboratory.unity";

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(scene);
                while (!unload.isDone) yield return null;
            }
            if (Application.isPlaying) yield return new ExitPlayMode();
        }

        private static IEnumerator WaitForSimulation(float seconds)
        {
            // EditMode runner는 WaitForSeconds를 PlayMode coroutine처럼 기다리지 않는다.
            float end = Time.time + seconds;
            while (Time.time < end) yield return null;
        }

        [UnityTest]
        public IEnumerator SavedSceneSupportsContextActionsAndToolRemoval()
        {
#if UNITY_EDITOR
            yield return new EnterPlayMode();
            var load = EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath,
                new LoadSceneParameters(LoadSceneMode.Additive));
            while (!load.isDone) yield return null;
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var example = scene.GetRootGameObjects()[0].GetComponent<MovementLabExample>();
            Assert.That(example.Target, Is.Not.Null);
            var coordinator = example.Coordinator;
            yield return null;
            Assert.That(coordinator.Target, Is.Not.Null, "Edit→Play에서 targetSource 직렬화 보존");
            Assert.That(coordinator.SlotCount, Is.EqualTo(24));

            example.SpawnSingleMvp();
            yield return WaitForSimulation(0.5f);
            Assert.That(coordinator.Agents.Count, Is.Zero, "MVP는 관리자 없이 직접 추적");
            example.SpawnCrowdMvp();
            yield return WaitForSimulation(0.5f);
            Assert.That(coordinator.Agents.Count, Is.Zero);
            example.SpawnSlotCrowd();
            yield return WaitForSimulation(1f);
            Assert.That(coordinator.Agents.Count, Is.EqualTo(24));
            int unrestricted = 0, flank = 0;
            foreach (var agent in coordinator.Agents)
            {
                if (agent.Profile.PolicyKind == MovementPolicyKind.KeepDistance)
                {
                    unrestricted++;
                    Assert.That(agent.Directive.HasSlotPosition, Is.False);
                }
                if (agent.Profile.PolicyKind == MovementPolicyKind.Flank) flank++;
            }
            Assert.That(unrestricted, Is.GreaterThan(0));
            Assert.That(flank, Is.GreaterThan(0));
            example.RemoveAssignedMonster();
            yield return WaitForSimulation(0.5f);
            Assert.That(coordinator.Agents.Count, Is.EqualTo(23));
            example.MoveTargetToCenter();
            // 이미 대기 슬롯에 도착한 개체도 제거 후의 새 목표로 실제 이동하게 만든다.
            example.Target.transform.position += Vector3.right * 3f;
            coordinator.RefreshSlots();
            var movement = coordinator.Agents[coordinator.Agents.Count - 1];
            Vector3 before = movement.transform.position;
            var gizmos = coordinator.GetComponent<MovementLabGizmos>();
            gizmos.enabled = false;
            Object.Destroy(gizmos);
            Object.Destroy(example);
            yield return WaitForSimulation(1f);
            Assert.That(movement.Profile, Is.Not.Null, "예제 제거가 살아있는 몬스터 프로필을 파괴하면 안 된다");
            Assert.That(movement.LastIntent.HasDestination, Is.True);
            Assert.That(Vector3.Distance(before, movement.transform.position), Is.GreaterThan(0.1f));
#else
            Assert.Ignore("저장된 에디터 예제 씬 로딩 검증은 Unity Editor PlayMode 전용이다.");
            yield break;
#endif
        }
    }
}
