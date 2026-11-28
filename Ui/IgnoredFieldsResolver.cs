using DcfToSf2.Parsing;

namespace DcfToSf2.Ui;

internal sealed class IgnoredFieldRow
{
    public required string Block { get; init; }
    public required string FieldName { get; init; }
    public required string Label { get; init; }
    public required string MatchedBy { get; init; }
}

internal static class IgnoredFieldsResolver
{
    public static List<IgnoredFieldRow> Resolve(DcfParseResult parsed, IEnumerable<string> ignoreFields)
    {
        var ignore = ignoreFields
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ignore.Count == 0)
            return [];

        var rows = new List<IgnoredFieldRow>();
        foreach (var field in parsed.Fields)
        {
            if (IsBlockOpenMarker(field))
                continue;
            if (DcfParser.IsBuiltInServiceField(field.FieldElement.ElementName))
                continue;

            string name = field.FieldElement.ElementName;
            string? match = DcfParser.GetUserIgnoreMatch(name, ignore);
            if (match is null)
                continue;

            string block = string.IsNullOrWhiteSpace(field.Path) ? Consts.DefaultBlockName : field.Path;
            string label = ExtractComment(field.FieldElement.ElementValue);
            if (string.IsNullOrWhiteSpace(label))
                label = DcfParser.GetLastToken(name);

            rows.Add(
                new IgnoredFieldRow
                {
                    Block = block,
                    FieldName = name,
                    Label = label,
                    MatchedBy = FormatMatchReason(match, name),
                }
            );
        }

        return rows
            .OrderBy(r => r.Block, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string FormatMatchReason(string match, string fieldName)
    {
        if (DcfParser.IsIgnorePattern(match))
            return $"шаблон «{match}»";

        if (match.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
            return "точное имя";

        return $"суффикс «{match}»";
    }

    private static bool IsBlockOpenMarker(Data f) =>
        string.IsNullOrEmpty(f.FieldElement.ElementValue) && !string.IsNullOrEmpty(f.FieldElement.ElementName);

    private static string ExtractComment(string elementValue)
    {
        int pipe = elementValue.IndexOf('|');
        if (pipe < 0)
            return string.Empty;

        string c = elementValue[(pipe + 1)..].Trim().TrimStart('*', '!').Trim();
        foreach (var key in new[] { "NAMESPACE=", "PATTERN=", "SEQ=", "CHOICE=", "PRNT=", "MAX=", "OPT=" })
        {
            int i = c.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
                c = c[..i].Trim();
        }

        return c;
    }
}
