using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace HybridShooter.Analytics
{
    /// <summary>
    /// 파일 저장. persistentDataPath/Playtest/시작시각/ 아래에 (빌드에서도 남는다)
    ///  - summary.txt  : 라운드별 요약 (사람이 읽는 용)
    ///  - timeline.csv : 라운드 시간 1초마다 HP·생존 몬스터·누적 스폰/처치·받은 피해
    ///  - events.csv   : 피격·스폰·처치·쓰러짐·아이템 등 사건 기록
    /// 라운드 클리어·게임오버·비활성화·종료, autoSaveInterval 마다 전체를 다시 쓴다. 엑셀에서 한글이 깨지지 않게 UTF-8 BOM
    /// </summary>
    public sealed partial class PlaytestAnalyzer
    {
        [Header("저장")]
        [Tooltip("이 간격(실제 초)마다 자동 저장. 0 = 라운드 클리어·게임오버·종료 때만")]
        [SerializeField, Min(0f)] private float autoSaveInterval = 30f;

        private static readonly UTF8Encoding Utf8Bom = new UTF8Encoding(true);
        private string reportFolder;
        private float nextAutoSave;
        private string lastSaveInfo = "아직 저장 안 함";

        public string ReportFolder => reportFolder;

        private void InitReport()
        {
            reportFolder = Path.Combine(Application.persistentDataPath, "Playtest", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            nextAutoSave = Time.unscaledTime + autoSaveInterval;
        }

        private void OnApplicationQuit() => SaveReport("종료");

        private void UpdateAutoSave()
        {
            if (autoSaveInterval <= 0f || Time.unscaledTime < nextAutoSave) return;
            nextAutoSave = Time.unscaledTime + autoSaveInterval;
            if (roundActive) SaveReport("자동");
        }

        [ContextMenu("지금 저장")]
        public void SaveNow() => SaveReport("수동");

        [ContextMenu("저장 폴더 열기")]
        public void OpenReportFolder()
        {
            Directory.CreateDirectory(reportFolder);
            Application.OpenURL("file:///" + reportFolder.Replace('\\', '/'));
        }

        private void SaveReport(string reason)
        {
            if (rounds.Count == 0) return;
            try
            {
                bool first = Directory.Exists(reportFolder) == false;
                Directory.CreateDirectory(reportFolder);
                File.WriteAllText(Path.Combine(reportFolder, "summary.txt"), BuildSummary(), Utf8Bom);
                File.WriteAllText(Path.Combine(reportFolder, "timeline.csv"), BuildTimelineCsv(), Utf8Bom);
                File.WriteAllText(Path.Combine(reportFolder, "events.csv"), BuildEventsCsv(), Utf8Bom);
                lastSaveInfo = $"{DateTime.Now:HH:mm:ss} {reason}";
                if (first || reason != "자동") Debug.Log($"[PlaytestAnalyzer] 분석 결과 저장 ({reason}): {reportFolder}", this);
            }
            catch (Exception ex)
            {
                lastSaveInfo = $"저장 실패: {ex.Message}";
                Debug.LogWarning($"[PlaytestAnalyzer] 저장 실패: {ex}", this);
            }
        }

        #region summary.txt
        private string BuildSummary()
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine($"=== 플레이 테스트 분석 · {DateTime.Now:yyyy-MM-dd HH:mm} · {Application.version} ===");
            sb.AppendLine($"기준: 군중전 {windowSeconds:0}초 동안 받은 피해  중 ≥ {mediumDamage} · 상 ≥ {hardDamage} (하 = 그 미만)");
            if (sawInvincible) sb.AppendLine("[주의] 디버그 무적이 켜진 적 있음 → 그동안 받은 피해는 기록되지 않았다");
            foreach (RoundRecord r in rounds) AppendRound(sb, r);
            return sb.ToString();
        }

        private void AppendRound(StringBuilder sb, RoundRecord r)
        {
            string result = r.GameOver ? "게임오버" : r.Cleared ? "클리어" : "진행 중";
            sb.AppendLine();
            sb.AppendLine($"── 라운드 {r.Round} ({result}, {Clock(r.EndTime)}) ──");

            // 1. 군중전 30초당 받은 피해
            float crowdEnd = CrowdEnd(r);
            float perWindow = crowdEnd > 0f ? r.CrowdDamage * windowSeconds / crowdEnd : 0f;
            sb.AppendLine($"[군중전 피해] 0:00~{Clock(crowdEnd)}  받은 피해 {r.CrowdDamage} → {windowSeconds:0}초당 평균 {perWindow:0.0} [{Grade(perWindow)}]");
            sb.AppendLine($"  최근 {windowSeconds:0}초 최대 {r.MaxWindowDamage} ({Clock(r.MaxWindowAt)} 시점) [{Grade(r.MaxWindowDamage)}]");
            var gradeCount = new Dictionary<string, int> { { "하", 0 }, { "중", 0 }, { "상", 0 } };
            foreach (Bucket b in Buckets(r))
            {
                string line = $"  {Clock(b.Start)}~{Clock(b.End)}  {b.Damage,4}";
                if (b.Duration < windowSeconds - 0.01f) line += $"  ({b.Duration:0}초, {windowSeconds:0}초 환산 {b.PerWindow:0})";
                if (b.Judged)
                {
                    string grade = Grade(b.PerWindow);
                    gradeCount[grade]++;
                    line += $"  [{grade}]";
                }
                else line += "  [판정 제외: 짧음]";
                sb.AppendLine(line);
            }
            sb.AppendLine($"  구간 판정: 하 {gradeCount["하"]} · 중 {gradeCount["중"]} · 상 {gradeCount["상"]}");

            // 2. 피해 출처
            AppendSources(sb, "[피해 출처 · 군중전]", r.CrowdSources, r.CrowdDamage);
            if (r.BossSources.Count > 0) AppendSources(sb, "[피해 출처 · 보스전]", r.BossSources, r.BossDamage);

            // 3. 필드 압박
            sb.AppendLine($"[필드 압박] 스폰 {r.Spawned} · 플레이어 처치 {r.PlayerKills} ({Percent(r.PlayerKills, r.Spawned)}) · 기타 사망 {r.OtherDeaths}" +
                          $" · 최대 동시 생존 {r.PeakAlive} ({Clock(r.PeakAliveAt)})");

            // 4. 스테이지별
            sb.AppendLine("[스테이지별]");
            foreach (StageRecord s in r.Stages)
            {
                string end = s.EndTime >= 0f ? Clock(s.EndTime) : "진행 중";
                string aliveAtEnd = s.AliveAtEnd >= 0 ? s.AliveAtEnd.ToString() : "-";
                sb.AppendLine($"  스테이지 {s.Id} ({Clock(s.StartTime)}~{end})  스폰 {s.Spawned,3} · 처치 {s.Kills,3} · 끝날 때 생존 {aliveAtEnd,3} · 받은 피해 {s.Damage,4}" +
                              $" · 평균 근접 {s.Near.AvgCount:0.0} · 근접 시간 {s.Near.OccupiedRatio * 100f:0}%");
            }

            // 근접 압박 · 몬스터 공격 (받은 피해가 적을 때 원인 가르기)
            string nearest = r.Near.AvgNearest >= 0f ? $"{r.Near.AvgNearest:0.0}m" : "-";
            sb.AppendLine($"[근접 압박 · 군중전] 반경 {nearRadius:0.#}m 안 평균 {r.Near.AvgCount:0.00}마리 · 1마리 이상 붙어 있던 시간 {r.Near.OccupiedRatio * 100f:0}%" +
                          $" · 최대 {r.MaxNear}마리 ({Clock(r.MaxNearAt)}) · 최근접 몬스터 평균 거리 {nearest}");
            sb.AppendLine("[몬스터 공격] 시도(예고) · 실행 · 취소 · 명중 · 명중률(명중/실행) · 피해 · 지속 피해 틱");
            if (r.Attacks.Count == 0) sb.AppendLine("  없음");
            foreach (AttackStat a in r.Attacks.Values.OrderByDescending(a => a.Attempts))
                sb.AppendLine($"  {a.Name,-24} {a.Attempts,4} · {a.Executed,4} · {a.Cancelled,4} · {a.Hits,4} · {Percent(a.Hits, a.Executed),4} · {a.Damage,4} · {a.StatusTicks,3}");
            sb.AppendLine("  해석: 시도가 적고 근접 시간이 짧으면 → 접근 실패(이동·길찾기·대기) / 실행은 많은데 명중률이 낮으면 → 회피 쉬움(예고 시간·사거리)");

            // 5. 몬스터 종류별 처치 시간
            sb.AppendLine("[몬스터별] 스폰 · 처치 · 기타 사망 · 평균 처치 시간(스폰→사망) · 최대");
            foreach (MonsterStat m in r.Monsters.Values.OrderByDescending(m => m.Spawned))
                sb.AppendLine($"  {m.Name,-14} {m.Spawned,3} · {m.Kills,3} · {m.OtherDeaths,3} · {m.AvgLifetime,5:0.0}초 · {m.LifetimeMax,5:0.0}초");

            // 5-1. 종류별 접근: 다가오기 전에 죽는지, 다가와서 붙어 있는지
            float[] edges = MonsterStat.KillDistanceEdges;
            sb.AppendLine($"[몬스터별 접근] 반경 {nearRadius:0.#}m 도달(한 번이라도 들어온 개체/스폰) · 도달까지 평균 · 평균 근접 마리(군중전)" +
                          $" · 처치 거리 평균 · 분포 <{edges[0]:0}m / {edges[0]:0}~{edges[1]:0} / {edges[1]:0}~{edges[2]:0} / {edges[2]:0}m+");
            foreach (MonsterStat m in r.Monsters.Values.OrderByDescending(m => m.Spawned))
            {
                string reachTime = m.Reached > 0 ? $"{m.AvgReachTime:0.0}초" : "-";
                string killDistance = m.AvgKillDistance >= 0f ? $"{m.AvgKillDistance:0.0}m" : "-";
                int[] bins = m.KillDistanceBins;
                sb.AppendLine($"  {m.Name,-14} {m.Reached,3}/{m.Spawned,-3} ({Percent(m.Reached, m.Spawned),4}) · {reachTime,6} · {m.Near.AvgCount,4:0.00}" +
                              $" · {killDistance,6} · {bins[0],3} / {bins[1],3} / {bins[2],3} / {bins[3],3}");
            }
            sb.AppendLine("  해석: 도달률이 낮고 처치 거리가 멀면 → 다가오기 전에 처치됨(체력·스폰 간격·스폰 거리) / 도달률은 높은데 공격 시도가 적으면 → 붙은 뒤 공격 조건 문제");

            // 6. 보스 진입 상태 · 보스전
            if (r.BossEntry != null)
            {
                BossEntrySnapshot b = r.BossEntry;
                string names = string.Join(", ", b.AliveByName.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}"));
                sb.AppendLine($"[보스 진입] {Clock(b.Time)}  HP {b.Hp}/{b.MaxHp} · 목숨 {b.Lives}/{b.MaxLives} · 수류탄 {b.Grenades} · 회복 팩 {b.HealPacks}" +
                              $" · 남은 몬스터 {b.Alive}" + (names.Length > 0 ? $" ({names})" : ""));
            }
            if (r.BossStartTime >= 0f)
            {
                float bossEnd = r.BossDefeatedTime >= 0f ? r.BossDefeatedTime : r.EndTime;
                float bossTime = bossEnd - r.BossStartTime;
                float bossPerWindow = bossTime > 0f ? r.BossDamage * windowSeconds / bossTime : 0f;
                string defeated = r.BossDefeatedTime >= 0f ? $"처치 {Clock(r.BossDefeatedTime)}" : "미처치";
                sb.AppendLine($"[보스전] {bossTime:0}초 · 받은 피해 {r.BossDamage} ({windowSeconds:0}초당 {bossPerWindow:0.0}) · {defeated}");
            }

            // 7. 위기 지표
            string minHp = r.MinHp == int.MaxValue ? "-" : $"{r.MinHp} ({Clock(r.MinHpAt)})";
            string downs = r.DownTimes.Count > 0 ? $" ({string.Join(", ", r.DownTimes.Select(Clock))})" : "";
            string supplies = r.Supplies.Count > 0 ? string.Join(", ", r.Supplies.Select(p => $"{p.Key} {p.Value}")) : "없음";
            sb.AppendLine($"[위기] 최저 HP {minHp} · 쓰러짐 {r.DownTimes.Count}회{downs} · 회복 팩 사용 {r.HealPacksUsed} (회복량 {r.HealedHp}) · 보급 획득 {supplies}");

            // 8. 사격
            int shots = r.Weapons.Values.Sum(w => w.Shots);
            int hitShots = r.Weapons.Values.Sum(w => w.HitShots);
            sb.AppendLine($"[사격] 전체 명중률 {Percent(hitShots, shots)} (발사 {shots}) · 수류탄 투척 {r.GrenadesThrown} · 수류탄 처치 {r.GrenadeKills}");
            foreach (WeaponStat w in r.Weapons.Values.OrderByDescending(w => w.Shots))
                sb.AppendLine($"  {w.Name,-12} 발사 {w.Shots,4} · 명중 {w.HitShots,4} ({Percent(w.HitShots, w.Shots)}) · 피해 {w.Damage,5} · 처치 {w.Kills,3}");
        }

        private static void AppendSources(StringBuilder sb, string title, Dictionary<string, SourceStat> sources, int total)
        {
            sb.AppendLine(title);
            if (sources.Count == 0) sb.AppendLine("  없음");
            foreach (KeyValuePair<string, SourceStat> p in sources.OrderByDescending(p => p.Value.Damage))
                sb.AppendLine($"  {p.Key,-24} {p.Value.Damage,4} ({p.Value.Hits}회, {Percent(p.Value.Damage, total)})");
        }

        private static string Percent(int part, int whole) => whole > 0 ? $"{part * 100f / whole:0}%" : "-";
        #endregion

        #region 30초 구간
        private readonly struct Bucket
        {
            public readonly float Start, End, PerWindow;
            public readonly int Damage;
            public readonly bool Judged;
            public float Duration => End - Start;

            public Bucket(float start, float end, int damage, float windowSeconds, float minJudgeSeconds)
            {
                Start = start; End = end; Damage = damage;
                float duration = end - start;
                Judged = duration >= Mathf.Min(minJudgeSeconds, windowSeconds);
                PerWindow = duration > 0f ? damage * windowSeconds / duration : 0f;
            }
        }

        /// <summary>군중전 끝(진행 중이면 지금) 시각</summary>
        private static float CrowdEnd(RoundRecord r) => r.InCrowd ? r.EndTime : r.CrowdEndTime;

        /// <summary>군중전을 windowSeconds 단위로 자른 구간들. 마지막 구간은 군중전 끝에서 잘린다</summary>
        private IEnumerable<Bucket> Buckets(RoundRecord r)
        {
            float crowdEnd = CrowdEnd(r);
            int count = Mathf.Max(r.CrowdBuckets.Count, Mathf.CeilToInt(crowdEnd / windowSeconds));
            for (int i = 0; i < count; i++)
            {
                float start = i * windowSeconds;
                float end = Mathf.Min(start + windowSeconds, crowdEnd);
                if (end <= start) yield break;
                int damage = i < r.CrowdBuckets.Count ? r.CrowdBuckets[i] : 0;
                yield return new Bucket(start, end, damage, windowSeconds, minJudgeSeconds);
            }
        }
        #endregion

        #region CSV
        private string BuildTimelineCsv()
        {
            var sb = new StringBuilder(timeline.Count * 48 + 128);
            sb.AppendLine($"라운드,라운드시간(초),상태,스테이지,HP,생존몬스터,근접몬스터({nearRadius:0.#}m),최근접거리(m),누적스폰,누적처치,이번1초피해,최근{windowSeconds:0}초피해");
            foreach (TimelineRow t in timeline)
            {
                string nearest = t.Nearest >= 0f ? t.Nearest.ToString("0.0") : "";
                sb.AppendLine($"{t.Round},{t.Second},{Csv(t.Phase)},{t.StageId},{t.Hp},{t.Alive},{t.Near},{nearest},{t.Spawned},{t.Kills},{t.Damage},{t.WindowDamage}");
            }
            return sb.ToString();
        }

        private string BuildEventsCsv()
        {
            var sb = new StringBuilder(events.Count * 64 + 128);
            sb.AppendLine("세션시간(초),라운드,라운드시간(초),상태,종류,내용,값,HP");
            foreach (EventRow e in events)
                sb.AppendLine($"{e.SessionTime:0.00},{e.Round},{e.RoundTime:0.00},{Csv(e.Phase)},{Csv(e.Type)},{Csv(e.Detail)},{e.Value},{e.Hp}");
            return sb.ToString();
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.IndexOfAny(new[] { ',', '"', '\n' }) < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        #endregion
    }
}
