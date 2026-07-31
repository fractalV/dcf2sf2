using System.Text;
using System.Text.RegularExpressions;

namespace DcfToSf2.Writing;

internal sealed class Sf2Section
{
    public required string Name { get; init; }
    public List<string> BodyLines { get; } = [];
    public HashSet<string> DataFieldNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class Sf2FileContent
{
    public string DocumentName { get; set; } = string.Empty;
    public required string OriginalText { get; init; }
    public List<Sf2Section> Sections { get; } = [];
    public HashSet<string> SectionNames { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal static class Sf2Reader
{
    private static readonly Regex DataFieldRegex = new(
        @"^Data\s*-?\d+\s+-?\d+\s+-?\d+\s+-?\d+\s+(\S+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public static Sf2FileContent Read(string path)
    {
        Encoding encoding = Encoding.GetEncoding(1251);
        string originalText = File.ReadAllText(path, encoding);
        string normalized = originalText.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');

        var content = new Sf2FileContent { OriginalText = originalText };

        Sf2Section? current = null;
        foreach (var raw in lines)
        {
            string line = raw.TrimEnd();

            if (line.StartsWith("DOCUMENT=", StringComparison.OrdinalIgnoreCase))
            {
                content.DocumentName = line["DOCUMENT=".Length..].Trim();
                continue;
            }

            if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
            {
                current = new Sf2Section { Name = line[1..^1] };
                content.Sections.Add(current);
                content.SectionNames.Add(current.Name);
                continue;
            }

            if (current is null)
                continue;

            if (line == "/")
            {
                current = null;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            current.BodyLines.Add(line);
            var match = DataFieldRegex.Match(line);
            if (match.Success)
                current.DataFieldNames.Add(match.Groups[1].Value);
        }

        return content;
    }

    public static HashSet<string> GetDataFieldNames(Sf2FileContent file, string sectionName)
    {
        var section = file.Sections.FirstOrDefault(s =>
            s.Name.Equals(sectionName, StringComparison.OrdinalIgnoreCase)
        );
        return section?.DataFieldNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
