using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DcfToSf2
{
    internal class Data
    {
        public int Level { get; set; }
        public required string NameBlock { get; set; }
        public string? RusNameBlock {get; set; }
        public required Element FieldElement { get; set;}      
    }

    public class Element
    {
        public required string ElementName { get; set; }
        public required string ElementValue { get; set; }
    }
}
