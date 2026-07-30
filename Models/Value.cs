namespace DcfToSf2;

public struct Value(string name, string comment)
{
    public string Name = name;
    public string Comment = comment;

    public override readonly string ToString()
    {
        if (string.IsNullOrEmpty(Comment))
            return Name ?? string.Empty;

        if (string.IsNullOrEmpty(Name))
            return $"{Consts.SpecialDelimetr}{Comment}";

        return $"{Name} {Consts.SpecialDelimetr}{Comment}";
    }
}
