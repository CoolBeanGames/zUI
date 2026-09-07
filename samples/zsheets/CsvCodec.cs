using System.Text;

namespace ZSheets;

internal sealed record SheetData(string[] Headers, List<string[]> Rows);

internal static class CsvCodec
{
    public static SheetData Parse(string text)
    {
        var records = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                records.Add(row);
                row = new List<string>();
            }
            else field.Append(ch);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            records.Add(row);
        }

        if (records.Count == 0) return new SheetData(["A"], []);
        var width = Math.Max(1, records.Max(r => r.Count));
        var headers = Normalize(records[0], width);
        var rows = records.Skip(1).Select(r => Normalize(r, width)).ToList();
        return new SheetData(headers, rows);
    }

    public static string Write(SheetData sheet)
    {
        var lines = new List<string> { string.Join(',', sheet.Headers.Select(Escape)) };
        lines.AddRange(sheet.Rows.Select(row => string.Join(',', row.Select(Escape))));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string[] Normalize(IReadOnlyList<string> source, int width) =>
        Enumerable.Range(0, width).Select(i => i < source.Count ? source[i] : "").ToArray();

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
