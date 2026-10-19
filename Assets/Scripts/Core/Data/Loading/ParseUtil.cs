using System;
using System.Collections.Generic;
using System.Globalization;

namespace HybridShooter.Core.Data.Loading
{
    /// <summary>DTO(문자열) → Def(타입) 변환 헬퍼. 실패하면 report 에 기록하고 기본값을 돌려준다.</summary>
    internal static class ParseUtil
    {
        public const char ListSeparator = ';';

        public static TEnum Enum<TEnum>(string raw, DataLoadReport report, string table, object id, string column)
            where TEnum : struct, Enum
        {
            if (System.Enum.TryParse(raw, true, out TEnum value) && System.Enum.IsDefined(typeof(TEnum), value))
                return value;

            report.Error(table, id, $"{column} 값 '{raw}' 은(는) 잘못된 값입니다. 허용: {string.Join("/", System.Enum.GetNames(typeof(TEnum)))}");
            return default;
        }

        /// <summary>"2001;2002" → [2001, 2002]. 빈 문자열이면 빈 배열.</summary>
        public static int[] IntList(string raw, DataLoadReport report, string table, object id, string column)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<int>();

            var parts = raw.Split(ListSeparator);
            var list = new List<int>(parts.Length);
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) list.Add(v);
                else report.Error(table, id, $"{column} 의 '{p}' 는 정수가 아닙니다. (구분자는 '{ListSeparator}')");
            }
            return list.ToArray();
        }
    }
}
