using DcfToSf2.Diagnostics;
using DcfToSf2.Parsing;
using DcfToSf2.Ui;
using Xunit;

namespace DcfToSf2.Tests;

public class IgnoreFieldsTreeBuilderTests
{
    private static Data Field(
        string name,
        string value,
        string path,
        int level,
        int line,
        string rus = ""
    ) =>
        new()
        {
            Level = level,
            NameBlock = path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path,
            Path = path,
            RusNameBlock = rus,
            SourceLine = line,
            FieldElement = new Element { ElementName = name, ElementValue = value },
        };

    private static DcfParseResult Parsed(params Data[] fields) =>
        new()
        {
            Header = new DcfHeader { DocumentName = "TEST", ShortName = "TEST", FullName = "TEST" },
            Fields = fields.ToList(),
            Diagnostics = new DcfDiagnostics(),
        };

    [Fact]
    public void Build_NestsPathSegments()
    {
        var parsed = Parsed(
            Field("A", "C10 |", "MAIN", 0, 1),
            Field("B", "C10 |", @"MAIN\Goods", 1, 2),
            Field("C", "C10 |", @"MAIN\Goods\Item", 2, 3)
        );

        var roots = IgnoreFieldsTreeBuilder.Build(parsed, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Single(roots);
        Assert.Equal("MAIN", roots[0].Path);
        Assert.Contains(roots[0].ChildBlocks, c => c.Path.Equals(@"MAIN\Goods", StringComparison.OrdinalIgnoreCase));
        var goods = roots[0].ChildBlocks.First(c => c.Path.Equals(@"MAIN\Goods", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(goods.ChildBlocks, c => c.Path.Equals(@"MAIN\Goods\Item", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_NestsBSectionWithNamespace()
    {
        var parsed = Parsed(
            Field("Plain", "C10 |обычное", "MAIN", 0, 1),
            Field(
                "GTDID",
                "B |* Регистрационный номер таможенной декларации NAMESPACE=\"GTDID\"",
                "MAIN",
                0,
                2
            ),
            Field("GTDID_CustomsCode", "C8 |код", "MAIN", 0, 3),
            Field("OtherCode", "C8 |другое NAMESPACE=\"GTDID\"", "MAIN", 0, 4)
        );

        var roots = IgnoreFieldsTreeBuilder.Build(parsed, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        Assert.Single(roots);
        var main = roots[0];
        Assert.Contains(main.Fields, f => f.Name == "Plain");
        Assert.DoesNotContain(main.Fields, f => f.Name == "GTDID");

        var gtdid = Assert.Single(main.ChildBlocks, c => c.IsSection && c.SectionKey == "GTDID");
        Assert.Contains("Регистрационный", gtdid.Title, StringComparison.Ordinal);
        Assert.Contains(gtdid.Fields, f => f.Name == "GTDID_CustomsCode");
        Assert.Contains(gtdid.Fields, f => f.Name == "OtherCode");
    }
}
