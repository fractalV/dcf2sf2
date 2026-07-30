using DcfToSf2.Parsing;

namespace DcfToSf2.Layout;

internal static class LayoutUtil
{
    public static string GetStem(string elementName)
    {
        int idx = elementName.LastIndexOf('_');
        return idx > 0 ? elementName[..idx] : string.Empty;
    }

    public static string GetLastToken(string elementName) => DcfParser.GetLastToken(elementName);

    public static bool IsVisible(Data f, HashSet<string> ignoreFields) =>
        !DcfParser.ShouldSkipField(f.FieldElement.ElementName, ignoreFields);

    public static bool IsBlockType(Data field)
    {
        var t = FieldTypeParser.Parse(field.FieldElement.ElementValue);
        return t.IsBlock
            || field.FieldElement.ElementValue.TrimStart().StartsWith("B ", StringComparison.OrdinalIgnoreCase)
            || field.FieldElement.ElementValue.Contains("B |");
    }

    public static bool IsBlockOpenMarker(Data f) =>
        string.IsNullOrEmpty(f.FieldElement.ElementValue)
        && !string.IsNullOrEmpty(f.FieldElement.ElementName);

    public static string ExtractComment(string elementValue)
    {
        int pipe = elementValue.IndexOf('|');
        if (pipe < 0)
            return string.Empty;
        string comment = elementValue[(pipe + 1)..].Trim();
        if (comment.StartsWith('!') || comment.StartsWith('*'))
            comment = comment[1..].Trim();
        foreach (var marker in new[] { "SEQ=", "CHOICE=", "PRNT=", "MAX=", "OPT=", "NAMESPACE=", "PATTERN=", "MINVAL=" })
        {
            int i = comment.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i >= 0)
                comment = comment[..i].Trim();
        }

        return comment.Trim();
    }

    public static Field TextField(int x, int y, int w, int h, string text) =>
        new(Type.Text, new Size(Math.Max(8, w), h), new Position(x, y), new Value("", text));

    public static Field DataField(Data data, int x, int y, int w, int h, string? labelOverride = null)
    {
        string comment = labelOverride ?? string.Empty;
        if (labelOverride is null)
            comment = string.Empty;
        return new Field(
            Type.Data,
            new Size(Math.Max(16, w), h),
            new Position(x, y),
            new Value(data.FieldElement.ElementName, comment)
        );
    }

    public const int PictSize = 12;
    public const int PictGap = 2;

    /// <summary>Whether a Data field with this last-token suffix (and optional type string) gets a Pict.</summary>
    public static bool ShouldAddPict(string suffixOrLastToken, string? elementValue = null, bool datePict = false)
    {
        string last = GetLastToken(suffixOrLastToken);
        bool isCode =
            last.EndsWith("Code", StringComparison.OrdinalIgnoreCase)
            || last.Equals("INN", StringComparison.OrdinalIgnoreCase);
        bool isDate =
            datePict
            || last.EndsWith("Date", StringComparison.OrdinalIgnoreCase)
            || (
                !string.IsNullOrEmpty(elementValue)
                && elementValue.TrimStart().StartsWith("D", StringComparison.OrdinalIgnoreCase)
            );
        return isCode || isDate;
    }

    /// <summary>
    /// Places Pict immediately to the right of the Data field (no overlap into the field).
    /// Returns horizontal space reserved after the field (0 if no pict).
    /// </summary>
    public static int MaybeAddPict(
        Block block,
        Data field,
        int fieldX,
        int fieldY,
        int fieldW,
        bool datePict = false
    )
    {
        string name = field.FieldElement.ElementName;
        if (!ShouldAddPict(name, field.FieldElement.ElementValue, datePict))
            return 0;

        string last = GetLastToken(name);
        bool isDate =
            datePict
            || last.EndsWith("Date", StringComparison.OrdinalIgnoreCase)
            || field.FieldElement.ElementValue.TrimStart().StartsWith("D", StringComparison.OrdinalIgnoreCase);

        string pictValue = isDate ? $"{name}=DATE" : name;
        int pictX = fieldX + Math.Max(fieldW, 16) + PictGap;
        int pictY = fieldY + Math.Max(0, (Math.Min(Consts.DefaultHeightData, 16) - PictSize) / 2);
        block.Fields.Add(
            new Field(
                Type.Pict,
                new Size(PictSize, PictSize),
                new Position(Math.Max(0, pictX), pictY),
                new Value("", pictValue)
            )
        );
        return PictSize + PictGap;
    }

    public static int WidthFor(Data f, int fallback)
    {
        var t = FieldTypeParser.Parse(f.FieldElement.ElementValue);
        if (!t.IsValid || t.IsBlock)
            return fallback;
        int w = FieldTypeParser.DisplayWidth(t);
        return Math.Min(w, fallback > 0 ? Math.Max(fallback, 80) : Consts.DefaultWidthBlock);
    }

    public static int HeightFor(Data f)
    {
        var t = FieldTypeParser.Parse(f.FieldElement.ElementValue);
        bool seq = f.FieldElement.ElementValue.Contains("SEQ=", StringComparison.OrdinalIgnoreCase);
        int rows = Math.Max(t.Rows, seq ? 2 : 1);
        if (t.Kind == 'M' && t.Rows > 1)
            return Consts.DefaultHeightData * t.Rows;
        return Consts.DefaultHeightData * Math.Max(1, rows);
    }

    public static int LayoutFlowRow(Block block, List<Data> fields, int baseX, int y, int maxWidth)
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
            string last = GetLastToken(f.FieldElement.ElementName);
            string? shortL = ShortLabelOrNull(last);
            block.Fields.Add(DataField(f, x, y, w, HeightFor(f), shortL ?? (label.Length > 40 ? null : label)));
            int pictExtra = MaybeAddPict(block, f, x, y, w);
            x += w + 4 + pictExtra;
        }

        return y + Consts.DefaultHeightData + 8;
    }

    public static Data? FindBySuffix(IEnumerable<Data> fields, string suffix) =>
        fields.FirstOrDefault(f =>
            GetLastToken(f.FieldElement.ElementName).Equals(suffix, StringComparison.OrdinalIgnoreCase)
        );

    public static bool StemContains(string stem, string token) =>
        stem.Equals(token, StringComparison.OrdinalIgnoreCase)
        || stem.EndsWith("_" + token, StringComparison.OrdinalIgnoreCase)
        || stem.Contains("_" + token + "_", StringComparison.OrdinalIgnoreCase);

    public static string ShortLabel(string suffix) =>
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

    public static string? ShortLabelOrNull(string suffix)
    {
        string s = ShortLabel(suffix);
        return s == suffix ? null : s;
    }

    public static int PlaceCompactRow(
        Block block,
        IReadOnlyList<(Data? Field, int Width)> slots,
        int baseX,
        int y,
        int maxWidth,
        bool addPict = true
    )
    {
        int x = baseX;
        bool any = false;
        foreach (var (field, width) in slots)
        {
            if (field is null)
                continue;
            any = true;
            int w = Math.Min(width, Math.Max(16, baseX + maxWidth - x));
            if (w < 16)
                break;
            string label = ExtractComment(field.FieldElement.ElementValue);
            block.Fields.Add(DataField(field, x, y, w, Consts.DefaultHeightData, string.IsNullOrEmpty(label) ? null : label));
            int pictExtra = 0;
            if (addPict)
                pictExtra = MaybeAddPict(block, field, x, y, w);
            x += w + 4 + pictExtra;
        }

        return any ? y + Consts.DefaultHeightData + 4 : y;
    }
}
