using System.Text;
using DcfToSf2.Layout;

namespace DcfToSf2.Writing;

internal static class Sf2Writer
{
    public static string GetPath(DirectoryInfo outputDirectory, string documentName) =>
        Path.Combine(outputDirectory.FullName, $"{documentName}.sf2");

    public static void Write(DirectoryInfo outputDirectory, Sf2Document document)
    {
        Directory.CreateDirectory(outputDirectory.FullName);
        string path = GetPath(outputDirectory, document.DocumentName);
        var lines = Render(document);
        using var sw = new StreamWriter(path, false, Encoding.GetEncoding(1251));
        foreach (var line in lines)
            sw.WriteLine(line);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Файл записан: " + path);
        Console.ResetColor();
    }

    public static List<string> Render(Sf2Document document)
    {
        List<string> lines = [$"DOCUMENT={document.DocumentName}"];

        foreach (var block in document.Blocks)
        {
            string sectionName = block.Name.Replace("MAIN\\", "", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(sectionName))
                sectionName = Consts.DefaultBlockName;

            lines.Add($"[{sectionName}]");

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
        }

        return lines;
    }
}
