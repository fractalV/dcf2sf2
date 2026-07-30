using System.Text;

namespace DcfToSf2.Diagnostics;

internal static class WarningsWriter
{
    public static string GetWarningsPath(DirectoryInfo outputDirectory, string documentName) =>
        Path.Combine(outputDirectory.FullName, $"{documentName}.dcf.warnings.txt");

    public static void Write(
        DirectoryInfo outputDirectory,
        string documentName,
        DcfDiagnostics diagnostics,
        IConversionLog? log = null
    )
    {
        log ??= ConsoleConversionLog.Instance;
        string path = GetWarningsPath(outputDirectory, documentName);

        if (!diagnostics.HasAny)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        Directory.CreateDirectory(outputDirectory.FullName);
        var sb = new StringBuilder();
        sb.AppendLine($"# DCF diagnostics for {documentName}");
        sb.AppendLine($"# source validation report");
        foreach (var item in diagnostics.Items)
            sb.AppendLine(item.ToString());
        sb.AppendLine($"errors={diagnostics.ErrorCount} warnings={diagnostics.WarningCount}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

        string message =
            $"Diagnostics: errors={diagnostics.ErrorCount} warnings={diagnostics.WarningCount} → {path}";
        if (diagnostics.HasErrors)
            log.Error(message);
        else
            log.Warning(message);
    }
}
