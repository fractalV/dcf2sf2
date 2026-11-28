using DcfToSf2.Parsing;

namespace DcfToSf2.Layout.Templates;

internal sealed class CustomsDocIdFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "CustomsDocId";

    private static readonly string[] StemTokens = ["CustomsDocIdDetails", "DTMDocDetails"];
    private static readonly HashSet<string> SuffixSet =
        new(
            ["CustomsCode", "RegistrationDate", "GTDNumber", "Code", "TransportModeCode"],
            StringComparer.OrdinalIgnoreCase
        );

    public string Name => TemplateName;

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        if (startIndex < 0 || startIndex >= fields.Count)
            return null;

        var first = fields[startIndex];
        if (!LayoutUtil.IsVisible(first, ignoreFields) || LayoutUtil.IsBlockType(first))
            return null;

        string stemToken = StemTokens.FirstOrDefault(t =>
            first.FieldElement.ElementName.Contains(t, StringComparison.OrdinalIgnoreCase)
        ) ?? string.Empty;
        if (string.IsNullOrEmpty(stemToken))
            return null;

        int idx = first.FieldElement.ElementName.IndexOf(stemToken, StringComparison.OrdinalIgnoreCase);
        string idStem = first.FieldElement.ElementName[..(idx + stemToken.Length)];

        var clustered = new List<Data>();
        for (int i = startIndex; i < fields.Count; i++)
        {
            var f = fields[i];
            if (!LayoutUtil.IsVisible(f, ignoreFields))
                continue;
            if (LayoutUtil.IsBlockType(f) || LayoutUtil.IsBlockOpenMarker(f))
            {
                if (clustered.Count > 0)
                    break;
                continue;
            }

            if (!f.FieldElement.ElementName.StartsWith(idStem, StringComparison.OrdinalIgnoreCase))
            {
                if (clustered.Count > 0)
                    break;
                return null;
            }

            string last = LayoutUtil.GetLastToken(f.FieldElement.ElementName);
            if (!SuffixSet.Contains(last) && clustered.Count > 0)
                break;

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
        if (TemplateLayoutStore.HasOverrides(TemplateName))
        {
            var slots = GetPreviewSlots(ignoreFields);
            // Prefer DTM title text when stem is DTM
            return TemplateSlotLayout.Apply(block, fields, slots, baseX, y, maxWidth);
        }

        string title = match.Stem.Contains("DTM", StringComparison.OrdinalIgnoreCase)
            ? "Регистрационный номер декларации на ТС"
            : "Регистрационный номер ДТ";
        block.Fields.Add(
            LayoutUtil.TextField(baseX, y - 12, Math.Min(title.Length * Consts.DefaultWidthChar, maxWidth), Consts.DefaultHeightText, title)
        );

        return LayoutCustomsDocId(block, fields, baseX, y, maxWidth);
    }

    public static int LayoutCustomsDocId(Block block, List<Data> fields, int baseX, int y, int maxWidth)
    {
        Data? Find(string suffix) => LayoutUtil.FindBySuffix(fields, suffix);
        return LayoutUtil.PlaceCompactRow(
            block,
            [
                (Find("CustomsCode"), 68),
                (Find("RegistrationDate"), 84),
                (Find("GTDNumber"), 60),
                (Find("Code"), 20),
                (Find("TransportModeCode"), 20),
            ],
            baseX,
            y,
            maxWidth
        );
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields)
    {
        var slots = new List<TemplateSlot>
        {
            TemplateSlotLayout.TextSlot(
                TemplateName,
                "Регистрационный номер ДТ",
                4,
                4,
                122,
                Consts.DefaultHeightText,
                "title"
            ),
        };
        slots.AddRange(
            AddressFieldGroupTemplate.CompactRows(
                [[("CustomsCode", 68), ("RegistrationDate", 84), ("GTDNumber", 60), ("Code", 20), ("TransportModeCode", 20)]],
                ignoreFields,
                TemplateName
            ).Select(s =>
            {
                s.Y = 16;
                return s;
            })
        );
        return slots;
    }

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));
}

