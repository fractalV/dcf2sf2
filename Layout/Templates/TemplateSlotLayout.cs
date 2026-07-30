namespace DcfToSf2.Layout.Templates;

internal static class TemplateSlotLayout
{
    public const int OriginX = 4;
    public const int OriginY = 4;

    public static int Apply(
        Block block,
        IReadOnlyList<Data> fields,
        IReadOnlyList<TemplateSlot> slots,
        int baseX,
        int startY,
        int maxWidth,
        bool addPict = true
    )
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int maxBottom = startY;
        bool hasPictSlots = slots.Any(s => s.Kind == TemplateSlotKind.Pict);

        foreach (var slot in slots)
        {
            if (slot.IsExcluded)
            {
                if (slot.Kind == TemplateSlotKind.Data)
                {
                    var skipped = FindField(fields, slot);
                    if (skipped is not null)
                        used.Add(skipped.FieldElement.ElementName);
                }

                continue;
            }

            int x = baseX + slot.X - OriginX;
            int y = startY + slot.Y - OriginY;
            int w = Math.Min(slot.Width, Math.Max(16, baseX + maxWidth - x));
            int h = Math.Max(8, slot.Height);

            if (slot.Kind == TemplateSlotKind.Text)
            {
                string text = string.IsNullOrEmpty(slot.Label) ? slot.Suffix : slot.Label;
                if (!string.IsNullOrEmpty(text))
                    block.Fields.Add(LayoutUtil.TextField(x, y, w, h, text));
                maxBottom = Math.Max(maxBottom, y + h);
                continue;
            }

            if (slot.Kind == TemplateSlotKind.Pict)
            {
                if (!addPict)
                    continue;
                string name = PictFieldName(slot);
                string pictValue = string.IsNullOrEmpty(slot.Label) ? name : slot.Label;
                block.Fields.Add(
                    new Field(
                        Type.Pict,
                        new Size(LayoutUtil.PictSize, LayoutUtil.PictSize),
                        new Position(Math.Max(0, x), y),
                        new Value("", pictValue)
                    )
                );
                maxBottom = Math.Max(maxBottom, y + LayoutUtil.PictSize);
                continue;
            }

            var field = FindField(fields, slot);
            if (field is null)
                continue;

            string comment = LayoutUtil.ExtractComment(field.FieldElement.ElementValue);
            block.Fields.Add(
                LayoutUtil.DataField(
                    field,
                    x,
                    y,
                    w,
                    h,
                    string.IsNullOrEmpty(comment) ? null : comment
                )
            );
            // Explicit Pict slots own placement; only auto-add when none exist for this template.
            if (addPict && !hasPictSlots)
                LayoutUtil.MaybeAddPict(block, field, x, y, w);
            else if (addPict && hasPictSlots && !HasPictSlotFor(slots, slot))
                LayoutUtil.MaybeAddPict(block, field, x, y, w);

            used.Add(field.FieldElement.ElementName);
            maxBottom = Math.Max(maxBottom, y + h);
        }

        var rest = fields
            .Where(f =>
                !used.Contains(f.FieldElement.ElementName)
                && !LayoutUtil.IsBlockType(f)
                && !LayoutUtil.IsBlockOpenMarker(f)
            )
            .ToList();
        if (rest.Count > 0)
            maxBottom = LayoutUtil.LayoutFlowRow(block, rest, baseX, maxBottom + 4, maxWidth);

        return maxBottom + 8;
    }

    /// <summary>
    /// Ensures each eligible Data slot has a companion Pict slot (movable/removable).
    /// Does not recreate Picts that already exist (including removed ones).
    /// </summary>
    public static List<TemplateSlot> EnsurePictSlots(IReadOnlyList<TemplateSlot> slots)
    {
        var list = slots.Select(s => s.Clone()).ToList();
        var pictKeys = new HashSet<string>(
            list.Where(s => s.Kind == TemplateSlotKind.Pict).Select(PictKey),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var data in list.Where(s => s.Kind == TemplateSlotKind.Data && !s.IsExcluded).ToList())
        {
            if (!LayoutUtil.ShouldAddPict(data.Suffix))
                continue;
            string key = PictKey(data);
            if (pictKeys.Contains(key))
                continue;

            var pict = CreateDefaultPictSlot(data);
            list.Add(pict);
            pictKeys.Add(key);
        }

        return list;
    }

    public static TemplateSlot CreateDefaultPictSlot(TemplateSlot dataSlot)
    {
        string name = PictFieldName(dataSlot);
        bool isDate = dataSlot.Suffix.EndsWith("Date", StringComparison.OrdinalIgnoreCase);
        return new TemplateSlot
        {
            Id = TemplateSlot.MakeId(
                TemplateSlotKind.Pict,
                dataSlot.Group,
                dataSlot.Suffix,
                dataSlot.StemHint
            ),
            Suffix = dataSlot.Suffix,
            Label = isDate ? $"{name}=DATE" : name,
            Kind = TemplateSlotKind.Pict,
            X = dataSlot.X + Math.Max(dataSlot.Width, 16) + LayoutUtil.PictGap,
            Y =
                dataSlot.Y
                + Math.Max(0, (Math.Min(Consts.DefaultHeightData, 16) - LayoutUtil.PictSize) / 2),
            Width = LayoutUtil.PictSize,
            Height = LayoutUtil.PictSize,
            Group = dataSlot.Group,
            StemHint = dataSlot.StemHint,
            IsIgnored = dataSlot.IsIgnored,
            IsEmbedded = dataSlot.IsEmbedded,
        };
    }

    private static bool HasPictSlotFor(IReadOnlyList<TemplateSlot> slots, TemplateSlot dataSlot) =>
        slots.Any(s =>
            s.Kind == TemplateSlotKind.Pict
            && s.Suffix.Equals(dataSlot.Suffix, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.StemHint, dataSlot.StemHint, StringComparison.OrdinalIgnoreCase)
        );

    private static string PictKey(TemplateSlot s) =>
        $"{s.Group}\0{s.Suffix}\0{s.StemHint}";

    public static string PictFieldName(TemplateSlot slot) =>
        string.IsNullOrEmpty(slot.StemHint) ? slot.Suffix : $"{slot.StemHint}_{slot.Suffix}";

    public static Data? FindField(IReadOnlyList<Data> fields, TemplateSlot slot)
    {
        var candidates = fields
            .Where(f =>
                LayoutUtil
                    .GetLastToken(f.FieldElement.ElementName)
                    .Equals(slot.Suffix, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        if (candidates.Count == 0)
            return null;

        if (!string.IsNullOrEmpty(slot.StemHint))
        {
            var hinted = candidates.FirstOrDefault(f =>
                f.FieldElement.ElementName.Contains(slot.StemHint, StringComparison.OrdinalIgnoreCase)
            );
            if (hinted is not null)
                return hinted;
        }

        return candidates[0];
    }

    public static TemplateSlot TextSlot(
        string group,
        string text,
        int x,
        int y,
        int width,
        int height = 12,
        string? idSuffix = null
    )
    {
        string key = idSuffix ?? text;
        return new TemplateSlot
        {
            Id = TemplateSlot.MakeId(TemplateSlotKind.Text, group, key),
            Suffix = key,
            Label = text,
            Kind = TemplateSlotKind.Text,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Group = group,
        };
    }
}
