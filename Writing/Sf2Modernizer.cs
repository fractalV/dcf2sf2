using System.Text;
using DcfToSf2.Diagnostics;
using DcfToSf2.Layout;

namespace DcfToSf2.Writing;

internal static class Sf2Modernizer
{
    /// <summary>
    /// Appends blocks from <paramref name="fresh"/> that are missing in the existing SF2.
    /// Existing sections are left byte-stable (original text preserved, new sections appended).
    /// </summary>
    /// <returns>0 on success (including no changes), 1 on error.</returns>
    public static int Apply(
        DirectoryInfo outputDirectory,
        Sf2Document fresh,
        IConversionLog? log = null
    )
    {
        log ??= ConsoleConversionLog.Instance;
        string path = Sf2Writer.GetPath(outputDirectory, fresh.DocumentName);

        if (!File.Exists(path))
        {
            log.Error($"SF2 не найден для модернизации: {path}");
            return 1;
        }

        var existing = Sf2Reader.Read(path);
        var toAppend = new List<Block>();
        var addedNames = new List<string>();

        foreach (var block in fresh.Blocks)
        {
            string sectionName = Sf2Writer.NormalizeSectionName(block.Name);
            if (existing.SectionNames.Contains(sectionName))
            {
                log.Info($"Skipped existing: [{sectionName}]");
                LogSkippedNewFields(existing, block, sectionName, log);
                continue;
            }

            toAppend.Add(block);
            addedNames.Add(sectionName);
        }

        if (toAppend.Count == 0)
        {
            log.Info("Изменений нет.");
            return 0;
        }

        var appendLines = new List<string>();
        foreach (var block in toAppend)
        {
            string sectionName = Sf2Writer.NormalizeSectionName(block.Name);
            appendLines.AddRange(Sf2Writer.RenderBlock(block));
            log.Success($"Added block: [{sectionName}]");
            LogParentHyperlinkHint(existing, sectionName, block, log);
        }

        AppendSections(path, existing.OriginalText, appendLines);
        log.Success("Файл обновлён: " + path);
        log.Info($"Добавлено блоков: {addedNames.Count} ({string.Join(", ", addedNames.Select(n => $"[{n}]"))})");
        return 0;
    }

    private static void AppendSections(string path, string originalText, List<string> appendLines)
    {
        Encoding encoding = Encoding.GetEncoding(1251);
        string text = originalText;
        if (text.Length > 0 && !text.EndsWith('\n') && !text.EndsWith('\r'))
            text += Environment.NewLine;

        using var sw = new StreamWriter(path, false, encoding);
        sw.Write(text);
        foreach (var line in appendLines)
            sw.WriteLine(line);
    }

    private static void LogParentHyperlinkHint(
        Sf2FileContent existing,
        string sectionName,
        Block block,
        IConversionLog log
    )
    {
        string? parent = GetParentSectionName(sectionName);
        if (parent is null || !existing.SectionNames.Contains(parent))
            return;

        string leaf = sectionName.Contains('\\')
            ? sectionName[(sectionName.LastIndexOf('\\') + 1)..]
            : sectionName;
        string label = (block.Title ?? string.Empty)
            .Replace("%d", "", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrEmpty(label))
            label = leaf;

        log.Warning(
            $"Добавьте ссылку `@{leaf}@{label}` в [{parent}] вручную"
        );
    }

    private static string? GetParentSectionName(string sectionName)
    {
        int idx = sectionName.LastIndexOf('\\');
        if (idx > 0)
            return sectionName[..idx];

        if (sectionName.Equals(Consts.DefaultBlockName, StringComparison.OrdinalIgnoreCase))
            return null;

        return Consts.DefaultBlockName;
    }

    private static void LogSkippedNewFields(
        Sf2FileContent existing,
        Block block,
        string sectionName,
        IConversionLog log
    )
    {
        var existingNames = Sf2Reader.GetDataFieldNames(existing, sectionName);
        var missing = new List<string>();

        foreach (var field in block.Fields)
        {
            if (field.FieldType != Type.Data)
                continue;

            string name = field.FieldValue.Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!existingNames.Contains(name))
                missing.Add(name);
        }

        if (missing.Count == 0)
            return;

        log.Warning(
            $"Skipped new fields in existing block [{sectionName}]: {string.Join(", ", missing)}"
        );
    }
}
