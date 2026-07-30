namespace DcfToSf2.Parsing;

internal sealed class DcfHeader
{
    public string DocumentName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? MainBlock { get; set; }
    public string? KeyField { get; set; }
    public string? DateField { get; set; }
    public string? ConstrMain { get; set; }
    public string? ConstrBlock { get; set; }
    public string? GCodeField { get; set; }
}

internal sealed class ParsedFieldType
{
    public string RawToken { get; init; } = string.Empty;
    public char Kind { get; init; }
    public int Length { get; init; }
    public int Rows { get; init; } = 1;
    public bool IsBlock { get; init; }
    public bool IsValid { get; init; }
}

internal sealed class DcfParseResult
{
    public required DcfHeader Header { get; init; }
    public required List<Data> Fields { get; init; }
    public required Diagnostics.DcfDiagnostics Diagnostics { get; init; }
    public Dictionary<string, string> XsdLabels { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
