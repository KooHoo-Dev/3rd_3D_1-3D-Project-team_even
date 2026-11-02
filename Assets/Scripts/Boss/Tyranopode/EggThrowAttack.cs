using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>
    /// 알 던지기. 플레이어 시선 앞쪽(EggArcAngle) 지형에 알을 흩뿌린다 → 크게 돌아서지 않고 깰 수 있다.
    /// 던진 뒤 EggQuietTime 동안 보스는 공격하지 않고 멀찍이 맴돈다 (알을 깨는 동안 등 뒤에서 맞지 않게).
    /// 페이즈 2: Phase2EggBonus 개 더. 발광 주머니 파괴: SacBrokenEggReduce 개 덜
    /// </summary>
    public sealed class EggThrowAttack : TyranopodeAttack
    {
        private const float MinScatter = 3f;
        private const int LandingTries = 6;

        [SerializeField, Range(0f, 1f)] private float throwTime = 0.4f;

        private bool thrown;

        protected override float WindupTime => Data.EggWindup;
        protected override float RecoveryTime => Data.EggRecovery;
        protected override float CooldownTime => Data.EggCooldown;

        protected override void StartWindup()
        {
            thrown = false;
            View.Play(TyranopodeView.Idle, 1.2f);
        }

        protected override void WindupTick(float deltaTime) => Move.Face(TargetPosition, 1.5f);

        protected override void OnActiveStarted() => View.Play(TyranopodeView.Spit, Owner.SpeedScale * 0.8f);

        protected override void OnActiveTick(float deltaTime)
        {
            float n = View.NormalizedTime(TyranopodeView.Spit);
            if (thrown == false && n >= throwTime)
            {
                thrown = true;
                Throw();
            }
            if (n >= 1f)
            {
                Finish();
                Owner.BeginQuiet(Data.EggQuietTime);
            }
        }

        private void Throw()
        {
            Owner.RaiseSoundCue(TyranopodeSoundCue.EggThrow, Owner.Mouth.position);
            int count = Data.EggCount
                        + (Owner.IsPhase2 ? Data.Phase2EggBonus : 0)
                        - (Owner.IsBroken(TyranopodePartType.Sac) ? Data.SacBrokenEggReduce : 0);

            Vector3 origin = Owner.Mouth.position;
            Vector3 view = Flat(Owner.PlayerView.forward);
            float viewYaw = view.sqrMagnitude > 0.01f ? Mathf.Atan2(view.x, view.z) * Mathf.Rad2Deg : 0f;
            for (int i = 0; i < count; i++)
            {
                // 시선 앞쪽 부채꼴을 고르게 나누고 조금 흔든다. 구덩이·맵 밖이면(부화한 잡몹이 갇힘) 다른 자리로
                float slot = count == 1 ? 0.5f : (float)i / (count - 1);
                if (TryLanding(viewYaw, slot, out Vector3 point) == false)
                {
                    Debug.LogWarning("[Tyranopode] 알을 떨어뜨릴 자리를 찾지 못했습니다 (주변 NavMesh 가 플레이어와 이어지지 않음).", this);
                    continue;
                }
                var egg = Instantiate(Owner.EggPrefab, origin, Quaternion.identity);
                egg.Launch(Owner, origin, point, Data.EggFlightTime);
            }
        }

        private bool TryLanding(float viewYaw, float slot, out Vector3 point)
        {
            for (int t = 0; t < LandingTries; t++)
            {
                // 다시 고를수록 흔들림을 키운다
                float angle = viewYaw + Mathf.Lerp(-Data.EggArcAngle, Data.EggArcAngle, slot) * 0.5f + Random.Range(-10f, 10f) * (1 + t);
                float distance = Random.Range(MinScatter, Mathf.Max(MinScatter, Data.EggScatterRadius));
                Vector3 candidate = TargetPosition + Quaternion.Euler(0f, angle, 0f) * Vector3.forward * distance;
                if (Owner.Minions.TryFindSpawnPoint(candidate, TargetPosition, out point)) return true;
            }
            point = TargetPosition;
            return false;
        }
    }
}
