namespace DcfToSf2.Diagnostics;

internal enum DiagnosticSeverity
{
    Warning,
    Error,
}

internal enum DiagnosticCategory
{
    Nesting,
    Structure,
    FieldType,
}

internal sealed class DcfDiagnostic
{
    public DiagnosticSeverity Severity { get; init; }
    public DiagnosticCategory Category { get; init; }
    public int? Line { get; init; }
    public string? Field { get; init; }
    public required string Message { get; init; }

    public override string ToString()
    {
        string severity = Severity == DiagnosticSeverity.Error ? "ERROR  " : "WARNING";
        string linePart = Line is int l ? $" line={l}" : string.Empty;
        string fieldPart = !string.IsNullOrEmpty(Field) ? $" field={Field}" : string.Empty;
        return $"{severity} {Category}{linePart}{fieldPart}: {Message}";
    }
}

internal sealed class DcfDiagnostics
{
    private readonly List<DcfDiagnostic> _items = [];

    public IReadOnlyList<DcfDiagnostic> Items => _items;
    public int WarningCount => _items.Count(i => i.Severity == DiagnosticSeverity.Warning);
    public int ErrorCount => _items.Count(i => i.Severity == DiagnosticSeverity.Error);
    public bool HasErrors => ErrorCount > 0;
    public bool HasAny => _items.Count > 0;

    public void Add(
        DiagnosticSeverity severity,
        DiagnosticCategory category,
        string message,
        int? line = null,
        string? field = null
    )
    {
        _items.Add(
            new DcfDiagnostic
            {
                Severity = severity,
                Category = category,
                Message = message,
                Line = line,
                Field = field,
            }
        );
    }

    public void Warn(DiagnosticCategory category, string message, int? line = null, string? field = null) =>
        Add(DiagnosticSeverity.Warning, category, message, line, field);

    public void Error(DiagnosticCategory category, string message, int? line = null, string? field = null) =>
        Add(DiagnosticSeverity.Error, category, message, line, field);
}
