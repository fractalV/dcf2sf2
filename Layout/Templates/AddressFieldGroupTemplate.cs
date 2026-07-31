using DcfToSf2.Parsing;

namespace DcfToSf2.Layout.Templates;

internal sealed class AddressFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "Address";

    public static readonly string[] Suffixes =
    [
        "AddressKindCode",
        "PostalCode",
        "CountryCode",
        "CounryName",
        "Region",
        "District",
        "Town",
        "City",
        "StreetHouse",
        "House",
        "Room",
        "AddressText",
        "PostOfficeBoxId",
        "TerritoryCode",
    ];

    private static readonly HashSet<string> SuffixSet = new(Suffixes, StringComparer.OrdinalIgnoreCase);

    public string Name => TemplateName;

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        return StemClusterMatcher.MatchBySuffixes(
            fields,
            startIndex,
            ignoreFields,
            SuffixSet,
            TemplateName,
            minCount: 2,
            stemFilter: stem => !LayoutUtil.StemContains(stem, "IdentityCard")
        );
    }

    public int Layout(Block block, FieldGroupMatch match, int baseX, int y, int maxWidth, HashSet<string> ignoreFields)
    {
        if (match.HeaderField is not null)
        {
            string c = LayoutUtil.ExtractComment(match.HeaderField.FieldElement.ElementValue);
            if (!string.IsNullOrEmpty(c))
            {
                block.Fields.Add(
                    LayoutUtil.TextField(
                        baseX,
                        y,
                        Math.Min(c.Length * Consts.DefaultWidthChar, maxWidth),
                        Consts.DefaultHeightText + 4,
                        c
                    )
                );
                y += Consts.DefaultHeightText + 6;
            }
        }

        var fields = match.Fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields)).ToList();
        return LayoutAddressFields(block, fields, baseX, y, maxWidth, ignoreFields);
    }

    public static int LayoutAddressFields(
        Block block,
        List<Data> fields,
        int baseX,
        int y,
        int maxWidth,
        HashSet<string>? ignoreFields = null
    )
    {
        ignoreFields ??= [];
        if (TemplateLayoutStore.HasOverrides(TemplateName))
        {
            var slots = new AddressFieldGroupTemplate().GetPreviewSlots(ignoreFields);
            var addressOnly = fields
                .Where(f => !IdentityCardFieldGroupTemplate.IsIdentityCardField(f.FieldElement.ElementName))
                .ToList();
            return TemplateSlotLayout.Apply(
                block,
                addressOnly,
                slots,
                baseX,
                y,
                maxWidth,
                suppressFieldComments: false
            );
        }

        Data? Find(string suffix) =>
            fields.FirstOrDefault(f =>
                LayoutUtil.GetLastToken(f.FieldElement.ElementName).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && !IdentityCardFieldGroupTemplate.IsIdentityCardField(f.FieldElement.ElementName)
            );

        y = LayoutUtil.PlaceCompactRow(
            block,
            [
                (Find("CountryCode"), 32),
                (Find("CounryName"), Math.Min(252, maxWidth / 2)),
                (Find("PostalCode"), 92),
            ],
            baseX,
            y,
            maxWidth
        );

        y = LayoutUtil.PlaceCompactRow(
            block,
            [(Find("Region"), Math.Min(220, maxWidth / 2)), (Find("City"), Math.Min(160, maxWidth / 2))],
            baseX,
            y,
            maxWidth
        );

        y = LayoutUtil.PlaceCompactRow(block, [(Find("StreetHouse"), maxWidth - 8)], baseX, y, maxWidth);

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CountryCode",
            "CounryName",
            "PostalCode",
            "Region",
            "City",
            "StreetHouse",
        };
        var extra = fields
            .Where(f => !known.Contains(LayoutUtil.GetLastToken(f.FieldElement.ElementName)))
            .ToList();
        if (extra.Count > 0)
            y = LayoutUtil.LayoutFlowRow(block, extra, baseX, y, maxWidth);

        return y;
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields) =>
        CompactRows(
            [
                [("CountryCode", 32), ("CounryName", 252), ("PostalCode", 92)],
                [("Region", 220), ("City", 160)],
                [("StreetHouse", 760)],
                [
                    ("AddressKindCode", 80),
                    ("District", 120),
                    ("Town", 120),
                    ("House", 80),
                    ("Room", 80),
                    ("AddressText", 200),
                    ("PostOfficeBoxId", 80),
                ],
            ],
            ignoreFields,
            TemplateName
        );

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));

    internal static List<TemplateSlot> CompactRows(
        IReadOnlyList<(string Suffix, int Width)[]> rows,
        HashSet<string> ignoreFields,
        string group,
        string? stemHint = null
    )
    {
        var result = new List<TemplateSlot>();
        int y = 4;
        foreach (var row in rows)
        {
            int x = 4;
            bool any = false;
            foreach (var (suffix, width) in row)
            {
                bool ignored = IsSuffixIgnored(suffix, ignoreFields);
                var slot = new TemplateSlot
                {
                    Id = TemplateSlot.MakeId(TemplateSlotKind.Data, group, suffix, stemHint),
                    Suffix = suffix,
                    Label = suffix,
                    Comment = LayoutUtil.DefaultFieldComment(suffix, group),
                    Kind = TemplateSlotKind.Data,
                    X = x,
                    Y = y,
                    Width = width,
                    Height = Consts.DefaultHeightData,
                    IsIgnored = ignored,
                    Group = group,
                    StemHint = stemHint ?? string.Empty,
                };
                result.Add(slot);
                if (ignored)
                    continue;

                any = true;
                x += LayoutUtil.AdvanceAfterData(width, suffix);
            }

            if (any)
                y += Consts.DefaultHeightData + 4;
        }

        return result;
    }

    private static bool IsSuffixIgnored(string suffix, HashSet<string> ignoreFields)
    {
        if (DcfParser.IsBuiltInServiceField(suffix))
            return true;
        if (ignoreFields.Contains(suffix))
            return true;
        return ignoreFields.Any(p =>
            DcfParser.IsIgnorePattern(p)
            && (
                DcfParser.MatchesIgnorePattern(p, suffix)
                || DcfParser.MatchesIgnorePattern(p, "X_" + suffix)
            )
        );
    }

    public static bool IsAddressSuffix(string lastToken) => SuffixSet.Contains(lastToken);
}
