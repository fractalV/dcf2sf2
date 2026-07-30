namespace DcfToSf2.Layout.Templates;

internal enum TemplateSlotKind
{
    Data = 0,
    Text = 1,
    Pict = 2,
}

internal sealed class TemplateSlot
{
    public string Id { get; set; } = string.Empty;
    public required string Suffix { get; set; }
    public string Label { get; set; } = string.Empty;
    public TemplateSlotKind Kind { get; set; } = TemplateSlotKind.Data;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsIgnored { get; set; }
    /// <summary>Removed from this template layout (persisted); not emitted to SF2.</summary>
    public bool IsRemoved { get; set; }
    /// <summary>Added by dragging another template into this one; persisted with full metadata.</summary>
    public bool IsEmbedded { get; set; }
    public string Group { get; set; } = string.Empty;
    /// <summary>Optional name fragment to disambiguate duplicate suffixes (e.g. DTMDocDetails).</summary>
    public string StemHint { get; set; } = string.Empty;

    public bool IsExcluded => IsIgnored || IsRemoved;

    public static string MakeId(TemplateSlotKind kind, string group, string suffix, string? stemHint = null)
    {
        string id = $"{kind}:{group}:{suffix}";
        if (!string.IsNullOrEmpty(stemHint))
            id += $"@{stemHint}";
        return id;
    }

    public TemplateSlot Clone() =>
        new()
        {
            Id = Id,
            Suffix = Suffix,
            Label = Label,
            Kind = Kind,
            X = X,
            Y = Y,
            Width = Width,
            Height = Height,
            IsIgnored = IsIgnored,
            IsRemoved = IsRemoved,
            IsEmbedded = IsEmbedded,
            Group = Group,
            StemHint = StemHint,
        };
}

internal sealed class FieldGroupMatch
{
    public required List<Data> Fields { get; init; }
    public Data? HeaderField { get; init; }
    public string Stem { get; init; } = string.Empty;
    public string TemplateName { get; init; } = string.Empty;
}

internal interface IFieldGroupTemplate
{
    string Name { get; }

    FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields);

    int Layout(Block block, FieldGroupMatch match, int baseX, int y, int maxWidth, HashSet<string> ignoreFields);

    /// <summary>Default preview slots (without persisted overrides).</summary>
    IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields);

    /// <summary>Preview slots with persisted overrides applied.</summary>
    IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields);
}
