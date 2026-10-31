using System;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;

namespace HybridShooter.Boss.Tyranopode
{
    /// <summary>등장 연출 시간표 (TyranopodeController 인스펙터). 처음 등장 = Full, 두 번째부터 = Brief</summary>
    [Serializable]
    public struct TyranopodeIntroSettings
    {
        [Tooltip("시네마틱 전환 뒤 보스가 솟아오르기까지(초). 착지 자리를 비추며 그림자가 커진다")] public float LeadTime;
        [Tooltip("벽 너머에서 착지까지 날아오는 시간(초)")] public float LeapTime;
        [Tooltip("착지 뒤 흡수를 시작하기까지(초)")] public float SettleTime;
        [Tooltip("땅으로 웨이브 몬스터의 에너지를 빨아들이는 시간(초)")] public float DrainTime;
        [Tooltip("포효 시간(초)")] public float RoarTime;
        [Tooltip("플레이어 카메라로 돌아가는 시간(초)")] public float BlendTime;
        [Tooltip("연출이 끝나고 첫 공격까지(초)")] public float FirstAttackDelay;
        [Tooltip("벽 바깥으로 얼마나 먼 곳에서 뛰어오르나(m)")] public float LaunchDistance;
        [Tooltip("포물선 꼭대기 높이(m, 착지점 기준)")] public float LeapHeight;

        public float LandTime => LeadTime + LeapTime;
        public float DrainStart => LandTime + SettleTime;
        public float RoarStart => DrainStart + DrainTime;
        public float BlendStart => RoarStart + RoarTime;
        public float TotalTime => BlendStart + BlendTime;

        public static TyranopodeIntroSettings Full => new TyranopodeIntroSettings
        {
            LeadTime = 0.7f,
            LeapTime = 1.6f,
            SettleTime = 0.6f,
            DrainTime = 3.4f,
            RoarTime = 2f,
            BlendTime = 0.9f,
            FirstAttackDelay = 1.5f,
            LaunchDistance = 25f,
            LeapHeight = 28f,
        };

        public static TyranopodeIntroSettings Brief => new TyranopodeIntroSettings
        {
            LeadTime = 0.15f,
            LeapTime = 0.8f,
            SettleTime = 0.2f,
            DrainTime = 1.3f,
            RoarTime = 1.1f,
            BlendTime = 0.6f,
            FirstAttackDelay = 1.5f,
            LaunchDistance = 10f,
            LeapHeight = 16f,
        };
    }

    /// <summary>
    /// Tyranopode 등장 연출 감독 (TyranopodeController.BeginIntro 가 만든다). 시간표대로 보스에게 단계를 시키고 카메라를 움직인다.
    ///  1) 시네마틱 카메라로 전환 (검은 띠, 플레이어 입력 차단·무적). 착지 자리를 비춘다
    ///  2) 보스가 벽 너머에서 포물선으로 날아와 내리꽂힌다 → 3) 땅으로 웨이브 몬스터의 생체 에너지를 빨아들이며 강해진다
    ///  4) 포효 (분석 화면 이름 카드) → 5) 플레이어 카메라로 돌아가며 전투 시작
    /// 컷이 바뀔 때는 이전 카메라 자리에서 새 자리로 부드럽게 옮긴다
    /// </summary>
    public sealed class TyranopodeIntro : MonoBehaviour
    {
        private const float CutBlend = 0.5f;

        private enum Shot { Lead, Drain, Roar, Blend }

        private TyranopodeController boss;
        private TyranopodeIntroSettings settings;
        private Func<Camera> playerCamera;
        private TyranopodeCinematicRig rig;
        private float startTime;
        private Vector3 landing;
        private Vector3 launchDir;

        private Shot shot;
        private float shotStart;
        private Vector3 fromPosition;
        private Quaternion fromRotation;
        private float fromFov;

        private bool landed;
        private bool draining;
        private bool roared;
        private bool finished;

        public void Begin(TyranopodeController controller, TyranopodeIntroSettings intro, Vector3 landingPoint, Vector3 launchPoint, Func<Camera> viewCamera)
        {
            boss = controller;
            settings = intro;
            playerCamera = viewCamera;
            landing = landingPoint;
            launchDir = launchPoint - landingPoint;
            launchDir.y = 0f;
            launchDir.Normalize();
            startTime = Time.time;

            ServiceLocator.Get<IPlayerCinematic>().SetCinematic(true);
            rig = new TyranopodeCinematicRig(transform, "Intro");
            rig.Camera.fieldOfView = 55f;
            shot = Shot.Lead;
            shotStart = 0f;
            var (position, rotation) = Desired(Shot.Lead, 0f);
            fromPosition = position;
            fromRotation = rotation;
            fromFov = rig.Camera.fieldOfView;
            rig.Place(position, rotation);
        }

        // ───────────────────────── 시간표 ─────────────────────────

