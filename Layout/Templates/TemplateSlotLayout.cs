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
        bool addPict = true,
        bool suppressFieldComments = false
    )
    {
        // Drop vertical space reserved for template Data that is absent in this DCF.
        slots = CompactAbsentRows(slots, fields);

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
                // Skip orphan labels (e.g. ИНН/КПП) when the related Data field is absent.
                if (slot.Suffix.StartsWith("label:", StringComparison.OrdinalIgnoreCase))
                {
                    string dataSuffix = slot.Suffix["label:".Length..];
                    bool hasData = fields.Any(f =>
                        LayoutUtil
                            .GetLastToken(f.FieldElement.ElementName)
                            .Equals(dataSuffix, StringComparison.OrdinalIgnoreCase)
                    );
                    if (!hasData)
                        continue;
                }

                if (!string.IsNullOrEmpty(text))
                    block.Fields.Add(LayoutUtil.TextField(x, y, w, h, text));
                maxBottom = Math.Max(maxBottom, y + h);
                continue;
            }

            if (slot.Kind == TemplateSlotKind.Pict)
            {
                if (!addPict)
                    continue;

                // Pict must reference a real Data field name — never UI-only embed: StemHint.
                var owner = FindField(fields, slot);
                if (owner is null)
                    continue;

                string name = owner.FieldElement.ElementName;
                bool datePict =
                    LayoutUtil.GetLastToken(name).EndsWith("Date", StringComparison.OrdinalIgnoreCase)
                    || slot.Label.Contains("=DATE", StringComparison.OrdinalIgnoreCase);
                string pictValue = datePict ? $"{name}=DATE" : name;
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

            string? comment = null;
            if (!suppressFieldComments)
            {
                if (!string.IsNullOrEmpty(slot.Comment))
                    comment = slot.Comment;
                else
                {
                    string c = LayoutUtil.ExtractComment(field.FieldElement.ElementValue);
                    if (!string.IsNullOrEmpty(c))
                        comment = c;
                }
            }

            block.Fields.Add(LayoutUtil.DataField(field, x, y, w, h, comment));
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
        {
            // Contact leftovers keep compact widths (saved Contact template may omit E_mail).
            if (rest.All(ContactFieldGroupTemplate.IsContactField))
            {
                // Continue the contact row started by this Apply (slots share startY after flatten).
                int rowY = startY;
                int rowRight = block
                    .Fields.Where(f =>
                        f.FieldType == Type.Data && Math.Abs(f.FieldPosition.Y - rowY) <= 2
                    )
                    .Select(f => f.FieldPosition.X + f.FieldSize.Length)
                    .DefaultIfEmpty(baseX)
                    .Max();
                int startX = rowRight > baseX ? rowRight + 4 : baseX;
                int avail = Math.Max(16, baseX + maxWidth - startX);
                maxBottom = Math.Max(
                    maxBottom,
                    ContactFieldGroupTemplate.LayoutContactFieldsDefault(
                        block,
                        rest,
                        startX,
                        rowY,
                        avail,
                        suppressFieldComments
                    )
                );
            }
            else
            {
                maxBottom = LayoutUtil.LayoutFlowRow(
                    block,
                    rest,
                    baseX,
                    maxBottom + 4,
                    maxWidth,
                    includeFieldComments: !suppressFieldComments
                );
            }
        }

        return maxBottom + 8;
    }

    /// <summary>
    /// When template Data slots have no match in the DCF, collapse their vertical bands
    /// so later rows (e.g. IdentityCard after missing Contact/Address) move up.
    /// </summary>
    public static IReadOnlyList<TemplateSlot> CompactAbsentRows(
        IReadOnlyList<TemplateSlot> slots,
        IReadOnlyList<Data> fields
    )
    {
        var list = slots.Select(s => s.Clone()).ToList();
        if (list.Count == 0)
            return list;

        // Anchor rows = distinct Y of Data slots (template structure).
        var dataSlots = list
            .Where(s => s.Kind == TemplateSlotKind.Data && !s.IsExcluded)
            .ToList();
        if (dataSlots.Count == 0)
            return list;

        var rowYs = dataSlots.Select(s => s.Y).Distinct().OrderBy(y => y).ToList();
        var absentRowYs = new HashSet<int>();
        foreach (var rowY in rowYs)
        {
            var rowData = dataSlots.Where(s => s.Y == rowY).ToList();
            bool anyPresent = rowData.Any(s => FindField(fields, s) is not null);
            if (!anyPresent)
                absentRowYs.Add(rowY);
        }

        if (absentRowYs.Count == 0)
            return list;

        int NextDataRowY(int rowY) =>
            rowYs.FirstOrDefault(y => y > rowY, rowY + Consts.DefaultHeightData + 8);

        // Exact vertical span of this data row up to the next — do not inflate with a
        // minimum per absent row (many tight Address rows would over-shift later bands
        // onto ShortName / requisites).
        int GapAfter(int rowY) => Math.Max(0, NextDataRowY(rowY) - rowY);

        // Drop only Data on absent rows + Text/Pict that belong to those suffixes
        // (do not drop unrelated Pict moved into the band, e.g. removed CountryCode pict).
        var absentSuffixKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var absY in absentRowYs)
        {
            foreach (var d in dataSlots.Where(s => s.Y == absY))
                absentSuffixKeys.Add($"{d.Suffix}\0{d.StemHint}");
        }

        bool MatchesAbsent(TemplateSlot s)
        {
            string key = $"{s.Suffix}\0{s.StemHint}";
            if (absentSuffixKeys.Contains(key))
                return true;
            if (
                s.Kind == TemplateSlotKind.Text
                && s.Suffix.StartsWith("label:", StringComparison.OrdinalIgnoreCase)
            )
            {
                string dataSuffix = s.Suffix["label:".Length..];
                return absentSuffixKeys.Any(k =>
                    k.StartsWith(dataSuffix + "\0", StringComparison.OrdinalIgnoreCase)
                );
            }

            return false;
        }

        const int labelPad = 12;
        list.RemoveAll(s =>
        {
            if (MatchesAbsent(s))
                return true;
            // Orphan Text on an absent row band without a clear suffix link (Contact "Т.").
            if (s.Kind != TemplateSlotKind.Text)
                return false;
            return absentRowYs.Any(absY =>
                s.Y >= absY - labelPad && s.Y < NextDataRowY(absY)
            );
        });

        // Present slots: subtract heights of absent rows strictly above them.
        int ShiftUp(int y)
        {
            int shift = 0;
            foreach (var absY in absentRowYs.OrderBy(yy => yy))
            {
                if (absY >= y)
                    break;
                shift += GapAfter(absY);
            }

            return Math.Max(OriginY, y - shift);
        }

        foreach (var s in list)
            s.Y = ShiftUp(s.Y);

        return list;
    }

    /// <summary>
    /// Ensures each eligible Data slot has a companion Pict slot (movable/removable).
    /// Does not recreate Picts that already exist (including removed ones).
    /// Repositions row siblings so Pict does not overlap the next Data.
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

        // Do not fully reflow saved layouts — only push siblings that Pict actually overlaps.
        FixPictOverlaps(list);
        return list;
    }

    /// <summary>If a Pict covers another Data/Text on the same row, shift that sibling (and those to its right).</summary>
    private static void FixPictOverlaps(List<TemplateSlot> list)
    {
        foreach (
            var pict in list
                .Where(s => s.Kind == TemplateSlotKind.Pict && !s.IsExcluded)
                .OrderBy(s => s.Y)
                .ThenBy(s => s.X)
                .ToList()
        )
        {
            var owner = list.FirstOrDefault(d =>
                d.Kind == TemplateSlotKind.Data
                && d.Suffix.Equals(pict.Suffix, StringComparison.OrdinalIgnoreCase)
                && string.Equals(d.StemHint, pict.StemHint, StringComparison.OrdinalIgnoreCase)
            );

            // Snap pict to the right of its Data if it still sits on top of the owner field.
            if (owner is not null && !owner.IsExcluded)
            {
                int ownerRight = owner.X + Math.Max(owner.Width, 16);
                if (pict.X < ownerRight + LayoutUtil.PictGap)
                    pict.X = ownerRight + LayoutUtil.PictGap;
            }

            for (int guard = 0; guard < 32; guard++)
            {
                int rowY = owner?.Y ?? pict.Y;
                var hit = list.FirstOrDefault(s =>
                    !s.IsExcluded
                    && s.Kind != TemplateSlotKind.Pict
                    && !ReferenceEquals(s, owner)
                    && Math.Abs(s.Y - rowY) <= 2
                    && HorizontalOverlap(pict, s)
                );
                if (hit is null)
                    break;

                int shift = pict.X + pict.Width + 4 - hit.X;
                if (shift <= 0)
                    break;

                foreach (
                    var s in list.Where(s =>
                        !s.IsExcluded
                        && !ReferenceEquals(s, owner)
                        && Math.Abs(s.Y - hit.Y) <= 2
                        && (ReferenceEquals(s, hit) || s.X >= pict.X)
                    )
                )
                {
                    s.X += shift;
                }
            }
        }
    }

    private static bool HorizontalOverlap(TemplateSlot a, TemplateSlot b) =>
        !(a.X + a.Width <= b.X || b.X + b.Width <= a.X);

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

    public static string PictFieldName(TemplateSlot slot)
    {
        string stem = EffectiveStemHint(slot.StemHint);
        return string.IsNullOrEmpty(stem) ? slot.Suffix : $"{stem}_{slot.Suffix}";
    }

    /// <summary>UI-only stem used when embedding templates in the preview editor.</summary>
    public static bool IsUiOnlyStemHint(string? stemHint) =>
        !string.IsNullOrEmpty(stemHint)
        && stemHint.Contains("embed:", StringComparison.OrdinalIgnoreCase);

    public static string EffectiveStemHint(string? stemHint)
    {
        if (string.IsNullOrEmpty(stemHint) || IsUiOnlyStemHint(stemHint))
            return string.Empty;

        // "Address:embed:2" → keep structural part before embed
        int embedAt = stemHint.IndexOf("embed:", StringComparison.OrdinalIgnoreCase);
        if (embedAt > 0)
        {
            string before = stemHint[..embedAt].TrimEnd(':');
            return before;
        }

        return stemHint;
    }

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

        // Disambiguate shared suffixes (CountryCode) by template group.
        if (slot.Group.Equals(IdentityCardFieldGroupTemplate.TemplateName, StringComparison.OrdinalIgnoreCase))
        {
            candidates = candidates
                .Where(f => IdentityCardFieldGroupTemplate.IsIdentityCardField(f.FieldElement.ElementName))
                .ToList();
        }
        else if (slot.Group.Equals(AddressFieldGroupTemplate.TemplateName, StringComparison.OrdinalIgnoreCase))
        {
            candidates = candidates
                .Where(f => !IdentityCardFieldGroupTemplate.IsIdentityCardField(f.FieldElement.ElementName))
                .ToList();
        }
        else if (slot.Group.Equals(ContactFieldGroupTemplate.TemplateName, StringComparison.OrdinalIgnoreCase))
        {
            candidates = candidates.Where(ContactFieldGroupTemplate.IsContactField).ToList();
        }

        if (candidates.Count == 0)
            return null;

        string stem = EffectiveStemHint(slot.StemHint);
        if (!string.IsNullOrEmpty(stem))
        {
            var hinted = candidates.FirstOrDefault(f =>
                f.FieldElement.ElementName.Contains(stem, StringComparison.OrdinalIgnoreCase)
            );
            if (hinted is not null)
                return hinted;
        }

        // Prefer Address_* over other non-IdentityCard CountryCode when group is Address.
        if (slot.Group.Equals(AddressFieldGroupTemplate.TemplateName, StringComparison.OrdinalIgnoreCase))
        {
            var addr = candidates.FirstOrDefault(f =>
                f.FieldElement.ElementName.Contains("Address", StringComparison.OrdinalIgnoreCase)
            );
            if (addr is not null)
                return addr;
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
