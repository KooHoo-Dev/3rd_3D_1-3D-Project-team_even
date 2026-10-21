using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

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

        /// <summary>Resources 기준 경로(확장자 제외)의 에셋을 로드. 비었거나 없으면 report 에 오류를 기록하고 null.</summary>
        public static T Asset<T>(string path, DataLoadReport report, string table, object id, string column)
            where T : Object
        {
            if (string.IsNullOrEmpty(path))
            {
                report.Error(table, id, $"{column} 가 비었습니다");
                return null;
            }

            var asset = Resources.Load<T>(path);
            if (asset == null) report.Error(table, id, $"{column} 'Resources/{path}' 에서 {typeof(T).Name} 를 찾을 수 없습니다");
            return asset;
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
