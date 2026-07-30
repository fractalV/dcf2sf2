using System.Text;
using DcfToSf2.Layout;
using DcfToSf2.Parsing;
using DcfToSf2.Writing;
using Xunit;

namespace DcfToSf2.Tests;

public class SmokeTests
{
    static SmokeTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static readonly string AltaDir = @"d:\Alta\data\ed\5_27_1";

    private static bool AltaAvailable() =>
        File.Exists(Path.Combine(AltaDir, "BILLOFLADING.XSD.Dcf"))
        && File.Exists(Path.Combine(AltaDir, "COMMERCIALINVOICE.XSD.Dcf"));

    [Fact]
    public void BillOfLading_Default_HasExpectedStructure()
    {
        if (!AltaAvailable())
            return;

        var file = new FileInfo(Path.Combine(AltaDir, "BILLOFLADING.XSD.Dcf"));
        var outDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "dcf2sf2_smoke_" + Guid.NewGuid().ToString("N")));
        outDir.Create();

        try
        {
            var options = new ConversionOptions
            {
                InputFile = file,
                OutputDirectory = outDir,
                TwoColumn = false,
                ForceList = true,
                IgnoreFields = [],
            };

            int code = MainOperations.MainOperations.ConvertFile(options);
            Assert.Equal(0, code);

            string sf2Path = Path.Combine(outDir.FullName, "BillOfLading.sf2");
            string listPath = Path.Combine(outDir.FullName, "BillOfLadingList.sf2");
            Assert.True(File.Exists(sf2Path));
            Assert.True(File.Exists(listPath));

            string text = File.ReadAllText(sf2Path, Encoding.GetEncoding(1251));
            Assert.Contains("DOCUMENT=BillOfLading", text);
            Assert.Contains("[Goods]", text);
            Assert.Contains("[MAIN]", text);
            Assert.Contains("[Goods\\Container]", text);
            Assert.Contains("Carrier_", text);
            Assert.Contains("Consignee_", text);
            Assert.Contains("Consignor_", text);
            Assert.Contains("@Container@", text);

            // Single-column: Carrier and Consignee should not share the same Y for OrganizationName
            var carrier = FindData(text, "Carrier_OrganizationName");
            var consignee = FindData(text, "Consignee_OrganizationName");
            Assert.NotNull(carrier);
            Assert.NotNull(consignee);
            Assert.NotEqual(carrier!.Value.y, consignee!.Value.y);

            string list = File.ReadAllText(listPath, Encoding.GetEncoding(1251));
            Assert.Contains("List=@FileDate", list);
            Assert.Contains("RegistrationDocument_PrDocumentNumber", list);
        }
        finally
        {
            try
            {
                outDir.Delete(true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    [Fact]
    public void CommercialInvoice_TwoColumn_PairsShareY()
    {
        if (!AltaAvailable())
            return;

        var file = new FileInfo(Path.Combine(AltaDir, "COMMERCIALINVOICE.XSD.Dcf"));
        var outDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "dcf2sf2_smoke_" + Guid.NewGuid().ToString("N")));
        outDir.Create();

        try
        {
            var options = new ConversionOptions
            {
                InputFile = file,
                OutputDirectory = outDir,
                TwoColumn = true,
                ForceList = true,
                IgnoreFields = [],
            };

            int code = MainOperations.MainOperations.ConvertFile(options);
            Assert.Equal(0, code);

            string text = File.ReadAllText(
                Path.Combine(outDir.FullName, "CommercialInvoice.sf2"),
                Encoding.GetEncoding(1251)
            );

            Assert.Contains("[InvoiceGoods]", text);
            Assert.Contains("[MAIN]", text);
            Assert.Contains("[PayDocsRegistration]", text);

            var seller = FindData(text, "Seller_OrganizationName");
            var buyer = FindData(text, "Buyer_OrganizationName");
            Assert.NotNull(seller);
            Assert.NotNull(buyer);
            Assert.Equal(seller!.Value.y, buyer!.Value.y);
            Assert.True(seller.Value.x < buyer.Value.x);

            Assert.Contains("Line", text);
            Assert.Contains("Pict", text);
        }
        finally
        {
            try
            {
                outDir.Delete(true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    [Fact]
    public void IgnoreFields_SkipsFax()
    {
        if (!AltaAvailable())
            return;

        var file = new FileInfo(Path.Combine(AltaDir, "BILLOFLADING.XSD.Dcf"));
        var parsed = DcfParser.Parse(file);
        var options = new ConversionOptions
        {
            InputFile = file,
            OutputDirectory = new DirectoryInfo(Path.GetTempPath()),
            IgnoreFields = ConversionOptions.ParseIgnoreFields("Fax,Telex"),
        };

        var doc = Sf2LayoutBuilder.Build(parsed, options);
        string rendered = string.Join("\n", Sf2Writer.Render(doc));
        Assert.DoesNotContain("Carrier_Fax", rendered);
        Assert.DoesNotContain("Carrier_Telex", rendered);
        Assert.Contains("Carrier_Phone", rendered);
    }

    [Fact]
    public void Parser_ValidatesNesting()
    {
        string bad =
            "[Test.Header]\r\nShortName=Test\r\n[Test.Fields]\r\nA=C10 |a\r\n}\r\n";
        string path = Path.Combine(Path.GetTempPath(), "bad_" + Guid.NewGuid().ToString("N") + ".dcf");
        File.WriteAllText(path, bad, Encoding.GetEncoding(1251));
        try
        {
            var parsed = DcfParser.Parse(new FileInfo(path));
            Assert.True(parsed.Diagnostics.HasAny);
            Assert.Contains(
                parsed.Diagnostics.Items,
                d => d.Category == Diagnostics.DiagnosticCategory.Nesting
            );
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static (int x, int y)? FindData(string sf2, string fieldName)
    {
        foreach (var line in sf2.Replace("\r\n", "\n").Split('\n'))
        {
            if (!line.StartsWith("Data", StringComparison.Ordinal))
                continue;
            if (!line.Contains(fieldName, StringComparison.OrdinalIgnoreCase))
                continue;

            // Data{x,12}{y,6}{w,6}{h,5} Name
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // After formatting: Data, x, y, w, h, name...
            if (parts.Length >= 5 && int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y))
                return (x, y);
        }

        return null;
    }
}
