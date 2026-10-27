using System;
using UnityEngine;

namespace HybridShooter.Player
{
    /// <summary>
    /// 넉백 (IKnockbackable, 돌진·도약 피격 등).
    ///  - 몬스터 공격이 피해 뒤 살아 있는 플레이어에 ApplyKnockback(방향, 거리)을 부른다 (MonsterAttackTable.KnockbackDistance)
    ///  - knockbackDuration 동안 knockbackSpeedCurve 모양으로 밀려나며, 실제 이동 거리가 distance 가 되도록 속도를 맞춘다
    ///  - 밀려나는 동안: 이동·점프·구르기·무기·아이템 입력 없음 (시선만 돌릴 수 있다), 피해 무시, 외부 속도(보스 흡입) 멈춤
    ///  - 끝난 뒤 knockbackExtraInvulnerableTime 동안 무적만 더 남는다
    ///  - 이미 밀려나는 중·쓰러짐·부활 연출·외부 제어(벽넘기) 중이면 무시한다. 쓰러짐·부활·순간이동·외부 제어 시작 때 끊긴다
    /// </summary>
    public partial class PlayerController
    {
        #region 설정(인스펙터)
        [Header("넉백 (돌진 피격 등)")]
        [Tooltip("밀려나는 시간(초). 이 동안 입력이 막히고 무적이다")]
        [SerializeField, Min(0.05f)] private float knockbackDuration = 0.4f;
        [Tooltip("넉백 진행도(0~1) → 속도 배율. 모양만 쓰고, 총 이동 거리는 공격의 KnockbackDistance 에 맞춘다")]
        [SerializeField] private AnimationCurve knockbackSpeedCurve = new AnimationCurve(new Keyframe(0f, 1.6f), new Keyframe(1f, 0.2f));
        [Tooltip("넉백이 끝난 뒤 추가로 무적인 시간(초). 입력은 이미 풀려 있다")]
        [SerializeField, Min(0f)] private float knockbackExtraInvulnerableTime = 0.1f;
        #endregion

        // 진행도 → 속도 배율 곡선 면적을 잴 때 나누는 구간 수
        private const int KnockbackCurveSamples = 20;

        private bool isKnockedBack;
        private Vector3 knockbackDirection;
        private float knockbackStartTime;
        private float knockbackSpeed;   // 곡선 배율 1 일 때 속도(m/s)
        private float knockbackInvulnerableEndTime = float.NegativeInfinity;

        /// <summary>인자 = 밀려나는 월드 수평 방향</summary>
        public event Action<Vector3> KnockbackStarted;
        public event Action KnockbackEnded;

        /// <summary>넉백으로 밀려나는 중 (입력 없음, 무적)</summary>
        public bool IsKnockedBack => isKnockedBack;
        /// <summary>넉백 중이거나 끝난 뒤 추가 무적 시간 안</summary>
        public bool IsKnockbackInvulnerable => isKnockedBack || Time.time < knockbackInvulnerableEndTime;

        public void ApplyKnockback(Vector3 direction, float distance)
        {
            if (distance <= 0f || isKnockedBack) return;
            if (IsDead || isReviving || externalControl) return;

            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = -Forward; // 공격자와 겹쳐 방향이 없으면 뒤로
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) return;

            if (isRolling)
            {
                isRolling = false;
                RollEnded?.Invoke();
            }

            isKnockedBack = true;
            knockbackDirection = direction.normalized;
            knockbackStartTime = Time.time;
            knockbackSpeed = distance / (knockbackDuration * KnockbackCurveArea());
            knockbackInvulnerableEndTime = Time.time + knockbackDuration + knockbackExtraInvulnerableTime;

            // 밀려나기 직전 입력이 남아 있다가 실행되지 않게 비운다
            SetMoveInput(Vector3.zero, false);
            jumpRequestedTime = float.NegativeInfinity;
            if (gunSlot != null)
            {
                gunSlot.TriggerUp();
                gunSlot.AimUp();
            }

            KnockbackStarted?.Invoke(knockbackDirection);
        }

        /// <summary>FixedUpdate, 모터 대신: 곡선 속도로 밀려난다. 벽은 미끄러지고, 공중이면 낙하 속도를 유지한다</summary>
        private void UpdateKnockback(float dt)
        {
            float t = (Time.time - knockbackStartTime) / knockbackDuration;
            if (t >= 1f)
            {
                EndKnockback();
                UpdateWalk(dt); // 이번 프레임은 입력 없는 걷기 감속으로 이어 간다
                return;
            }

            Vector3 horizontal = knockbackDirection * (knockbackSpeed * Mathf.Max(0f, knockbackSpeedCurve.Evaluate(t)));
            horizontal = SlideAlongWalls(horizontal, dt);
            if (isGrounded)
            {
                rb.linearVelocity = AlongGround(horizontal) + Vector3.down * snapDownSpeed;
            }
            else
            {
                float y = rb.linearVelocity.y;
                rb.linearVelocity = new Vector3(horizontal.x, y, horizontal.z);
            }
        }

        /// <summary>넉백을 끝낸다 (추가 무적은 그대로)</summary>
        private void EndKnockback()
        {
            if (isKnockedBack == false) return;
            isKnockedBack = false;
            KnockbackEnded?.Invoke();
        }

        /// <summary>넉백과 추가 무적을 모두 끊는다 (쓰러짐·부활·순간이동·외부 제어)</summary>
        private void CancelKnockback()
        {
            knockbackInvulnerableEndTime = float.NegativeInfinity;
            EndKnockback();
        }

        /// <summary>진행도 0~1 에서 곡선 배율의 면적 (사다리꼴 근사). 0 이하면 1 로 본다</summary>
        private float KnockbackCurveArea()
        {
            float area = 0f;
            float previous = Mathf.Max(0f, knockbackSpeedCurve.Evaluate(0f));
            for (int i = 1; i <= KnockbackCurveSamples; i++)
            {
                float current = Mathf.Max(0f, knockbackSpeedCurve.Evaluate((float)i / KnockbackCurveSamples));
                area += (previous + current) * 0.5f / KnockbackCurveSamples;
                previous = current;
            }
            return area > 0.0001f ? area : 1f;
        }
    }
}
