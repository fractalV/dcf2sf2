using System.CommandLine;
using System.Runtime.InteropServices;
using System.Text;

namespace DcfToSf2;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    private const int AttachParentProcess = -1;

    [STAThread]
    static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Length > 0)
            return RunCli(args);

        var app = new App();
        app.InitializeComponent();
        app.Run();
        return 0;
    }

    private static int RunCli(string[] args)
    {
        if (!AttachConsole(AttachParentProcess))
            AllocConsole();

        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;

        var fileOption = new Option<FileInfo>("--file", "-f")
        {
            Description = "Входной DCF файл",
            Required = true,
        };

        var outOption = new Option<DirectoryInfo?>("--out", "-o")
        {
            Description = "Каталог вывода (по умолчанию — каталог DCF)",
        };

        var twoColumnOption = new Option<bool>("--two-column", "-2")
        {
            Description =
                "Двухколоночная раскладка пар организаций (Seller|Buyer, Consignor|Consignee)",
        };

        var ignoreOption = new Option<string?>("--ignore-fields")
        {
            Description = "Поля для пропуска (суффиксы через запятую), напр. Fax,Telex",
        };

        var forceListOption = new Option<bool>("--force-list")
        {
            Description = "Перезаписать List.sf2 если уже существует",
        };

        var rootCommand = new RootCommand("Утилита для конвертации dcf в sf2");
        rootCommand.Options.Add(fileOption);
        rootCommand.Options.Add(outOption);
        rootCommand.Options.Add(twoColumnOption);
        rootCommand.Options.Add(ignoreOption);
        rootCommand.Options.Add(forceListOption);

        rootCommand.SetAction(parseResult =>
        {
            var file = parseResult.GetValue(fileOption)!;
            var outDir = parseResult.GetValue(outOption);
            var output =
                outDir
                ?? file.Directory
                ?? new DirectoryInfo(Environment.CurrentDirectory);

            var options = new ConversionOptions
            {
                InputFile = file,
                OutputDirectory = output,
                TwoColumn = parseResult.GetValue(twoColumnOption),
                ForceList = parseResult.GetValue(forceListOption),
                IgnoreFields = ConversionOptions.ParseIgnoreFields(
                    parseResult.GetValue(ignoreOption)
                ),
            };

            return MainOperations.MainOperations.ConvertFile(options);
        });

        return rootCommand.Parse(args).Invoke();
    }
}
