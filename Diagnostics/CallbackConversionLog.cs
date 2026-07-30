namespace DcfToSf2.Diagnostics;

internal sealed class CallbackConversionLog : IConversionLog
{
    private readonly Action<string, ConversionLogLevel> _write;

    public CallbackConversionLog(Action<string, ConversionLogLevel> write)
    {
        _write = write;
    }

    public void Info(string message) => _write(message, ConversionLogLevel.Info);

    public void Success(string message) => _write(message, ConversionLogLevel.Success);

    public void Warning(string message) => _write(message, ConversionLogLevel.Warning);

    public void Error(string message) => _write(message, ConversionLogLevel.Error);
}

internal enum ConversionLogLevel
{
    Info,
    Success,
    Warning,
    Error,
}
