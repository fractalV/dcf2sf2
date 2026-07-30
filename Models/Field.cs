using System.Text;

namespace DcfToSf2;

internal struct Field(Type type, Size size, Position position, Value value)
{
    public Type FieldType { get; init; } = type;
    public Size FieldSize { get; init; } = size;
    public Position FieldPosition { get; init; } = position;
    public Value FieldValue { get; init; } = value;

    public override readonly string ToString()
    {
        string typeName = FieldType switch
        {
            Type.Data => "Data",
            Type.Text => "Text",
            Type.Line => "Line",
            Type.Pict => "Pict",
            Type.Rect => "Rect",
            _ => "Data",
        };

        var sb = new StringBuilder();
        sb.Append(typeName);
        sb.Append($"{FieldPosition.X,12}");
        sb.Append($"{FieldPosition.Y,6}");
        sb.Append($"{FieldSize.Length,6}");
        sb.Append($"{FieldSize.Height,5}");
        var valueText = FieldValue.ToString();
        if (!string.IsNullOrEmpty(valueText))
        {
            sb.Append(' ');
            sb.Append(valueText);
        }

        return sb.ToString();
    }
}
