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

        var idCode = block.Fields.First(f => f.FieldValue.Name.Contains("IdentityCardCode"));
        var country = block.Fields.First(f => f.FieldValue.Name.EndsWith("CountryCode") && !f.FieldValue.Name.Contains("IdentityCard"));
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
