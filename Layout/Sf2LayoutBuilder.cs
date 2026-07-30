using DcfToSf2.Parsing;

namespace DcfToSf2.Layout;

internal static class Sf2LayoutBuilder
{
    private static readonly string[] OrgPrefixes =
    [
        "Seller",
        "Buyer",
        "Consignor",
        "Consignee",
        "Carrier",
        "Shipper",
        "Declarant",
        "Broker",
        "Manufacturer",
        "Destination",
    ];

    private static readonly (string Left, string Right)[] OrgPairs =
    [
        ("Seller", "Buyer"),
        ("Consignor", "Consignee"),
    ];

    private static readonly string[] AddressSuffixes =
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

    private static readonly string[] ContactSuffixes = ["Phone", "Fax", "Telex", "E_mail", "mail"];

    private static readonly string[] OrgRequisiteSuffixes =
    [
        "OrganizationName",
        "ShortName",
        "OrganizationLanguage",
        "OGRN",
        "INN",
        "KPP",
        "OKPOID",
        "OKATOCode",
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

        // Split: org groups vs other fields vs nested hyperlinks
        var orgBuckets = new Dictionary<string, List<Data>>(StringComparer.OrdinalIgnoreCase);
        var other = new List<Data>();
        var hyperlinks = new List<Data>();

        foreach (var f in fields)
        {
            if (IsBlockOpenMarker(f))
            {
                hyperlinks.Add(f);
                continue;
            }

            string? org = GetOrgPrefix(f.FieldElement.ElementName);
            if (org is not null)
            {
                if (!orgBuckets.TryGetValue(org, out var list))
                {
                    list = [];
                    orgBuckets[org] = list;
                }

                list.Add(f);
            }
            else if (IsBlockType(f))
            {
                // section header text
                string c = ExtractComment(f.FieldElement.ElementValue);
                if (!string.IsNullOrEmpty(c))
                {
                    block.Fields.Add(
                        TextField(
                            Consts.Default_X,
                            y,
                            Math.Min(c.Length * Consts.DefaultWidthChar, Consts.DefaultWidthBlock),
                            Consts.DefaultHeightText + 4,
                            c
                        )
                    );
                    y += Consts.DefaultHeightText + 8;
                }
            }
            else
            {
                other.Add(f);
            }
        }

        // Non-org fields first (document header area)
        y = LayoutGenericFields(block, other, Consts.Default_X, y, Consts.DefaultWidthBlock - 8, options);

        // Orgs
        var remainingOrgs = orgBuckets.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (options.TwoColumn)
        {
            foreach (var (left, right) in OrgPairs)
            {
                if (!orgBuckets.ContainsKey(left) && !orgBuckets.ContainsKey(right))
                    continue;

                y = LayoutOrgPair(
                    block,
                    orgBuckets.GetValueOrDefault(left) ?? [],
                    orgBuckets.GetValueOrDefault(right) ?? [],
                    left,
                    right,
                    y,
                    options
                );
                remainingOrgs.Remove(left);
                remainingOrgs.Remove(right);
                AddHorizontalLine(block, y);
                y += 8;
            }
        }

        foreach (var org in remainingOrgs.OrderBy(o => o))
        {
            y = LayoutOrgSingle(block, orgBuckets[org], org, y, options);
            AddHorizontalLine(block, y);
            y += 8;
        }

        // Hyperlinks to nested blocks
        foreach (var h in hyperlinks)
        {
            string label = FirstNonEmpty(h.RusNameBlock, h.FieldElement.ElementName);
            string comment = $"@{h.FieldElement.ElementName}@{label}";
            block.Fields.Add(
                TextField(
                    Consts.Default_X,
                    y,
                    Math.Min(comment.Length * Consts.DefaultWidthChar, Consts.DefaultWidthBlock),
                    Consts.DefaultHeightText + 4,
                    comment.Replace("@@", "@").Replace($"@{Consts.SpecialDelimetr}", "@")
                )
            );
            // Fix: hyperlink format in SF2 is @Block@Title without backtick between
            block.Fields[^1] = new Field(
                Type.Text,
                new Size(Math.Min((label.Length + h.FieldElement.ElementName.Length + 2) * Consts.DefaultWidthChar, 400), Consts.DefaultHeightText + 4),
                new Position(Consts.Default_X, y),
                new Value("", $"@{h.FieldElement.ElementName}@{label}")
            );
            y += Consts.DefaultHeightData + 4;
        }

        return block;
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
        y = LayoutGenericFields(block, content.Where(f => !IsBlockOpenMarker(f)).ToList(), Consts.Default_X, y, Consts.DefaultWidthBlock - 8, options);

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

    private static int LayoutOrgSingle(
        Block block,
        List<Data> fields,
        string orgName,
        int startY,
        ConversionOptions options
    )
    {
        int y = startY;
        block.Fields.Add(
            TextField(Consts.Default_X, y, orgName.Length * Consts.DefaultWidthChar + 20, 16, OrgTitle(orgName))
        );
        y += 20;

        return LayoutOrgFields(block, fields, Consts.Default_X, y, Consts.DefaultWidthBlock - 12, options);
    }

    private static int LayoutOrgPair(
        Block block,
        List<Data> leftFields,
        List<Data> rightFields,
        string leftName,
        string rightName,
        int startY,
        ConversionOptions options
    )
    {
        int y = startY;
        block.Fields.Add(TextField(Consts.Default_X, y, 80, 20, OrgTitle(leftName)));
        block.Fields.Add(TextField(Consts.RightColumnX, y, 80, 20, OrgTitle(rightName)));
        y += 24;

        // Vertical separator
        block.Fields.Add(
            new Field(
                Type.Line,
                new Size(4, 200),
                new Position(Consts.RightColumnX - 4, y - 4),
                new Value("", "")
            )
        );

        int yLeft = LayoutOrgFields(block, leftFields, Consts.Default_X, y, Consts.ColumnWidth, options);
        int yRight = LayoutOrgFields(block, rightFields, Consts.RightColumnX, y, Consts.ColumnWidth, options);
        return Math.Max(yLeft, yRight);
    }

    private static int LayoutOrgFields(
        Block block,
        List<Data> fields,
        int baseX,
        int startY,
        int maxWidth,
        ConversionOptions options
    )
    {
        int y = startY;
        var bySuffix = fields.ToLookup(f => DcfParser.GetLastToken(f.FieldElement.ElementName), StringComparer.OrdinalIgnoreCase);

        // OrganizationName
        foreach (var f in fields.Where(f => DcfParser.GetLastToken(f.FieldElement.ElementName).Equals("OrganizationName", StringComparison.OrdinalIgnoreCase)))
        {
            y = PlaceLabeledData(block, f, baseX, y, maxWidth, options, putLabelOnData: true);
        }

        // ShortName + requisites row
        var shortName = fields.FirstOrDefault(f =>
            DcfParser.GetLastToken(f.FieldElement.ElementName).Equals("ShortName", StringComparison.OrdinalIgnoreCase)
        );
        var requisites = fields
            .Where(f =>
            {
                string s = DcfParser.GetLastToken(f.FieldElement.ElementName);
                return s is "OGRN" or "INN" or "KPP" or "OKPOID" or "OKATOCode";
            })
            .ToList();

        if (shortName is not null || requisites.Count > 0)
        {
            int x = baseX;
            if (shortName is not null && !DcfParser.ShouldSkipField(shortName.FieldElement.ElementName, options.IgnoreFields))
            {
                int w = Math.Min(240, maxWidth / 2);
                block.Fields.Add(DataField(shortName, x, y, w, Consts.DefaultHeightData));
                MaybeAddPict(block, shortName, x + w - 16, y);
                x += w + 4;
            }

            foreach (var r in requisites)
            {
                if (DcfParser.ShouldSkipField(r.FieldElement.ElementName, options.IgnoreFields))
                    continue;
                string label = ShortLabel(DcfParser.GetLastToken(r.FieldElement.ElementName));
                block.Fields.Add(TextField(x, y - 12, label.Length * Consts.DefaultWidthChar, Consts.DefaultHeightText, label));
                int w = WidthFor(r, 100);
                block.Fields.Add(DataField(r, x, y, w, Consts.DefaultHeightData, labelOverride: null));
                MaybeAddPict(block, r, x + w - 4, y - 4);
                x += w + 4;
                if (x > baseX + maxWidth - 40)
                {
                    x = baseX;
                    y += Consts.DefaultHeightData + 16;
                }
            }

            y += Consts.DefaultHeightData + 8;
        }

        // Contacts row with ignore-fields compaction
        var contacts = ContactSuffixes
            .Select(s => fields.FirstOrDefault(f => DcfParser.GetLastToken(f.FieldElement.ElementName).Equals(s, StringComparison.OrdinalIgnoreCase)))
            .Where(f => f is not null && !DcfParser.ShouldSkipField(f!.FieldElement.ElementName, options.IgnoreFields))
            .Cast<Data>()
            .ToList();

        if (contacts.Count > 0)
        {
            int x = baseX;
            int slot = Math.Max(80, (maxWidth - 8) / Math.Max(1, contacts.Count));
            foreach (var c in contacts)
            {
                string label = ExtractComment(c.FieldElement.ElementValue);
                if (string.IsNullOrEmpty(label))
                    label = ShortLabel(DcfParser.GetLastToken(c.FieldElement.ElementName));
                int w = Math.Min(slot - 4, WidthFor(c, slot - 4));
                block.Fields.Add(DataField(c, x, y, w, Consts.DefaultHeightData, label));
                x += w + 4;
            }

            y += Consts.DefaultHeightData + 8;
        }

        // Address fields compacted
        var addressFields = fields
            .Where(f =>
            {
                string s = DcfParser.GetLastToken(f.FieldElement.ElementName);
                return AddressSuffixes.Contains(s, StringComparer.OrdinalIgnoreCase)
                    && !DcfParser.ShouldSkipField(f.FieldElement.ElementName, options.IgnoreFields);
            })
            .ToList();

        if (addressFields.Count > 0)
            y = LayoutAddressRow(block, addressFields, baseX, y, maxWidth);

        // Remaining org fields (EAEU features etc.)
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields)
        {
            string last = DcfParser.GetLastToken(f.FieldElement.ElementName);
            if (
                last is "OrganizationName" or "ShortName" or "OGRN" or "INN" or "KPP" or "OKPOID" or "OKATOCode"
                || ContactSuffixes.Contains(last, StringComparer.OrdinalIgnoreCase)
                || AddressSuffixes.Contains(last, StringComparer.OrdinalIgnoreCase)
                || last.Equals("OrganizationLanguage", StringComparison.OrdinalIgnoreCase)
            )
            {
                placed.Add(f.FieldElement.ElementName);
            }
        }

        var rest = fields
            .Where(f =>
                !placed.Contains(f.FieldElement.ElementName)
                && !IsBlockType(f)
                && !DcfParser.ShouldSkipField(f.FieldElement.ElementName, options.IgnoreFields)
            )
            .ToList();

        if (rest.Count > 0)
            y = LayoutFlowRow(block, rest, baseX, y, maxWidth);

        return y;
    }

