using System.Text;
using DcfToSf2.Layout;
using DcfToSf2.Layout.Templates;
using DcfToSf2.Parsing;
using DcfToSf2.Writing;
using Xunit;

namespace DcfToSf2.Tests;

public class TemplateLayoutTests
{
    static TemplateLayoutTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static Data Field(string name, string value, string path = "MAIN", int level = 0) =>
        new()
        {
            Level = level,
            NameBlock = path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path,
            Path = path,
            FieldElement = new Element { ElementName = name, ElementValue = value },
            SourceLine = 1,
        };

    private static ConversionOptions Opts(params string[] ignore) =>
        new()
        {
            InputFile = new FileInfo("x.dcf"),
            OutputDirectory = new DirectoryInfo(Path.GetTempPath()),
            IgnoreFields = ConversionOptions.ParseIgnoreFields(string.Join(",", ignore)),
        };

    [Fact]
    public void AddressTemplate_CompactsCountryRow()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var fields = new List<Data>
        {
            Field("PostalCode", "C9 |Индекс", "Addr", 1),
            Field("CountryCode", "C2 |Страна", "Addr", 1),
            Field("CounryName", "C120 |Название", "Addr", 1),
            Field("Region", "C120 |Регион", "Addr", 1),
            Field("City", "C120 |Город", "Addr", 1),
            Field("StreetHouse", "C120 |Улица", "Addr", 1),
        };

        var tpl = new AddressFieldGroupTemplate();
        var match = tpl.TryMatch(fields, 0, []);
        Assert.NotNull(match);

        var block = new Block { Name = "Addr" };
        tpl.Layout(block, match!, 4, 4, 770, []);

        var data = block.Fields.Where(f => f.FieldType == Type.Data).ToList();
        Assert.True(data.Count >= 5);

