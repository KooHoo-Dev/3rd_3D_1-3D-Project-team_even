using UnityEngine;

namespace HybridShooter.UI.Intro
{
    /// <summary>
    /// 인트로 우주선 컷: 카메라 뒤에서 나타난 우주선이 행성으로 내려가다(enter → hit) 지표에서 솟은 생체 대공 포화에 맞고(hit),
    /// 불꽃 꼬리를 끌며 대기권으로 추락한다(hit → fallEnd). 네러티브의 '대기권 진입 중 격추 → 불시착'.
    /// 모든 상태를 시각 t 하나로 계산한다(Evaluate) → 에디터 미리보기·건너뛰기에서도 같은 화면이 나온다.
    /// 파티클·TrailRenderer 대신 LineRenderer 에 지난 경로를 직접 찍는 것도 같은 이유
    /// 위치·경로 값은 IntroSceneBuilder 가 행성 배치에 맞춰 넣는다
    /// </summary>
    public sealed class IntroShipCut : MonoBehaviour
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [Header("타이밍(초) — intro_bgm.py 마디 20(행성)·22(격추)·24(종지)")]
        [SerializeField] private float enterTime = 64f;
        [SerializeField] private float hitTime = 70.4f;
        [SerializeField] private float fallEndTime = 76.8f;

        [Header("우주선")]
        [SerializeField] private Transform ship;
        [Tooltip("격추 전 경로 (3차 베지어, 시작은 카메라 뒤·위)")]
        [SerializeField] private Vector3[] approachPath = new Vector3[4];
        [Tooltip("격추 후 추락 끝 지점 (행성 표면 근처)")]
        [SerializeField] private Vector3 crashPoint;
        [Tooltip("추락 중 회전 속도(도/초, 진행 방향 축)")]
        [SerializeField] private float tumbleSpeed = 160f;

        [Header("엔진·불꽃")]
        [SerializeField] private LineRenderer engineGlow;
        [SerializeField] private float engineLength = 0.9f;
        [SerializeField] private LineRenderer fireTrail;
        [Tooltip("불꽃 꼬리 점 사이 시간 간격(초)")]
        [SerializeField] private float trailStep = 0.07f;

        [Header("피격")]
        [SerializeField] private Renderer explosion;
        [SerializeField] private Light explosionLight;
        [ColorUsage(false, true)] [SerializeField] private Color explosionColor = new Color(6f, 2.4f, 0.8f);
        [SerializeField] private float explosionSize = 7f;
        [Tooltip("격추 뒤 추가 폭발 (hit 기준 초)")]
        [SerializeField] private float[] secondaryBursts = { 1.3f, 2.9f, 4.4f };

        [Header("생체 대공 포화")]
        [SerializeField] private LineRenderer[] flak;
        [SerializeField] private Transform planet;
        [SerializeField] private float planetRadius = 200f;
        [Tooltip("첫 탄이 솟는 시각. 마지막 탄이 hitTime 에 명중")]
        [SerializeField] private float flakStart = 65.6f;
        [SerializeField] private float flakTravel = 1f;
        [SerializeField] private float flakLength = 0.3f;

        [Header("카메라 흔들림")]
        [SerializeField] private float shakeAngle = 1.4f;
        [SerializeField] private float shakeDecay = 0.5f;

        private MaterialPropertyBlock block;
        private Vector3[] trailPoints;

        public float HitTime => hitTime;
        public float EnterTime => enterTime;

        /// <summary>화면 전체를 하얗게 덮을 정도 (피격 순간 섬광, 0~1)</summary>
        public float FlashAmount(float t) => t < hitTime ? 0f : 0.55f * Mathf.Exp(-(t - hitTime) / 0.12f);

        /// <summary>피격 뒤 카메라 흔들림 (카메라 회전에 곱한다)</summary>
        public Quaternion Shake(float t)
        {
            if (t < hitTime) return Quaternion.identity;
            float amp = shakeAngle * Mathf.Exp(-(t - hitTime) / shakeDecay);
            float x = (Mathf.PerlinNoise(t * 18f, 0.3f) - 0.5f) * 2f * amp;
            float y = (Mathf.PerlinNoise(0.7f, t * 18f) - 0.5f) * 2f * amp;
            return Quaternion.Euler(x, y, 0f);
        }

