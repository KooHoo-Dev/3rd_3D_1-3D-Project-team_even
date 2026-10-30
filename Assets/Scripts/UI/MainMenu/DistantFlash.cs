using UnityEngine;

namespace HybridShooter.UI.MainMenu
{
    /// <summary>
    /// 멀리서 간헐적으로 번쩍이는 섬광 (포격·폭발 느낌). 원경 Point Light 하나를 무작위 간격으로 켰다가 빠르게 끈다.
    /// 번쩍일 때마다 원경 지점 중 하나로 옮기고, 가끔 두 번 연속으로 번쩍인다. 안개 속 몬스터·바위의 윤곽이 잠깐 드러난다
    /// </summary>
    [RequireComponent(typeof(Light))]
    public sealed class DistantFlash : MonoBehaviour
    {
        [Tooltip("섬광 위치 후보 (비우면 제자리)")]
        [SerializeField] private Vector3[] points;
        [Tooltip("다음 섬광까지(초) 무작위 범위")]
        [SerializeField] private Vector2 interval = new Vector2(4f, 10f);
        [Tooltip("최대 밝기. Point Light 는 거리 제곱으로 약해지므로 40~50m 떨어진 무리를 비추려면 크게")]
        [SerializeField] private float peakIntensity = 600f;
        [Tooltip("밝아지는 시간(초)")]
        [SerializeField] private float riseTime = 0.06f;
        [Tooltip("꺼지는 시간(초)")]
        [SerializeField] private float decayTime = 0.7f;
        [Tooltip("두 번 연속 번쩍일 확률")]
        [Range(0f, 1f)] [SerializeField] private float doubleChance = 0.35f;

        private Light flash;
        private float nextTime;
        private float startTime = -1f;
        private bool pendingSecond;

        private void Awake()
        {
            flash = GetComponent<Light>();
            flash.intensity = 0f;
            nextTime = Time.unscaledTime + Random.Range(1f, interval.y);
        }

        private void Update()
        {
            float now = Time.unscaledTime;
            if (now >= nextTime) Begin(now);
            if (startTime < 0f) return;

            float t = now - startTime;
            float k = t < riseTime ? t / riseTime : 1f - Mathf.Clamp01((t - riseTime) / decayTime);
            flash.intensity = peakIntensity * k * k;
            flash.enabled = k > 0f;
            if (k <= 0f) startTime = -1f;
        }

        private void Begin(float now)
        {
            startTime = now;
            if (pendingSecond)
            {
                // 두 번째 섬광은 같은 자리에서
                pendingSecond = false;
                nextTime = now + Random.Range(interval.x, interval.y);
                return;
            }
            if (points != null && points.Length > 0) transform.localPosition = points[Random.Range(0, points.Length)];
            pendingSecond = Random.value < doubleChance;
            nextTime = now + (pendingSecond ? riseTime + decayTime * Random.Range(0.3f, 0.6f) : Random.Range(interval.x, interval.y));
        }
    }
}
