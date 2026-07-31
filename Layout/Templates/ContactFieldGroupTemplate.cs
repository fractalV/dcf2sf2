using DcfToSf2.Parsing;

namespace DcfToSf2.Layout.Templates;

/// <summary>Phone / Fax / Telex / E_mail (CommunicationDetails / Contact).</summary>
internal sealed class ContactFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "Contact";

    public static readonly string[] Suffixes = ["Phone", "Fax", "Telex", "E_mail", "mail"];

    private static readonly HashSet<string> SuffixSet = new(Suffixes, StringComparer.OrdinalIgnoreCase);

    private static readonly (string Suffix, int Width)[] DefaultWidths =
    [
        ("Phone", 200),
        ("Fax", 120),
        ("Telex", 120),
        ("E_mail", 240),
        ("mail", 240),
    ];

    public string Name => TemplateName;

    public static bool IsContactSuffix(string suffix) => SuffixSet.Contains(suffix);

    public static bool IsContactField(Data f) =>
        IsContactSuffix(LayoutUtil.GetLastToken(f.FieldElement.ElementName));

    public static bool IsContactOnlyCluster(IEnumerable<Data> fields)
    {
        var data = fields.Where(f => !LayoutUtil.IsBlockType(f) && !LayoutUtil.IsBlockOpenMarker(f)).ToList();
        return data.Count > 0 && data.All(IsContactField);
    }

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        return StemClusterMatcher.MatchBySuffixes(
            fields,
            startIndex,
            ignoreFields,
            SuffixSet,
            TemplateName,
            minCount: 1,
            requireSameStem: true
        );
    }

    public int Layout(Block block, FieldGroupMatch match, int baseX, int y, int maxWidth, HashSet<string> ignoreFields)
    {
        var fields = match.Fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields)).ToList();
        return LayoutContactFields(block, fields, baseX, y, maxWidth, ignoreFields);
    }

    public static int LayoutContactFields(
        Block block,
        List<Data> fields,
        int baseX,
        int y,
        int maxWidth,
        HashSet<string>? ignoreFields = null,
        bool suppressFieldComments = false
    )
    {
        ignoreFields ??= [];
        var visible = fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields) && IsContactField(f)).ToList();
        if (visible.Count == 0)
            return y;

        if (TemplateLayoutStore.HasOverrides(TemplateName))
        {
            var slots = FlattenToSingleRow(
                new ContactFieldGroupTemplate().GetPreviewSlots(ignoreFields),
                visible
            );
            return TemplateSlotLayout.Apply(
                block,
                visible,
                slots,
                baseX,
                y,
                maxWidth,
                suppressFieldComments: suppressFieldComments
            );
        }

        return LayoutContactFieldsDefault(block, visible, baseX, y, maxWidth, suppressFieldComments);
    }

    /// <summary>
    /// Contact template is always one horizontal row (Phone|Fax|Telex|E_mail).
    /// Saved slots may drift to Y+20 after removing Fax/Telex — snap back to one line.
    /// </summary>
    public static IReadOnlyList<TemplateSlot> FlattenToSingleRow(
        IReadOnlyList<TemplateSlot> slots,
        IReadOnlyList<Data>? presentFields = null
    )
    {
        var list = slots.Select(s => s.Clone()).ToList();
        var presentSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (presentFields is not null)
        {
            foreach (var f in presentFields)
                presentSuffixes.Add(LayoutUtil.GetLastToken(f.FieldElement.ElementName));
        }

        var data = list
            .Where(s =>
                s.Kind == TemplateSlotKind.Data
                && !s.IsExcluded
                && IsContactSuffix(s.Suffix)
                && (presentSuffixes.Count == 0 || presentSuffixes.Contains(s.Suffix))
            )
            .ToList();
        if (data.Count == 0)
            return list;

        int rowY = data.Min(s => s.Y);
        if (data.All(s => Math.Abs(s.Y - rowY) <= 2))
        {
            foreach (var d in data)
                d.Y = rowY;
            PlaceTextLabelsBeforeData(list);
            return TemplateSlotLayout.EnsurePictSlots(list);
        }

        var ordered = new List<TemplateSlot>();
        foreach (var (suffix, _) in DefaultWidths)
        {
            var hit = data.FirstOrDefault(d =>
                d.Suffix.Equals(suffix, StringComparison.OrdinalIgnoreCase)
            );
            if (hit is not null && !ordered.Contains(hit))
                ordered.Add(hit);
        }

        foreach (var d in data)
        {
            if (!ordered.Contains(d))
                ordered.Add(d);
        }

        // Bind Text to Data: prefer label→suffix match, else nearest geometry.
        var labelByOwner = new Dictionary<TemplateSlot, TemplateSlot>();
        foreach (
            var text in list.Where(s => !s.IsExcluded && s.Kind == TemplateSlotKind.Text)
        )
        {
            var owner = MatchLabelToData(text.Label, ordered) ?? NearestData(text, ordered);
            // One primary label per field (first wins).
            if (!labelByOwner.ContainsKey(owner))
                labelByOwner[owner] = text;
        }

        int x = TemplateSlotLayout.OriginX;
        const int gap = 4;
        foreach (var d in ordered)
        {
            int w = Math.Max(16, d.Width);
            if (labelByOwner.TryGetValue(d, out var label))
            {
                int labelW = Math.Max(8, label.Width);
                label.X = x;
                label.Y = rowY + 4;
                label.Width = labelW;
                x += labelW + gap;
            }

            d.X = x;
            d.Y = rowY;
            d.Width = w;
            d.Height = Consts.DefaultHeightData;
            x += LayoutUtil.AdvanceAfterData(w, d.Suffix, gap);
        }

        // Leftover texts (not primary label) — park left of their owner if possible.
        foreach (
            var text in list.Where(s => !s.IsExcluded && s.Kind == TemplateSlotKind.Text)
        )
        {
            if (labelByOwner.ContainsValue(text))
                continue;
            var owner = MatchLabelToData(text.Label, ordered) ?? NearestData(text, ordered);
            text.X = Math.Max(TemplateSlotLayout.OriginX, owner.X - Math.Max(8, text.Width) - gap);
            text.Y = rowY + 4;
        }

        PlaceTextLabelsBeforeData(list);
        return TemplateSlotLayout.EnsurePictSlots(list);
    }

    private static TemplateSlot? MatchLabelToData(string label, List<TemplateSlot> data)
    {
        string l = (label ?? string.Empty).Trim().ToLowerInvariant();
        if (l.Length == 0)
            return null;

        if (l.Contains("mail", StringComparison.Ordinal))
            return data.FirstOrDefault(d =>
                d.Suffix.Equals("E_mail", StringComparison.OrdinalIgnoreCase)
                || d.Suffix.Equals("mail", StringComparison.OrdinalIgnoreCase)
            );

        if (
            l is "т." or "т" or "tel" or "phone" or "тф"
            || l.Contains("телефон", StringComparison.Ordinal)
            || l.Contains("phone", StringComparison.Ordinal)
        )
            return data.FirstOrDefault(d => d.Suffix.Equals("Phone", StringComparison.OrdinalIgnoreCase));

        if (l.Contains("fax", StringComparison.Ordinal) || l.Contains("факс", StringComparison.Ordinal))
            return data.FirstOrDefault(d => d.Suffix.Equals("Fax", StringComparison.OrdinalIgnoreCase));

        if (l.Contains("telex", StringComparison.Ordinal) || l.Contains("телекс", StringComparison.Ordinal))
            return data.FirstOrDefault(d => d.Suffix.Equals("Telex", StringComparison.OrdinalIgnoreCase));

        return null;
    }

    private static TemplateSlot NearestData(TemplateSlot s, List<TemplateSlot> data) =>
        data.OrderBy(d =>
            {
                // Prefer field that starts at/after the label (label-before-field).
                int dx = d.X >= s.X - 2 ? Math.Abs(d.X - s.X) : 10_000 + Math.Abs(d.X - s.X);
                int dy = Math.Abs(s.Y - d.Y);
                return dy * 1000 + dx;
            })
            .First();

    /// <summary>
    /// If a Text sits on the same X (or inside) a Data field, move it just to the left.
    /// Avoids canvas illusion where wider Data looks "after" Text while text mode shows equal X.
    /// </summary>
    public static void PlaceTextLabelsBeforeData(IList<TemplateSlot> list)
    {
        const int gap = 4;
        var data = list.Where(s => s.Kind == TemplateSlotKind.Data && !s.IsExcluded).ToList();
        foreach (var text in list.Where(s => s.Kind == TemplateSlotKind.Text && !s.IsExcluded))
        {
            var owner =
                MatchLabelToData(text.Label, data)
                ?? data.Where(d => text.X >= d.X - 2 && text.X < d.X + Math.Max(d.Width, 16))
                    .OrderBy(d => Math.Abs(d.Y - text.Y))
                    .ThenBy(d => Math.Abs(d.X - text.X))
                    .FirstOrDefault();
            if (owner is null)
                continue;

            // Already strictly left of the field — keep.
            if (text.X + text.Width + gap <= owner.X)
                continue;

            int labelW = Math.Max(8, text.Width);
            int left = owner.X - labelW - gap;
            if (left >= TemplateSlotLayout.OriginX)
                text.X = left;
            else if (Math.Abs(text.X - owner.X) <= 2)
                owner.X = text.X + labelW + gap;

            if (Math.Abs(text.Y - owner.Y) <= 12)
                text.Y = owner.Y + Math.Clamp(text.Y - owner.Y, 0, 8);
        }
    }

    /// <summary>Compact Phone|Fax|Telex|E_mail row without reading saved overrides.</summary>
    public static int LayoutContactFieldsDefault(
        Block block,
        List<Data> fields,
        int baseX,
        int y,
        int maxWidth,
        bool suppressFieldComments = false
    )
    {
        var visible = fields.Where(IsContactField).ToList();
        if (visible.Count == 0)
            return y;

        var ordered = new List<(Data Field, int Width)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (suffix, width) in DefaultWidths)
        {
            var f = visible.FirstOrDefault(v =>
                LayoutUtil.GetLastToken(v.FieldElement.ElementName).Equals(suffix, StringComparison.OrdinalIgnoreCase)
            );
            if (f is null || !used.Add(f.FieldElement.ElementName))
                continue;
            ordered.Add((f, width));
        }

        foreach (var f in visible)
        {
            if (!used.Add(f.FieldElement.ElementName))
                continue;
            ordered.Add((f, Math.Min(200, maxWidth / 2)));
        }

        int x = baseX;
        int rowH = Consts.DefaultHeightData;
        const int gap = 4;
        int right = baseX + maxWidth;

        foreach (var (f, preferW) in ordered)
        {
            int h = Consts.DefaultHeightData;
            int naturalW = Math.Min(LayoutUtil.NaturalWidth(f), preferW);
            int remain = right - x;
            if (x > baseX && naturalW + gap > remain)
            {
                x = baseX;
                y += rowH + gap;
                rowH = Consts.DefaultHeightData;
                remain = maxWidth;
            }

            int w = Math.Min(naturalW, Math.Max(16, remain));
            string? label = null;
            if (!suppressFieldComments)
            {
                label =
                    LayoutUtil.ShortLabelOrNull(LayoutUtil.GetLastToken(f.FieldElement.ElementName))
                    ?? LayoutUtil.ShortLabel(LayoutUtil.GetLastToken(f.FieldElement.ElementName));
            }

            block.Fields.Add(LayoutUtil.DataField(f, x, y, w, h, label));
            x += w + gap;
            rowH = Math.Max(rowH, h);
        }

        return y + rowH + 8;
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields)
    {
        (string Suffix, int Width)[] row =
        [
            ("Phone", 200),
            ("Fax", 120),
            ("Telex", 120),
            ("E_mail", 240),
        ];
        return AddressFieldGroupTemplate.CompactRows([row], ignoreFields, TemplateName);
    }

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        FlattenToSingleRow(TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields)));
}
