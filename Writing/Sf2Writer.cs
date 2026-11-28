using System.Text;
using DcfToSf2.Diagnostics;
using DcfToSf2.Layout;

namespace DcfToSf2.Writing;

internal static class Sf2Writer
{
    public static string GetPath(DirectoryInfo outputDirectory, string documentName) =>
        Path.Combine(outputDirectory.FullName, $"{documentName}.sf2");

    public static string NormalizeSectionName(string blockName)
    {
        string sectionName = blockName.Replace("MAIN\\", "", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(sectionName))
            sectionName = Consts.DefaultBlockName;
        return sectionName;
    }

    public static void Write(
        DirectoryInfo outputDirectory,
        Sf2Document document,
        IConversionLog? log = null
    )
    {
        log ??= ConsoleConversionLog.Instance;
        Directory.CreateDirectory(outputDirectory.FullName);
        string path = GetPath(outputDirectory, document.DocumentName);
        var lines = Render(document);
        using var sw = new StreamWriter(path, false, Encoding.GetEncoding(1251));
        foreach (var line in lines)
            sw.WriteLine(line);

        log.Success("Файл записан: " + path);
    }

    public static List<string> Render(Sf2Document document)
    {
        List<string> lines = [$"DOCUMENT={document.DocumentName}"];

        foreach (var block in document.Blocks)
            lines.AddRange(RenderBlock(block));

        return lines;
    }

    public static List<string> RenderBlock(Block block)
    {
        string sectionName = NormalizeSectionName(block.Name);
        var lines = new List<string> { $"[{sectionName}]" };

        int height = Math.Max(block.MaxY() + Consts.DefaultHeightData + 20, 40);
        string title = block.Title ?? string.Empty;
        if (block.IsRepeating && !string.IsNullOrEmpty(title) && !title.Contains("%d"))
            title = title.TrimEnd() + " %d";

        lines.Add($"{Consts.DefaultWidthBlock}x{height} 0 0 `{title}");

        string body = block.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(body))
        {
            foreach (var row in body.Replace("\r\n", "\n").Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(row))
                    lines.Add(row);
            }
        }

        lines.Add("/");
        return lines;
    }
}
