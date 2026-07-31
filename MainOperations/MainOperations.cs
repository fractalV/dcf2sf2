using DcfToSf2.Diagnostics;
using DcfToSf2.Layout;
using DcfToSf2.Parsing;
using DcfToSf2.Writing;

namespace DcfToSf2.MainOperations;

internal static class MainOperations
{
    public static int ConvertFile(
        ConversionOptions options,
        IConversionLog? log = null,
        bool writePreview = true
    )
    {
        log ??= ConsoleConversionLog.Instance;

        try
        {
            // Apply saved template geometry from UI settings (if any)
            _ = Ui.UiSettings.Load();

            if (!options.InputFile.Exists)
            {
                log.Error($"Файл не найден: {options.InputFile.FullName}");
                return 1;
            }

            var parsed = DcfParser.Parse(options.InputFile);
            string documentName = string.IsNullOrWhiteSpace(parsed.Header.DocumentName)
                ? Path.GetFileNameWithoutExtension(options.InputFile.Name)
                : parsed.Header.DocumentName;

            // Normalize document name from *.XSD.Dcf filenames
            if (documentName.EndsWith(".XSD", StringComparison.OrdinalIgnoreCase))
                documentName = documentName[..^4];
            parsed.Header.DocumentName = documentName;

            WarningsWriter.Write(options.OutputDirectory, documentName, parsed.Diagnostics, log);

            if (parsed.Diagnostics.HasErrors)
            {
                log.Error("Критические ошибки DCF — SF2 не создан.");
                return 2;
            }

            var document = Sf2LayoutBuilder.Build(parsed, options);

            if (options.Modernize)
                return Sf2Modernizer.Apply(options.OutputDirectory, document, log);

            if (writePreview)
            {
                foreach (var line in Sf2Writer.Render(document))
                    log.Info(line);
            }

            Sf2Writer.Write(options.OutputDirectory, document, log);
            ListSf2Writer.Write(options.OutputDirectory, parsed, document, options, log);
            return 0;
        }
        catch (NotSupportedException)
        {
            log.Error("Install System.Text.Encoding.CodePages...");
            log.Info("dotnet add package System.Text.Encoding.CodePages");
            return 1;
        }
        catch (Exception ex)
        {
            log.Error(ex.ToString());
            return 1;
        }
    }
}
