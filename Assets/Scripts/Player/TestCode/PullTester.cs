using HybridShooter.Core.Contracts;
using HybridShooter.Core.Services;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player.Tester
{
    /// <summary>
    /// 끌어당김 테스트 (보스 없이). 씬에 두고 Play. 보스처럼 ServiceLocator 의 IPlayerPullable 로만 부른다.
    ///  - F2 계속 당김 켜기/끄기 (BossInhale): 켤 때 중심(이 오브젝트)을 플레이어 앞 startDistance 로 옮긴다
    ///  - F4 짧고 강한 당김 한 번 (Yank)
    ///  - swayAmplitude > 0 이면 중심이 좌우로 움직인다 (움직이는 Anchor 확인)
    /// 볼 것: 가만히 있으면 Speed 로 수렴하는지(점점 빨라지지 않는지), 반대로 걸으면 버티는지, 벽·경사·구르기·점프·벽넘기,
    ///        쓰러짐·부활 중 멈추는지, StopRadius 에서 떨리지 않는지, 이 오브젝트를 지우면 끝나는지
    /// </summary>
    public sealed class PullTester : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private Key toggleKey = Key.F2;
        [SerializeField] private Key yankKey = Key.F4;
        [Tooltip("F2 로 켤 때 중심을 둘 플레이어 앞 거리(m)")]
        [SerializeField] private float startDistance = 15f;
        [SerializeField] private PlayerPullSettings inhale = PlayerPullPresets.BossInhale;
        [SerializeField] private PlayerPullSettings yank = PlayerPullPresets.Yank;
        [Tooltip("중심 좌우 흔들기 폭(m). 0 = 고정")]
        [SerializeField] private float swayAmplitude = 0f;
        [SerializeField] private float swayPeriod = 4f;
        [Tooltip("StopPull 때 꺼지는 시간(초)")]
        [SerializeField] private float stopFadeOut = 0.4f;

        private int inhaleHandle;
        private Vector3 basePosition;
        private GUIStyle style;

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            basePosition = transform.position;
            CreateMarker();
        }

        private void OnDisable() => StopInhale(0f);

        private void Update()
        {
            if (swayAmplitude > 0f && swayPeriod > 0f)
                transform.position = basePosition + transform.right * (Mathf.Sin(Time.time * Mathf.PI * 2f / swayPeriod) * swayAmplitude);

            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[toggleKey].wasPressedThisFrame)
            {
                if (inhaleHandle != 0) StopInhale(stopFadeOut);
                else StartInhale();
            }
            if (keyboard[yankKey].wasPressedThisFrame && ServiceLocator.TryGet(out IPlayerPullable pullable))
                pullable.StartPull(yank.Toward(transform));
        }

        private void StartInhale()
        {
            if (ServiceLocator.TryGet(out IPlayerPullable pullable) == false) return;
            if (player != null)
            {
                Vector3 front = player.Forward;
                front.y = 0f;
                transform.position = player.transform.position + front.normalized * startDistance;
            }
            basePosition = transform.position;
            inhaleHandle = pullable.StartPull(inhale.Toward(transform));
        }

        private void StopInhale(float fadeOut)
        {
            if (inhaleHandle == 0) return;
            if (ServiceLocator.TryGet(out IPlayerPullable pullable)) pullable.StopPull(inhaleHandle, fadeOut);
            inhaleHandle = 0;
        }

        /// <summary>중심이 화면에 보이게 작은 구 (충돌 없음)</summary>
        private void CreateMarker()
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "PullCenter";
            Destroy(marker.GetComponent<Collider>());
            marker.transform.SetParent(transform, false);
            marker.transform.localScale = Vector3.one * 0.6f;
        }

        private void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
            float y = Screen.height - 110f;
            GUI.Label(new Rect(10f, y, 900f, 24f), $"[{toggleKey}] 계속 당김 켜기/끄기  [{yankKey}] 짧고 강한 당김", style);
            string status = player == null ? "Player 없음"
                : $"당김 {(inhaleHandle != 0 ? "켜짐" : "꺼짐")}  걸림 {player.IsBeingPulled}  더한 속도 {player.PullVelocity.magnitude:0.00} m/s  " +
                  $"수평 속도 {player.HorizontalSpeed:0.00} m/s  거리 {HorizontalDistance():0.0} m";
            GUI.Label(new Rect(10f, y + 22f, 900f, 24f), status, style);
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
            if (inhale.MaxRadius > 0f)
            {
                Gizmos.color = new Color(1f, 0.6f, 0f);
                Gizmos.DrawWireSphere(transform.position, inhale.MaxRadius);
            }
        }
    }
}
