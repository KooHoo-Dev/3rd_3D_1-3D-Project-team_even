using System;
using HybridShooter.Monster.Movement;
using UnityEngine;
using UnityEngine.AI;

namespace HybridShooter.Boss
{
    [RequireComponent(typeof(NavMeshAgent), typeof(NavMeshMoveExecutor))]
    public class BossMovement : MonoBehaviour
    {
        private const float MoveUpdateInterval = 0.2f;

        private NavMeshAgent agent;
        private NavMeshMoveExecutor executor;
        
        private Transform target;
        private bool following;
        private float nextMoveUpdateTime;
        private bool paused;

        /// <summary>추적 중이며 실제 속도가 있을 때만 이동 상태</summary>
        public bool IsMoving => following && (paused == false) && agent.isActiveAndEnabled && agent.isOnNavMesh
                                && executor.Velocity.sqrMagnitude > 0.01f;
        
        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            executor = GetComponent<NavMeshMoveExecutor>();
        }

        public void Initialize(float moveSpeed, Transform target)
        {
            Stop();

            agent.speed = moveSpeed;
            this.target = target;

            nextMoveUpdateTime = 0f;
            following = true;
        }

        private void Update()
        {
            if (following == false || paused) return;
            if (target == null)
            {
                Stop();
                return;
            }

            Vector3 direction = target.position - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion rotation = Quaternion.LookRotation(direction);

                transform.rotation = Quaternion.RotateTowards(transform.rotation
                    , rotation, agent.angularSpeed * Time.deltaTime);
            }

            if (Time.time < nextMoveUpdateTime) return;

            nextMoveUpdateTime = Time.time + MoveUpdateInterval;

            MoveIntent intent = new MoveIntent(MovementState.Approach, target.position, agent.speed, agent.stoppingDistance);

            executor.Apply(intent);
        }
        
        public void Stop()
        {
            following = false;
            paused = false;
            executor.Stop();
        }

        public void Pause()
        {
            if (following == false || paused) return;

            paused = true;
            executor.Stop();
        }

        public void Resume()
        {
            if (following == false || paused == false) return;

            paused = false;
            nextMoveUpdateTime = 0f;
        }
        
        private void OnDisable()
        {
            Stop();
        }
    }
}
