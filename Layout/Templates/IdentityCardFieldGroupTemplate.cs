using DcfToSf2.Parsing;

namespace DcfToSf2.Layout.Templates;

internal sealed class IdentityCardFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "IdentityCard";
    public const string StemToken = "IdentityCard";

    public static readonly string[] Suffixes =
    [
        "IdentityCardCode",
        "IdentityCardSeries",
        "IdentityCardNumber",
        "IdentityCardDate",
        "DocValidityDate",
        "OrganizationName",
        "IssuerCode",
        "AuthorityId",
        "CountryCode",
    ];

    private static readonly HashSet<string> SuffixSet = new(Suffixes, StringComparer.OrdinalIgnoreCase);

    public string Name => TemplateName;

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        if (startIndex < 0 || startIndex >= fields.Count)
            return null;

        var first = fields[startIndex];
        if (!LayoutUtil.IsVisible(first, ignoreFields) || LayoutUtil.IsBlockType(first))
            return null;

        string name = first.FieldElement.ElementName;
        if (!name.Contains(StemToken, StringComparison.OrdinalIgnoreCase))
            return null;

        string last = LayoutUtil.GetLastToken(name);
        // Accept any field under IdentityCard stem; prefer known suffixes
        var clustered = new List<Data>();
        string stem = LayoutUtil.GetStem(name);

        // Normalize stem to ...IdentityCard
        int idIdx = name.IndexOf(StemToken, StringComparison.OrdinalIgnoreCase);
        string idStem =
            idIdx >= 0
                ? name[..(idIdx + StemToken.Length)]
                : stem;

        for (int i = startIndex; i < fields.Count; i++)
        {
            var f = fields[i];
            if (!LayoutUtil.IsVisible(f, ignoreFields))
                continue;
            if (LayoutUtil.IsBlockOpenMarker(f))
                break;
            if (LayoutUtil.IsBlockType(f))
            {
                if (clustered.Count > 0)
                    break;
                continue;
            }

            if (!f.FieldElement.ElementName.StartsWith(idStem, StringComparison.OrdinalIgnoreCase)
                && !LayoutUtil.StemContains(LayoutUtil.GetStem(f.FieldElement.ElementName), StemToken))
            {
                if (clustered.Count > 0)
                    break;
                return null;
            }

            // Stop if we hit a different IdentityCard group
            string fStem = LayoutUtil.GetStem(f.FieldElement.ElementName);
            if (!f.FieldElement.ElementName.StartsWith(idStem + "_", StringComparison.OrdinalIgnoreCase)
                && !f.FieldElement.ElementName.Equals(idStem, StringComparison.OrdinalIgnoreCase)
                && !fStem.Equals(idStem, StringComparison.OrdinalIgnoreCase))
            {
                // still under same IdentityCard if name contains idStem_
                if (!f.FieldElement.ElementName.StartsWith(idStem, StringComparison.OrdinalIgnoreCase))
                {
                    if (clustered.Count > 0)
                        break;
                    return null;
                }
            }

            clustered.Add(f);
        }

        if (clustered.Count < 2)
            return null;

        return new FieldGroupMatch
        {
            Fields = clustered,
            Stem = idStem,
            TemplateName = TemplateName,
        };
    }

    public int Layout(Block block, FieldGroupMatch match, int baseX, int y, int maxWidth, HashSet<string> ignoreFields)
    {
        var fields = match.Fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields)).ToList();
        return LayoutIdentityCardFields(block, fields, baseX, y, maxWidth, ignoreFields);
    }

    public static int LayoutIdentityCardFields(
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
            var slots = new IdentityCardFieldGroupTemplate().GetPreviewSlots(ignoreFields);
            return TemplateSlotLayout.Apply(block, fields, slots, baseX, y, maxWidth);
        }

        Data? Find(string suffix) => LayoutUtil.FindBySuffix(fields, suffix);

        y = LayoutUtil.PlaceCompactRow(
            block,
            [
                (Find("IdentityCardCode"), 44),
                (Find("IdentityCardSeries"), 60),
                (Find("IdentityCardNumber"), 120),
                (Find("IdentityCardDate"), 84),
                (Find("DocValidityDate"), 84),
            ],
            baseX,
            y,
            maxWidth
        );

        y = LayoutUtil.PlaceCompactRow(
            block,
            [
                (Find("OrganizationName"), Math.Min(280, maxWidth / 2)),
                (Find("IssuerCode"), 60),
                (Find("AuthorityId"), 100),
                (Find("CountryCode"), 32),
            ],
            baseX,
            y,
            maxWidth
        );

        var known = new HashSet<string>(Suffixes, StringComparer.OrdinalIgnoreCase);
        var extra = fields
            .Where(f => !known.Contains(LayoutUtil.GetLastToken(f.FieldElement.ElementName)))
            .ToList();
        if (extra.Count > 0)
            y = LayoutUtil.LayoutFlowRow(block, extra, baseX, y, maxWidth);

        return y;
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields) =>
        AddressFieldGroupTemplate.CompactRows(
            [
                [
                    ("IdentityCardCode", 44),
                    ("IdentityCardSeries", 60),
                    ("IdentityCardNumber", 120),
                    ("IdentityCardDate", 84),
                    ("DocValidityDate", 84),
                ],
                [
                    ("OrganizationName", 280),
                    ("IssuerCode", 60),
                    ("AuthorityId", 100),
                    ("CountryCode", 32),
                ],
            ],
            ignoreFields,
            TemplateName
        );

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));

    public static bool IsIdentityCardField(string elementName) =>
        elementName.Contains(StemToken, StringComparison.OrdinalIgnoreCase);
}
