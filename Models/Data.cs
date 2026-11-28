namespace DcfToSf2;

internal class Data
{
    public int Level { get; set; }
    public required string NameBlock { get; set; }
    public string Path { get; set; } = Consts.DefaultBlockName;
    public string? RusNameBlock { get; set; }
    public required Element FieldElement { get; set; }
    public int SourceLine { get; set; }
}

public class Element
{
    public required string ElementName { get; set; }
    public required string ElementValue { get; set; }
}