internal sealed class PiDocumentNumberFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "PiDocumentNumber";
    public const string StemToken = "PIDocumentNumber";

    public string Name => TemplateName;

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        if (startIndex < 0 || startIndex >= fields.Count)
            return null;
        var first = fields[startIndex];
        if (!LayoutUtil.IsVisible(first, ignoreFields))
            return null;
        if (!first.FieldElement.ElementName.Contains(StemToken, StringComparison.OrdinalIgnoreCase))
            return null;

        int idx = first.FieldElement.ElementName.IndexOf(StemToken, StringComparison.OrdinalIgnoreCase);
        string idStem = first.FieldElement.ElementName[..(idx + StemToken.Length)];
        var clustered = new List<Data>();
        for (int i = startIndex; i < fields.Count; i++)
        {
            var f = fields[i];
            if (!LayoutUtil.IsVisible(f, ignoreFields))
                continue;
            if (LayoutUtil.IsBlockType(f) || LayoutUtil.IsBlockOpenMarker(f))
            {
                if (clustered.Count > 0)
                    break;
                continue;
            }

            if (!f.FieldElement.ElementName.StartsWith(idStem, StringComparison.OrdinalIgnoreCase))
            {
                if (clustered.Count > 0)
                    break;
                return null;
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
        if (TemplateLayoutStore.HasOverrides(TemplateName))
            return TemplateSlotLayout.Apply(block, fields, GetPreviewSlots(ignoreFields), baseX, y, maxWidth);

        const string title = "Регистрационный номер предварительной информации";
        block.Fields.Add(
            LayoutUtil.TextField(baseX, y - 12, Math.Min(title.Length * Consts.DefaultWidthChar, maxWidth), Consts.DefaultHeightText, title)
        );
        return LayoutPi(block, fields, baseX, y, maxWidth);
    }

    public static int LayoutPi(Block block, List<Data> fields, int baseX, int y, int maxWidth)
    {
        Data? Find(string suffix) => LayoutUtil.FindBySuffix(fields, suffix);
        return LayoutUtil.PlaceCompactRow(
            block,
            [(Find("CountryCode"), 20), (Find("PINumber"), 132), (Find("Date"), 84)],
            baseX,
            y,
            maxWidth
        );
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields)
    {
        var slots = new List<TemplateSlot>
        {
            TemplateSlotLayout.TextSlot(
                TemplateName,
                "Регистрационный номер предварительной информации",
                4,
                4,
                244,
                Consts.DefaultHeightText,
                "title"
            ),
        };
        slots.AddRange(
            AddressFieldGroupTemplate.CompactRows(
                [[("CountryCode", 20), ("PINumber", 132), ("Date", 84)]],
                ignoreFields,
                TemplateName
            ).Select(s =>
            {
                s.Y = 16;
                return s;
            })
        );
        return slots;
    }

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));
}

internal sealed class TirIdFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "TirId";
    public const string StemToken = "TIRIdDetails";

    public string Name => TemplateName;

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        if (startIndex < 0 || startIndex >= fields.Count)
            return null;
        var first = fields[startIndex];
        if (!LayoutUtil.IsVisible(first, ignoreFields))
            return null;
        if (!first.FieldElement.ElementName.Contains(StemToken, StringComparison.OrdinalIgnoreCase))
            return null;

        int idx = first.FieldElement.ElementName.IndexOf(StemToken, StringComparison.OrdinalIgnoreCase);
        string idStem = first.FieldElement.ElementName[..(idx + StemToken.Length)];
        var clustered = new List<Data>();
        for (int i = startIndex; i < fields.Count; i++)
        {
            var f = fields[i];
            if (!LayoutUtil.IsVisible(f, ignoreFields))
                continue;
            if (LayoutUtil.IsBlockType(f) || LayoutUtil.IsBlockOpenMarker(f))
            {
                if (clustered.Count > 0)
                    break;
                continue;
            }

            if (!f.FieldElement.ElementName.StartsWith(idStem, StringComparison.OrdinalIgnoreCase))
            {
                if (clustered.Count > 0)
                    break;
                return null;
            }

            clustered.Add(f);
        }

        if (clustered.Count < 1)
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
        if (TemplateLayoutStore.HasOverrides(TemplateName))
            return TemplateSlotLayout.Apply(block, fields, GetPreviewSlots(ignoreFields), baseX, y, maxWidth);

        const string title = "Номер книжки МДП";
        block.Fields.Add(
            LayoutUtil.TextField(baseX, y - 12, Math.Min(title.Length * Consts.DefaultWidthChar, maxWidth), Consts.DefaultHeightText, title)
        );
        return LayoutTir(block, fields, baseX, y, maxWidth);
    }

    public static int LayoutTir(Block block, List<Data> fields, int baseX, int y, int maxWidth)
    {
        Data? Find(string suffix) => LayoutUtil.FindBySuffix(fields, suffix);
        return LayoutUtil.PlaceCompactRow(
            block,
            [(Find("TIRSeries"), 20), (Find("TIRID"), 68)],
            baseX,
            y,
            maxWidth
        );
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields)
    {
        var slots = new List<TemplateSlot>
        {
            TemplateSlotLayout.TextSlot(TemplateName, "Номер книжки МДП", 4, 4, 91, Consts.DefaultHeightText, "title"),
        };
        slots.AddRange(
            AddressFieldGroupTemplate.CompactRows(
                [[("TIRSeries", 20), ("TIRID", 68)]],
                ignoreFields,
                TemplateName
            ).Select(s =>
            {
                s.Y = 16;
                return s;
            })
        );
        return slots;
    }

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));
}

