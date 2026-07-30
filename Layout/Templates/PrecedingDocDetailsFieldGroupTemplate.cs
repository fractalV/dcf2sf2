using DcfToSf2.Parsing;

namespace DcfToSf2.Layout.Templates;

internal sealed class PrecedingDocDetailsFieldGroupTemplate : IFieldGroupTemplate
{
    public const string TemplateName = "PrecedingDocDetails";

    private readonly CustomsDocIdFieldGroupTemplate _customs = new();
    private readonly PiDocumentNumberFieldGroupTemplate _pi = new();
    private readonly TirIdFieldGroupTemplate _tir = new();
    private readonly GoodsQuantityFieldGroupTemplate _qty = new();

    public string Name => TemplateName;

    public FieldGroupMatch? TryMatch(IReadOnlyList<Data> fields, int startIndex, HashSet<string> ignoreFields)
    {
        // Applied when scanning from index 0 of a PrecedingDoc-like section, or when first field matches.
        if (startIndex != 0)
        {
            // Also allow match if current field is ModeCode and section looks like preceding doc
            if (startIndex < 0 || startIndex >= fields.Count)
                return null;
            string last = LayoutUtil.GetLastToken(fields[startIndex].FieldElement.ElementName);
            if (!last.Equals("PrecedingDocumentModeCode", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        var visible = fields
            .Where(f => LayoutUtil.IsVisible(f, ignoreFields) && !LayoutUtil.IsBlockOpenMarker(f))
            .ToList();

        bool hasMode = visible.Any(f =>
            LayoutUtil.GetLastToken(f.FieldElement.ElementName).Equals("PrecedingDocumentModeCode", StringComparison.OrdinalIgnoreCase)
            || LayoutUtil.GetLastToken(f.FieldElement.ElementName).Equals("PrecedingDocumentName", StringComparison.OrdinalIgnoreCase)
        );
        bool hasChild =
            visible.Any(f =>
                f.FieldElement.ElementName.Contains("CustomsDocIdDetails", StringComparison.OrdinalIgnoreCase)
                || f.FieldElement.ElementName.Contains("DTMDocDetails", StringComparison.OrdinalIgnoreCase)
                || f.FieldElement.ElementName.Contains("PIDocumentNumber", StringComparison.OrdinalIgnoreCase)
                || f.FieldElement.ElementName.Contains("TIRIdDetails", StringComparison.OrdinalIgnoreCase)
                || LayoutUtil.GetLastToken(f.FieldElement.ElementName).Equals("PrecedingDocumentNum", StringComparison.OrdinalIgnoreCase)
            );

        if (!hasMode || !hasChild)
            return null;

        // Consume all visible non-hyperlink fields of the section when matched at start
        if (startIndex == 0)
        {
            return new FieldGroupMatch
            {
                Fields = visible.Where(f => !LayoutUtil.IsBlockType(f)).ToList(),
                TemplateName = TemplateName,
            };
        }

        return new FieldGroupMatch
        {
            Fields = visible.Where(f => !LayoutUtil.IsBlockType(f)).Skip(startIndex).ToList(),
            TemplateName = TemplateName,
        };
    }

    /// <summary>Match by block path leaf name.</summary>
    public static bool MatchesPath(string? path) =>
        !string.IsNullOrEmpty(path)
        && path.Contains("PrecedingDocDetails", StringComparison.OrdinalIgnoreCase);

    public int Layout(Block block, FieldGroupMatch match, int baseX, int y, int maxWidth, HashSet<string> ignoreFields)
    {
        var fields = match.Fields.Where(f => LayoutUtil.IsVisible(f, ignoreFields) && !LayoutUtil.IsBlockType(f)).ToList();
        if (TemplateLayoutStore.HasOverrides(TemplateName))
            return TemplateSlotLayout.Apply(block, fields, GetPreviewSlots(ignoreFields), baseX, y, maxWidth);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Data? FindBareOrPrefixed(string suffix) =>
            fields.FirstOrDefault(f =>
                LayoutUtil.GetLastToken(f.FieldElement.ElementName).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                && !f.FieldElement.ElementName.Contains("CustomsDocId", StringComparison.OrdinalIgnoreCase)
                && !f.FieldElement.ElementName.Contains("DTMDoc", StringComparison.OrdinalIgnoreCase)
                && !f.FieldElement.ElementName.Contains("PIDocument", StringComparison.OrdinalIgnoreCase)
                && !f.FieldElement.ElementName.Contains("TIRId", StringComparison.OrdinalIgnoreCase)
                && !f.FieldElement.ElementName.Contains("PrecedingGoods", StringComparison.OrdinalIgnoreCase)
            );

        // Row 1: ModeCode | Name | GoodsNumeric
        var mode = FindBareOrPrefixed("PrecedingDocumentModeCode");
        var name = FindBareOrPrefixed("PrecedingDocumentName");
        var goodsNum = FindBareOrPrefixed("PrecedingDocumentGoodsNumeric");
        if (mode is not null || name is not null || goodsNum is not null)
        {
            if (mode is not null)
            {
                block.Fields.Add(LayoutUtil.TextField(baseX + 708, y + 4, 30, 12, "Тов. №"));
            }

            y = LayoutUtil.PlaceCompactRow(
                block,
                [(mode, 44), (name, 640), (goodsNum, 28)],
                baseX,
                y,
                maxWidth
            );
            if (mode is not null)
            {
                // mode is first slot (width 44) in PlaceCompactRow above; pict already added there
                used.Add(mode.FieldElement.ElementName);
            }

            if (name is not null)
                used.Add(name.FieldElement.ElementName);
            if (goodsNum is not null)
                used.Add(goodsNum.FieldElement.ElementName);
            y += 8;
        }

        // Row 2: CustomsDocId | DTMDoc | PI — side by side
        int row2Y = y + 12;
        int x = baseX;

        var customsFields = fields
            .Where(f =>
                f.FieldElement.ElementName.Contains("CustomsDocIdDetails", StringComparison.OrdinalIgnoreCase)
                && !used.Contains(f.FieldElement.ElementName)
            )
            .ToList();
        if (customsFields.Count > 0)
        {
            block.Fields.Add(LayoutUtil.TextField(x, row2Y - 12, 122, 12, "Регистрационный номер ДТ"));
            CustomsDocIdFieldGroupTemplate.LayoutCustomsDocId(block, customsFields, x, row2Y, 240);
            foreach (var f in customsFields)
                used.Add(f.FieldElement.ElementName);
            x += 252;
        }

        var dtmFields = fields
            .Where(f =>
                f.FieldElement.ElementName.Contains("DTMDocDetails", StringComparison.OrdinalIgnoreCase)
                && !used.Contains(f.FieldElement.ElementName)
            )
            .ToList();
        if (dtmFields.Count > 0)
        {
            block.Fields.Add(LayoutUtil.TextField(x, row2Y - 12, 188, 12, "Регистрационный номер декларации на ТС"));
            CustomsDocIdFieldGroupTemplate.LayoutCustomsDocId(block, dtmFields, x, row2Y, 240);
            foreach (var f in dtmFields)
                used.Add(f.FieldElement.ElementName);
            x += 252;
        }

        var piFields = fields
            .Where(f =>
                f.FieldElement.ElementName.Contains("PIDocumentNumber", StringComparison.OrdinalIgnoreCase)
                && !used.Contains(f.FieldElement.ElementName)
            )
            .ToList();
        if (piFields.Count > 0)
        {
            block.Fields.Add(
                LayoutUtil.TextField(x, row2Y - 12, 244, 12, "Регистрационный номер предварительной информации")
            );
            PiDocumentNumberFieldGroupTemplate.LayoutPi(block, piFields, x, row2Y, 240);
            foreach (var f in piFields)
                used.Add(f.FieldElement.ElementName);
        }

        if (customsFields.Count > 0 || dtmFields.Count > 0 || piFields.Count > 0)
            y = row2Y + Consts.DefaultHeightData + 16;

        // Row 3: PrecedingDocumentNum | TIR | OtherPr
        int row3Y = y;
        x = baseX;
        var precNum = FindBareOrPrefixed("PrecedingDocumentNum");
        if (precNum is not null)
        {
            block.Fields.Add(LayoutUtil.DataField(precNum, x, row3Y, 244, Consts.DefaultHeightData));
            used.Add(precNum.FieldElement.ElementName);
            x += 252;
        }

        var tirFields = fields
            .Where(f =>
                f.FieldElement.ElementName.Contains("TIRIdDetails", StringComparison.OrdinalIgnoreCase)
                && !used.Contains(f.FieldElement.ElementName)
            )
            .ToList();
        if (tirFields.Count > 0)
        {
            block.Fields.Add(LayoutUtil.TextField(x, row3Y - 12, 91, 12, "Номер книжки МДП"));
            TirIdFieldGroupTemplate.LayoutTir(block, tirFields, x, row3Y, 120);
            foreach (var f in tirFields)
                used.Add(f.FieldElement.ElementName);
            x += 100;
        }

        var otherNum = FindBareOrPrefixed("OtherPrDocumentNumber");
        var otherDate = FindBareOrPrefixed("OtherPrDocumentDate");
        if (otherNum is not null || otherDate is not null)
        {
            block.Fields.Add(
                LayoutUtil.TextField(x, row3Y - 12, 298, 12, "Регистрационный номер и дата иного предшествующего документа")
            );
            if (otherNum is not null)
            {
                block.Fields.Add(LayoutUtil.DataField(otherNum, x, row3Y, 268, Consts.DefaultHeightData));
                used.Add(otherNum.FieldElement.ElementName);
                x += 272;
            }

            if (otherDate is not null)
            {
                block.Fields.Add(LayoutUtil.DataField(otherDate, x, row3Y, 84, Consts.DefaultHeightData));
                used.Add(otherDate.FieldElement.ElementName);
            }
        }

        if (precNum is not null || tirFields.Count > 0 || otherNum is not null || otherDate is not null)
            y = row3Y + Consts.DefaultHeightData + 20;

        // Goods details header + quantity
        var qtyFields = fields
            .Where(f =>
                f.FieldElement.ElementName.Contains("PrecedingGoodsDetails", StringComparison.OrdinalIgnoreCase)
                && !used.Contains(f.FieldElement.ElementName)
                && (
                    LayoutUtil.GetLastToken(f.FieldElement.ElementName) is "GoodsQuantity" or "MeasureUnitQualifierName" or "MeasureUnitQualifierCode"
                    || f.FieldElement.ElementName.Contains("DeclGrossWeightQuantity", StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();

        if (qtyFields.Count > 0)
        {
            block.Fields.Add(
                LayoutUtil.TextField(baseX, y, 334, 16, "Сведения о товаре, заявленные в предшествующем документе")
            );
            y += 20;
            block.Fields.Add(LayoutUtil.TextField(baseX, y, 167, 12, "Масса брутто декларируемого товара"));
            y += 16;
            y = GoodsQuantityFieldGroupTemplate.LayoutQuantity(block, qtyFields, baseX, y, maxWidth, stacked: false);
            foreach (var f in qtyFields)
                used.Add(f.FieldElement.ElementName);
        }

        var rest = fields.Where(f => !used.Contains(f.FieldElement.ElementName)).ToList();
        if (rest.Count > 0)
            y = LayoutUtil.LayoutFlowRow(block, rest, baseX, y, maxWidth);

        return y;
    }

    public IReadOnlyList<TemplateSlot> GetDefaultPreviewSlots(HashSet<string> ignoreFields)
    {
        var slots = new List<TemplateSlot>();
        slots.Add(TemplateSlotLayout.TextSlot(TemplateName, "Тов. №", 712, 8, 30, 12, "goodsNo"));
        slots.AddRange(
            AddressFieldGroupTemplate.CompactRows(
                [
                    [
                        ("PrecedingDocumentModeCode", 44),
                        ("PrecedingDocumentName", 640),
                        ("PrecedingDocumentGoodsNumeric", 28),
                    ],
                ],
                ignoreFields,
                TemplateName
            )
        );

        int y = 4 + Consts.DefaultHeightData + 20;

        void AddChildDefaults(IFieldGroupTemplate t, int offsetX, string? stemHint, string? title, string titleId)
        {
            if (!string.IsNullOrEmpty(title))
            {
                slots.Add(
                    TemplateSlotLayout.TextSlot(
                        t.Name,
                        title!,
                        offsetX + 4,
                        y,
                        Math.Min(244, title!.Length * Consts.DefaultWidthChar),
                        Consts.DefaultHeightText,
                        titleId
                    )
                );
            }

            foreach (var s in t.GetDefaultPreviewSlots(ignoreFields))
            {
                if (s.Kind == TemplateSlotKind.Text)
                    continue; // titles added above with unique ids
                var c = s.Clone();
                c.X = offsetX + s.X;
                c.Y = y + 12 + (s.Y > 16 ? s.Y - 16 : 0);
                if (!string.IsNullOrEmpty(stemHint))
                {
                    c.StemHint = stemHint;
                    c.Id = TemplateSlot.MakeId(c.Kind, c.Group, c.Suffix, stemHint);
                }

                slots.Add(c);
            }
        }

        AddChildDefaults(_customs, 0, "CustomsDocIdDetails", "Регистрационный номер ДТ", "titleDt");
        AddChildDefaults(_customs, 252, "DTMDocDetails", "Регистрационный номер декларации на ТС", "titleDtm");
        AddChildDefaults(_pi, 504, "PIDocumentNumber", "Регистрационный номер предварительной информации", "titlePi");
        y += Consts.DefaultHeightData + 28;

        slots.Add(TemplateSlotLayout.TextSlot(TemplateName, "Номер книжки МДП", 256, y, 91, 12, "titleTir"));
        slots.Add(
            TemplateSlotLayout.TextSlot(
                TemplateName,
                "Регистрационный номер и дата иного предшествующего документа",
                356,
                y,
                298,
                12,
                "titleOther"
            )
        );

        foreach (var s in _tir.GetDefaultPreviewSlots(ignoreFields))
        {
            if (s.Kind == TemplateSlotKind.Text)
                continue;
            var c = s.Clone();
            c.X = 256 + s.X;
            c.Y = y + 12;
            c.StemHint = "TIRIdDetails";
            c.Id = TemplateSlot.MakeId(c.Kind, c.Group, c.Suffix, c.StemHint);
            slots.Add(c);
        }

        slots.AddRange(
            AddressFieldGroupTemplate.CompactRows(
                [[("PrecedingDocumentNum", 244), ("OtherPrDocumentNumber", 268), ("OtherPrDocumentDate", 84)]],
                ignoreFields,
                TemplateName
            ).Select(s =>
            {
                s.Y = y + 12;
                return s;
            })
        );
        y += Consts.DefaultHeightData + 28;

        slots.Add(
            TemplateSlotLayout.TextSlot(
                TemplateName,
                "Сведения о товаре, заявленные в предшествующем документе",
                4,
                y,
                334,
                16,
                "goodsHeader"
            )
        );
        y += 20;
        slots.Add(
            TemplateSlotLayout.TextSlot(TemplateName, "Масса брутто декларируемого товара", 4, y, 167, 12, "grossHeader")
        );
        y += 16;
        foreach (var s in _qty.GetDefaultPreviewSlots(ignoreFields))
        {
            var c = s.Clone();
            c.Y = y + s.Y;
            c.StemHint = "DeclGrossWeightQuantity";
            c.Id = TemplateSlot.MakeId(c.Kind, c.Group, c.Suffix, c.StemHint);
            slots.Add(c);
        }

        return slots;
    }

    public IReadOnlyList<TemplateSlot> GetPreviewSlots(HashSet<string> ignoreFields) =>
        TemplateLayoutStore.ApplyOverrides(Name, GetDefaultPreviewSlots(ignoreFields));
}