        public void Evaluate(float t, Transform viewer)
        {
            block ??= new MaterialPropertyBlock();
            bool active = t >= enterTime && t <= fallEndTime + 0.5f;
            ship.gameObject.SetActive(active);
            if (planet != null) planet.localRotation = Quaternion.Euler(0f, t * 0.4f, 0f); // 아주 느린 자전 — 살아 있는 행성

            EvaluateFlak(t, viewer);
            if (active == false)
            {
                engineGlow.enabled = false;
                fireTrail.enabled = false;
                SetExplosion(0f, Vector3.zero, viewer);
                return;
            }

            var position = ShipPosition(t);
            var velocity = ShipPosition(t + 0.02f) - position;
            var look = Quaternion.LookRotation(velocity.sqrMagnitude > 1e-8f ? velocity : Vector3.forward, Vector3.up);
            if (t >= hitTime)
            {
                float after = t - hitTime;
                look *= Quaternion.Euler(Mathf.Sin(after * 3.1f) * 12f, Mathf.Sin(after * 2.3f) * 9f, after * tumbleSpeed);
            }
            else
            {
                // 진입 중 약한 뱅크
                look *= Quaternion.Euler(0f, 0f, Mathf.Sin((t - enterTime) * 0.9f) * 10f);
            }
            ship.SetPositionAndRotation(position, look);

            engineGlow.enabled = t < hitTime;
            if (engineGlow.enabled)
            {
                engineGlow.SetPosition(0, position - ship.forward * 0.35f);
                engineGlow.SetPosition(1, position - ship.forward * (0.35f + engineLength * (0.85f + 0.15f * Mathf.PerlinNoise(t * 20f, 0f))));
            }

            fireTrail.enabled = t >= hitTime;
            if (fireTrail.enabled) EvaluateTrail(t);

            EvaluateExplosion(t, position, viewer);
        }

        // ───────────────────────── 경로 ─────────────────────────

        private Vector3 ShipPosition(float t)
        {
            if (t < hitTime)
            {
                float k = Mathf.Clamp01((t - enterTime) / (hitTime - enterTime));
                float u = 1f - Mathf.Pow(1f - k, 1.6f); // 멀어질수록 느려 보이게
                return Bezier(approachPath[0], approachPath[1], approachPath[2], approachPath[3], u);
            }
            // 격추 직후엔 관성으로 앞으로, 점점 아래(행성)로 꺾인다
            float f = Mathf.Clamp01((t - hitTime) / (fallEndTime - hitTime));
            var start = approachPath[3];
            var momentum = start + (approachPath[3] - approachPath[2]).normalized * 10f;
            return Bezier(start, momentum, crashPoint, crashPoint, Mathf.Pow(f, 1.3f));
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float u)
        {
            float v = 1f - u;
            return v * v * v * a + 3f * v * v * u * b + 3f * v * u * u * c + u * u * u * d;
        }

