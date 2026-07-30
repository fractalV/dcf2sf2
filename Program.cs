using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text;

namespace DcfToSf2;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var fileOption = new Option<FileInfo>(
            name: "--file",
            description: "Входной DCF файл"
        )
        {
            IsRequired = true,
        };
        fileOption.AddAlias("-f");

        var outOption = new Option<DirectoryInfo?>(
            name: "--out",
            description: "Каталог вывода (по умолчанию — каталог DCF)"
        );
        outOption.AddAlias("-o");

        var twoColumnOption = new Option<bool>(
            name: "--two-column",
            description: "Двухколоночная раскладка пар организаций (Seller|Buyer, Consignor|Consignee)"
        );
        twoColumnOption.AddAlias("-2");

        var ignoreOption = new Option<string?>(
            name: "--ignore-fields",
            description: "Поля для пропуска (суффиксы через запятую), напр. Fax,Telex"
        );

        var forceListOption = new Option<bool>(
            name: "--force-list",
            description: "Перезаписать List.sf2 если уже существует"
        );

        var rootCommand = new RootCommand("Утилита для конвертации dcf в sf2");
        rootCommand.AddOption(fileOption);
        rootCommand.AddOption(outOption);
        rootCommand.AddOption(twoColumnOption);
        rootCommand.AddOption(ignoreOption);
        rootCommand.AddOption(forceListOption);

        int exitCode = 0;
        rootCommand.SetHandler(
            (file, outDir, twoColumn, ignoreFields, forceList) =>
            {
                var output =
                    outDir
                    ?? file.Directory
                    ?? new DirectoryInfo(Environment.CurrentDirectory);

                var options = new ConversionOptions
                {
                    InputFile = file,
                    OutputDirectory = output,
                    TwoColumn = twoColumn,
                    ForceList = forceList,
                    IgnoreFields = ConversionOptions.ParseIgnoreFields(ignoreFields),
                };

                exitCode = MainOperations.MainOperations.ConvertFile(options);
            },
            fileOption,
            outOption,
            twoColumnOption,
            ignoreOption,
            forceListOption
        );

        var parser = new CommandLineBuilder(rootCommand).UseDefaults().Build();
        await parser.InvokeAsync(args);
        return exitCode;
    }
}