internal sealed class GoodsQuantityFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "GoodsQuantity";

    private static readonly HashSet<string> SuffixSet =
        new(
            ["GoodsQuantity", "MeasureUnitQualifierName", "MeasureUnitQualifierCode"],
            StringComparer.OrdinalIgnoreCase
        );

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
            requireSameStem: true
        );
    }

    public int Layout(Block block, FieldGroupMatch match, int baseX, int y, int maxWidth, HashSet<string> ignoreFields)
    {
        var fields = match.Fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields)).ToList();
        if (TemplateLayoutStore.HasOverrides(TemplateName))
            return TemplateSlotLayout.Apply(block, fields, GetPreviewSlots(ignoreFields), baseX, y, maxWidth);

        bool stacked = IsQuantityOnlySection(fields, match);
        return LayoutQuantity(block, fields, baseX, y, maxWidth, stacked);
    }

    public static int LayoutQuantity(Block block, List<Data> fields, int baseX, int y, int maxWidth, bool stacked)
    {
        Data? Find(string suffix) => LayoutUtil.FindBySuffix(fields, suffix);
        if (stacked)
        {
            foreach (var suffix in new[] { "GoodsQuantity", "MeasureUnitQualifierName", "MeasureUnitQualifierCode" })
            {
                var f = Find(suffix);
                if (f is null)
                    continue;
                string comment = LayoutUtil.ExtractComment(f.FieldElement.ElementValue);
                if (!string.IsNullOrEmpty(comment))
                {
                    block.Fields.Add(
                        LayoutUtil.TextField(
                            baseX,
                            y,
                            Math.Min(comment.Length * Consts.DefaultWidthChar, maxWidth),
                            Consts.DefaultHeightText,
                            comment
                        )
                    );
                    y += Consts.DefaultHeightText + 2;
                }

                int w = suffix switch
                {
                    "GoodsQuantity" => Math.Min(252, maxWidth),
                    "MeasureUnitQualifierName" => Math.Min(404, maxWidth),
                    _ => Math.Min(36, maxWidth),
                };
                block.Fields.Add(LayoutUtil.DataField(f, baseX, y, w, Consts.DefaultHeightData));
                LayoutUtil.MaybeAddPict(block, f, baseX, y, w);
                y += Consts.DefaultHeightData + 8;
            }

            return y;
        }

        return LayoutUtil.PlaceCompactRow(
            block,
            [
                (Find("GoodsQuantity"), 252),
                (Find("MeasureUnitQualifierName"), 108),
                (Find("MeasureUnitQualifierCode"), 28),
            ],
            baseX,
            y,
            maxWidth
        );
    }

    private static bool IsQuantityOnlySection(List<Data> fields, FieldGroupMatch match)
    {
        string stem = match.Stem;
        return stem.Contains("SupplementaryQuantity", StringComparison.OrdinalIgnoreCase)
            || fields.All(f => SuffixSet.Contains(LayoutUtil.GetLastToken(f.FieldElement.ElementName)));
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields) =>
        AddressFieldGroupTemplate.CompactRows(
            [[("GoodsQuantity", 252), ("MeasureUnitQualifierName", 108), ("MeasureUnitQualifierCode", 28)]],
            ignoreFields,
            TemplateName
        );

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));
}
