using DcfToSf2.Diagnostics;
using DcfToSf2.Layout;
using DcfToSf2.Parsing;
using DcfToSf2.Writing;

namespace DcfToSf2.MainOperations;

internal static class MainOperations
{
    public static int ConvertFile(ConversionOptions options)
    {
        try
        {
            if (!options.InputFile.Exists)
            {
                Console.Error.WriteLine($"Файл не найден: {options.InputFile.FullName}");
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

            WarningsWriter.Write(options.OutputDirectory, documentName, parsed.Diagnostics);

            if (parsed.Diagnostics.HasErrors)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Критические ошибки DCF — SF2 не создан.");
                Console.ResetColor();
                return 2;
            }

            var document = Sf2LayoutBuilder.Build(parsed, options);

            // Console preview
            foreach (var line in Sf2Writer.Render(document))
                Console.WriteLine(line);

            Sf2Writer.Write(options.OutputDirectory, document);
            ListSf2Writer.Write(options.OutputDirectory, parsed, document, options);
            return 0;
        }
        catch (NotSupportedException)
        {
            Console.WriteLine("Install System.Text.Encoding.CodePages...");
            Console.WriteLine("dotnet add package System.Text.Encoding.CodePages");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }
}
