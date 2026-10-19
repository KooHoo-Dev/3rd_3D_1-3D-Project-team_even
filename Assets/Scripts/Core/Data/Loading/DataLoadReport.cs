using System.Collections.Generic;
using System.Text;

namespace HybridShooter.Core.Data.Loading
{
    /// <summary>
    /// 테이블 로드 중 발견된 오류/경고를 모아두는 보고서.
    /// 하나 틀렸다고 바로 멈추지 않고 "전부" 모아서 한 번에 보여준다. (기획 데이터 수정 → 재실행 횟수를 줄이기 위함)
    /// </summary>
    public sealed class DataLoadReport
    {
        private readonly List<string> errors = new List<string>();
        private readonly List<string> warnings = new List<string>();

        public IReadOnlyList<string> Errors => errors;
        public IReadOnlyList<string> Warnings => warnings;
        public bool HasError => errors.Count > 0;

        public void Error(string table, object id, string message) => errors.Add($"[{table} #{id}] {message}");
        public void Warn(string table, object id, string message) => warnings.Add($"[{table} #{id}] {message}");

        /// <summary>조건이 false 면 오류를 기록한다. 검증 코드를 한 줄로 쓰기 위한 헬퍼.</summary>
        public bool Check(bool condition, string table, object id, string message)
        {
            if (condition == false) Error(table, id, message);
            return condition;
        }

        public string BuildSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[DataManager] 테이블 로드 결과 - 오류 {errors.Count}건, 경고 {warnings.Count}건");
            foreach (var e in errors) sb.AppendLine("  ERROR " + e);
            foreach (var w in warnings) sb.AppendLine("  WARN  " + w);
            return sb.ToString();
        }
    }
}