        private void Update()
        {
            float t = Time.time - startTime;

            if (landed == false)
            {
                float k = Mathf.Clamp01((t - settings.LeadTime) / settings.LeapTime);
                boss.IntroFly(k);
                if (t >= settings.LandTime)
                {
                    landed = true;
                    boss.IntroLand();
                    rig.Shake(1.1f, 0.9f);
                }
            }

            if (draining == false && t >= settings.DrainStart)
            {
                draining = true;
                boss.IntroDrain(settings.DrainTime);
                ChangeShot(Shot.Drain, t);
                rig.Shake(0.06f, settings.DrainTime);
            }
            // 빨아들일수록 땅이 더 떨린다
            if (draining && roared == false)
                rig.ShakeStrength = Mathf.Lerp(0.04f, 0.18f, Mathf.Clamp01((t - settings.DrainStart) / settings.DrainTime));

            if (roared == false && t >= settings.RoarStart)
            {
                roared = true;
                boss.IntroRoar();
                ChangeShot(Shot.Roar, t);
                rig.Shake(0.5f, settings.RoarTime * 0.8f);
            }

            if (shot != Shot.Blend && t >= settings.BlendStart) ChangeShot(Shot.Blend, t);

            if (finished == false && t >= settings.TotalTime) Finish();

            // 검은 띠: 처음에 내려오고 돌아갈 때 걷힌다
            float bars = t < settings.BlendStart ? t / 0.5f : 1f - (t - settings.BlendStart) / settings.BlendTime;
            rig.SetBars(Mathf.Clamp01(bars));
        }

        private void Finish()
        {
            finished = true;
            ServiceLocator.Get<IPlayerCinematic>().SetCinematic(false);
            boss.IntroFinish(settings.FirstAttackDelay);
            Destroy(gameObject);
        }

        // ───────────────────────── 카메라 ─────────────────────────

        private void LateUpdate()
        {
            if (finished) return;
            float t = Time.time - startTime;
            var (position, rotation) = Desired(shot, t);
            float fov = shot == Shot.Roar ? 45f : 55f;

            if (shot == Shot.Blend)
            {
                // 플레이어 카메라 자리·화각으로 옮겨 간다 (끝나면 그대로 넘겨준다)
                var target = playerCamera();
                float k = Mathf.SmoothStep(0f, 1f, (t - shotStart) / settings.BlendTime);
                rig.Camera.fieldOfView = Mathf.Lerp(fromFov, target.fieldOfView, k);
                rig.Place(Vector3.Lerp(fromPosition, target.transform.position, k), Quaternion.Slerp(fromRotation, target.transform.rotation, k));
                return;
            }

            float blend = Mathf.SmoothStep(0f, 1f, (t - shotStart) / CutBlend);
            rig.Camera.fieldOfView = Mathf.Lerp(fromFov, fov, blend);
            rig.Place(Vector3.Lerp(fromPosition, position, blend), Quaternion.Slerp(fromRotation, rotation, blend));
        }

        private void ChangeShot(Shot next, float t)
        {
            // 흔들림이 섞이지 않은 지금 자리에서 출발
            var (position, rotation) = Desired(shot, t);
            float blend = Mathf.SmoothStep(0f, 1f, (t - shotStart) / CutBlend);
            fromPosition = Vector3.Lerp(fromPosition, position, blend);
            fromRotation = Quaternion.Slerp(fromRotation, rotation, blend);
            fromFov = rig.Camera.fieldOfView;
            shot = next;
            shotStart = t;
        }

        /// <summary>
        /// Lead: 착지점 너머 낮은 곳에서 날아오는 보스를 올려다본다 / Drain: 보스 주위를 천천히 돌며 다가간다 /
        /// Roar: 머리 앞으로 밀고 들어간다
        /// </summary>
        private (Vector3, Quaternion) Desired(Shot which, float t)
        {
            Transform body = boss.transform;
            float scale = body.localScale.x / boss.IntroBaseScale;
            Vector3 center = body.position + Vector3.up * 4f * scale;

            switch (which)
            {
                case Shot.Lead:
                {
                    // 보스가 날아오는 방향을 마주 보는 자리 (착지점 너머, 옆으로 비껴서)
                    Vector3 side = Vector3.Cross(Vector3.up, launchDir);
                    Vector3 position = landing - launchDir * 26f + side * 9f + Vector3.up * 3f;
                    Vector3 look = landed ? center : Vector3.Lerp(landing + Vector3.up * 6f, body.position, 0.7f);
                    return (position, Quaternion.LookRotation(look - position));
                }
                case Shot.Drain:
                {
                    float k = Mathf.Clamp01((t - settings.DrainStart) / settings.DrainTime);
                    float yaw = Mathf.Lerp(35f, 80f, k);
                    float distance = Mathf.Lerp(28f, 20f, k) * scale;
                    Vector3 around = Quaternion.LookRotation(body.forward) * (Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
                    Vector3 position = center + around * distance + Vector3.up * Mathf.Lerp(4f, 7f, k);
                    return (position, Quaternion.LookRotation(center - position));
                }
                default:
                {
                    Vector3 head = boss.Mouth.position;
                    float k = Mathf.Clamp01((t - settings.RoarStart) / settings.RoarTime);
                    Vector3 position = head + body.forward * Mathf.Lerp(16f, 10f, k) * scale + body.right * 3f * scale + Vector3.up * 1.5f * scale;
                    return (position, Quaternion.LookRotation(head + Vector3.up * 1f - position));
                }
            }
        }

        private void OnDestroy()
        {
            // 연출 도중 보스가 사라졌다 (라운드 정리): 플레이어를 풀어 준다
            if (finished == false && ServiceLocator.TryGet(out IPlayerCinematic player)) player.SetCinematic(false);
        }
    }
}
