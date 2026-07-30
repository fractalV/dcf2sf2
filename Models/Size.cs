namespace DcfToSf2
{
    public struct Size(int length, int h)
    {
        public int Length = length, Height = h;
        public override string ToString()
        {
            return $"{this.Length} {this.Height}";
        }
    }

}
