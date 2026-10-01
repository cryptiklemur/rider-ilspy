namespace Fixture.Symbols
{
    public delegate void NeedleHandler();

    public class NeedleType
    {
        public int NeedleField;
        public int NeedleProperty { get; set; }
        public event NeedleHandler NeedleEvent;
        public void NeedleMethod() { NeedleEvent?.Invoke(); }
    }

    public class Unrelated
    {
        public int Plain;
    }
}
