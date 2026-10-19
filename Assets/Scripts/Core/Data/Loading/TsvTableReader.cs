using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace HybridShooter.Core.Data.Loading
{
    /// <summary>
    /// TSV 파일 → DTO 리스트. (StudySample 의 Jay.FileIO.TSVReader 설정 기반 + 행 단위 오류 보고)
    ///  - 1행: 헤더(=DTO 필드 이름과 정확히 일치)
    ///  - '#' 으로 시작하는 줄: 주석(기획 메모용), 빈 줄: 무시
    ///  - 한 행이 깨져도 나머지 행은 계속 읽고, 깨진 행은 DataLoadReport 에 줄 번호와 함께 기록
    /// UnityEngine 에 의존하지 않으므로 에디터/테스트 어디서든 호출 가능.
    /// </summary>
    public static class TsvTableReader
    {
        public const string Extension = ".tsv";

        private static readonly CsvConfiguration Config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = "\t",
            Mode = CsvMode.NoEscape,
            HasHeaderRecord = true,
            AllowComments = true,
            Comment = '#',
            IgnoreBlankLines = true,
            TrimOptions = TrimOptions.Trim,
            MemberTypes = MemberTypes.Fields,
            BadDataFound = null,
        };

        public static List<T> Read<T>(string folder, string tableName, DataLoadReport report)
        {
            var result = new List<T>();
            string path = Path.Combine(folder, tableName + Extension);

            if (File.Exists(path) == false)
            {
                report.Error(tableName, "-", $"파일이 없습니다: {path}");
                return result;
            }

            using var reader = new StreamReader(path, Encoding.UTF8);
            using var csv = new CsvReader(reader, Config);

            try
            {
                csv.Read();
                csv.ReadHeader();
                csv.ValidateHeader<T>();
            }
            catch (HeaderValidationException e)
            {
                report.Error(tableName, "header", $"헤더가 {typeof(T).Name} 필드와 맞지 않습니다. {FirstLine(e.Message)}");
                return result;
            }

            while (csv.Read())
            {
                try
                {
                    result.Add(csv.GetRecord<T>());
                }
                catch (CsvHelper.TypeConversion.TypeConverterException e)
                {
                    string column = e.MemberMapData?.Member?.Name ?? "?";
                    report.Error(tableName, $"line {csv.Parser.RawRow}", $"{column} 값 '{e.Text}' 을(를) {e.MemberMapData?.Type?.Name} 로 변환할 수 없습니다");
                }
                catch (Exception e)
                {
                    report.Error(tableName, $"line {csv.Parser.RawRow}", $"행 파싱 실패: {FirstLine(e.Message)}");
                }
            }

            return result;
        }

        private static string FirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return string.Empty;
            int idx = message.IndexOf('\n');
            return idx < 0 ? message : message.Substring(0, idx).TrimEnd('\r');
        }
    }
}