    private static int LayoutAddressRow(Block block, List<Data> fields, int baseX, int y, int maxWidth)
    {
        // Row1: CountryCode, CounryName, PostalCode
        // Row2: Region, City
        // Row3: StreetHouse
        int x = baseX;
        void Place(Data? f, int w)
        {
            if (f is null)
                return;
            string label = ExtractComment(f.FieldElement.ElementValue);
            block.Fields.Add(DataField(f, x, y, w, Consts.DefaultHeightData, string.IsNullOrEmpty(label) ? null : label));
            x += w + 4;
        }

        Data? Find(string suffix) =>
            fields.FirstOrDefault(f =>
                DcfParser.GetLastToken(f.FieldElement.ElementName).Equals(suffix, StringComparison.OrdinalIgnoreCase)
            );

        Place(Find("CountryCode"), 32);
        Place(Find("CounryName"), Math.Min(252, maxWidth / 2));
        Place(Find("PostalCode"), 92);
        y += Consts.DefaultHeightData + 4;
        x = baseX;
        Place(Find("Region"), Math.Min(220, maxWidth / 2));
        Place(Find("City"), Math.Min(160, maxWidth / 2));
        y += Consts.DefaultHeightData + 4;
        x = baseX;
        Place(Find("StreetHouse"), maxWidth - 8);
        y += Consts.DefaultHeightData + 4;

        // any other address leftovers
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
            .Where(f => !known.Contains(DcfParser.GetLastToken(f.FieldElement.ElementName)))
            .ToList();
        if (extra.Count > 0)
            y = LayoutFlowRow(block, extra, baseX, y, maxWidth);

        return y;
    }

