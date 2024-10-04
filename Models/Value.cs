namespace DcfToSf2
{
    public struct Value(string name, string comment)
    {
        public string Name = name;
        public string Comment = comment;

        public override string ToString()
        {
            if (Comment != null)
            {
                return $"{this.Name} {Consts.SpecialDelimetr}{this.Comment}";
            } else 
            { 
                return Name ; 
            }
            
        }
    }
}
