namespace DcfToSf2.Layout;

internal sealed class Sf2Document
{
    public required string DocumentName { get; init; }
    public List<Block> Blocks { get; } = [];
    public string ShortTitle { get; init; } = string.Empty;
}
