using DcfToSf2.Parsing;

namespace DcfToSf2.Layout.Templates;

internal sealed class OrganizationFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "Organization";

    private static readonly string[] ContactSuffixes = ContactFieldGroupTemplate.Suffixes;

    private readonly AddressFieldGroupTemplate _address = new();
    private readonly IdentityCardFieldGroupTemplate _identityCard = new();
    private readonly ContactFieldGroupTemplate _contact = new();

    public string Name => TemplateName;

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        // Organization is applied from org buckets, not generic scan.
        return null;
    }

    public int Layout(Block block, FieldGroupMatch match, int baseX, int y, int maxWidth, HashSet<string> ignoreFields) =>
        LayoutOrgFields(block, match.Fields, baseX, y, maxWidth, ignoreFields);

    public int LayoutOrgFields(
        Block block,
        List<Data> fields,
        int baseX,
        int startY,
        int maxWidth,
        HashSet<string> ignoreFields,
        bool suppressFieldComments = false
    )
    {
        if (TemplateLayoutStore.HasOverrides(TemplateName))
        {
            var visibleForSlots = fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields) && !LayoutUtil.IsBlockType(f)).ToList();
            var slots = GetPreviewSlots(ignoreFields);
            return TemplateSlotLayout.Apply(
                block,
                visibleForSlots,
                slots,
                baseX,
                startY,
                maxWidth,
                suppressFieldComments: suppressFieldComments
            );
        }

        int y = startY;
        var visible = fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields) && !LayoutUtil.IsBlockType(f)).ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (
            var f in visible.Where(f =>
                LayoutUtil.GetLastToken(f.FieldElement.ElementName).Equals("OrganizationName", StringComparison.OrdinalIgnoreCase)
                && !IdentityCardFieldGroupTemplate.IsIdentityCardField(f.FieldElement.ElementName)
            )
        )
        {
            int h = LayoutUtil.HeightFor(f);
            int w = Math.Min(LayoutUtil.WidthFor(f, maxWidth), maxWidth);
            string? comment = suppressFieldComments ? null : LayoutUtil.ExtractComment(f.FieldElement.ElementValue);
            block.Fields.Add(LayoutUtil.DataField(f, baseX, y, w, h, comment));
            used.Add(f.FieldElement.ElementName);
            y += h + 8;
        }

        var shortName = visible.FirstOrDefault(f =>
            LayoutUtil.GetLastToken(f.FieldElement.ElementName).Equals("ShortName", StringComparison.OrdinalIgnoreCase)
        );
        var requisites = visible
            .Where(f =>
            {
                string s = LayoutUtil.GetLastToken(f.FieldElement.ElementName);
                return s is "OGRN" or "INN" or "KPP" or "OKPOID" or "OKATOCode";
            })
            .ToList();

        if (shortName is not null || requisites.Count > 0)
        {
            int x = baseX;
            if (shortName is not null)
            {
                int w = Math.Min(240, maxWidth / 2);
                block.Fields.Add(LayoutUtil.DataField(shortName, x, y, w, Consts.DefaultHeightData));
                int pictExtra = LayoutUtil.MaybeAddPict(block, shortName, x, y, w);
                used.Add(shortName.FieldElement.ElementName);
                x += w + 4 + pictExtra;
            }

            foreach (var r in requisites)
            {
                string label = LayoutUtil.ShortLabel(LayoutUtil.GetLastToken(r.FieldElement.ElementName));
                block.Fields.Add(
                    LayoutUtil.TextField(x, y - 12, label.Length * Consts.DefaultWidthChar, Consts.DefaultHeightText, label)
                );
                int w = LayoutUtil.WidthFor(r, 100);
                block.Fields.Add(LayoutUtil.DataField(r, x, y, w, Consts.DefaultHeightData));
                int pictExtra = LayoutUtil.MaybeAddPict(block, r, x, y, w);
                used.Add(r.FieldElement.ElementName);
                x += w + 4 + pictExtra;
                if (x > baseX + maxWidth - 40)
                {
                    x = baseX;
                    y += Consts.DefaultHeightData + 16;
                }
            }

            y += Consts.DefaultHeightData + 8;
        }

        var contacts = ContactSuffixes
            .Select(s => visible.FirstOrDefault(f => LayoutUtil.GetLastToken(f.FieldElement.ElementName).Equals(s, StringComparison.OrdinalIgnoreCase)))
            .Where(f => f is not null)
            .Cast<Data>()
            .ToList();

        if (contacts.Count > 0)
        {
            y = ContactFieldGroupTemplate.LayoutContactFields(
                block,
                contacts,
                baseX,
                y,
                maxWidth,
                ignoreFields,
                suppressFieldComments
            );
            foreach (var c in contacts)
                used.Add(c.FieldElement.ElementName);
        }

        // Nested IdentityCard / Address by first appearance order
        var idFields = visible
            .Where(f =>
                IdentityCardFieldGroupTemplate.IsIdentityCardField(f.FieldElement.ElementName)
                && !used.Contains(f.FieldElement.ElementName)
            )
            .ToList();
        var addressFields = visible
            .Where(f =>
                AddressFieldGroupTemplate.IsAddressSuffix(LayoutUtil.GetLastToken(f.FieldElement.ElementName))
                && !IdentityCardFieldGroupTemplate.IsIdentityCardField(f.FieldElement.ElementName)
                && !used.Contains(f.FieldElement.ElementName)
            )
            .ToList();

        int idFirst = idFields.Count > 0 ? visible.IndexOf(idFields[0]) : int.MaxValue;
        int addrFirst = addressFields.Count > 0 ? visible.IndexOf(addressFields[0]) : int.MaxValue;

        void PlaceId()
        {
            if (idFields.Count < 1)
                return;
            y = IdentityCardFieldGroupTemplate.LayoutIdentityCardFields(block, idFields, baseX, y, maxWidth, ignoreFields);
            foreach (var f in idFields)
                used.Add(f.FieldElement.ElementName);
        }

        void PlaceAddr()
        {
            if (addressFields.Count < 1)
                return;
            y = AddressFieldGroupTemplate.LayoutAddressFields(block, addressFields, baseX, y, maxWidth, ignoreFields);
            foreach (var f in addressFields)
                used.Add(f.FieldElement.ElementName);
        }

        if (idFirst <= addrFirst)
        {
            PlaceId();
            PlaceAddr();
        }
        else
        {
            PlaceAddr();
            PlaceId();
        }

        foreach (var f in visible.Where(f =>
                     LayoutUtil.GetLastToken(f.FieldElement.ElementName)
                         .Equals("OrganizationLanguage", StringComparison.OrdinalIgnoreCase)))
            used.Add(f.FieldElement.ElementName);

        var rest = visible.Where(f => !used.Contains(f.FieldElement.ElementName)).ToList();
        if (rest.Count > 0)
            y = LayoutUtil.LayoutFlowRow(block, rest, baseX, y, maxWidth);

        return y;
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields)
    {
        var slots = new List<TemplateSlot>
        {
            new()
            {
                Id = TemplateSlot.MakeId(TemplateSlotKind.Data, TemplateName, "OrganizationName"),
                Suffix = "OrganizationName",
                Label = "OrganizationName",
                Comment = LayoutUtil.DefaultFieldComment("OrganizationName", TemplateName),
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 4,
                Width = 400,
                Height = Consts.DefaultHeightData,
                IsIgnored = IsIgnored("OrganizationName", ignoreFields),
                Group = TemplateName,
            },
        };

        int y = 4 + Consts.DefaultHeightData + 8;
        slots.AddRange(
            AddressFieldGroupTemplate.CompactRows(
                [[("ShortName", 240), ("INN", 80), ("KPP", 80), ("OGRN", 100)]],
                ignoreFields,
                TemplateName
            ).Select(s =>
            {
                s.Y = y;
                return s;
            })
        );

        // Text labels for requisites
        foreach (var suffix in new[] { "INN", "KPP", "OGRN" })
        {
            var data = slots.FirstOrDefault(s => s.Suffix == suffix && !s.IsExcluded);
            if (data is null)
                continue;
            slots.Add(
                TemplateSlotLayout.TextSlot(
                    TemplateName,
                    LayoutUtil.ShortLabel(suffix),
                    data.X,
                    y - 12,
                    Math.Max(20, LayoutUtil.ShortLabel(suffix).Length * Consts.DefaultWidthChar),
                    Consts.DefaultHeightText,
                    $"label:{suffix}"
                )
            );
        }

        y += Consts.DefaultHeightData + 8;

        var contactSlots = _contact.GetDefaultPreviewSlots(ignoreFields);
        foreach (var s in contactSlots)
        {
            var c = s.Clone();
            c.Y = y + (s.Y - TemplateSlotLayout.OriginY);
            slots.Add(c);
        }

        int contactH = contactSlots.Count > 0 ? contactSlots.Max(s => s.Y + s.Height) - TemplateSlotLayout.OriginY : 0;
        y += contactH + 8;

        var idSlots = _identityCard.GetDefaultPreviewSlots(ignoreFields);
        foreach (var s in idSlots)
        {
            var c = s.Clone();
            c.Y = y + s.Y;
            slots.Add(c);
        }

        int idH = idSlots.Count > 0 ? idSlots.Max(s => s.Y + s.Height) : 0;
        y += idH + 8;

        var addrSlots = _address.GetDefaultPreviewSlots(ignoreFields);
        foreach (var s in addrSlots)
        {
            var c = s.Clone();
            c.Y = y + s.Y;
            slots.Add(c);
        }

        return slots;
    }

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));

    private static bool IsIgnored(string suffix, HashSet<string> ignoreFields) =>
        ignoreFields.Contains(suffix)
        || ignoreFields.Any(p => DcfParser.IsIgnorePattern(p) && DcfParser.MatchesIgnorePattern(p, suffix));
}
