using System.Text;

namespace DcfToSf2;

internal class Block
{
    public string Name = Consts.DefaultBlockName;
    public string Title = string.Empty;
    public bool IsRepeating;
    public List<Field> Fields = [];

    public override string ToString()
    {
        if (string.IsNullOrEmpty(Name))
            throw new ArgumentNullException(nameof(Name));

        return GetList().ToString();
    }

    private StringBuilder GetList()
    {
        var sb = new StringBuilder();
        foreach (Field field in Fields)
        {
            if (field.FieldType == Type.Text && string.IsNullOrEmpty(field.FieldValue.Comment))
                continue;

            sb.AppendLine(field.ToString());
        }

        return sb;
    }

    public int MaxY()
    {
        int max = Consts.Default_Y;
        foreach (var field in Fields)
        {
            int bottom = field.FieldPosition.Y + field.FieldSize.Height;
            if (bottom > max)
                max = bottom;
        }

        return max;
    }
}