    private static int LayoutFlowRow(Block block, List<Data> fields, int baseX, int y, int maxWidth)
    {
        int x = baseX;
        foreach (var f in fields)
        {
            int w = Math.Min(WidthFor(f, 120), maxWidth);
            if (x + w > baseX + maxWidth)
            {
                x = baseX;
                y += Consts.DefaultHeightData + 4;
            }

            string label = ExtractComment(f.FieldElement.ElementValue);
            // Prefer short labels for EAEU codes
            string last = DcfParser.GetLastToken(f.FieldElement.ElementName);
            string? shortL = ShortLabelOrNull(last);
            block.Fields.Add(DataField(f, x, y, w, HeightFor(f), shortL ?? (label.Length > 40 ? null : label)));
            MaybeAddPict(block, f, x + w - 4, y);
            x += w + 4;
        }

        return y + Consts.DefaultHeightData + 8;
    }

    private static int LayoutGenericFields(
        Block block,
        List<Data> fields,
        int baseX,
        int startY,
        int maxWidth,
        ConversionOptions options
    )
    {
        int y = startY;
        int x = baseX;

        // Group known templates: PrDocument*, Loading/Unloading, signature, DT number
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
                block.Fields.Add(DataField(f, lx, rowY, w, Consts.DefaultHeightData, ExtractComment(f.FieldElement.ElementValue)));
                lx += w + 8;
            }

