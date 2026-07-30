namespace DcfToSf2;

internal sealed class ConversionOptions
{
    public required FileInfo InputFile { get; init; }
    public required DirectoryInfo OutputDirectory { get; init; }
    public bool TwoColumn { get; init; }
    public bool ForceList { get; init; }
    public HashSet<string> IgnoreFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static HashSet<string> ParseIgnoreFields(string? value)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
            return set;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(part))
                set.Add(part);
        }

        return set;
    }
}
