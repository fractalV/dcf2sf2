using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Text;

namespace DcfToSf2
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            EncodingProvider provider = CodePagesEncodingProvider.Instance;
            Encoding.RegisterProvider(provider);

            var fileOption = new Option<FileInfo?>(
            name: "--file",           
            description: "Файл");
            fileOption.AddAlias("-f");

            var rootCommand = new RootCommand("Утилита для конвертации dcf в sf2");
            rootCommand.AddOption(fileOption);
            

            rootCommand.SetHandler((file) =>
            {                
                MainOperations.MainOperations.ConvertFile(file!);                
            },  fileOption);

            var commandLineBuilder = new CommandLineBuilder(rootCommand);

            commandLineBuilder.AddMiddleware(async (context, next) =>
            {
                await next(context);
            });

            commandLineBuilder.UseDefaults();
            Parser parser = commandLineBuilder.Build();

            await parser.InvokeAsync(args);
        }
    }
}
