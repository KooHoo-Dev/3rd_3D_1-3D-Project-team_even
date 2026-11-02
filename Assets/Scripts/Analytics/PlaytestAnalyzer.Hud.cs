using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HybridShooter.Analytics
{
    /// <summary>
    /// 화면 표시 (F8 켜기/끄기 · F9 지금 저장 · F10 저장 폴더 열기).
    /// 지금 라운드의 최근 30초 피해와 판정, 30초 구간, 필드 압박, 피해 출처, 위기, 명중률을 띄운다
    /// </summary>
    public sealed partial class PlaytestAnalyzer
    {
        private const string ColorEasy = "#7CD67C", ColorMedium = "#FFD166", ColorHard = "#FF6B6B";

        [Header("HUD")]
        [SerializeField] private bool hudVisible = true;
        [SerializeField] private Key toggleKey = Key.F8;
        [SerializeField] private Key saveKey = Key.F9;
        [SerializeField] private Key openFolderKey = Key.F10;
        [Tooltip("화면 왼쪽 위 기준 위치 (애니메이션 디버그 HUD(F3)와 겹치지 않게 아래로)")]
        [SerializeField] private Vector2 hudPosition = new Vector2(10f, 220f);
        [SerializeField] private int fontSize = 15;

        private readonly StringBuilder hud = new StringBuilder(1024);
        private GUIStyle hudStyle;

        private void UpdateHudInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard[toggleKey].wasPressedThisFrame) hudVisible = !hudVisible;
            if (keyboard[saveKey].wasPressedThisFrame) SaveNow();
            if (keyboard[openFolderKey].wasPressedThisFrame) OpenReportFolder();
        }

        private void OnGUI()
        {
            if (hudVisible == false) return;
            if (hudStyle == null) hudStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = fontSize, richText = true };

            hud.Clear();
            hud.AppendLine($"<b>플레이 분석</b>  ({toggleKey} 숨기기 · {saveKey} 저장 · {openFolderKey} 폴더 열기)");
            if (player != null && player.DebugInvincible) hud.AppendLine($"<color={ColorHard}><b>디버그 무적 켜짐: 받은 피해가 기록되지 않음</b></color>");

            if (current == null)
            {
                hud.AppendLine("라운드 시작 대기 중");
            }
            else
            {
                RoundRecord r = current;
                StageRecord stage = r.CurrentStage;
                hud.AppendLine($"라운드 {r.Round} · {PhaseName(phase)} · {Clock(roundTime)}" + (stage != null ? $" · 스테이지 {stage.Id}" : ""));

                hud.AppendLine($"최근 {windowSeconds:0}초 피해 {Graded(windowDamage, windowDamage.ToString())} / 기준 {mediumDamage}" +
                               $"   군중전 최대 {Graded(r.MaxWindowDamage, r.MaxWindowDamage.ToString())}");

                hud.Append($"{windowSeconds:0}초 구간: ");
                bool any = false;
                foreach (Bucket b in Buckets(r))
                {
                    if (any) hud.Append(" · ");
                    bool running = r.InCrowd && b.End >= roundTime - 0.01f;
                    string text = running ? $"{b.Damage}(진행)" : b.Damage.ToString();
                    hud.Append(b.Judged && running == false ? Graded(b.PerWindow, text) : text);
                    any = true;
                }
                hud.AppendLine(any ? "" : "-");

                hud.AppendLine($"생존 {alive.Count} (최대 {r.PeakAlive}) · 스폰 {r.Spawned} · 처치 {r.PlayerKills}" +
                               (r.OtherDeaths > 0 ? $" · 기타 사망 {r.OtherDeaths}" : ""));

                string nearest = nearestDistance >= 0f ? $"{nearestDistance:0.0}m" : "-";
                int attempts = r.Attacks.Values.Sum(a => a.Attempts);
                int executed = r.Attacks.Values.Sum(a => a.Executed);
                int hits = r.Attacks.Values.Sum(a => a.Hits);
                hud.AppendLine($"근접({nearRadius:0.#}m) {nearCount} · 최근접 {nearest} · 붙어 있던 시간 {r.Near.OccupiedRatio * 100f:0}%" +
                               $" · 몬스터 공격 시도 {attempts} 실행 {executed} 명중 {hits}");
                string nearKinds = string.Join(" · ", r.Monsters.Values.Where(m => m.NearNow > 0).Select(m => $"{m.Name} {m.NearNow}"));
                if (roundActive && nearKinds.Length > 0) hud.AppendLine($"지금 근접: {nearKinds}");

                var sources = r.InCrowd ? r.CrowdSources : r.BossSources;
                string top = string.Join(" · ", sources.OrderByDescending(p => p.Value.Damage).Take(3).Select(p => $"{p.Key} {p.Value.Damage}"));
                hud.AppendLine($"피해 출처: {(top.Length > 0 ? top : "-")}");

                string minHp = r.MinHp == int.MaxValue ? "-" : r.MinHp.ToString();
                hud.AppendLine($"HP {hp}/{maxHp} (최저 {minHp}) · 쓰러짐 {r.DownTimes.Count} · 목숨 {lives}/{maxLives} · 회복 팩 사용 {r.HealPacksUsed}");

                int shots = r.Weapons.Values.Sum(w => w.Shots);
                int hitShots = r.Weapons.Values.Sum(w => w.HitShots);
                hud.AppendLine($"명중률 {Percent(hitShots, shots)} (발사 {shots}) · 수류탄 {r.GrenadesThrown}개 처치 {r.GrenadeKills}");
            }
            hud.Append($"<size={Mathf.Max(10, fontSize - 3)}>저장: {lastSaveInfo}</size>");

            var content = new GUIContent(hud.ToString());
            Vector2 size = hudStyle.CalcSize(content);
            GUI.Box(new Rect(hudPosition.x, hudPosition.y, size.x + 10f, size.y + 6f), content, hudStyle);
        }

        /// <summary>구간 피해 값에 따라 하/중/상 색을 입힌 글자</summary>
        private string Graded(float damage, string text)
        {
            string color = damage >= hardDamage ? ColorHard : damage >= mediumDamage ? ColorMedium : ColorEasy;
            return $"<color={color}><b>{text}</b></color> [{Grade(damage)}]";
        }
    }
}
