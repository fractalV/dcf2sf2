namespace DcfToSf2
{
    public struct Position(int x, int y)
    {
       public int X = x, Y = y;

        public override readonly string ToString()
        {
            return $"{this.X} {this.Y}";
        }
    }
}
