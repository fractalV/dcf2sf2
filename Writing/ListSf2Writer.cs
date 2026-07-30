using System.Text;
using System.Text.RegularExpressions;
using DcfToSf2.Layout;
using DcfToSf2.Parsing;

namespace DcfToSf2.Writing;

internal static class ListSf2Writer
{
    public static string GetPath(DirectoryInfo outputDirectory, string documentName) =>
        Path.Combine(outputDirectory.FullName, $"{documentName}List.sf2");

    public static void Write(
        DirectoryInfo outputDirectory,
        DcfParseResult parsed,
        Sf2Document document,
        ConversionOptions options
    )
    {
        string path = GetPath(outputDirectory, document.DocumentName);
        if (File.Exists(path) && !options.ForceList)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("List уже существует (пропуск): " + path);
            Console.ResetColor();
            return;
        }

        Directory.CreateDirectory(outputDirectory.FullName);
        var lines = Render(parsed, document);
        using var sw = new StreamWriter(path, false, Encoding.GetEncoding(1251));
        foreach (var line in lines)
            sw.WriteLine(line);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Файл List записан: " + path);
        Console.ResetColor();
    }

    public static List<string> Render(DcfParseResult parsed, Sf2Document document)
    {
        var header = parsed.Header;
        string title = FirstNonEmpty(
            header.ShortName,
            header.FullName,
            parsed.XsdLabels.GetValueOrDefault("__root__"),
            document.DocumentName
        );

        List<string> lines = [$"DOCUMENT={document.DocumentName}"];
        lines.Add("List=@FileDate,Дата и время,18,T");

        if (!string.IsNullOrWhiteSpace(header.KeyField))
        {
            foreach (var key in SplitKeyFields(header.KeyField))
                lines.Add($"List={key},Номер,20");
        }

        if (!string.IsNullOrWhiteSpace(header.DateField))
            lines.Add($"List={header.DateField},Дата,12");

        // Heuristic columns if Key/Date missing
        if (string.IsNullOrWhiteSpace(header.KeyField) && string.IsNullOrWhiteSpace(header.DateField))
        {
            foreach (var col in HeuristicListColumns(parsed.Fields))
                lines.Add(col);
        }
        else
        {
            // Extra useful columns from PRNT=1 / OrganizationName
            foreach (var f in parsed.Fields.Where(HasPrnt).Take(2))
            {
                string label = Trunc(ExtractComment(f.FieldElement.ElementValue), 20);
                if (string.IsNullOrEmpty(label))
                    label = DcfParser.GetLastToken(f.FieldElement.ElementName);
                lines.Add($"List={f.FieldElement.ElementName},{label},24");
            }
        }

        lines.Add($"[{Consts.DefaultBlockName}]");
        lines.Add("780x160 0 0 `");
        lines.Add(FormatText(8, 0, Math.Min(200, title.Length * 5), 24, title));

        // Compact preview fields
        int y = 36;
        var preview = PickPreviewFields(parsed);
        int x = 8;
        foreach (var f in preview.Take(8))
        {
            int w = 180;
            lines.Add(FormatData(x, y, w, 16, f.FieldElement.ElementName));
            x += w + 8;
            if (x > 600)
            {
                x = 8;
                y += 28;
            }
        }

        lines.Add("/");
        return lines;
    }

    private static IEnumerable<string> HeuristicListColumns(List<Data> fields)
    {
        var number = fields.FirstOrDefault(f =>
        {
            string n = f.FieldElement.ElementName;
            string last = DcfParser.GetLastToken(n);
            return last.Contains("Number", StringComparison.OrdinalIgnoreCase)
                || last.Equals("Num", StringComparison.OrdinalIgnoreCase);
        });
        if (number is not null)
            yield return $"List={number.FieldElement.ElementName},Номер,20";

        var date = fields.FirstOrDefault(f =>
            DcfParser.GetLastToken(f.FieldElement.ElementName).Contains("Date", StringComparison.OrdinalIgnoreCase)
        );
        if (date is not null)
            yield return $"List={date.FieldElement.ElementName},Дата,12";

        foreach (var f in fields.Where(HasPrnt).Take(2))
        {
            string label = Trunc(ExtractComment(f.FieldElement.ElementValue), 24);
            yield return $"List={f.FieldElement.ElementName},{label},24";
        }
    }

    private static List<Data> PickPreviewFields(DcfParseResult parsed)
    {
        var list = new List<Data>();
        if (!string.IsNullOrWhiteSpace(parsed.Header.KeyField))
        {
            foreach (var key in SplitKeyFields(parsed.Header.KeyField))
            {
                var f = parsed.Fields.FirstOrDefault(i =>
                    i.FieldElement.ElementName.Equals(key, StringComparison.OrdinalIgnoreCase)
                );
                if (f is not null)
                    list.Add(f);
            }
        }

        if (!string.IsNullOrWhiteSpace(parsed.Header.DateField))
        {
            var f = parsed.Fields.FirstOrDefault(i =>
                i.FieldElement.ElementName.Equals(parsed.Header.DateField, StringComparison.OrdinalIgnoreCase)
            );
            if (f is not null)
                list.Add(f);
        }

        list.AddRange(parsed.Fields.Where(HasPrnt).Take(4));
        return list.DistinctBy(f => f.FieldElement.ElementName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<string> SplitKeyFields(string keyField)
    {
        // KeyField can be A+B or A\#+B — take identifiers
        foreach (Match m in Regex.Matches(keyField, @"[A-Za-z_][A-Za-z0-9_]*"))
            yield return m.Value;
    }

    private static bool HasPrnt(Data f) =>
        f.FieldElement.ElementValue.Contains("PRNT=", StringComparison.OrdinalIgnoreCase);

    private static string ExtractComment(string value)
    {
        int pipe = value.IndexOf('|');
        if (pipe < 0)
            return string.Empty;
        string c = value[(pipe + 1)..].Trim().TrimStart('*', '!').Trim();
        int cut = c.IndexOfAny(['N', 'P', 'S', 'C', 'M']); // rough; better strip attrs
        foreach (var key in new[] { "NAMESPACE=", "PATTERN=", "SEQ=", "CHOICE=", "PRNT=", "MAX=", "OPT=" })
        {
            int i = c.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
                c = c[..i].Trim();
        }

        return c;
    }

    private static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

    private static string FormatData(int x, int y, int w, int h, string name) =>
        $"Data{x,12}{y,6}{w,6}{h,5} {name}";

    private static string FormatText(int x, int y, int w, int h, string text) =>
        $"Text{x,12}{y,6}{w,6}{h,5} `{text}";

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
