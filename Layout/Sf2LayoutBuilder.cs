using DcfToSf2.Layout.Templates;
using DcfToSf2.Parsing;

namespace DcfToSf2.Layout;

internal static class Sf2LayoutBuilder
{
    private static readonly FieldGroupTemplateRegistry Templates = FieldGroupTemplateRegistry.Default;

    private static readonly string[] OrgStructuralSegments =
    [
        "OrganizationName",
        "OrganizationLanguage",
        "ShortName",
        "RFOrganizationFeatures",
        "RKOrganizationFeatures",
        "RBOrganizationFeatures",
        "RAOrganizationFeatures",
        "KGOrganizationFeatures",
        "Address",
        "Contact",
        "IdentityCard",
        "OGRN",
        "INN",
        "KPP",
        "OKPOID",
        "OKATOCode",
    ];

    private static readonly HashSet<string> OrgStructuralSet =
        new(OrgStructuralSegments, StringComparer.OrdinalIgnoreCase);

    private static readonly (string Left, string Right)[] OrgPairs =
    [
        ("Seller", "Buyer"),
        ("Consignor", "Consignee"),
    ];

    public static Sf2Document Build(DcfParseResult parsed, ConversionOptions options)
    {
        var header = parsed.Header;
        string title = FirstNonEmpty(
            header.ShortName,
            header.FullName,
            parsed.XsdLabels.GetValueOrDefault("__root__"),
            header.DocumentName
        );

        var doc = new Sf2Document { DocumentName = header.DocumentName, ShortTitle = title };

        // Group fields by SF2 block path derived only from DCF nesting
        var blockGroups = GroupByBlockPath(parsed.Fields, options.IgnoreFields);
        string? mainBlockName = ResolveMainBlock(header, parsed.Fields);

        // Build each block
        var built = new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, fields) in blockGroups)
        {
            bool isMain = string.Equals(path, Consts.DefaultBlockName, StringComparison.OrdinalIgnoreCase);
            bool isRepeating = fields.Any(f => string.IsNullOrEmpty(f.FieldElement.ElementValue))
                || path.Equals(mainBlockName, StringComparison.OrdinalIgnoreCase)
                || fields.Any(f =>
                    f.FieldElement.ElementValue.Contains("&&Num", StringComparison.OrdinalIgnoreCase)
                );

            // Filter: for repeating block open markers (empty value), treat as section header only
            var dataFields = fields
                .Where(f =>
                    !string.IsNullOrEmpty(f.FieldElement.ElementValue)
                    || IsBlockOpenMarker(f)
                )
                .ToList();

            Block block;
            if (isMain)
            {
                block = BuildMainBlock(dataFields, options, title, parsed);
            }
            else
            {
                block = BuildNestedBlock(path, dataFields, options, isRepeating || path == mainBlockName);
            }

            built[path] = block;
        }

        // Order: MainBlock first (if any), then MAIN, then others
        if (
            mainBlockName is not null
            && built.TryGetValue(mainBlockName, out var mb)
        )
        {
            doc.Blocks.Add(mb);
            built.Remove(mainBlockName);
        }

        // Also match path that equals main block leaf
        var mainKey = built.Keys.FirstOrDefault(k =>
            k.Equals(mainBlockName, StringComparison.OrdinalIgnoreCase)
            || k.EndsWith("\\" + mainBlockName, StringComparison.OrdinalIgnoreCase)
        );
        if (mainKey is not null && built.TryGetValue(mainKey, out var mb2))
        {
            if (!doc.Blocks.Contains(mb2))
                doc.Blocks.Add(mb2);
            built.Remove(mainKey);
        }

        if (built.TryGetValue(Consts.DefaultBlockName, out var main))
        {
            doc.Blocks.Add(main);
            built.Remove(Consts.DefaultBlockName);
        }

        foreach (var b in built.Values.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
            doc.Blocks.Add(b);

        return doc;
    }

    private static string? ResolveMainBlock(DcfHeader header, List<Data> fields)
    {
        if (!string.IsNullOrWhiteSpace(header.MainBlock))
            return header.MainBlock.Trim();

        // First repeating block open from DCF
        var open = fields.FirstOrDefault(f =>
            string.IsNullOrEmpty(f.FieldElement.ElementValue)
            && !string.IsNullOrEmpty(f.FieldElement.ElementName)
            && f.Level > 0
        );
        return open?.FieldElement.ElementName;
    }

    private static bool IsBlockOpenMarker(Data f) =>
        string.IsNullOrEmpty(f.FieldElement.ElementValue)
        && !string.IsNullOrEmpty(f.FieldElement.ElementName);

    private static Dictionary<string, List<Data>> GroupByBlockPath(
        List<Data> fields,
        HashSet<string> ignoreFields
    )
    {
        var result = new Dictionary<string, List<Data>>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            if (IsBlockOpenMarker(field))
            {
                // Hyperlink lives on parent; content section is the block path itself
                string parent =
                    field.Level <= 1
                        ? Consts.DefaultBlockName
                        : ParentPath(field.Path);
                Add(result, parent, field);
                Add(result, field.Path, field);
                continue;
            }

            if (DcfParser.ShouldSkipField(field.FieldElement.ElementName, ignoreFields))
                continue;

            if (IsBlockType(field) && SkipCountryFeatureHeader(field))
                continue;

            string path = field.Level == 0 ? Consts.DefaultBlockName : field.Path;
            Add(result, path, field);
        }

        return result;
    }

    private static string ParentPath(string path)
    {
        int idx = path.LastIndexOf('\\');
        return idx > 0 ? path[..idx] : Consts.DefaultBlockName;
    }

    private static void Add(Dictionary<string, List<Data>> map, string path, Data field)
    {
        if (!map.TryGetValue(path, out var list))
        {
            list = [];
            map[path] = list;
        }

        list.Add(field);
    }

    private static bool IsBlockType(Data field)
    {
        var t = FieldTypeParser.Parse(field.FieldElement.ElementValue);
        return t.IsBlock || field.FieldElement.ElementValue.TrimStart().StartsWith("B ", StringComparison.OrdinalIgnoreCase)
            || field.FieldElement.ElementValue.Contains("B |");
    }

    private static bool SkipCountryFeatureHeader(Data field)
    {
        string comment = ExtractComment(field.FieldElement.ElementValue).ToLowerInvariant();
        string[] markers =
        [
            "российской федерации",
            "республики казахстан",
            "республики беларусь",
            "республики армения",
            "кыргызской республики",
        ];
        return markers.Any(m => comment.Contains(m));
    }

    private static Block BuildMainBlock(
        List<Data> fields,
        ConversionOptions options,
        string title,
        DcfParseResult parsed
    )
    {
        var block = new Block { Name = Consts.DefaultBlockName, Title = string.Empty };
        int y = Consts.Default_Y;

        // Title
        if (!string.IsNullOrEmpty(title))
        {
            block.Fields.Add(
                TextField(Consts.Default_X + 260, 0, Math.Min(title.Length * Consts.DefaultWidthChar, 200), 24, title)
            );
        }

        // Root B-block titles: Applicant=B |* 2. Сведения о заявителе
        var sectionTitles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields.Where(IsBlockType))
        {
            if (SkipCountryFeatureHeader(f))
                continue;
            string name = f.FieldElement.ElementName;
            string comment = ExtractComment(f.FieldElement.ElementValue);
            if (!string.IsNullOrWhiteSpace(comment))
                sectionTitles[name] = comment;
            else
                sectionTitles.TryAdd(name, name);
        }

        var sectionBuckets = new Dictionary<string, List<Data>>(StringComparer.OrdinalIgnoreCase);
        var other = new List<Data>();
        var hyperlinks = new List<Data>();

        foreach (var f in fields)
        {
            if (IsBlockOpenMarker(f))
            {
                hyperlinks.Add(f);
                continue;
            }

            if (IsBlockType(f))
                continue;

            string? stem = GetSectionStem(f.FieldElement.ElementName, sectionTitles.Keys);
            if (stem is not null)
            {
                if (!sectionBuckets.TryGetValue(stem, out var list))
                {
                    list = [];
                    sectionBuckets[stem] = list;
                }

                list.Add(f);
            }
            else
            {
                other.Add(f);
            }
        }

        // Loose root fields (no B-block parent)
        y = LayoutGenericFields(block, other, Consts.Default_X, y, Consts.DefaultWidthBlock - 8, options, Consts.DefaultBlockName);

        var remaining = sectionBuckets.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (options.TwoColumn)
        {
            foreach (var (leftRole, rightRole) in OrgPairs)
            {
                string? leftKey = FindOrgBucketKey(remaining, leftRole);
                string? rightKey = FindOrgBucketKey(remaining, rightRole);
                if (leftKey is null && rightKey is null)
                    continue;

                y = LayoutOrgPair(
                    block,
                    leftKey is not null ? sectionBuckets[leftKey] : [],
                    rightKey is not null ? sectionBuckets[rightKey] : [],
                    leftKey ?? leftRole,
                    rightKey ?? rightRole,
                    y,
                    options,
                    sectionTitles
                );
                if (leftKey is not null)
                    remaining.Remove(leftKey);
                if (rightKey is not null)
                    remaining.Remove(rightKey);
                y += 8;
            }
        }

        foreach (var stem in remaining.OrderBy(s => SectionOrder(s, fields)))
        {
            string sectionTitle = ResolveSectionTitle(stem, sectionTitles);
            y = LayoutSectionWithRect(block, sectionBuckets[stem], stem, sectionTitle, y, options);
            y += 8;
        }

        foreach (var h in hyperlinks)
        {
            string label = FirstNonEmpty(h.RusNameBlock, h.FieldElement.ElementName);
            block.Fields.Add(
                new Field(
                    Type.Text,
                    new Size(
                        Math.Min((label.Length + h.FieldElement.ElementName.Length + 2) * Consts.DefaultWidthChar, 400),
                        Consts.DefaultHeightText + 4
                    ),
                    new Position(Consts.Default_X, y),
                    new Value("", $"@{h.FieldElement.ElementName}@{label}")
                )
            );
            y += Consts.DefaultHeightData + 4;
        }

        return block;
    }

    private static int SectionOrder(string stem, List<Data> fields)
    {
        int idx = fields.FindIndex(f =>
            f.FieldElement.ElementName.Equals(stem, StringComparison.OrdinalIgnoreCase)
            || f.FieldElement.ElementName.StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase)
        );
        return idx < 0 ? int.MaxValue : idx;
    }

    private static string ResolveSectionTitle(string stem, Dictionary<string, string> sectionTitles)
    {
        if (sectionTitles.TryGetValue(stem, out var t) && !string.IsNullOrWhiteSpace(t))
            return t;
        return OrgTitle(stem);
    }

    private static string? GetSectionStem(string fieldName, IEnumerable<string> sectionNames)
    {
        string? org = GetOrgStem(fieldName);
        if (org is not null)
            return org;

        foreach (var sec in sectionNames.OrderByDescending(s => s.Length))
        {
            if (
                fieldName.Equals(sec, StringComparison.OrdinalIgnoreCase)
                || fieldName.StartsWith(sec + "_", StringComparison.OrdinalIgnoreCase)
            )
                return sec;
        }

        return null;
    }

    private static int LayoutSectionWithRect(
        Block block,
        List<Data> fields,
        string stem,
        string sectionTitle,
        int startY,
        ConversionOptions options
    )
    {
        bool isContact = ContactFieldGroupTemplate.IsContactOnlyCluster(fields);
        bool isOrg =
            !isContact
            && fields.Any(f => GetOrgStem(f.FieldElement.ElementName) is not null);
        int contentWidth = Consts.DefaultWidthBlock - 16;
        int contentX = Consts.Default_X + 4;

        // Probe without comments: one data row → title Text only, no Rect.
        // (Comments can become Text labels in some layouts and skew the single-row check.)
        var probe = new Block { Name = "_probe" };
        LayoutSectionContent(
            probe,
            fields,
            isOrg,
            isContact,
            contentX,
            0,
            contentWidth,
            options,
            suppressFieldComments: true
        );
        bool singleLine = !isOrg && IsSingleDataRow(probe);

        if (singleLine)
        {
            int y = startY;
            if (!string.IsNullOrWhiteSpace(sectionTitle))
            {
                int tw = Math.Min(
                    Math.Max(sectionTitle.Length * Consts.DefaultWidthChar, 40),
                    Consts.DefaultWidthBlock - 8
                );
                block.Fields.Add(TextField(Consts.Default_X, y, tw, Consts.DefaultHeightText, sectionTitle.Trim()));
                y += Consts.DefaultHeightText + 4;
            }

            return LayoutSectionContent(
                block,
                fields,
                isOrg,
                isContact,
                contentX,
                y,
                contentWidth,
                options,
                suppressFieldComments: false
            );
        }

        int topPad = RectCaptionTopPad(sectionTitle, Consts.DefaultWidthBlock - 8);
        const int bottomPad = 8;
        int endY = LayoutSectionContent(
            block,
            fields,
            isOrg,
            isContact,
            contentX,
            startY + topPad,
            contentWidth,
            options,
            suppressFieldComments: false
        );
        int height = Math.Max(endY - startY + bottomPad, Consts.DefaultHeightData + topPad + bottomPad);
        AddSectionRect(block, Consts.Default_X, startY, Consts.DefaultWidthBlock - 8, height, sectionTitle);
        return startY + height;
    }

    private static bool IsSingleDataRow(Block probe)
    {
        var dataYs = probe
            .Fields.Where(f => f.FieldType == Type.Data)
            .Select(f => f.FieldPosition.Y)
            .Distinct()
            .ToList();
        return dataYs.Count <= 1;
    }

    private static int LayoutSectionContent(
        Block block,
        List<Data> fields,
        bool isOrg,
        bool isContact,
        int baseX,
        int startY,
        int maxWidth,
        ConversionOptions options,
        bool suppressFieldComments
    )
    {
        if (isContact)
        {
            return ContactFieldGroupTemplate.LayoutContactFields(
                block,
                fields,
                baseX,
                startY,
                maxWidth,
                options.IgnoreFields,
                suppressFieldComments
            );
        }

        if (isOrg)
        {
            return Templates.Organization.LayoutOrgFields(
                block,
                fields,
                baseX,
                startY,
                maxWidth,
                options.IgnoreFields,
                suppressFieldComments
            );
        }

        return LayoutGenericFields(
            block,
            fields,
            baseX,
            startY,
            maxWidth,
            options,
            Consts.DefaultBlockName,
            suppressFieldComments
        );
    }

    private static void AddSectionRect(Block block, int x, int y, int w, int h, string title)
    {
        if (h < 12 || w < 16)
            return;

        // Rect caption is plain text (no `), as in Alta samples.
        block.Fields.Add(
            new Field(Type.Rect, new Size(w, h), new Position(x, y), new Value(title.Trim(), string.Empty))
        );
    }

    private static Block BuildNestedBlock(
        string path,
        List<Data> fields,
        ConversionOptions options,
        bool repeating
    )
    {
        string leaf = path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path;
        string title = fields.Select(f => f.RusNameBlock).FirstOrDefault(s => !string.IsNullOrEmpty(s)) ?? leaf;
        if (repeating && !title.Contains("%d", StringComparison.Ordinal))
            title = string.IsNullOrWhiteSpace(title) ? $"%d" : $"{title} %d".Replace("  ", " ");

        var block = new Block
        {
            Name = path,
            Title = title.Trim(),
            IsRepeating = repeating,
        };

        var content = fields.Where(f => !IsBlockOpenMarker(f) || f.FieldElement.ElementName != leaf).ToList();
        // nested hyperlinks inside
        int y = Consts.Default_Y;
        y = LayoutGenericFields(
            block,
            content.Where(f => !IsBlockOpenMarker(f)).ToList(),
            Consts.Default_X,
            y,
            Consts.DefaultWidthBlock - 8,
            options,
            path
        );

        foreach (var h in content.Where(IsBlockOpenMarker))
        {
            string label = FirstNonEmpty(h.RusNameBlock, h.FieldElement.ElementName);
            block.Fields.Add(
                new Field(
                    Type.Text,
                    new Size(200, Consts.DefaultHeightText),
                    new Position(Consts.Default_X + 200, y),
                    new Value("", $"@{h.FieldElement.ElementName}@{label}")
                )
            );
            y += Consts.DefaultHeightData;
        }

        return block;
    }

    private static int LayoutOrgPair(
        Block block,
        List<Data> leftFields,
        List<Data> rightFields,
        string leftName,
        string rightName,
        int startY,
        ConversionOptions options,
        Dictionary<string, string> sectionTitles
    )
    {
        string leftTitle = ResolveSectionTitle(leftName, sectionTitles);
        string rightTitle = ResolveSectionTitle(rightName, sectionTitles);
        int rectW = Consts.ColumnWidth + 4;
        int topPad = Math.Max(RectCaptionTopPad(leftTitle, rectW), RectCaptionTopPad(rightTitle, rectW));
        const int bottomPad = 8;
        int contentY = startY + topPad;
        int colW = Consts.ColumnWidth - 4;

        int yLeft = leftFields.Count == 0
            ? contentY
            : Templates.Organization.LayoutOrgFields(
                block,
                leftFields,
                Consts.Default_X + 4,
                contentY,
                colW,
                options.IgnoreFields,
                suppressFieldComments: false
            );
        int yRight = rightFields.Count == 0
            ? contentY
            : Templates.Organization.LayoutOrgFields(
                block,
                rightFields,
                Consts.RightColumnX + 4,
                contentY,
                colW,
                options.IgnoreFields,
                suppressFieldComments: false
            );

        int endY = Math.Max(yLeft, yRight);
        int height = Math.Max(endY - startY + bottomPad, Consts.DefaultHeightData + topPad + bottomPad);

        if (leftFields.Count > 0)
            AddSectionRect(block, Consts.Default_X, startY, rectW, height, leftTitle);

        if (rightFields.Count > 0)
            AddSectionRect(block, Consts.RightColumnX, startY, rectW, height, rightTitle);

        return startY + height;
    }

    /// <summary>
    /// Space under the Rect top edge so fields do not cover the frame caption.
    /// Long titles wrap; estimate line count from caption width.
    /// </summary>
    private static int RectCaptionTopPad(string title, int rectWidth)
    {
        // Extra air under the Rect top/caption so first Data row is not tight to the frame.
        const int extraBelowCaption = 16;
        string t = (title ?? string.Empty).Trim();
        if (t.Length == 0)
            return Consts.DefaultHeightText + extraBelowCaption;

        int usable = Math.Max(rectWidth - 16, Consts.DefaultWidthChar * 10);
        int charsPerLine = Math.Max(usable / Consts.DefaultWidthChar, 1);
        int lines = Math.Max(1, (t.Length + charsPerLine - 1) / charsPerLine);
        return lines * Consts.DefaultHeightText + extraBelowCaption;
    }

    private static int LayoutGenericFields(
        Block block,
        List<Data> fields,
        int baseX,
        int startY,
        int maxWidth,
        ConversionOptions options,
        string? blockPath = null,
        bool suppressFieldComments = false
    )
    {
        int y = startY;

        var remaining = new List<Data>();
        foreach (var f in fields)
        {
            if (DcfParser.ShouldSkipField(f.FieldElement.ElementName, options.IgnoreFields))
                continue;
            if (IsBlockType(f))
                continue;
            remaining.Add(f);
        }

        // Loading | Unloading on one row if both present
        var loading = remaining
            .Where(f => f.FieldElement.ElementName.StartsWith("Loading_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var unloading = remaining
            .Where(f => f.FieldElement.ElementName.StartsWith("Unloading_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (loading.Count > 0 || unloading.Count > 0)
        {
            remaining = remaining.Except(loading).Except(unloading).ToList();
            int rowY = y;
            int lx = baseX;
            foreach (var f in loading)
            {
                int w = WidthFor(f, 120);
                string? label = suppressFieldComments ? null : ExtractComment(f.FieldElement.ElementValue);
                block.Fields.Add(DataField(f, lx, rowY, w, Consts.DefaultHeightData, label));
                lx += w + 8;
            }

            int rx = baseX + maxWidth / 2;
            foreach (var f in unloading)
            {
                int w = WidthFor(f, 120);
                string? label = suppressFieldComments ? null : ExtractComment(f.FieldElement.ElementValue);
                block.Fields.Add(DataField(f, rx, rowY, w, Consts.DefaultHeightData, label));
                rx += w + 8;
            }

            y += Consts.DefaultHeightData + 20;
        }

        // Document triad PrDocumentName/Number/Date
        y = LayoutDocumentTriad(block, remaining, baseX, y, maxWidth, out var usedDoc, suppressFieldComments);
        remaining = remaining.Where(f => !usedDoc.Contains(f.FieldElement.ElementName)).ToList();

        // Signature person row
        y = LayoutSignature(block, remaining, baseX, y, maxWidth, out var usedSig);
        remaining = remaining.Where(f => !usedSig.Contains(f.FieldElement.ElementName)).ToList();

        y = Templates.LayoutWithTemplates(
            block,
            remaining,
            baseX,
            y,
            maxWidth,
            options.IgnoreFields,
            blockPath,
            (b, fs, bx, yy, mw) => LayoutUtil.LayoutFlowRow(b, fs, bx, yy, mw, includeFieldComments: !suppressFieldComments)
        );

        return y;
    }

    private static int StackFields(Block block, List<Data> fields, int baseX, int y, int maxWidth) =>
        LayoutUtil.LayoutFlowRow(block, fields, baseX, y, maxWidth);

    private static int LayoutDocumentTriad(
        Block block,
        List<Data> fields,
        int baseX,
        int y,
        int maxWidth,
        out HashSet<string> used,
        bool suppressFieldComments = false
    )
    {
        used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = fields.LastOrDefault(f =>
            DcfParser.GetLastToken(f.FieldElement.ElementName).Equals("PrDocumentName", StringComparison.OrdinalIgnoreCase)
        );
        var number = fields.LastOrDefault(f =>
            DcfParser.GetLastToken(f.FieldElement.ElementName).Equals("PrDocumentNumber", StringComparison.OrdinalIgnoreCase)
        );
        var date = fields.LastOrDefault(f =>
            DcfParser.GetLastToken(f.FieldElement.ElementName).Equals("PrDocumentDate", StringComparison.OrdinalIgnoreCase)
        );
        if (name is null && number is null && date is null)
            return y;

        int x = baseX;
        if (name is not null)
        {
            int w = Math.Min(334, maxWidth / 2);
            string? label = suppressFieldComments ? null : ExtractComment(name.FieldElement.ElementValue);
            block.Fields.Add(DataField(name, x, y, w, Consts.DefaultHeightData, label));
            used.Add(name.FieldElement.ElementName);
            x += w + 4;
        }

        if (number is not null)
        {
            block.Fields.Add(TextField(x - 2, y, 10, 16, "№"));
            int w = 200;
            block.Fields.Add(DataField(number, x + 12, y, w, Consts.DefaultHeightData));
            used.Add(number.FieldElement.ElementName);
            x += w + 20;
        }

        if (date is not null)
        {
            block.Fields.Add(TextField(x, y, 16, 16, "от"));
            int w = 84;
            block.Fields.Add(DataField(date, x + 20, y, w, Consts.DefaultHeightData));
            MaybeAddPict(block, date, x + 20, y, w, datePict: true);
            used.Add(date.FieldElement.ElementName);
        }

        return y + Consts.DefaultHeightData + 12;
    }

    private static int LayoutSignature(
        Block block,
        List<Data> fields,
        int baseX,
        int y,
        int maxWidth,
        out HashSet<string> used
    )
    {
        used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] order = ["PersonSurname", "PersonName", "PersonMiddleName", "PersonPost", "IssueDate"];
        var found = order
            .Select(s =>
                fields.FirstOrDefault(f =>
                    DcfParser.GetLastToken(f.FieldElement.ElementName).Equals(s, StringComparison.OrdinalIgnoreCase)
                )
            )
            .Where(f => f is not null)
            .Cast<Data>()
            .ToList();

        if (found.Count < 2)
            return y;

        // Group by prefix before last person token
        var groups = found.GroupBy(f =>
        {
            string n = f.FieldElement.ElementName;
            int idx = n.LastIndexOf('_');
            return idx > 0 ? n[..idx] : "";
        });

        foreach (var g in groups)
        {
            int x = baseX;
            foreach (var suffix in order)
            {
                var f = g.FirstOrDefault(i =>
                    DcfParser.GetLastToken(i.FieldElement.ElementName).Equals(suffix, StringComparison.OrdinalIgnoreCase)
                );
                if (f is null)
                    continue;
                int w = suffix switch
                {
                    "PersonSurname" => 196,
                    "PersonName" => 96,
                    "PersonMiddleName" => 104,
                    "PersonPost" => 240,
                    "IssueDate" => 84,
                    _ => 100,
                };
                string label = ExtractComment(f.FieldElement.ElementValue);
                block.Fields.Add(DataField(f, x, y, w, Consts.DefaultHeightData, string.IsNullOrEmpty(label) ? null : label));
                int pictExtra = 0;
                if (suffix == "IssueDate")
                    pictExtra = MaybeAddPict(block, f, x, y, w, datePict: true);
                used.Add(f.FieldElement.ElementName);
                x += w + 4 + pictExtra;
            }

            y += Consts.DefaultHeightData + 16;
        }

        return y;
    }

    private static void AddHorizontalLine(Block block, int y)
    {
        block.Fields.Add(
            new Field(Type.Line, new Size(Consts.DefaultWidthBlock, 4), new Position(0, y), new Value("", ""))
        );
    }

    private static int MaybeAddPict(Block block, Data field, int fieldX, int fieldY, int fieldW, bool datePict = false) =>
        LayoutUtil.MaybeAddPict(block, field, fieldX, fieldY, fieldW, datePict);

    private static Field TextField(int x, int y, int w, int h, string text) =>
        new(Type.Text, new Size(Math.Max(8, w), h), new Position(x, y), new Value("", text));

    private static Field DataField(Data data, int x, int y, int w, int h, string? labelOverride = null)
    {
        string comment = labelOverride ?? string.Empty;
        // when labelOverride is null, keep empty (label already as Text); when provided use it
        if (labelOverride is null)
            comment = string.Empty;
        return new Field(
            Type.Data,
            new Size(Math.Max(16, w), h),
            new Position(x, y),
            new Value(data.FieldElement.ElementName, comment)
        );
    }

    private static int PlaceLabeledData(
        Block block,
        Data f,
        int x,
        int y,
        int maxWidth,
        ConversionOptions options,
        bool putLabelOnData
    )
    {
        int h = HeightFor(f);
        int w = Math.Min(WidthFor(f, maxWidth), maxWidth);
        string comment = ExtractComment(f.FieldElement.ElementValue);
        block.Fields.Add(DataField(f, x, y, w, h, putLabelOnData ? comment : null));
        return y + h + 8;
    }

    private static int WidthFor(Data f, int fallback)
    {
        var t = FieldTypeParser.Parse(f.FieldElement.ElementValue);
        if (!t.IsValid || t.IsBlock)
            return fallback;
        int w = FieldTypeParser.DisplayWidth(t);
        // multi-row text widths in dcf often huge — cap
        return Math.Min(w, fallback > 0 ? Math.Max(fallback, 80) : Consts.DefaultWidthBlock);
    }

    private static int HeightFor(Data f)
    {
        var t = FieldTypeParser.Parse(f.FieldElement.ElementValue);
        bool seq = f.FieldElement.ElementValue.Contains("SEQ=", StringComparison.OrdinalIgnoreCase);
        int rows = Math.Max(t.Rows, seq ? 2 : 1);
        // sizes like 75x2 mean 2 rows of data height
        if (t.Kind == 'M' && t.Rows > 1)
            return Consts.DefaultHeightData * t.Rows;
        return Consts.DefaultHeightData * Math.Max(1, rows);
    }

    private static string ExtractComment(string elementValue)
    {
        int pipe = elementValue.IndexOf('|');
        if (pipe < 0)
            return string.Empty;
        string comment = elementValue[(pipe + 1)..].Trim();
        if (comment.StartsWith('!') || comment.StartsWith('*'))
            comment = comment[1..].Trim();
        int seq = comment.IndexOf("SEQ=", StringComparison.OrdinalIgnoreCase);
        if (seq >= 0)
            comment = comment[..seq].Trim();
        int choice = comment.IndexOf("CHOICE=", StringComparison.OrdinalIgnoreCase);
        if (choice >= 0)
            comment = comment[..choice].Trim();
        int prnt = comment.IndexOf("PRNT=", StringComparison.OrdinalIgnoreCase);
        if (prnt >= 0)
            comment = comment[..prnt].Trim();
        int max = comment.IndexOf("MAX=", StringComparison.OrdinalIgnoreCase);
        if (max >= 0)
            comment = comment[..max].Trim();
        int opt = comment.IndexOf("OPT=", StringComparison.OrdinalIgnoreCase);
        if (opt >= 0)
            comment = comment[..opt].Trim();
        return comment.Trim();
    }

    /// <summary>
    /// Stem of an organization field group, e.g. Seller, ConsignorDetails.
    /// Matches classic Seller_* and compound ConsignorDetails_RFOrganizationFeatures_INN.
    /// </summary>
    private static string? GetOrgStem(string fieldName)
    {
        if (string.IsNullOrEmpty(fieldName))
            return null;

        var parts = fieldName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        int structuralAt = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            if (OrgStructuralSet.Contains(parts[i]))
            {
                structuralAt = i;
                break;
            }
        }

        if (structuralAt <= 0)
            return null;

        return string.Join("_", parts.Take(structuralAt));
    }

    private static string? FindOrgBucketKey(IEnumerable<string> keys, string role) =>
        keys.Where(k =>
                k.Equals(role, StringComparison.OrdinalIgnoreCase)
                || k.StartsWith(role, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(k => k.Length)
            .FirstOrDefault();

    private static string OrgTitle(string org)
    {
        foreach (var (role, title) in new (string Role, string Title)[]
        {
            ("Consignee", "Получатель"),
            ("Consignor", "Отправитель"),
            ("Seller", "Продавец"),
            ("Buyer", "Покупатель"),
            ("Carrier", "Перевозчик"),
            ("Shipper", "Грузоотправитель"),
            ("Declarant", "Декларант"),
            ("Broker", "Брокер"),
            ("Manufacturer", "Производитель"),
            ("Destination", "Место назначения"),
        })
        {
            if (
                org.Equals(role, StringComparison.OrdinalIgnoreCase)
                || org.StartsWith(role, StringComparison.OrdinalIgnoreCase)
            )
                return title;
        }

        return org;
    }

    private static string ShortLabel(string suffix) =>
        suffix.ToUpperInvariant() switch
        {
            "OGRN" => "ОГРН",
            "INN" => "ИНН",
            "KPP" => "КПП",
            "OKPOID" => "ОКПО",
            "OKATOCODE" => "ОКАТО",
            "PHONE" => "Телефон",
            "FAX" => "Факс",
            "TELEX" => "Телекс",
            "E_MAIL" or "MAIL" => "E-mail",
            "BIN" => "БИН",
            "IIN" => "ИИН",
            "UNP" => "УНП",
            "UNN" => "УНН",
            "KGINN" => "ПИН",
            "KGOKPO" => "ОКПО",
            _ => suffix,
        };

    private static string? ShortLabelOrNull(string suffix)
    {
        string s = ShortLabel(suffix);
        return s == suffix ? null : s;
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