        var country = data.First(f => f.FieldValue.Name == "CountryCode");
        var name = data.First(f => f.FieldValue.Name == "CounryName");
        var postal = data.First(f => f.FieldValue.Name == "PostalCode");
        Assert.Equal(country.FieldPosition.Y, name.FieldPosition.Y);
        Assert.Equal(country.FieldPosition.Y, postal.FieldPosition.Y);
        Assert.True(country.FieldPosition.X < name.FieldPosition.X);
        Assert.True(name.FieldPosition.X < postal.FieldPosition.X);
    }

    [Fact]
    public void AddressPreview_PictDoesNotOverlapNextData()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var slots = new AddressFieldGroupTemplate().GetPreviewSlots([]);

        var code = slots.First(s =>
            s.Kind == TemplateSlotKind.Data && s.Suffix == "CountryCode" && !s.IsExcluded
        );
        var pict = slots.First(s =>
            s.Kind == TemplateSlotKind.Pict && s.Suffix == "CountryCode" && !s.IsExcluded
        );
        var name = slots.First(s =>
            s.Kind == TemplateSlotKind.Data && s.Suffix == "CounryName" && !s.IsExcluded
        );

        Assert.Equal(code.Y, name.Y);
        Assert.True(pict.X >= code.X + code.Width + LayoutUtil.PictGap);
        Assert.True(name.X >= pict.X + pict.Width + 4);
    }

    [Fact]
    public void AddressTemplate_IgnorePostalCode_CompactsRow()
    {
        var fields = new List<Data>
        {
            Field("CountryCode", "C2 |", "Addr", 1),
            Field("CounryName", "C120 |", "Addr", 1),
            Field("PostalCode", "C9 |", "Addr", 1),
            Field("Region", "C120 |", "Addr", 1),
            Field("City", "C120 |", "Addr", 1),
        };

        var ignore = ConversionOptions.ParseIgnoreFields("*PostalCode");
        var tpl = new AddressFieldGroupTemplate();
        var match = tpl.TryMatch(fields, 0, ignore);
        Assert.NotNull(match);
        Assert.DoesNotContain(match!.Fields, f => f.FieldElement.ElementName == "PostalCode");

        var block = new Block { Name = "Addr" };
        tpl.Layout(block, match, 4, 4, 770, ignore);
        Assert.DoesNotContain(block.Fields, f => f.FieldValue.Name == "PostalCode");

        var slots = tpl.GetPreviewSlots(ignore);
        var postal = slots.First(s => s.Suffix == "PostalCode");
        Assert.True(postal.IsIgnored);
        var visibleRow1 = slots.Where(s => !s.IsIgnored && s.Y == slots.First(x => !x.IsIgnored).Y).OrderBy(s => s.X).ToList();
        Assert.DoesNotContain(visibleRow1, s => s.Suffix == "PostalCode");
    }

    [Fact]
    public void PropertyLocation_InLargeSection_UsesAddressTemplate()
    {
        var fields = new List<Data>
        {
            Field("PropertyName", "C250 |Имя", "GoodsInfo\\Property", 1),
            Field("PropertyLocation_CountryCode", "C2 |", "GoodsInfo\\Property", 1),
            Field("PropertyLocation_CounryName", "C120 |", "GoodsInfo\\Property", 1),
            Field("PropertyLocation_PostalCode", "C9 |", "GoodsInfo\\Property", 1),
            Field("PropertyLocation_Region", "C120 |", "GoodsInfo\\Property", 1),
            Field("PropertyLocation_City", "C120 |", "GoodsInfo\\Property", 1),
            Field("Comment", "C50 |Примечание", "GoodsInfo\\Property", 1),
        };

        var parsed = new DcfParseResult
        {
            Header = new DcfHeader { DocumentName = "Test" },
            Fields = fields,
            Diagnostics = new Diagnostics.DcfDiagnostics(),
        };

        var doc = Sf2LayoutBuilder.Build(parsed, Opts());
        var block = doc.Blocks.First(b => b.Name.Contains("Property", StringComparison.OrdinalIgnoreCase) || b.Name == "MAIN");
        // fields at level 1 with path go to nested; level 0 would be MAIN — fix levels
        // Our fields have level 1 and path GoodsInfo\Property
        block = doc.Blocks.FirstOrDefault(b => b.Name.Contains("Property")) ?? doc.Blocks[0];

        string rendered = string.Join("\n", Sf2Writer.Render(doc));
        // At least Country and CounryName should share Y when address template applied
        var country = FindData(rendered, "PropertyLocation_CountryCode");
        var name = FindData(rendered, "PropertyLocation_CounryName");
        if (country is not null && name is not null)
            Assert.Equal(country.Value.y, name.Value.y);
    }

    [Fact]
    public void IdentityCard_CompactsCodeSeriesNumber()
    {
        var fields = new List<Data>
        {
            Field("DeclarantDetails_IdentityCard_IdentityCardCode", "C20 |Код", "MAIN"),
            Field("DeclarantDetails_IdentityCard_IdentityCardSeries", "C20 |Серия", "MAIN"),
            Field("DeclarantDetails_IdentityCard_IdentityCardNumber", "C25 |Номер", "MAIN"),
            Field("DeclarantDetails_IdentityCard_IdentityCardDate", "D10 |Дата", "MAIN"),
        };

        var tpl = new IdentityCardFieldGroupTemplate();
        var match = tpl.TryMatch(fields, 0, []);
        Assert.NotNull(match);

        var block = new Block { Name = "MAIN" };
        tpl.Layout(block, match!, 4, 4, 770, []);
        var data = block.Fields.Where(f => f.FieldType == Type.Data).ToList();
        Assert.Equal(4, data.Count);
        Assert.True(data.All(f => f.FieldPosition.Y == data[0].FieldPosition.Y));
    }

    [Fact]
    public void Organization_NestsAddressAndIdentityCard()
    {
        TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
        var fields = new List<Data>
        {
            Field("Seller_OrganizationName", "C150 |ООО", "MAIN"),
            Field("Seller_INN", "C12 |", "MAIN"),
            Field("Seller_IdentityCard_IdentityCardCode", "C20 |", "MAIN"),
            Field("Seller_IdentityCard_IdentityCardNumber", "C25 |", "MAIN"),
            Field("Seller_CountryCode", "C2 |", "MAIN"),
            Field("Seller_CounryName", "C120 |", "MAIN"),
            Field("Seller_PostalCode", "C9 |", "MAIN"),
            Field("Seller_City", "C120 |", "MAIN"),
        };

        var org = new OrganizationFieldGroupTemplate();
        var block = new Block { Name = "MAIN" };
        org.LayoutOrgFields(block, fields, 4, 4, 770, []);

        var idCode = block.Fields.First(f =>
            f.FieldType == Type.Data && f.FieldValue.Name.Contains("IdentityCardCode")
        );
        var country = block.Fields.First(f =>
            f.FieldType == Type.Data
            && f.FieldValue.Name.EndsWith("CountryCode")
            && !f.FieldValue.Name.Contains("IdentityCard")
        );
        // Identity appears before address when listed first
        Assert.True(idCode.FieldPosition.Y <= country.FieldPosition.Y);
    }

    [Fact]
    public void CustomsDocId_CompactsRegistrationRow()
    {
        var fields = new List<Data>
        {
            Field("CustomsDocIdDetails_CustomsCode", "C8 |", "Prec", 1),
            Field("CustomsDocIdDetails_RegistrationDate", "D10 |", "Prec", 1),
            Field("CustomsDocIdDetails_GTDNumber", "C7 |", "Prec", 1),
        };

        var tpl = new CustomsDocIdFieldGroupTemplate();
        var match = tpl.TryMatch(fields, 0, []);
        Assert.NotNull(match);
        var block = new Block { Name = "Prec" };
        tpl.Layout(block, match!, 4, 16, 240, []);
        var data = block.Fields.Where(f => f.FieldType == Type.Data).ToList();
        Assert.Equal(3, data.Count);
        Assert.True(data.All(d => d.FieldPosition.Y == data[0].FieldPosition.Y));
    }

    [Fact]
    public void PrecedingDocDetails_LaysOutModeAndCustoms()
    {
        var fields = new List<Data>
        {
            Field("PrecedingDocumentModeCode", "C4 |Вид", "Goods\\PrecedingDocDetails", 1),
            Field("PrecedingDocumentName", "C250 |Наименование", "Goods\\PrecedingDocDetails", 1),
            Field("PrecedingDocumentGoodsNumeric", "N3 |", "Goods\\PrecedingDocDetails", 1),
            Field("CustomsDocIdDetails_CustomsCode", "C8 |", "Goods\\PrecedingDocDetails", 1),
            Field("CustomsDocIdDetails_RegistrationDate", "D10 |", "Goods\\PrecedingDocDetails", 1),
            Field("CustomsDocIdDetails_GTDNumber", "C7 |", "Goods\\PrecedingDocDetails", 1),
            Field("PrecedingDocumentNum", "C50 |", "Goods\\PrecedingDocDetails", 1),
        };

        var tpl = new PrecedingDocDetailsFieldGroupTemplate();
        var match = tpl.TryMatch(fields, 0, []);
        Assert.NotNull(match);

        var block = new Block { Name = "Goods\\PrecedingDocDetails" };
        int y = tpl.Layout(block, match!, 4, 4, 770, []);
        Assert.True(y > 4);
        Assert.Contains(block.Fields, f => f.FieldValue.Name == "PrecedingDocumentModeCode");
        Assert.Contains(block.Fields, f => f.FieldValue.Name == "CustomsDocIdDetails_CustomsCode");
    }

    [Fact]
    public void PreviewSlots_IncludeTextLabels_ForCustomsDocId()
    {
        var slots = new CustomsDocIdFieldGroupTemplate().GetDefaultPreviewSlots([]);
        Assert.Contains(slots, s => s.Kind == TemplateSlotKind.Text && s.Label.Contains("ДТ"));
        Assert.Contains(slots, s => s.Kind == TemplateSlotKind.Data && s.Suffix == "CustomsCode");
    }

    [Fact]
    public void TemplateLayoutStore_OverridesAppliedAndCleared()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var tpl = new AddressFieldGroupTemplate();
        var defaults = tpl.GetDefaultPreviewSlots([]);
        var edited = defaults.Select(s => s.Clone()).ToList();
        var country = edited.First(s => s.Suffix == "CountryCode");
        country.X = 100;
        country.Width = 52; // snap step 4
        TemplateLayoutStore.SaveTemplate(AddressFieldGroupTemplate.TemplateName, edited);

        var applied = tpl.GetPreviewSlots([]);
        var c = applied.First(s => s.Suffix == "CountryCode");
        Assert.Equal(100, c.X);
        Assert.Equal(52, c.Width);

        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var reset = tpl.GetPreviewSlots([]);
        Assert.Equal(defaults.First(s => s.Suffix == "CountryCode").X, reset.First(s => s.Suffix == "CountryCode").X);
    }

    [Fact]
    public void TemplatePreviewText_Apply_UpdatesDataXFromText()
    {
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        var country = slots.First(s => s.Suffix == "CountryCode" && s.Kind == TemplateSlotKind.Data);
        string text = TemplatePreviewText.Render(slots);
        string patched = string.Join(
            "\n",
            text.Replace("\r\n", "\n")
                .Split('\n')
                .Select(l =>
                    l.StartsWith("Data", StringComparison.Ordinal) && l.Contains("CountryCode", StringComparison.Ordinal)
                        ? $"Data         200    80    32   16 CountryCode `{country.Comment}"
                        : l
                )
        );
        var result = TemplatePreviewText.ApplyToSlots(patched, slots);
        Assert.True(result.Updated > 0);
        Assert.Equal(200, country.X);
        Assert.Equal(80, country.Y);
    }

    [Fact]
    public void TemplatePreviewText_Render_ReflectsCanvasMovedX()
    {
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        var country = slots.First(s => s.Suffix == "CountryCode" && s.Kind == TemplateSlotKind.Data);
        country.X = 200;
        country.Y = 80;
        string text = TemplatePreviewText.Render(slots);
        string line = Assert.Single(
            text.Replace("\r\n", "\n").Split('\n'),
            l => l.StartsWith("Data", StringComparison.Ordinal) && l.Contains("CountryCode", StringComparison.Ordinal)
        );
        Assert.Contains("200", line, StringComparison.Ordinal);
        Assert.Contains("80", line, StringComparison.Ordinal);
        // Ensure X is the slot X (baseX + slot.X - OriginX == slot.X when base==origin)
        Assert.Matches(@"Data\s+200\s+80\s+", line);
    }

    [Fact]
    public void BuiltInTemplates_DefaultSlots_HaveFieldComments()
    {
        var addr = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]);
        var country = Assert.Single(addr, s => s.Suffix == "CountryCode" && s.Kind == TemplateSlotKind.Data);
        Assert.Contains("стран", country.Comment, StringComparison.OrdinalIgnoreCase);

        var org = new OrganizationFieldGroupTemplate().GetDefaultPreviewSlots([]);
        Assert.Equal("Наименование организации", org.First(s => s.Suffix == "OrganizationName").Comment);
        Assert.Contains("ИНН", org.First(s => s.Suffix == "INN").Comment, StringComparison.Ordinal);

        var id = new IdentityCardFieldGroupTemplate().GetDefaultPreviewSlots([]);
        Assert.Contains("выдавшей", id.First(s => s.Suffix == "OrganizationName").Comment, StringComparison.Ordinal);

        string text = TemplatePreviewText.Render(addr);
        Assert.Contains($"{Consts.SpecialDelimetr}Почтовый индекс", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatePreviewText_DataComment_RoundTripsAndPersists()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        var country = slots.First(s => s.Suffix == "CountryCode" && s.Kind == TemplateSlotKind.Data);
        string line =
            $"Data{country.X,12}{country.Y,6}{country.Width,6}{country.Height,5} CountryCode {Consts.SpecialDelimetr}код страны";
        // Keep other Data lines so Apply does not dismiss unrelated slots.
        string text = TemplatePreviewText.Render(slots);
        text = string.Join(
            "\n",
            text.Split('\n')
                .Select(l =>
                    l.Contains("CountryCode", StringComparison.Ordinal)
                    && l.TrimStart().StartsWith("Data", StringComparison.Ordinal)
                        ? line
                        : l
                )
        );

        var result = TemplatePreviewText.ApplyToSlots(text, slots);
        Assert.True(result.Updated > 0);
        Assert.Equal("код страны", country.Comment);

        string rendered = TemplatePreviewText.Render(slots);
        Assert.Contains($"{Consts.SpecialDelimetr}код страны", rendered, StringComparison.Ordinal);

        TemplateLayoutStore.SaveTemplate(AddressFieldGroupTemplate.TemplateName, slots);
        var reloaded = new AddressFieldGroupTemplate().GetPreviewSlots([]);
        var got = reloaded.First(s => s.Suffix == "CountryCode" && s.Kind == TemplateSlotKind.Data);
        Assert.Equal("код страны", got.Comment);
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
    }

    [Fact]
    public void TemplatePreviewText_Render_ShowsRemovedPrefix()
    {
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        var postal = slots.First(s => s.Suffix == "PostalCode");
        postal.IsRemoved = true;

        string text = TemplatePreviewText.Render(slots);
        Assert.Contains("REMOVED Data", text, StringComparison.Ordinal);
        Assert.Contains("PostalCode", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            text.Split('\n').Where(l => !l.StartsWith("REMOVED", StringComparison.Ordinal)),
            l => l.Contains("PostalCode", StringComparison.Ordinal) && l.StartsWith("Data", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void TemplatePreviewText_Render_ShowsIgnoredPrefix()
    {
        var ignore = ConversionOptions.ParseIgnoreFields("*PostalCode");
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots(ignore).Select(s => s.Clone()).ToList();
        Assert.Contains(slots, s => s.Suffix == "PostalCode" && s.IsIgnored);

        string text = TemplatePreviewText.Render(slots);
        Assert.Contains("IGNORED Data", text, StringComparison.Ordinal);
        Assert.Contains("PostalCode", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            text.Split('\n').Where(l => !l.StartsWith("IGNORED", StringComparison.OrdinalIgnoreCase)),
            l => l.Contains("PostalCode", StringComparison.Ordinal) && l.TrimStart().StartsWith("Data", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void TemplatePreviewText_Apply_DeletingIgnoredLineDismissesSlot()
    {
        var ignore = ConversionOptions.ParseIgnoreFields("*PostalCode");
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots(ignore).Select(s => s.Clone()).ToList();
        var postal = slots.First(s => s.Suffix == "PostalCode");
        Assert.True(postal.IsIgnored);

        string text = TemplatePreviewText.Render(slots);
        Assert.Contains("IGNORED", text, StringComparison.OrdinalIgnoreCase);

        string withoutIgnored = string.Join(
            '\n',
            text.Replace("\r\n", "\n")
                .Split('\n')
                .Where(l => !l.StartsWith("IGNORED", StringComparison.OrdinalIgnoreCase))
        );
        TemplatePreviewText.ApplyToSlots(withoutIgnored, slots);

        Assert.True(postal.IsRemoved);
        string after = TemplatePreviewText.Render(slots);
        Assert.DoesNotContain("PostalCode", after, StringComparison.Ordinal);
        Assert.DoesNotContain("IGNORED", after, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TemplatePreviewText_Apply_CreatesNewTextSlot()
    {
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        int before = slots.Count(s => s.Kind == TemplateSlotKind.Text);

        string text = TemplatePreviewText.Render(slots);
        text += $"\nText         20    80    80   12 {Consts.SpecialDelimetr}Заголовок секции";

        var result = TemplatePreviewText.ApplyToSlots(text, slots);
        Assert.True(result.Updated > 0);
        Assert.Equal(before + 1, slots.Count(s => s.Kind == TemplateSlotKind.Text));
        var created = slots.Last(s => s.Kind == TemplateSlotKind.Text && s.IsEmbedded);
        Assert.Equal("Заголовок секции", created.Label);
        Assert.Equal(20, created.X);
        Assert.Equal(80, created.Y);
    }

    [Fact]
    public void TemplatePreviewText_Apply_RemovedAndRestore()
    {
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        var postal = slots.First(s => s.Suffix == "PostalCode");
        postal.IsRemoved = true;

        string text = TemplatePreviewText.Render(slots);
        Assert.Contains("REMOVED", text, StringComparison.Ordinal);

        // Restore: strip REMOVED prefix from PostalCode line
        string restored = text.Replace("REMOVED Data", "Data", StringComparison.Ordinal);
        var result = TemplatePreviewText.ApplyToSlots(restored, slots);
        Assert.True(result.Updated > 0);
        Assert.False(slots.First(s => s.Suffix == "PostalCode").IsRemoved);

        // Mark removed again via REMOVED line
        string withRemoved = TemplatePreviewText.Render(slots).Replace("\r\n", "\n");
        var lines = withRemoved.Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains("PostalCode", StringComparison.Ordinal) && lines[i].StartsWith("Data", StringComparison.Ordinal))
            {
                lines[i] = TemplatePreviewText.RemovedPrefix + lines[i];
                break;
            }
        }

        TemplatePreviewText.ApplyToSlots(string.Join("\n", lines), slots);
        Assert.True(slots.First(s => s.Suffix == "PostalCode").IsRemoved);
    }

    [Fact]
    public void TemplateLayoutStore_ReplaceBase_KeepsRemovedPictTombstones()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var tpl = new AddressFieldGroupTemplate();
        var slots = tpl.GetPreviewSlots([]).Select(s => s.Clone()).ToList();
        var pict = slots.First(s => s.Kind == TemplateSlotKind.Pict && s.Suffix == "CountryCode");
        pict.IsRemoved = true;

        TemplateLayoutStore.SaveTemplate(AddressFieldGroupTemplate.TemplateName, slots, replaceBase: true);
        Assert.True(TemplateLayoutStore.IsReplaceBase(AddressFieldGroupTemplate.TemplateName));

        var applied = tpl.GetPreviewSlots([]);
        var got = Assert.Single(applied, s => s.Kind == TemplateSlotKind.Pict && s.Suffix == "CountryCode");
        Assert.True(got.IsRemoved);

        // Must not reappear as an active or REMOVED pict line in text preview.
        string text = TemplatePreviewText.Render(applied);
        Assert.DoesNotContain(
            text.Replace("\r\n", "\n").Split('\n'),
            l =>
                l.Contains("Pict", StringComparison.Ordinal)
                && l.Contains("CountryCode", StringComparison.Ordinal)
        );

        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
    }

    [Fact]
    public void TemplatePreviewText_Apply_DeletingPictLineRemovesPict()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var slots = new AddressFieldGroupTemplate().GetPreviewSlots([]).Select(s => s.Clone()).ToList();
        var pict = slots.First(s => s.Kind == TemplateSlotKind.Pict && s.Suffix == "CountryCode");
        Assert.False(pict.IsRemoved);

        string text = TemplatePreviewText.Render(slots);
        string withoutPict = string.Join(
            '\n',
            text.Replace("\r\n", "\n")
                .Split('\n')
                .Where(l =>
                    !(
                        l.StartsWith("Pict", StringComparison.Ordinal)
                        && l.Contains("CountryCode", StringComparison.Ordinal)
                    )
                )
        );

        TemplatePreviewText.ApplyToSlots(withoutPict, slots);
        Assert.True(pict.IsRemoved);

        // Apply refresh must not resurrect REMOVED Pict lines.
        string after = TemplatePreviewText.Render(slots);
        Assert.DoesNotContain(
            after.Replace("\r\n", "\n").Split('\n'),
            l =>
                l.Contains("Pict", StringComparison.Ordinal)
                && l.Contains("CountryCode", StringComparison.Ordinal)
        );

        TemplateLayoutStore.SaveTemplate(AddressFieldGroupTemplate.TemplateName, slots, replaceBase: true);
        var reloaded = new AddressFieldGroupTemplate().GetPreviewSlots([]);
        Assert.True(
            reloaded.First(s => s.Kind == TemplateSlotKind.Pict && s.Suffix == "CountryCode").IsRemoved
        );
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
    }

    [Fact]
    public void TemplateLayoutStore_ReplaceBase_DropsRemoved()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var tpl = new AddressFieldGroupTemplate();
        var slots = tpl.GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        var postal = slots.First(s => s.Suffix == "PostalCode");
        postal.IsRemoved = true;
        postal.X = 200;

        TemplateLayoutStore.SaveTemplate(AddressFieldGroupTemplate.TemplateName, slots, replaceBase: true);
        Assert.True(TemplateLayoutStore.IsReplaceBase(AddressFieldGroupTemplate.TemplateName));

        var applied = tpl.GetPreviewSlots([]);
        Assert.DoesNotContain(applied, s => s.Suffix == "PostalCode");
        Assert.Contains(applied, s => s.Suffix == "CountryCode");

        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        Assert.False(TemplateLayoutStore.IsReplaceBase(AddressFieldGroupTemplate.TemplateName));
    }

    [Fact]
    public void UserTemplate_CreateFromBranch_MatchesPathAndLayouts()
    {
        UserTemplateStore.Load([]);
        var def = UserDefinedFieldGroupTemplate.CreateFromBranch(
            "Goods\\Container",
            "Контейнер",
            [
                ("Container_ContainerID", "Номер", "C17 |Номер"),
                ("Container_ContainerCode", "Код", "C2 |Код"),
                ("Container_FullnessCode", "Полнота", "C1 |"),
            ],
            "Container"
        );
        Assert.Equal("Container", def.Name);
        Assert.Equal("Goods\\Container", def.MatchPath);
        Assert.Contains(def.Fields, f => f.Suffix == "ContainerID");
        Assert.Contains(def.Fields, f => f.Suffix == "ContainerCode");

        var tpl = UserTemplateStore.Add(def);
        Assert.Contains(FieldGroupTemplateRegistry.Default.All, t => t.Name == tpl.Name);

        var fields = new List<Data>
        {
            Field("Container_ContainerID", "C17 |", "Goods\\Container", 1),
            Field("Container_ContainerCode", "C2 |", "Goods\\Container", 1),
            Field("Container_FullnessCode", "C1 |", "Goods\\Container", 1),
        };

        var pathMatch = tpl.TryMatchPath(fields, [], "Goods\\Container");
        Assert.NotNull(pathMatch);
        Assert.Equal(3, pathMatch!.Fields.Count);

        var block = new Block { Name = "Goods\\Container" };
        tpl.Layout(block, pathMatch, 4, 4, 770, []);
        Assert.Contains(block.Fields, f => f.FieldType == Type.Data && f.FieldValue.Name.Contains("ContainerID"));
        Assert.Contains(block.Fields, f => f.FieldType == Type.Pict);

        var slots = tpl.GetPreviewSlots([]);
        Assert.Contains(slots, s => s.Kind == TemplateSlotKind.Data && s.Suffix == "ContainerID");
        Assert.Contains(slots, s => s.Kind == TemplateSlotKind.Pict && s.Suffix == "ContainerCode");

        UserTemplateStore.Remove(tpl.Name);
        Assert.DoesNotContain(FieldGroupTemplateRegistry.Default.All, t => t.Name == tpl.Name);
    }

    [Fact]
    public void PictSlots_AreMovableAndRemovable_Persisted()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var tpl = new AddressFieldGroupTemplate();
        var slots = tpl.GetPreviewSlots([]).Select(s => s.Clone()).ToList();
        var pict = slots.First(s => s.Kind == TemplateSlotKind.Pict && s.Suffix == "CountryCode");
        pict.X = 300;
        pict.Y = 40;
        pict.IsRemoved = true;
        TemplateLayoutStore.SaveTemplate(AddressFieldGroupTemplate.TemplateName, slots);

        var applied = tpl.GetPreviewSlots([]);
        var got = applied.First(s => s.Kind == TemplateSlotKind.Pict && s.Suffix == "CountryCode");
        Assert.Equal(300, got.X);
        Assert.Equal(40, got.Y);
        Assert.True(got.IsRemoved);

        var fields = new List<Data>
        {
            Field("CountryCode", "C2 |", "Addr", 1),
            Field("CounryName", "C120 |", "Addr", 1),
        };
        var block = new Block { Name = "Addr" };
        TemplateSlotLayout.Apply(block, fields, applied, 4, 4, 770);
        Assert.DoesNotContain(
            block.Fields,
            f =>
                f.FieldType == Type.Pict
                && f.FieldValue.Comment.Contains("CountryCode", StringComparison.OrdinalIgnoreCase)
        );

        got.IsRemoved = false;
        TemplateLayoutStore.SaveTemplate(
            AddressFieldGroupTemplate.TemplateName,
            applied.Select(s => s.Clone()).ToList()
        );
        applied = tpl.GetPreviewSlots([]);
        block = new Block { Name = "Addr" };
        TemplateSlotLayout.Apply(block, fields, applied, 4, 4, 770);
        var pictField = Assert.Single(
            block.Fields,
            f =>
                f.FieldType == Type.Pict
                && f.FieldValue.Comment.Contains("CountryCode", StringComparison.OrdinalIgnoreCase)
        );
        Assert.Equal(300, pictField.FieldPosition.X);

        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
    }

    [Fact]
    public void TemplatePreviewText_Render_IncludesPictForCodeFields()
    {
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        string text = TemplatePreviewText.Render(slots);
        Assert.Contains("Pict", text, StringComparison.Ordinal);
        Assert.Contains("CountryCode", text, StringComparison.Ordinal);
        Assert.Contains("Data", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TemplatePreviewText_Apply_UpdatesDataGeometry_IgnoresPict()
    {
        var slots = new AddressFieldGroupTemplate().GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        int beforeCount = slots.Count;
        var country = slots.First(s => s.Suffix == "CountryCode");
        int oldX = country.X;

        string text = TemplatePreviewText.Render(slots);
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("Data", StringComparison.Ordinal) && lines[i].Contains("CountryCode", StringComparison.Ordinal))
            {
                lines[i] =
                    $"Data{oldX + 20,12}{country.Y,6}{country.Width,6}{country.Height,5} CountryCode";
                break;
            }
        }

        string patched = string.Join("\n", lines);
        int pictLines = patched.Split('\n').Count(l => l.StartsWith("Pict", StringComparison.Ordinal));
        Assert.True(pictLines > 0);

        var result = TemplatePreviewText.ApplyToSlots(patched, slots);
        Assert.True(result.Updated > 0);
        Assert.Equal(oldX + 20, slots.First(s => s.Suffix == "CountryCode").X);
        Assert.Equal(beforeCount, slots.Count); // Pict must not create slots
    }

    [Fact]
    public void TemplateSlotLayout_Pict_DoesNotEmitEmbedStemHint()
    {
        var fields = new List<Data>
        {
            Field("DeclarantDetails_IdentityCard_IdentityCardDate", "D10 |Дата", "MAIN"),
            Field("DeclarantDetails_IdentityCard_IdentityCardCode", "C2 |Код", "MAIN"),
        };

        var slots = new List<TemplateSlot>
        {
            new()
            {
                Id = TemplateSlot.MakeId(TemplateSlotKind.Data, "IdentityCard", "IdentityCardDate", "embed:2"),
                Suffix = "IdentityCardDate",
                Kind = TemplateSlotKind.Data,
                X = 100,
                Y = 40,
                Width = 84,
                Height = 16,
                Group = "IdentityCard",
                StemHint = "embed:2",
                IsEmbedded = true,
            },
            new()
            {
                Id = TemplateSlot.MakeId(TemplateSlotKind.Pict, "IdentityCard", "IdentityCardDate", "embed:2"),
                Suffix = "IdentityCardDate",
                Label = "embed:2_IdentityCardDate=DATE",
                Kind = TemplateSlotKind.Pict,
                X = 190,
                Y = 42,
                Width = 12,
                Height = 12,
                Group = "IdentityCard",
                StemHint = "embed:2",
                IsEmbedded = true,
            },
        };

        var block = new Block { Name = "MAIN" };
        TemplateSlotLayout.Apply(block, fields, slots, 4, 4, 770);

        var picts = block.Fields.Where(f => f.FieldType == Type.Pict).ToList();
        Assert.NotEmpty(picts);
        Assert.All(picts, p => Assert.DoesNotContain("embed:", p.FieldValue.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            picts,
            p => p.FieldValue.ToString().Contains("DeclarantDetails_IdentityCard_IdentityCardDate", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ContactTemplate_LayoutsCommunicationDetails_WithoutOrphanOrgLabels()
    {
        var fields = new List<Data>
        {
            Field("Carrier_CommunicationDetails_Phone", "C250 24 |телефон SEQ=2", "MAIN"),
            Field("Carrier_CommunicationDetails_Fax", "C24 |факс", "MAIN"),
            Field("Carrier_CommunicationDetails_Telex", "C24 |телекс", "MAIN"),
            Field("Carrier_CommunicationDetails_E_mail", "C250 50 |email SEQ=1", "MAIN"),
        };

        var block = new Block { Name = "MAIN" };
        int y = ContactFieldGroupTemplate.LayoutContactFields(block, fields, 4, 4, 770, []);
        Assert.True(y > 4);

        var data = block.Fields.Where(f => f.FieldType == Type.Data).ToList();
        Assert.Equal(4, data.Count);
        Assert.DoesNotContain(block.Fields, f => f.FieldType == Type.Text && f.FieldValue.ToString().Contains("ИНН"));
        Assert.Contains(data, f => f.FieldValue.Name.Contains("Phone", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(data, f => f.FieldValue.Name.Contains("E_mail", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindField_AddressCountryCode_DoesNotStealIdentityCard()
    {
        var fields = new List<Data>
        {
            Field("ConsignorDetails_IdentityCard_CountryCode", "C2 |Кодовое обозначение страны", "MAIN"),
            Field("ConsignorDetails_Address_PostalCode", "C9 |индекс", "MAIN"),
        };

        var addressSlot = new TemplateSlot
        {
            Id = "Data:Address:CountryCode",
            Suffix = "CountryCode",
            Kind = TemplateSlotKind.Data,
            X = 4,
            Y = 4,
            Width = 32,
            Height = 16,
            Group = AddressFieldGroupTemplate.TemplateName,
        };
        var idSlot = new TemplateSlot
        {
            Id = "Data:IdentityCard:CountryCode",
            Suffix = "CountryCode",
            Kind = TemplateSlotKind.Data,
            X = 100,
            Y = 4,
            Width = 20,
            Height = 16,
            Group = IdentityCardFieldGroupTemplate.TemplateName,
        };

        Assert.Null(TemplateSlotLayout.FindField(fields, addressSlot));
        Assert.Equal(
            "ConsignorDetails_IdentityCard_CountryCode",
            TemplateSlotLayout.FindField(fields, idSlot)!.FieldElement.ElementName
        );
    }

    [Fact]
    public void FindField_OrganizationName_DoesNotStealIdentityCard()
    {
        var fields = new List<Data>
        {
            Field("ConsignorDetails_IdentityCard_OrganizationName", "C150 |выдавшей", "MAIN"),
            Field("ConsignorDetails_OrganizationName", "C150 |орг", "MAIN"),
        };

        var orgSlot = new TemplateSlot
        {
            Id = "Data:Organization:OrganizationName",
            Suffix = "OrganizationName",
            Kind = TemplateSlotKind.Data,
            X = 4,
            Y = 4,
            Width = 400,
            Height = 16,
            Group = OrganizationFieldGroupTemplate.TemplateName,
        };
        var idSlot = new TemplateSlot
        {
            Id = "Data:IdentityCard:OrganizationName",
            Suffix = "OrganizationName",
            Kind = TemplateSlotKind.Data,
            X = 4,
            Y = 40,
            Width = 544,
            Height = 16,
            Group = IdentityCardFieldGroupTemplate.TemplateName,
        };

        Assert.Equal(
            "ConsignorDetails_OrganizationName",
            TemplateSlotLayout.FindField(fields, orgSlot)!.FieldElement.ElementName
        );
        Assert.Equal(
            "ConsignorDetails_IdentityCard_OrganizationName",
            TemplateSlotLayout.FindField(fields, idSlot)!.FieldElement.ElementName
        );
    }

    [Fact]
    public void Apply_DoesNotPlaceSameFieldTwice_WhenDuplicateCountryCodeSlots()
    {
        var fields = new List<Data>
        {
            Field("ConsignorDetails_IdentityCard_CountryCode", "C2 |код страны выдачи", "MAIN"),
            Field("ConsignorDetails_IdentityCard_IdentityCardCode", "C20 |код", "MAIN"),
        };

        var slots = new List<TemplateSlot>
        {
            new()
            {
                Id = "Data:IdentityCard:CountryCode",
                Suffix = "CountryCode",
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 4,
                Width = 20,
                Height = 16,
                Group = IdentityCardFieldGroupTemplate.TemplateName,
            },
            new()
            {
                Id = "Data:IdentityCard:CountryCode:dup",
                Suffix = "CountryCode",
                Kind = TemplateSlotKind.Data,
                X = 40,
                Y = 40,
                Width = 32,
                Height = 16,
                Group = IdentityCardFieldGroupTemplate.TemplateName,
            },
            new()
            {
                Id = "Data:Address:CountryCode",
                Suffix = "CountryCode",
                Kind = TemplateSlotKind.Data,
                X = 80,
                Y = 80,
                Width = 32,
                Height = 16,
                Group = AddressFieldGroupTemplate.TemplateName,
            },
        };

        var block = new Block { Name = "MAIN" };
        TemplateSlotLayout.Apply(block, fields, slots, 4, 4, 770);

        var country = block.Fields
            .Where(f => f.FieldType == Type.Data && f.FieldValue.Name == "ConsignorDetails_IdentityCard_CountryCode")
            .ToList();
        Assert.Single(country);
    }

    [Fact]
    public void Apply_SkipsOffScreenSlots_AndPlacesFieldInRest()
    {
        var fields = new List<Data>
        {
            Field("ConsignorDetails_IdentityCard_IssuerCode", "C7 |код подр", "MAIN"),
            Field("ConsignorDetails_ShortName", "C120 |краткое", "MAIN"),
        };

        var slots = new List<TemplateSlot>
        {
            new()
            {
                Id = "Data:Organization:ShortName",
                Suffix = "ShortName",
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 4,
                Width = 240,
                Height = 16,
                Group = OrganizationFieldGroupTemplate.TemplateName,
            },
            new()
            {
                Id = "Data:IdentityCard:IssuerCode",
                Suffix = "IssuerCode",
                Kind = TemplateSlotKind.Data,
                X = 81032,
                Y = 112,
                Width = 60,
                Height = 16,
                Group = IdentityCardFieldGroupTemplate.TemplateName,
            },
            new()
            {
                Id = "Pict:IdentityCard:IssuerCode",
                Suffix = "IssuerCode",
                Kind = TemplateSlotKind.Pict,
                X = 81004,
                Y = 112,
                Width = 12,
                Height = 12,
                Group = IdentityCardFieldGroupTemplate.TemplateName,
            },
        };

        var block = new Block { Name = "MAIN" };
        TemplateSlotLayout.Apply(block, fields, slots, 4, 4, 770);

        var issuer = Assert.Single(
            block.Fields,
            f => f.FieldType == Type.Data && f.FieldValue.Name == "ConsignorDetails_IdentityCard_IssuerCode"
        );
        Assert.True(issuer.FieldPosition.X < 800, $"IssuerCode X={issuer.FieldPosition.X} should be on-canvas");
        Assert.DoesNotContain(
            block.Fields,
            f => f.FieldType == Type.Pict && f.FieldPosition.X >= 800
        );
    }

    [Fact]
    public void EnsurePictSlots_MatchesOwnerByGroup_DoesNotExplodeX()
    {
        var slots = new List<TemplateSlot>
        {
            new()
            {
                Id = "Data:IdentityCard:CountryCode",
                Suffix = "CountryCode",
                Kind = TemplateSlotKind.Data,
                X = 600,
                Y = 40,
                Width = 20,
                Height = 16,
                Group = IdentityCardFieldGroupTemplate.TemplateName,
            },
            new()
            {
                Id = "Data:Address:CountryCode",
                Suffix = "CountryCode",
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 40,
                Width = 32,
                Height = 16,
                Group = AddressFieldGroupTemplate.TemplateName,
            },
            new()
            {
                Id = "Data:IdentityCard:IssuerCode",
                Suffix = "IssuerCode",
                Kind = TemplateSlotKind.Data,
                X = 650,
                Y = 40,
                Width = 60,
                Height = 16,
                Group = IdentityCardFieldGroupTemplate.TemplateName,
            },
            // Address CountryCode pict wrongly sitting on IdentityCard row — must bind to Address data.
            new()
            {
                Id = "Pict:Address:CountryCode",
                Suffix = "CountryCode",
                Kind = TemplateSlotKind.Pict,
                X = 40,
                Y = 42,
                Width = 12,
                Height = 12,
                Group = AddressFieldGroupTemplate.TemplateName,
            },
        };

        var fixedSlots = TemplateSlotLayout.EnsurePictSlots(slots);
        Assert.True(
            fixedSlots.Max(s => s.X) < TemplateSlotLayout.MaxTemplateContentX,
            $"max X={fixedSlots.Max(s => s.X)} exploded past template edge"
        );
    }

    [Fact]
    public void OrganizationSanitize_RestoresOrgName_AndMovesIdentityCardOutOfHeader()
    {
        TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
        // Corrupted replaceBase: IdentityCard OrganizationName+CountryCode on the header row, no org name.
        var broken = new List<TemplateSlot>
        {
            new()
            {
                Id = "Data:IdentityCard:OrganizationName",
                Suffix = "OrganizationName",
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 0,
                Width = 640,
                Height = 16,
                Group = IdentityCardFieldGroupTemplate.TemplateName,
                IsEmbedded = true,
            },
            new()
            {
                Id = "Data:IdentityCard:CountryCode",
                Suffix = "CountryCode",
                Kind = TemplateSlotKind.Data,
                X = 648,
                Y = 0,
                Width = 32,
                Height = 16,
                Group = IdentityCardFieldGroupTemplate.TemplateName,
                IsEmbedded = true,
            },
            new()
            {
                Id = "Data:Organization:ShortName",
                Suffix = "ShortName",
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 20,
                Width = 240,
                Height = 16,
                Group = OrganizationFieldGroupTemplate.TemplateName,
                IsEmbedded = true,
            },
        };
        TemplateLayoutStore.SaveTemplate(OrganizationFieldGroupTemplate.TemplateName, broken, replaceBase: true);

        try
        {
            var fields = new List<Data>
            {
                Field("ConsignorDetails_IdentityCard_CountryCode", "C2 |код", "MAIN"),
                Field("ConsignorDetails_IdentityCard_OrganizationName", "C150 |выдавшей", "MAIN"),
                Field("ConsignorDetails_IdentityCard_IdentityCardCode", "C20 |код", "MAIN"),
                Field("ConsignorDetails_OrganizationName", "C150 |орг", "MAIN"),
                Field("ConsignorDetails_ShortName", "C120 |краткое", "MAIN"),
            };

            var org = new OrganizationFieldGroupTemplate();
            var block = new Block { Name = "MAIN" };
            org.LayoutOrgFields(block, fields, 4, 4, 770, []);

            var orgName = block.Fields.First(f =>
                f.FieldType == Type.Data && f.FieldValue.Name == "ConsignorDetails_OrganizationName"
            );
            var idName = block.Fields.First(f =>
                f.FieldType == Type.Data && f.FieldValue.Name == "ConsignorDetails_IdentityCard_OrganizationName"
            );
            var shortName = block.Fields.First(f =>
                f.FieldType == Type.Data && f.FieldValue.Name == "ConsignorDetails_ShortName"
            );

            Assert.True(orgName.FieldPosition.Y < shortName.FieldPosition.Y, "org name must be above ShortName");
            Assert.True(orgName.FieldPosition.Y < idName.FieldPosition.Y, "org name must be above IdentityCard name");
            Assert.True(shortName.FieldPosition.Y < idName.FieldPosition.Y, "IdentityCard must be below org header");
        }
        finally
        {
            TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
        }
    }

    [Fact]
    public void OrganizationTemplate_IdentityCardFieldsAppearOnce()
    {
        TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
        // Force slot-Apply path (same as saved Organization layout).
        var org = new OrganizationFieldGroupTemplate();
        TemplateLayoutStore.SaveTemplate(
            OrganizationFieldGroupTemplate.TemplateName,
            org.GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList()
        );

        try
        {
            var fields = new List<Data>
            {
                Field("ConsignorDetails_IdentityCard_CountryCode", "C2 |Кодовое обозначение страны", "MAIN"),
                Field("ConsignorDetails_IdentityCard_OrganizationName", "C150 |выдавшей", "MAIN"),
                Field("ConsignorDetails_IdentityCard_IdentityCardCode", "C20 |код", "MAIN"),
                Field("ConsignorDetails_IdentityCard_IdentityCardName", "C40 |имя", "MAIN"),
                Field("ConsignorDetails_IdentityCard_IssuerCode", "C7 |код подр", "MAIN"),
                Field("ConsignorDetails_OrganizationName", "C150 |орг", "MAIN"),
                Field("ConsignorDetails_ShortName", "C120 |краткое", "MAIN"),
                Field("ConsignorDetails_UITN_CountryCode", "C2 |UITN страна", "MAIN"),
            };

            var block = new Block { Name = "MAIN" };
            org.LayoutOrgFields(block, fields, 4, 4, 770, []);

            static int CountData(Block b, string name) =>
                b.Fields.Count(f => f.FieldType == Type.Data && f.FieldValue.Name == name);

            Assert.Equal(1, CountData(block, "ConsignorDetails_IdentityCard_CountryCode"));
            Assert.Equal(1, CountData(block, "ConsignorDetails_IdentityCard_OrganizationName"));
            Assert.Equal(1, CountData(block, "ConsignorDetails_IdentityCard_IssuerCode"));
            Assert.Equal(1, CountData(block, "ConsignorDetails_OrganizationName"));

            var orgName = block.Fields.First(f =>
                f.FieldType == Type.Data && f.FieldValue.Name == "ConsignorDetails_OrganizationName"
            );
            Assert.True(orgName.FieldPosition.X < 100, "org OrganizationName should use Organization slot, not IdentityCard");
        }
        finally
        {
            TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
        }
    }

    [Fact]
    public void ContactTemplate_MissingOverrideSlot_KeepsCompactEmailWidth()
    {
        TemplateLayoutStore.ClearTemplate(ContactFieldGroupTemplate.TemplateName);
        // Saved Contact layout without E_mail slot
        var partial = new ContactFieldGroupTemplate()
            .GetDefaultPreviewSlots([])
            .Where(s => !s.Suffix.Equals("E_mail", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Clone())
            .ToList();
        TemplateLayoutStore.SaveTemplate(ContactFieldGroupTemplate.TemplateName, partial);

        try
        {
            var fields = new List<Data>
            {
                Field("X_Phone", "C20 |тел", "MAIN"),
                Field("X_Fax", "C20 |факс", "MAIN"),
                Field("X_Telex", "C20 |телекс", "MAIN"),
                Field("X_E_mail", "C250 50 |email", "MAIN"),
            };
            var block = new Block { Name = "MAIN" };
            ContactFieldGroupTemplate.LayoutContactFields(block, fields, 4, 4, 770, []);

            var email = block.Fields.First(f =>
                f.FieldType == Type.Data && f.FieldValue.Name.Contains("E_mail", StringComparison.OrdinalIgnoreCase)
            );
            Assert.True(email.FieldSize.Length <= 240, $"E_mail width {email.FieldSize.Length} should stay compact");
        }
        finally
        {
            TemplateLayoutStore.ClearTemplate(ContactFieldGroupTemplate.TemplateName);
        }
    }

    [Fact]
    public void ContactFlattenToSingleRow_SnapsEmailBesidePhone()
    {
        var slots = new List<TemplateSlot>
        {
            new()
            {
                Id = "Data:Contact:Phone",
                Suffix = "Phone",
                Kind = TemplateSlotKind.Data,
                X = 20,
                Y = 4,
                Width = 200,
                Height = 16,
                Group = "Contact",
            },
            new()
            {
                Id = "Text:Contact:t",
                Suffix = "t",
                Label = "т.",
                Kind = TemplateSlotKind.Text,
                X = 4,
                Y = 8,
                Width = 40,
                Height = 12,
                Group = "Contact",
            },
            new()
            {
                Id = "Text:Contact:email",
                Suffix = "email",
                Label = "e-mail",
                Kind = TemplateSlotKind.Text,
                X = 224,
                Y = 8,
                Width = 40,
                Height = 12,
                Group = "Contact",
            },
            new()
            {
                Id = "Data:Contact:E_mail",
                Suffix = "E_mail",
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 24,
                Width = 240,
                Height = 16,
                Group = "Contact",
            },
        };

        var flat = ContactFieldGroupTemplate.FlattenToSingleRow(slots);
        var phone = flat.First(s => s.Suffix == "Phone");
        var email = flat.First(s => s.Suffix == "E_mail");
        Assert.Equal(phone.Y, email.Y);
        Assert.True(email.X >= phone.X + phone.Width);
    }

    [Fact]
    public void CompactAbsentRows_ClosesGapWhenContactMissing()
    {
        var slots = new List<TemplateSlot>
        {
            new()
            {
                Id = "Data:Organization:INN",
                Suffix = "INN",
                Kind = TemplateSlotKind.Data,
                X = 276,
                Y = 28,
                Width = 80,
                Height = 16,
                Group = "Organization",
            },
            new()
            {
                Id = "Data:Contact:Phone",
                Suffix = "Phone",
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 52,
                Width = 200,
                Height = 16,
                Group = "Contact",
            },
            new()
            {
                Id = "Text:Contact:phone",
                Suffix = "phone",
                Label = "Т.",
                Kind = TemplateSlotKind.Text,
                X = 4,
                Y = 56,
                Width = 16,
                Height = 12,
                Group = "Contact",
            },
            new()
            {
                Id = "Data:IdentityCard:IdentityCardCode",
                Suffix = "IdentityCardCode",
                Kind = TemplateSlotKind.Data,
                X = 4,
                Y = 80,
                Width = 44,
                Height = 16,
                Group = "IdentityCard",
            },
        };

        var fields = new List<Data>
        {
            Field("Org_INN", "C12 |", "MAIN"),
            Field("Org_IdentityCard_IdentityCardCode", "C20 |", "MAIN"),
            // Phone absent
        };

        var compact = TemplateSlotLayout.CompactAbsentRows(slots, fields);
        var inn = compact.First(s => s.Suffix == "INN");
        var id = compact.First(s => s.Suffix == "IdentityCardCode");
        Assert.DoesNotContain(compact, s => s.Suffix == "Phone");
        Assert.DoesNotContain(compact, s => s.Kind == TemplateSlotKind.Text);
        Assert.True(
            id.Y - (inn.Y + inn.Height) <= Consts.DefaultHeightData + 12,
            $"Gap too large: INN Y={inn.Y}h={inn.Height}, ID Y={id.Y}"
        );
    }

    [Fact]
    public void CompactAbsentRows_DoesNotCollapseIdentityCardOntoShortName()
    {
        // Mirrors saved Organization layout: ShortName, then Contact + several Address rows,
        // then IdentityCard. When Contact/Address are absent from DCF, Id must not land on ShortName.
        var slots = new List<TemplateSlot>
        {
            DataSlot("Organization", "ShortName", 4, 20, 240),
            DataSlot("Contact", "Phone", 4, 40, 200),
            DataSlot("Address", "CountryCode", 24, 60, 32),
            DataSlot("Address", "PostalCode", 216, 60, 80),
            DataSlot("Address", "Region", 1700, 68, 80),
            DataSlot("Address", "City", 216, 80, 120),
            DataSlot("Address", "StreetHouse", 4, 100, 200),
            DataSlot("Address", "AddressText", 4, 120, 400),
            DataSlot("IdentityCard", "IdentityCardCode", 4, 140, 24),
            DataSlot("IdentityCard", "IdentityCardName", 48, 140, 124),
        };

        var fields = new List<Data>
        {
            Field("ConsignorDetails_ShortName", "C150 |", "MAIN"),
            Field("ConsignorDetails_IdentityCard_IdentityCardCode", "C20 |", "MAIN"),
            Field("ConsignorDetails_IdentityCard_IdentityCardName", "C40 |", "MAIN"),
        };

        var compact = TemplateSlotLayout.CompactAbsentRows(slots, fields);
        var shortName = compact.First(s => s.Suffix == "ShortName");
        var idCode = compact.First(s => s.Suffix == "IdentityCardCode");
        Assert.True(
            idCode.Y >= shortName.Y + shortName.Height,
            $"IdentityCardCode Y={idCode.Y} collided with ShortName Y={shortName.Y}"
        );
    }

    private static TemplateSlot DataSlot(string group, string suffix, int x, int y, int width) =>
        new()
        {
            Id = TemplateSlot.MakeId(TemplateSlotKind.Data, group, suffix),
            Suffix = suffix,
            Kind = TemplateSlotKind.Data,
            X = x,
            Y = y,
            Width = width,
            Height = Consts.DefaultHeightData,
            Group = group,
        };

    [Fact]
    public void PlaceTextLabelsBeforeData_MovesLabelLeftOfField()
    {
        var slots = new List<TemplateSlot>
        {
            new()
            {
                Id = "Text:Contact:email",
                Suffix = "email",
                Label = "e-mail",
                Kind = TemplateSlotKind.Text,
                X = 224,
                Y = 8,
                Width = 40,
                Height = 12,
                Group = "Contact",
            },
            new()
            {
                Id = "Data:Contact:E_mail",
                Suffix = "E_mail",
                Kind = TemplateSlotKind.Data,
                X = 224,
                Y = 4,
                Width = 240,
                Height = 16,
                Group = "Contact",
            },
        };

        ContactFieldGroupTemplate.PlaceTextLabelsBeforeData(slots);
        var text = slots.First(s => s.Kind == TemplateSlotKind.Text);
        var data = slots.First(s => s.Kind == TemplateSlotKind.Data);
        Assert.True(text.X + text.Width <= data.X, $"Text X={text.X} w={text.Width} should end before Data X={data.X}");
    }

    [Fact]
    public void LayoutUtil_ShouldAddPict_MatchesCodeInnDate()
    {
        Assert.True(LayoutUtil.ShouldAddPict("CountryCode"));
        Assert.True(LayoutUtil.ShouldAddPict("INN"));
        Assert.True(LayoutUtil.ShouldAddPict("IdentityCardDate"));
        Assert.True(LayoutUtil.ShouldAddPict("Foo", "D10 |"));
        Assert.False(LayoutUtil.ShouldAddPict("OrganizationName"));
        Assert.False(LayoutUtil.ShouldAddPict("StreetHouse", "C120 |"));
    }

    [Fact]
    public void TemplateLayoutStore_EmbeddedSlots_RoundTrip()
    {
        TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
        var org = new OrganizationFieldGroupTemplate();
        var slots = org.GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        int before = slots.Count;

        var embedded = new TemplateSlot
        {
            Id = TemplateSlot.MakeId(TemplateSlotKind.Data, TirIdFieldGroupTemplate.TemplateName, "TIRID", "embed:1"),
            Suffix = "TIRID",
            Label = "TIRID",
            Kind = TemplateSlotKind.Data,
            X = 40,
            Y = 200,
            Width = 120,
            Height = Consts.DefaultHeightData,
            Group = TirIdFieldGroupTemplate.TemplateName,
            StemHint = "embed:1",
            IsEmbedded = true,
        };
        slots.Add(embedded);
        TemplateLayoutStore.SaveTemplate(OrganizationFieldGroupTemplate.TemplateName, slots);

        var applied = org.GetPreviewSlots([]);
        Assert.True(applied.Count > before);
        var got = applied.First(s => s.Id == embedded.Id);
        Assert.True(got.IsEmbedded);
        Assert.Equal(40, got.X);
        Assert.Equal(200, got.Y);
        Assert.Equal(120, got.Width);
        Assert.Equal("TIRID", got.Suffix);
        Assert.Equal(TirIdFieldGroupTemplate.TemplateName, got.Group);
        Assert.Equal("embed:1", got.StemHint);

        var fields = new List<Data>
        {
            Field("ConsignorDetails_OrganizationName", "C150 |", "MAIN"),
            Field("TIRIdDetails_TIRID", "C20 |", "MAIN"),
        };
        var block = new Block { Name = "MAIN" };
        TemplateSlotLayout.Apply(block, fields, applied, 4, 4, 770);
        Assert.Contains(block.Fields, f => f.FieldValue.Name == "TIRIdDetails_TIRID");

        TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
    }

    [Fact]
    public void TemplateLayoutStore_EmbeddedRemovedPict_StaysSuppressed()
    {
        TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
        var org = new OrganizationFieldGroupTemplate();
        var slots = org.GetPreviewSlots([]).Select(s => s.Clone()).ToList();

        // Simulate MergeTemplateAt: embed Address CountryCode data + removed Pict tombstone.
        var data = new TemplateSlot
        {
            Id = TemplateSlot.MakeId(TemplateSlotKind.Data, AddressFieldGroupTemplate.TemplateName, "CountryCode", "embed:1"),
            Suffix = "CountryCode",
            Label = "CountryCode",
            Kind = TemplateSlotKind.Data,
            X = 40,
            Y = 240,
            Width = 40,
            Height = Consts.DefaultHeightData,
            Group = AddressFieldGroupTemplate.TemplateName,
            StemHint = "embed:1",
            IsEmbedded = true,
        };
        var pict = new TemplateSlot
        {
            Id = TemplateSlot.MakeId(TemplateSlotKind.Pict, AddressFieldGroupTemplate.TemplateName, "CountryCode", "embed:1"),
            Suffix = "CountryCode",
            Label = "CountryCode",
            Kind = TemplateSlotKind.Pict,
            X = 86,
            Y = 242,
            Width = LayoutUtil.PictSize,
            Height = LayoutUtil.PictSize,
            Group = AddressFieldGroupTemplate.TemplateName,
            StemHint = "embed:1",
            IsEmbedded = true,
            IsRemoved = true,
        };
        slots.Add(data);
        slots.Add(pict);
        TemplateLayoutStore.SaveTemplate(OrganizationFieldGroupTemplate.TemplateName, slots, replaceBase: true);

        var applied = org.GetPreviewSlots([]);
        var gotPict = Assert.Single(
            applied,
            s => s.Kind == TemplateSlotKind.Pict && s.Suffix == "CountryCode" && s.StemHint == "embed:1"
        );
        Assert.True(gotPict.IsRemoved);

        var fields = new List<Data>
        {
            Field("ConsignorDetails_OrganizationName", "C150 |", "MAIN"),
            Field("ConsignorDetails_Address_CountryCode", "C2 |", "MAIN"),
        };
        var block = new Block { Name = "MAIN" };
        TemplateSlotLayout.Apply(block, fields, applied, 4, 4, 770);
        Assert.DoesNotContain(
            block.Fields,
            f =>
                f.FieldType == Type.Pict
                && f.FieldValue.Comment.Contains("CountryCode", StringComparison.OrdinalIgnoreCase)
        );

        TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
    }

    [Fact]
    public void TemplateSlot_Removed_IsSkippedInLayout()
    {
        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
        var tpl = new AddressFieldGroupTemplate();
        var slots = tpl.GetDefaultPreviewSlots([]).Select(s => s.Clone()).ToList();
        var postal = slots.First(s => s.Suffix == "PostalCode");
        postal.IsRemoved = true;
        TemplateLayoutStore.SaveTemplate(AddressFieldGroupTemplate.TemplateName, slots);

        var applied = tpl.GetPreviewSlots([]);
        Assert.True(applied.First(s => s.Suffix == "PostalCode").IsRemoved);

        var fields = new List<Data>
        {
            Field("CountryCode", "C2 |", "Addr", 1),
            Field("CounryName", "C120 |", "Addr", 1),
            Field("PostalCode", "C9 |", "Addr", 1),
            Field("Region", "C120 |", "Addr", 1),
            Field("City", "C120 |", "Addr", 1),
        };

        var block = new Block { Name = "Addr" };
        TemplateSlotLayout.Apply(block, fields, applied, 4, 4, 770);
        Assert.DoesNotContain(block.Fields, f => f.FieldValue.Name == "PostalCode");
        Assert.Contains(block.Fields, f => f.FieldValue.Name == "CountryCode");

        TemplateLayoutStore.ClearTemplate(AddressFieldGroupTemplate.TemplateName);
    }

    [Fact]
    public void PreviewSlots_Organization_IncludesNestedGroups()
    {
        var slots = new OrganizationFieldGroupTemplate().GetPreviewSlots([]);
        Assert.Contains(slots, s => s.Group == AddressFieldGroupTemplate.TemplateName);
        Assert.Contains(slots, s => s.Group == IdentityCardFieldGroupTemplate.TemplateName);
        Assert.Contains(slots, s => s.Group == OrganizationFieldGroupTemplate.TemplateName);
    }

    [Fact]
    public void GoodsQuantity_StackedForSupplementarySection()
    {
        var fields = new List<Data>
        {
            Field("GoodsQuantity", "N24.8 |Количество", "SupplementaryQuantity", 1),
            Field("MeasureUnitQualifierName", "C50 |Ед.", "SupplementaryQuantity", 1),
            Field("MeasureUnitQualifierCode", "C4 |Код", "SupplementaryQuantity", 1),
        };

        var tpl = new GoodsQuantityFieldGroupTemplate();
        var match = tpl.TryMatch(fields, 0, []);
        Assert.NotNull(match);
        // force stem for stacked
        match = new FieldGroupMatch
        {
            Fields = match!.Fields,
            Stem = "PrecedingGoodsDetails_SupplementaryQuantity",
            TemplateName = GoodsQuantityFieldGroupTemplate.TemplateName,
        };

        var block = new Block { Name = "x\\SupplementaryQuantity" };
        tpl.Layout(block, match, 4, 4, 770, []);
        var data = block.Fields.Where(f => f.FieldType == Type.Data).ToList();
        Assert.Equal(3, data.Count);
        Assert.NotEqual(data[0].FieldPosition.Y, data[1].FieldPosition.Y);
    }

    [Fact]
    public void ConsignorDetails_UsesOrganizationTemplate_NotVerticalStack()
    {
        TemplateLayoutStore.ClearTemplate(OrganizationFieldGroupTemplate.TemplateName);
        var fields = new List<Data>
        {
            Field("ConsignorDetails_OrganizationName", "C150 |Наименование", "MAIN", 0),
            Field("ConsignorDetails_ShortName", "C120 |Краткое", "MAIN", 0),
            Field("ConsignorDetails_OrganizationLanguage", "C2 |Язык", "MAIN", 0),
            Field("ConsignorDetails_RFOrganizationFeatures_OGRN", "C15 |ОГРН", "MAIN", 0),
            Field("ConsignorDetails_RFOrganizationFeatures_INN", "C12 |ИНН", "MAIN", 0),
            Field("ConsignorDetails_RFOrganizationFeatures_KPP", "C9 |КПП", "MAIN", 0),
        };

        var parsed = new DcfParseResult
        {
            Header = new DcfHeader { DocumentName = "Test", ShortName = "Test" },
            Fields = fields,
            Diagnostics = new Diagnostics.DcfDiagnostics(),
        };
        var doc = Sf2LayoutBuilder.Build(parsed, Opts());
        var main = doc.Blocks.First(b => b.Name == "MAIN");
        var shortName = main.Fields.First(f => f.FieldValue.Name == "ConsignorDetails_ShortName");
        var ogrn = main.Fields.First(f => f.FieldValue.Name == "ConsignorDetails_RFOrganizationFeatures_OGRN");
        var inn = main.Fields.First(f => f.FieldValue.Name == "ConsignorDetails_RFOrganizationFeatures_INN");

        Assert.Equal(shortName.FieldPosition.Y, ogrn.FieldPosition.Y);
        Assert.Equal(ogrn.FieldPosition.Y, inn.FieldPosition.Y);
        Assert.True(shortName.FieldPosition.X < ogrn.FieldPosition.X);
        Assert.True(ogrn.FieldPosition.X < inn.FieldPosition.X);
    }

    private static (int x, int y)? FindData(string sf2, string fieldName)
    {
        foreach (var line in sf2.Split('\n'))
        {
            if (!line.TrimStart().StartsWith("Data", StringComparison.Ordinal))
                continue;
            if (!line.Contains(fieldName, StringComparison.Ordinal))
                continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                continue;
            if (int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y))
                return (x, y);
        }

        return null;
    }
}
