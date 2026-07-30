using System.Text;
using System.Xml.Linq;
using DcfToSf2.Diagnostics;

namespace DcfToSf2.Parsing;

internal static class XsdHints
{
    /// <summary>
    /// Loads documentation labels only. Does not affect block nesting.
    /// </summary>
    public static Dictionary<string, string> LoadLabels(FileInfo dcfFile, DcfDiagnostics diagnostics)
    {
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var xsd = FindSiblingXsd(dcfFile);
            if (xsd is null)
                return labels;

            XNamespace xs = "http://www.w3.org/2001/XMLSchema";
            var doc = XDocument.Load(xsd.FullName);

            var rootDoc = doc.Root?
                .Element(xs + "annotation")?
                .Element(xs + "documentation")?
                .Value?.Trim();
            if (!string.IsNullOrEmpty(rootDoc))
                labels["__root__"] = rootDoc;

            foreach (var el in doc.Descendants(xs + "element"))
            {
                string? name = el.Attribute("name")?.Value;
                if (string.IsNullOrEmpty(name))
                    continue;

                string? documentation = el.Element(xs + "annotation")?
                    .Element(xs + "documentation")?
                    .Value?.Trim();
                if (!string.IsNullOrEmpty(documentation))
                    labels.TryAdd(name, documentation);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Warn(DiagnosticCategory.Structure, $"failed to read sibling XSD for labels: {ex.Message}");
        }

        return labels;
    }

    public static FileInfo? FindSiblingXsd(FileInfo dcfFile)
    {
        string dir = dcfFile.DirectoryName ?? ".";
        string baseName = dcfFile.Name;

        // COMMERCIALINVOICE.XSD.Dcf → CommercialInvoice.xsd / COMMERCIALINVOICE.xsd
        string stripped = baseName;
        if (stripped.EndsWith(".Dcf", StringComparison.OrdinalIgnoreCase))
            stripped = stripped[..^4];
        if (stripped.EndsWith(".XSD", StringComparison.OrdinalIgnoreCase))
            stripped = stripped[..^4];
        if (stripped.EndsWith(".xsd", StringComparison.OrdinalIgnoreCase))
            stripped = stripped[..^4];

        var candidates = Directory
            .EnumerateFiles(dir, "*.xsd", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .Where(f =>
                string.Equals(Path.GetFileNameWithoutExtension(f.Name), stripped, StringComparison.OrdinalIgnoreCase)
            )
            .ToList();

        return candidates.FirstOrDefault();
    }
}
