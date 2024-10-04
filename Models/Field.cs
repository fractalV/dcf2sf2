using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DcfToSf2
{
    internal struct Field(Type Type, Size Size, Position Position, Value Value)
    {
        public Type FieldType { get; init; } = Type;
        public Size FieldSize { get; init; } = Size;
        public Position FieldPosition { get; init; } = Position;
        public Value FieldValue { get; init; } = Value;

        public override readonly string ToString()
        {
            string space = " ";
            StringBuilder res = new();
            if (FieldType == Type.Data)
            {
                res.Append("Data");
                res.Append(space);
            } else
            {
                res.Append("Text");
                res.Append(space);
            }
            res.Append(FieldPosition.ToString());
            res.Append(space);
            res.Append(FieldSize.ToString());
            res.Append(space);
            res.Append(FieldValue.ToString());

            return res.ToString();
        }
    }
}
