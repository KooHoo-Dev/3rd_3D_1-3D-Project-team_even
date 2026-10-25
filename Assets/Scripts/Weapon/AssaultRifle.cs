using UnityEngine;

namespace HybridShooter.Weapon
{
    /// <summary>
    /// 돌격소총 — 중거리 완전 자동. (참고: CS 의 고정 반동 패턴 + 발로란트 첫 발 정확도)
    ///  - 반동이 매번 같은 모양(sprayPattern)으로 튄다 → 마우스를 반대로 당겨 "제압"하는 실력 요소
    ///  - 쉬었다 쏜 첫 발은 거의 정확 (탭 사격·점사가 장거리에서 유리)
    ///  - 오래 갈길수록 퍼짐 누적 (bloom)
    /// </summary>
    public class AssaultRifle : Gun
    {
        [Header("돌격소총")]
        [Tooltip("n 번째 발의 반동 (x = 위, y = 오른쪽, 도). 끝까지 쏘면 마지막 4발 구간을 반복")]
        [SerializeField] private Vector2[] sprayPattern =
        {
            new Vector2(0.5f, 0f), new Vector2(0.6f, 0.05f), new Vector2(0.7f, -0.05f), new Vector2(0.8f, 0.1f),
            new Vector2(0.8f, 0.15f), new Vector2(0.7f, 0.25f), new Vector2(0.5f, 0.35f), new Vector2(0.4f, 0.4f),
            new Vector2(0.3f, 0.2f), new Vector2(0.3f, -0.35f), new Vector2(0.25f, -0.5f), new Vector2(0.25f, -0.45f),
            new Vector2(0.2f, 0.3f), new Vector2(0.2f, 0.5f), new Vector2(0.2f, -0.3f), new Vector2(0.2f, -0.5f),
        };
        [Tooltip("패턴에 더하는 무작위 흔들림(±도)")]
        [SerializeField] private float patternRandomness = 0.08f;
        [Tooltip("첫 발 퍼짐 배율 (0 = 완벽히 정확)")]
        [SerializeField, Range(0f, 1f)] private float firstShotSpreadMultiplier = 0.15f;

        public AssaultRifle()
        {
            bloomPerShot = 0.15f;
            maxBloom = 2.5f;
            bloomRecovery = 6f;
            moveSpreadPenalty = 2f;
            airSpreadPenalty = 4f;
            aimSpreadMultiplier = 0.35f;
            recoilRecoverRatio = 0.5f;
            aimFovMultiplier = 0.75f;
            aimMoveSpeedMultiplier = 0.7f;
        }

        public override float CurrentSpread
        {
            get
            {
                float spread = base.CurrentSpread;
                return ShotsInSpray == 0 ? spread * firstShotSpreadMultiplier + MovementSpread : spread;
            }
        }

        protected override void OnTick(float dt)
        {
            if (IsTriggerHeld) TryFire();
        }

        protected override Vector2 GetRecoilKick()
        {
            if (sprayPattern == null || sprayPattern.Length == 0) return base.GetRecoilKick();

            int index = ShotsInSpray;
            int length = sprayPattern.Length;
            if (index >= length)
            {
                int loop = Mathf.Min(4, length);
                index = length - loop + (index - length) % loop;
            }
            Vector2 kick = sprayPattern[index];
            kick.x += Random.Range(-patternRandomness, patternRandomness);
            kick.y += Random.Range(-patternRandomness, patternRandomness);
            return kick;
        }

        protected override void Shoot() => FireBullet(SpreadDirection());
    }
}