        private void EvaluateTrail(float t)
        {
            int count = fireTrail.positionCount;
            if (trailPoints == null || trailPoints.Length != count) trailPoints = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                float ti = Mathf.Max(hitTime, t - i * trailStep);
                // 꼬리로 갈수록 흔들리며 퍼지는 연기
                var wobble = new Vector3(Mathf.PerlinNoise(i * 0.4f, ti) - 0.5f, Mathf.PerlinNoise(ti, i * 0.4f) - 0.5f, 0f) * (i * 0.06f);
                trailPoints[i] = ShipPosition(ti) + wobble;
            }
            fireTrail.SetPositions(trailPoints);
        }

        // ───────────────────────── 폭발 ─────────────────────────

        private void EvaluateExplosion(float t, Vector3 position, Transform viewer)
        {
            float amount = Burst(t, hitTime, 1f);
            foreach (float offset in secondaryBursts) amount = Mathf.Max(amount, Burst(t, hitTime + offset, 0.45f));
            SetExplosion(amount, position, viewer);
        }

        /// <summary>순간 커지고(80ms) 천천히 꺼지는(350ms) 밝기</summary>
        private static float Burst(float t, float at, float strength)
        {
            if (t < at) return 0f;
            float dt = t - at;
            return strength * (1f - Mathf.Exp(-dt / 0.03f)) * Mathf.Exp(-dt / 0.35f);
        }

        private void SetExplosion(float amount, Vector3 position, Transform viewer)
        {
            bool visible = amount > 0.01f;
            explosion.enabled = visible;
            explosionLight.enabled = visible;
            if (visible == false) return;

            var tr = explosion.transform;
            tr.position = position;
            if (viewer != null) tr.rotation = Quaternion.LookRotation(tr.position - viewer.position, viewer.up); // 카메라를 보는 판
            tr.localScale = Vector3.one * (explosionSize * (0.4f + 0.6f * Mathf.Sqrt(amount)));
            block.SetColor(ColorId, explosionColor * amount);
            explosion.SetPropertyBlock(block);
            explosionLight.transform.position = position;
            explosionLight.intensity = amount * 60f;
        }

        // ───────────────────────── 대공 포화 ─────────────────────────

        /// <summary>
        /// 카메라에서 우주선 너머로 보이는 지표(시선 광선과 행성의 교점)에서 우주선을 향해 솟는다 → 화면 안에서 탄이 날아오는 게 보인다.
        /// 우주선 바로 아래 지표는 숙인 카메라 시야 밖이라 거기서 쏘면 보이지 않는다
        /// </summary>
        private void EvaluateFlak(float t, Transform viewer)
        {
            if (flak == null || planet == null || viewer == null) return;
            int count = flak.Length;
            float last = hitTime - flakTravel;
            for (int i = 0; i < count; i++)
            {
                float fire = count > 1 ? Mathf.Lerp(flakStart, last, i / (count - 1f)) : last;
                bool hits = i == count - 1;
                // 빗나가는 탄은 우주선 근처를 스친다 (카메라가 우주선 가까이에 있어 더 날아가면 화면을 가로지르는 굵은 띠가 된다)
                var miss = hits ? Vector3.zero : new Vector3(Hash(i, 1) - 0.5f, Hash(i, 2) * 0.6f, Hash(i, 3) - 0.5f) * 9f;
                var target = ShipPosition(fire + flakTravel) + miss;
                // 시선과 거의 같은 방향이면 탄이 점으로 보인다 → 좌우로 30~50° 벌린 지표에서 화면을 가로질러 날아오게
                float side = (i % 2 == 0 ? 1f : -1f) * (30f + Hash(i, 5) * 20f);
                var spread = Quaternion.Euler(Hash(i, 4) * 18f, side, 0f);
                var source = SurfaceBehind(viewer.position, spread * (target - viewer.position).normalized, target);

                float k = (t - fire) / flakTravel;
                var line = flak[i];
                line.enabled = k > 0f && k < 1f;
                if (line.enabled == false) continue;
                line.SetPosition(0, Vector3.LerpUnclamped(source, target, Mathf.Max(0f, k - flakLength)));
                line.SetPosition(1, Vector3.LerpUnclamped(source, target, k));
            }
        }

        /// <summary>origin 에서 direction 으로 쏜 광선이 행성 표면과 만나는 점. 안 만나면 fallback 바로 아래 지표</summary>
        private Vector3 SurfaceBehind(Vector3 origin, Vector3 direction, Vector3 fallback)
        {
            var toCenter = planet.position - origin;
            float along = Vector3.Dot(toCenter, direction);
            float d2 = toCenter.sqrMagnitude - along * along;
            float r2 = planetRadius * planetRadius;
            if (along <= 0f || d2 > r2) return planet.position + (fallback - planet.position).normalized * planetRadius;
            return origin + direction * (along - Mathf.Sqrt(r2 - d2));
        }

        private static float Hash(int i, int salt)
        {
            uint h = (uint)(i * 374761393 + salt * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }
}
