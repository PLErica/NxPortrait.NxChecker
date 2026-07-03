namespace NxCheck.Core.Checks.Support;

/// <summary>
/// Elasticsearch `_cat/*?v` 표 형식 파서. 첫 줄이 헤더, 나머지가 행.
/// 각 행을 헤더명→값 딕셔너리로 반환(공백 구분).
/// </summary>
public static class CatTableParser
{
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> Parse(string text)
    {
        var lines = text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
            return [];

        var headers = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var rows = new List<IReadOnlyDictionary<string, string>>();

        foreach (var line in lines[1..])
        {
            var cells = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length && i < cells.Length; i++)
                row[headers[i]] = cells[i];
            rows.Add(row);
        }

        return rows;
    }
}
