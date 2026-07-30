namespace DcfToSf2.Diagnostics;

internal interface IConversionLog
{
    void Info(string message);
    void Success(string message);
    void Warning(string message);
    void Error(string message);
}
