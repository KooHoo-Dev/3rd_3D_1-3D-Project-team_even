using System.Collections;
using UnityEngine;

namespace HybridShooter.Tutorial.Steps
{
    /// <summary>
    /// ① 이동 · 질주 · 구르기. 목표 지점까지 이동 → 질주 누적 SprintSeconds 초 → 피해 차단막을 모두 구르며 통과.
    /// 차단막에 그냥 닿으면 피해 + 되돌려지고(TutorialDamageBarrier) 안내 대사, 스태미나가 바닥나면 한 번 안내한다
    /// </summary>
    public sealed class MoveRollStep : TutorialStep
    {
        /// <summary>차단막 피격 안내를 다시 하기까지(초)</summary>
        private const float HitLineCooldown = 6f;

        public override string Title => "기동";

        private int cleared;
        private float lastHitLine = float.NegativeInfinity;
        private bool staminaLineShown;

        public override IEnumerator Run()
        {
            // 이동
            Objective("obj_move");
            D.SayNow("vo_tut_01_move");
            if (D.MoveTarget != null)
            {
                D.ShowDestination(D.MoveTarget, "wp_move");
                yield return D.WaitUntil(() => D.MoveTarget.Reached, "vo_tut_01_hint");
                D.HideDestination();
            }

            // 질주
            yield return D.Say("vo_tut_01_sprint");
            float sprinted = 0f;
            int goal = Mathf.CeilToInt(Script.SprintSeconds);
            Objective("obj_sprint", 0, goal);
            yield return D.WaitUntil(() =>
            {
                if (D.Player.IsSprinting)
                {
                    sprinted += Time.deltaTime;
                    D.MarkProgress();
                    Objective("obj_sprint", Mathf.Min(goal, Mathf.FloorToInt(sprinted)), goal);
                }
                return sprinted >= Script.SprintSeconds;
            }, "vo_tut_01_hint");

            // 구르기 (피해 차단막)
            foreach (var barrier in D.RollBarriers)
            {
                if (barrier == null) continue;
                barrier.Damage = Script.BarrierDamage;
                barrier.Hit += OnBarrierHit;
                barrier.Cleared += OnBarrierCleared;
                if (barrier.IsCleared) cleared++;
            }
            int barriers = D.RollBarriers.Count;
            Objective("obj_roll", cleared, barriers);
            D.SayNow("vo_tut_01_barrier");
            yield return D.WaitUntil(() =>
            {
                if (staminaLineShown == false && D.Player.IsStaminaExhausted)
                {
                    staminaLineShown = true;
                    D.SayNow("vo_tut_01_stamina");
                }
                return cleared >= barriers;
            }, "vo_tut_01_hint");

            yield return D.Say("vo_tut_01_clear");
            if (D.MovementGate != null) D.MovementGate.Open();
        }

        public override void Cleanup()
        {
            foreach (var barrier in D.RollBarriers)
            {
                if (barrier == null) continue;
                barrier.Hit -= OnBarrierHit;
                barrier.Cleared -= OnBarrierCleared;
            }
        }

        private void OnBarrierHit()
        {
            D.MarkProgress();
            if (Time.time - lastHitLine < HitLineCooldown) return;
            lastHitLine = Time.time;
            D.SayNow(D.Player.IsStaminaExhausted ? "vo_tut_01_stamina" : "vo_tut_01_barrier_hit");
        }

        private void OnBarrierCleared()
        {
            cleared++;
            D.MarkProgress();
            Objective("obj_roll", cleared, D.RollBarriers.Count);
        }
    }
}
