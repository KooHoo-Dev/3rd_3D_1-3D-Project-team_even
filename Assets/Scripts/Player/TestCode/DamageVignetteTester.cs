using System.Collections;
using System.Collections.Generic;
using HybridShooter.Core.Contracts;
using HybridShooter.Core.Events;
using HybridShooter.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Player.Tester
{
    /// <summary>
    /// 피격 비네트(DamageVignetteHud) 자동 테스트. 씬에 DamageVignetteHud 프리팹과 함께 두고 Play.
    ///  - 시작하면 시나리오를 자동 실행: 실제 PlayerController.TakeDamage / Heal 로 피해·회복을 주고 비네트 alpha 를 측정
    ///  - 각 단계 PASS/FAIL 을 콘솔과 화면 좌상단에 출력. 하나라도 실패하면 LogError
    ///  - 체력이 0 이 되면 되돌릴 수 없으므로 시나리오는 최소 체력 5% 까지만 내린다
    ///  - P: 시나리오 재실행 / K: 피해 5% / L: 피해 30% / O: 회복 20% (육안 확인용)
    /// </summary>
    public sealed class DamageVignetteTester : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [SerializeField] private DamageVignetteHud hud;
        [SerializeField] private bool runOnStart = true;
        [Tooltip("프레임 지연·감쇠로 생기는 측정 오차 허용치")]
        [SerializeField] private float tolerance = 0.03f;

        private struct Result
        {
            public string Name;
            public bool Passed;
            public string Detail;
        }

        private readonly List<Result> results = new List<Result>();
        private Coroutine running;
        private int damagedEvents;
        private float sampleMin, sampleMax;
        private GUIStyle style;

        private void Awake()
        {
            if (player == null) player = FindAnyObjectByType<PlayerController>();
            if (hud == null) hud = FindAnyObjectByType<DamageVignetteHud>();
        }

        private void OnEnable() => EventBus<PlayerDamagedEvent>.Subscribe(OnPlayerDamaged);
        private void OnDisable() => EventBus<PlayerDamagedEvent>.Unsubscribe(OnPlayerDamaged);

        private void Start()
        {
            if (runOnStart) Run();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || player == null) return;

            if (keyboard.pKey.wasPressedThisFrame) Run();
            if (running != null) return;
            if (keyboard.kKey.wasPressedThisFrame) Damage(Percent(0.05f));
            if (keyboard.lKey.wasPressedThisFrame) Damage(Percent(0.30f));
            if (keyboard.oKey.wasPressedThisFrame) player.Heal(Percent(0.20f));
        }

        private void OnPlayerDamaged(PlayerDamagedEvent e) => damagedEvents++;

        // ── 시나리오 ──

        private void Run()
        {
            if (player == null || hud == null)
            {
                Debug.LogError($"[DamageVignetteTester] {(player == null ? "PlayerController" : "DamageVignetteHud")} 를 씬에서 찾지 못했습니다.");
                return;
            }
            if (player.IsAlive == false)
            {
                Debug.LogError("[DamageVignetteTester] 플레이어가 사망 상태라 테스트할 수 없습니다. Play 를 다시 시작하세요.");
                return;
            }
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Scenario());
        }

        private IEnumerator Scenario()
        {
            results.Clear();
            Debug.Log("[DamageVignetteTester] 테스트 시작");

            // 준비: 체력 가득, 비네트가 꺼질 때까지 대기
            player.Heal(player.MaxHealth);
            yield return WaitUntilClear(3f);
            yield return Sample(0.5f);
            Check("대기 상태: 비네트 없음", sampleMax <= 0f, $"max {sampleMax:0.000}");

            // 이벤트 연동 (PlayerController.TakeDamage → PlayerDamagedEvent)
            int before = damagedEvents;
            Damage(Percent(0.05f));
            yield return Sample(0.1f);
            float smallPeak = sampleMax;
            Check("피격 이벤트 발행", damagedEvents == before + 1, $"{damagedEvents - before}회");
            Check("작은 피해(5%): 약한 플래시", smallPeak >= hud.MinFlashAlpha - tolerance && smallPeak <= hud.MaxFlashAlpha + tolerance,
                  $"peak {smallPeak:0.000} (기대 {hud.MinFlashAlpha:0.00}~{hud.MaxFlashAlpha:0.00})");

            yield return Sample(1f);
            Check("플래시 1초 내 소멸", hud.CurrentAlpha <= 0f, $"1초 후 {hud.CurrentAlpha:0.000}");

            Damage(Percent(0.30f));
            yield return Sample(0.1f);
            float bigPeak = sampleMax;
            Check("큰 피해(30%): 더 강한 플래시", bigPeak > smallPeak && bigPeak <= hud.MaxFlashAlpha + tolerance,
                  $"peak {bigPeak:0.000} > {smallPeak:0.000}, 상한 {hud.MaxFlashAlpha:0.00}");
            yield return WaitUntilClear(2f);

            // 연사: 피해 3% x 10회, 0.08초 간격 → 누적되지 않아야 함
            float burstMax = 0f;
            for (int i = 0; i < 10; i++)
            {
                Damage(Percent(0.03f));
                yield return Sample(0.08f);
                burstMax = Mathf.Max(burstMax, sampleMax);
            }
            Check("연사 10회: 누적 없음", burstMax <= hud.MaxFlashAlpha + tolerance, $"max {burstMax:0.000} (상한 {hud.MaxFlashAlpha:0.00})");
            yield return WaitUntilClear(2f);
            Check("저체력 기준 이상에서는 테두리 없음", hud.CurrentAlpha <= 0f,
                  $"HP {Ratio():0%} / 기준 {hud.LowHealthRatio:0%}, alpha {hud.CurrentAlpha:0.000}");

            // 저체력: 20% 까지 내리고 플래시가 사라진 뒤 2초 측정
            SetHealthRatio(0.20f);
            yield return new WaitForSeconds(1.5f);
            yield return Sample(2f);
            Check("저체력(20%): 테두리 유지", sampleMin > 0f, $"min {sampleMin:0.000}");
            Check("저체력(20%): 맥박 깜빡임", sampleMax - sampleMin > 0.01f, $"폭 {sampleMax - sampleMin:0.000}");
            Check("저체력(20%): 상한 이하", sampleMax <= hud.MaxTotalAlpha + tolerance, $"max {sampleMax:0.000}");
            float lowMax20 = sampleMax;

            // 저체력 + 큰 피해: 합산 상한
            Damage(Percent(0.15f));
            yield return Sample(0.3f);
            Check("저체력 + 피격: 전체 상한(maxTotalAlpha) 이하", sampleMax <= hud.MaxTotalAlpha + 0.001f,
                  $"max {sampleMax:0.000} (상한 {hud.MaxTotalAlpha:0.00})");

            yield return new WaitForSeconds(1.5f);
            yield return Sample(2f);
            Check("체력이 낮을수록 더 진함 (5% > 20%)", sampleMax > lowMax20, $"{sampleMax:0.000} > {lowMax20:0.000}");

            // 회복: 기준 위로 올리면 사라져야 함
            player.Heal(Percent(0.55f));
            yield return Sample(1.5f);
            Check("회복 후 1.5초 내 소멸", hud.CurrentAlpha <= 0f, $"HP {Ratio():0%}, alpha {hud.CurrentAlpha:0.000}");

            // 피해 0 은 무시
            before = damagedEvents;
            Damage(0);
            yield return Sample(0.3f);
            Check("피해 0: 반응 없음", sampleMax <= 0f && damagedEvents == before, $"max {sampleMax:0.000}, 이벤트 {damagedEvents - before}회");

            player.Heal(player.MaxHealth);
            int passed = results.FindAll(r => r.Passed).Count;
            string summary = $"[DamageVignetteTester] 결과 {passed} / {results.Count} 통과";
            if (passed == results.Count) Debug.Log(summary);
            else Debug.LogError(summary);
            running = null;
        }

        // ── 헬퍼 ──

        private int Percent(float ratio) => Mathf.Max(1, Mathf.RoundToInt(player.MaxHealth * ratio));
        private float Ratio() => player.MaxHealth > 0 ? (float)player.CurrentHealth / player.MaxHealth : 0f;

        private void Damage(int amount)
        {
            var self = transform.position;
            var target = player.transform.position;
            player.TakeDamage(new DamageInfo(amount, DamageKind.MonsterAttack, Faction.Monster, gameObject,
                                             target, (target - self).normalized));
        }

        private void SetHealthRatio(float ratio)
        {
            int target = Mathf.Max(1, Mathf.RoundToInt(player.MaxHealth * ratio));
            if (player.CurrentHealth > target) Damage(player.CurrentHealth - target);
            else if (player.CurrentHealth < target) player.Heal(target - player.CurrentHealth);
        }

        /// <summary>seconds 동안 매 프레임 alpha 를 재서 sampleMin / sampleMax 에 기록</summary>
        private IEnumerator Sample(float seconds)
        {
            sampleMin = float.MaxValue;
            sampleMax = 0f;
            float end = Time.time + seconds;
            do
            {
                yield return null;
                float a = hud.CurrentAlpha;
                sampleMin = Mathf.Min(sampleMin, a);
                sampleMax = Mathf.Max(sampleMax, a);
            } while (Time.time < end);
        }

        private IEnumerator WaitUntilClear(float timeout)
        {
            float end = Time.time + timeout;
            while (hud.CurrentAlpha > 0f && Time.time < end) yield return null;
        }

        private void Check(string name, bool passed, string detail)
        {
            results.Add(new Result { Name = name, Passed = passed, Detail = detail });
            string line = $"[DamageVignetteTester] {(passed ? "PASS" : "FAIL")} {name} — {detail}";
            if (passed) Debug.Log(line);
            else Debug.LogWarning(line);
        }

        // ── 화면 표시 ──

        private void OnGUI()
        {
            style ??= new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true };
            var lines = new List<string>
            {
                running != null ? "<b>비네트 테스트 실행 중...</b>" : "<b>비네트 테스트</b>  [P] 재실행  [K] 피해5%  [L] 피해30%  [O] 회복20%",
                player != null && hud != null ? $"HP {player.CurrentHealth}/{player.MaxHealth}   alpha {hud.CurrentAlpha:0.000}" : "Player / HUD 없음",
            };
            foreach (var r in results)
                lines.Add($"<color={(r.Passed ? "#7CFC7C" : "#FF6060")}>{(r.Passed ? "PASS" : "FAIL")}</color> {r.Name}  <size=14>{r.Detail}</size>");
            GUI.Label(new Rect(20f, 20f, 900f, 40f + lines.Count * 26f), string.Join("\n", lines), style);
        }
    }
}