            int rx = baseX + maxWidth / 2;
            foreach (var f in unloading)
            {
                int w = WidthFor(f, 120);
                block.Fields.Add(DataField(f, rx, rowY, w, Consts.DefaultHeightData, ExtractComment(f.FieldElement.ElementValue)));
                rx += w + 8;
            }

            y += Consts.DefaultHeightData + 20;
        }

        // Document triad PrDocumentName/Number/Date
        y = LayoutDocumentTriad(block, remaining, baseX, y, maxWidth, out var usedDoc);
        remaining = remaining.Where(f => !usedDoc.Contains(f.FieldElement.ElementName)).ToList();

        // Signature person row
        y = LayoutSignature(block, remaining, baseX, y, maxWidth, out var usedSig);
        remaining = remaining.Where(f => !usedSig.Contains(f.FieldElement.ElementName)).ToList();

        // Default stacked / flow for the rest
        foreach (var f in remaining)
        {
            string last = DcfParser.GetLastToken(f.FieldElement.ElementName);
            bool seq = f.FieldElement.ElementValue.Contains("SEQ=", StringComparison.OrdinalIgnoreCase);
            int h = HeightFor(f);
            int w = Math.Min(WidthFor(f, maxWidth), maxWidth);
            string comment = ExtractComment(f.FieldElement.ElementValue);

            if (!string.IsNullOrEmpty(comment) && comment.Length < 60)
            {
                block.Fields.Add(TextField(baseX, y, Math.Min(comment.Length * Consts.DefaultWidthChar, maxWidth), Consts.DefaultHeightText, comment));
                y += Consts.DefaultHeightText + 2;
                block.Fields.Add(DataField(f, baseX, y, w, h));
            }
            else
            {
                block.Fields.Add(DataField(f, baseX, y, w, h, comment.Length > 0 && comment.Length < 80 ? comment : null));
            }

            MaybeAddPict(block, f, baseX + w - 4, y);
            y += h + (seq ? 4 : 8);
        }

        return y;
    }

    private static int LayoutDocumentTriad(
        Block block,
        List<Data> fields,
        int baseX,
        int y,
        int maxWidth,
        out HashSet<string> used
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
            block.Fields.Add(DataField(name, x, y, w, Consts.DefaultHeightData, ExtractComment(name.FieldElement.ElementValue)));
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
            MaybeAddPict(block, date, x + 20 + w, y, datePict: true);
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
                if (suffix == "IssueDate")
                    MaybeAddPict(block, f, x + w, y, datePict: true);
                used.Add(f.FieldElement.ElementName);
                x += w + 4;
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

    private static void MaybeAddPict(Block block, Data field, int x, int y, bool datePict = false)
    {
        string name = field.FieldElement.ElementName;
        string last = DcfParser.GetLastToken(name);
        bool isCode = last.EndsWith("Code", StringComparison.OrdinalIgnoreCase) || last.Equals("INN", StringComparison.OrdinalIgnoreCase);
        bool isDate =
            datePict
            || last.EndsWith("Date", StringComparison.OrdinalIgnoreCase)
            || field.FieldElement.ElementValue.TrimStart().StartsWith("D", StringComparison.OrdinalIgnoreCase);

        if (!isCode && !isDate)
            return;

        string pictValue = isDate ? $"{name}=DATE" : name;
        block.Fields.Add(
            new Field(Type.Pict, new Size(12, 12), new Position(Math.Max(0, x), y), new Value("", pictValue))
        );
    }

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

    private static string? GetOrgPrefix(string fieldName)
    {
        foreach (var p in OrgPrefixes.OrderByDescending(x => x.Length))
        {
            if (
                fieldName.Equals(p, StringComparison.OrdinalIgnoreCase)
                || fieldName.StartsWith(p + "_", StringComparison.OrdinalIgnoreCase)
            )
                return p;
        }

        return null;
    }

    private static string OrgTitle(string org) =>
        org.ToLowerInvariant() switch
        {
            "seller" => "Продавец",
            "buyer" => "Покупатель",
            "consignor" => "Отправитель",
            "consignee" => "Получатель",
            "carrier" => "Перевозчик",
            "shipper" => "Грузоотправитель",
            "declarant" => "Декларант",
            "broker" => "Брокер",
            "manufacturer" => "Производитель",
            "destination" => "Место назначения",
            _ => org,
        };

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
