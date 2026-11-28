using DcfToSf2.Parsing;

namespace DcfToSf2.Layout.Templates;

internal sealed class UserTemplateDefinitionDto
{
    public string Name { get; set; } = string.Empty;

    public string? MatchPath { get; set; }

    public bool MatchBySuffixes { get; set; } = true;

    public List<UserTemplateFieldDto> Fields { get; set; } = [];
}

internal sealed class UserTemplateFieldDto
{
    public string Suffix { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Width { get; set; } = 120;
    public int Height { get; set; } = Consts.DefaultHeightData;
}

internal sealed class UserDefinedFieldGroupTemplate : IFieldGroupTemplate
{
    private readonly UserTemplateDefinitionDto _def;
    private readonly HashSet<string> _suffixes;

    public UserDefinedFieldGroupTemplate(UserTemplateDefinitionDto definition)
    {
        _def = definition;
        _suffixes = definition
            .Fields.Select(f => f.Suffix)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string Name => _def.Name;
    public string? MatchPath => _def.MatchPath;
    public UserTemplateDefinitionDto Definition => _def;

    public static bool MatchesPath(string? blockPath, string? matchPath)
    {
        if (string.IsNullOrWhiteSpace(matchPath) || string.IsNullOrWhiteSpace(blockPath))
            return false;
        if (blockPath.Equals(matchPath, StringComparison.OrdinalIgnoreCase))
            return true;
        string leaf = matchPath.Contains('\\')
            ? matchPath[(matchPath.LastIndexOf('\\') + 1)..]
            : matchPath;
        string blockLeaf = blockPath.Contains('\\')
            ? blockPath[(blockPath.LastIndexOf('\\') + 1)..]
            : blockPath;
        return blockLeaf.Equals(leaf, StringComparison.OrdinalIgnoreCase);
    }

    public FieldGroupMatch? TryMatchPath(
        IReadOnlyList<Data> fields,
        HashSet<string> ignoreFields,
        string? blockPath
    )
    {
        if (!MatchesPath(blockPath, _def.MatchPath))
            return null;

        var matched = fields
            .Where(f =>
                LayoutUtil.IsVisible(f, ignoreFields)
                && !LayoutUtil.IsBlockType(f)
                && !LayoutUtil.IsBlockOpenMarker(f)
                && (
                    _suffixes.Count == 0
                    || _suffixes.Contains(LayoutUtil.GetLastToken(f.FieldElement.ElementName))
                )
            )
            .ToList();

        if (matched.Count == 0)
            return null;

        return new FieldGroupMatch
        {
            Fields = matched,
            Stem = _def.MatchPath ?? Name,
            TemplateName = Name,
        };
    }

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        if (!_def.MatchBySuffixes || _suffixes.Count == 0)
            return null;

        int minCount = _suffixes.Count == 1 ? 1 : Math.Min(2, _suffixes.Count);
        return StemClusterMatcher.MatchBySuffixes(
            fields,
            startIndex,
            ignoreFields,
            _suffixes,
            Name,
            minCount: minCount,
            requireSameStem: true
        );
    }

    public int Layout(Block block, FieldGroupMatch match, int baseX, int y, int maxWidth, HashSet<string> ignoreFields)
    {
        var fields = match.Fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields)).ToList();
        return TemplateSlotLayout.Apply(block, fields, GetPreviewSlots(ignoreFields), baseX, y, maxWidth);
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields)
    {
        var slots = new List<TemplateSlot>();
        int x = TemplateSlotLayout.OriginX;
        int y = TemplateSlotLayout.OriginY;
        const int rowMax = 760;
        int rowH = Consts.DefaultHeightData;

        foreach (var f in _def.Fields)
        {
            if (string.IsNullOrWhiteSpace(f.Suffix))
                continue;

            int w = Math.Max(16, f.Width);
            int h = Math.Max(8, f.Height <= 0 ? Consts.DefaultHeightData : f.Height);
            if (x > TemplateSlotLayout.OriginX && x + w > rowMax)
            {
                x = TemplateSlotLayout.OriginX;
                y += rowH + 4;
                rowH = Consts.DefaultHeightData;
            }

            bool ignored =
                ignoreFields.Contains(f.Suffix)
                || ignoreFields.Any(p =>
                    DcfParser.IsIgnorePattern(p)
                    && (
                        DcfParser.MatchesIgnorePattern(p, f.Suffix)
                        || DcfParser.MatchesIgnorePattern(p, "X_" + f.Suffix)
                    )
                );

            slots.Add(
                new TemplateSlot
                {
                    Id = TemplateSlot.MakeId(TemplateSlotKind.Data, Name, f.Suffix),
                    Suffix = f.Suffix,
                    Label = string.IsNullOrWhiteSpace(f.Label) ? f.Suffix : f.Label,
                    Comment = !string.IsNullOrWhiteSpace(f.Label) && !f.Label.Equals(f.Suffix, StringComparison.Ordinal)
                        ? f.Label
                        : LayoutUtil.DefaultFieldComment(f.Suffix, Name),
                    Kind = TemplateSlotKind.Data,
                    X = x,
                    Y = y,
                    Width = w,
                    Height = h,
                    IsIgnored = ignored,
                    Group = Name,
                }
            );

            if (!ignored)
            {
                x += LayoutUtil.AdvanceAfterData(w, f.Suffix);
                rowH = Math.Max(rowH, h);
            }
        }

        return slots;
    }

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));

    public static UserTemplateDefinitionDto CreateFromBranch(
        string path,
        string title,
        IEnumerable<(string ElementName, string Label, string ElementValue)> fields,
        string? preferredName = null
    )
    {
        string leaf = path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path;
        string name = string.IsNullOrWhiteSpace(preferredName) ? leaf : preferredName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = "UserTemplate";

        var fieldDtos = new List<UserTemplateFieldDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (elementName, label, elementValue) in fields)
        {
            string suffix = DcfParser.GetLastToken(elementName);
            if (string.IsNullOrWhiteSpace(suffix) || !seen.Add(suffix))
                continue;
            if (DcfParser.IsBuiltInServiceField(suffix))
                continue;

            int width = LayoutUtil.WidthFor(
                new Data
                {
                    Level = 0,
                    NameBlock = leaf,
                    Path = path,
                    FieldElement = new Element { ElementName = elementName, ElementValue = elementValue },
                },
                200
            );
            width = Math.Clamp(width, 32, 760);

            fieldDtos.Add(
                new UserTemplateFieldDto
                {
                    Suffix = suffix,
                    Label = string.IsNullOrWhiteSpace(label) ? suffix : label,
                    Width = width,
                    Height = Consts.DefaultHeightData,
                }
            );
        }

        return new UserTemplateDefinitionDto
        {
            Name = name,
            MatchPath = path,
            MatchBySuffixes = true,
            Fields = fieldDtos,
        };
    }
}
