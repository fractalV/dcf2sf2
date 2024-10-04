using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DcfToSf2
{
    internal class Block
    {
        public string Name = Consts.DefaultBlockName;   
        public List<Field> Fields = [];

        public override string ToString() {
            if (string.IsNullOrEmpty(Name))  {
                throw new ArgumentNullException();
            }            
            StringBuilder stringBuilder = new();        
            stringBuilder.Append(GetList());
            return stringBuilder.ToString();
        }

        private StringBuilder GetList() {
            StringBuilder sb = new(Fields.Count);
            foreach (Field field in Fields)
            {
                //Учитываем только поля с комментариями
                if (!string.IsNullOrEmpty(field.FieldValue.Comment)) 
                    sb.AppendLine(field.ToString());
            }
            return sb;
        }
    }
}
