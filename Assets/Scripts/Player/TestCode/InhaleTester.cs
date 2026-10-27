using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player.Tester
{
    /// <summary>
    /// 외부 속도 테스트 (보스 없이). 씬에 두고 Play. 보스처럼 InhaleSource / IPlayerExternalVelocity 로만 부른다.
    ///  - F2 공기 흡입 켜기/끄기: 켤 때 중심(이 오브젝트)을 플레이어 앞 startDistance 로 옮긴다. 끝나면 이유를 화면·콘솔에 남긴다
    ///  - F4 짧은 당김 한 번: 소스를 직접 구현하는 예시 (YankSource)
    ///  - swayAmplitude > 0 이면 중심이 좌우로 움직인다 (움직이는 Anchor 확인)
    /// 볼 것: 반대로 달리면 범위 밖은 FarExtraSpeed, 안은 NearExtraSpeed 만큼씩만 다가가는지, 가만히 있으면 StopRadius 까지 끌려가는지,
    ///        10초 / 근처 2초 체류로 끝나는지(구르기로 범위를 벗어나면 체류 초기화), 벽·경사·점프·벽넘기, 쓰러짐·부활 중 멈추는지
    /// </summary>
    public sealed class InhaleTester : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Key toggleKey = Key.F2;
        [SerializeField] private Key yankKey = Key.F4;
        [Tooltip("F2 로 켤 때 중심을 둘 플레이어 앞 거리(m)")]
        [SerializeField] private float startDistance = 20f;
        [SerializeField] private InhaleSettings inhale = InhaleSettings.Default;
        [Tooltip("F4 짧은 당김: 달리기보다 이만큼(m/s) 빠르게")]
        [SerializeField] private float yankExtraSpeed = 5f;
        [SerializeField] private float yankDuration = 0.6f;
        [Tooltip("중심 좌우 흔들기 폭(m). 0 = 고정")]
        [SerializeField] private float swayAmplitude = 0f;
        [SerializeField] private float swayPeriod = 4f;

        private InhaleSource current;
        private InhaleEndReason lastEndReason;
        private float lastElapsed;
        private Vector3 basePosition;
        private GUIStyle style;

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            basePosition = transform.position;
            CreateMarker();
        }

        private void OnDisable() => current?.Stop();

        private void Update()
        {
            if (swayAmplitude > 0f && swayPeriod > 0f)
                transform.position = basePosition + transform.right * (Mathf.Sin(Time.time * Mathf.PI * 2f / swayPeriod) * swayAmplitude);

            if (current != null && current.IsRunning == false)
            {
                lastEndReason = current.EndReason;
                lastElapsed = current.Elapsed;
                Debug.Log($"[InhaleTester] 흡입 종료: {lastEndReason} ({lastElapsed:0.00}초)");
                current = null;
            }

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[toggleKey].wasPressedThisFrame)
            {
                if (current != null) current.Stop();
                else StartInhale();
            }
            if (keyboard[yankKey].wasPressedThisFrame && ServiceLocator.TryGet(out IPlayerExternalVelocity external))
                external.AddVelocitySource(new YankSource(transform, yankExtraSpeed, yankDuration, inhale.StopRadius));
        }

        private void StartInhale()
        {
            if (player != null)
            {
                Vector3 front = player.Forward;
                front.y = 0f;
                transform.position = player.transform.position + front.normalized * startDistance;
            }
            basePosition = transform.position;
            current = InhaleSource.Start(inhale, transform);
        }

        /// <summary>
        /// 소스를 직접 만드는 예시: duration 동안 center 쪽으로 (달리기 + extraSpeed) 로 당긴다.
        /// 계산은 전부 여기서 하고, 플레이어는 결과 속도만 더한다
        /// </summary>
        private sealed class YankSource : IPlayerVelocitySource
        {
            private readonly Transform center;
            private readonly float extraSpeed;
            private readonly float endTime;
            private readonly float stopRadius;

            public YankSource(Transform center, float extraSpeed, float duration, float stopRadius)
            {
                this.center = center;
                this.extraSpeed = extraSpeed;
                this.stopRadius = stopRadius;
                endTime = Time.time + duration;
            }

            public bool IsFinished => center == null || Time.time >= endTime;

            public Vector3 GetVelocity(in PlayerMotionContext context)
            {
                Vector3 toCenter = center.position - context.Position;
                toCenter.y = 0f;
                float distance = toCenter.magnitude;
                float reach = distance - stopRadius;
                if (reach <= 0f || distance < 0.0001f) return Vector3.zero;
                float speed = Mathf.Min(context.RunSpeed + extraSpeed, reach / Mathf.Max(context.DeltaTime, 0.0001f));
                return toCenter / distance * speed;
            }

            public void OnRemovedByPlayer() { }
        }

        /// <summary>중심이 화면에 보이게 작은 구 (충돌 없음)</summary>
        private void CreateMarker()
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "InhaleCenter";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = Vector3.one * 0.6f;
        }

        private void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            float y = Screen.height - 132f;
            GUI.Label(new Rect(10f, y, 900f, 24f), $"[{toggleKey}] 공기 흡입 켜기/끄기  [{yankKey}] 짧은 당김 (직접 구현 예시)", style);
            if (player == null)
            {
                GUI.Label(new Rect(10f, y + 22f, 900f, 24f), "Player 없음", style);
                return;
            }

            string state = current != null
                ? $"흡입 중 {current.Elapsed:0.0}/{inhale.MaxDuration:0}초  근처 {(current.IsPlayerNear ? "안" : "밖")}  체류 {current.NearStayTime:0.00}/{inhale.NearDwellTime:0}초"
                : $"흡입 꺼짐  마지막 종료: {lastEndReason} ({lastElapsed:0.00}초)";
            GUI.Label(new Rect(10f, y + 22f, 900f, 24f), state, style);
            GUI.Label(new Rect(10f, y + 44f, 900f, 24f),
                $"달리기 기준 {player.RunSpeed:0.00} m/s  더한 속도 {player.ExternalVelocity.magnitude:0.00} m/s  " +
                $"수평 속도 {player.HorizontalSpeed:0.00} m/s  거리 {HorizontalDistance():0.0} m", style);
        }

        private float HorizontalDistance()
        {
            Vector3 d = transform.position - player.transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, inhale.StopRadius);
            Gizmos.color = new Color(1f, 0.6f, 0f);
            Gizmos.DrawWireSphere(transform.position, inhale.NearRadius);
        }
    }
}
