namespace DcfToSf2.Diagnostics;

internal sealed class ConsoleConversionLog : IConversionLog
{
    public static ConsoleConversionLog Instance { get; } = new();

    public void Info(string message) => Console.WriteLine(message);

    public void Success(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void Warning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
