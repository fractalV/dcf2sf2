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

    public static int LayoutFlowRow(
        Block block,
        List<Data> fields,
        int baseX,
        int y,
        int maxWidth,
        bool includeFieldComments = true
    )
    {
        if (fields.Count == 0)
            return y;

        int x = baseX;
        int rowH = Consts.DefaultHeightData;
        const int gap = 4;
        int right = baseX + Math.Max(maxWidth, 16);

        foreach (var f in fields)
        {
            int h = HeightFor(f);
            int naturalW = NaturalWidth(f);
            bool wantsPict = ShouldAddPict(f.FieldElement.ElementName, f.FieldElement.ElementValue);
            int pictReserve = wantsPict ? PictSize + PictGap : 0;
            int maxFieldW = Math.Max(16, right - baseX - pictReserve);

            if (x > baseX && x + Math.Min(naturalW, maxFieldW) + pictReserve + gap > right)
            {
                x = baseX;
                y += rowH + gap;
                rowH = Consts.DefaultHeightData;
            }

            int remain = Math.Max(16, right - x - pictReserve);
            int w = Math.Min(naturalW, remain);

            string? dataLabel = null;
            if (includeFieldComments)
            {
                string label = ExtractComment(f.FieldElement.ElementValue);
                string last = GetLastToken(f.FieldElement.ElementName);
                string? shortL = ShortLabelOrNull(last);
                dataLabel = shortL ?? (label.Length > 0 && label.Length < 80 ? label : null);
            }

            block.Fields.Add(DataField(f, x, y, w, h, dataLabel));
            int pictExtra = MaybeAddPict(block, f, x, y, w);
            x += w + gap + pictExtra;
            rowH = Math.Max(rowH, h);
        }

        return y + rowH + 8;
    }

    public static int NaturalWidth(Data f)
    {
        var t = FieldTypeParser.Parse(f.FieldElement.ElementValue);
        if (!t.IsValid || t.IsBlock)
            return 80;
        return Math.Clamp(FieldTypeParser.DisplayWidth(t), 16, Consts.DefaultWidthBlock);
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

    public static string DefaultFieldComment(string suffix, string? group = null)
    {
        string g = group ?? string.Empty;
        if (suffix.Equals("OrganizationName", StringComparison.OrdinalIgnoreCase))
        {
            if (g.Equals("IdentityCard", StringComparison.OrdinalIgnoreCase))
                return "Наименование организации, выдавшей документ";
            return "Наименование организации";
        }

        if (suffix.Equals("CountryCode", StringComparison.OrdinalIgnoreCase)
            && g.Equals("IdentityCard", StringComparison.OrdinalIgnoreCase))
            return "Код страны выдачи документа, удостоверяющего личность";

        return suffix.ToUpperInvariant() switch
        {
            "COUNTRYCODE" => "Буквенный код страны в соответствии с классификатором стран мира",
            "COUNRYNAME" => "Краткое название страны в соответствии с классификатором стран мира",
            "POSTALCODE" => "Почтовый индекс",
            "REGION" => "Регион (Наименование единицы административно-территориального деления первого уровня)",
            "CITY" => "Населенный пункт",
            "STREETHOUSE" => "Улица (Наименование элемента улично-дорожной сети городской инфраструктуры)",
            "ADDRESSKINDCODE" =>
                "Кодовое обозначение вида адреса: \"1\" - адрес регистрации; \"2\" - фактический адрес; \"3\" - почтовый адрес",
            "DISTRICT" => "Район (Наименование единицы административно-территориального деления второго уровня)",
            "TOWN" => "Город",
            "HOUSE" => "Обозначение дома, корпуса, строения",
            "ROOM" => "Обозначение офиса или квартиры",
            "ADDRESSTEXT" => "Набор элементов адреса, представленных в свободной форме в виде текста.",
            "POSTOFFICEBOXID" => "Номер абонентского ящика",
            "SHORTNAME" => "Краткое наименование организации",
            "OGRN" => "ОГРН/ОГРНИП. Основной государственный регистрационный номер",
            "INN" => "ИНН - Индивидуальный номер налогоплательщика",
            "KPP" => "КПП - Код причины постановки на учет",
            "OKPOID" => "ОКПО",
            "OKATOCODE" => "ОКАТО",
            "PHONE" => "Номер телефона",
            "FAX" => "Номер факса",
            "TELEX" => "Номер телекса",
            "E_MAIL" or "MAIL" => "Адрес электронной почты",
            "BIN" => "БИН",
            "IIN" => "ИИН",
            "UNP" => "УНП",
            "UNN" => "УНН",
            "KGINN" => "ПИН",
            "KGOKPO" => "ОКПО",
            "IDENTITYCARDCODE" =>
                "Код вида документа, удостоверяющего личность, в соответствии с классификатором видов документов, удостоверяющих личность",
            "IDENTITYCARDNAME" => "Краткое наименование документа, удостоверяющего личность",
            "FULLIDENTITYCARDNAME" => "Полное наименование документа, удостоверяющего личность",
            "IDENTITYCARDSERIES" => "Серия документа, удостоверяющего личность",
            "IDENTITYCARDNUMBER" => "Номер документа, удостоверяющего личность",
            "IDENTITYCARDDATE" => "Дата выдачи документа, удостоверяющего личность",
            "DOCVALIDITYDATE" => "Дата окончания срока, в течение которого документ имеет силу",
            "ISSUERCODE" => "Код подразделения органа, выдавшего документ, удостоверяющий личность",
            "AUTHORITYID" =>
                "Строка, идентифицирующая орган государственной власти государства-члена либо уполномоченную им организацию, выдавшую документ",
            "CUSTOMSCODE" => "Код таможенного органа, зарегистрировавшего документ",
            "REGISTRATIONDATE" => "Дата регистрации документа",
            "GTDNUMBER" => "Порядковый номер документа по журналу регистрации",
            "CODE" => "Номер (идентификатор) вносимых изменений и (или) дополнений",
            "TRANSPORTMODECODE" =>
                "Код вида транспортного средства по классификатору видов транспорта и транспортировки товаров",
            "PRECEDINGDOCUMENTMODECODE" => "Кодовое обозначение вида документа",
            "PRECEDINGDOCUMENTNAME" => "Наименование предшествующего документа",
            "PRECEDINGDOCUMENTGOODSNUMERIC" => "Порядковый номер товара в предшествующем документе",
            "PRECEDINGDOCUMENTNUM" => "Номер предшествующего документа",
            "OTHERPRDOCUMENTNUMBER" => "Номер иного предшествующего документа",
            "OTHERPRDOCUMENTDATE" => "Дата иного предшествующего документа",
            "GOODSQUANTITY" => "Количество товара в единице измерения",
            "MEASUREUNITQUALIFIERNAME" => "Условное обозначение единицы измерения",
            "MEASUREUNITQUALIFIERCODE" =>
                "Код единицы измерения в соответствии с единицами измерения, применяемыми в ТН ВЭД ЕАЭС",
            "TIRID" => "Номер книжки МДП",
            "TIRSERIES" => "Серия книжки МДП",
            _ => ShortLabelOrNull(suffix) ?? string.Empty,
        };
    }

    public static string? ShortLabelOrNull(string suffix)
    {
        string s = ShortLabel(suffix);
        return s == suffix ? null : s;
    }

    public static int AdvanceAfterData(int fieldWidth, string suffixOrLastToken, int fieldGap = 4) =>
        Math.Max(fieldWidth, 16)
        + fieldGap
        + (ShouldAddPict(suffixOrLastToken) ? PictSize + PictGap : 0);

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
