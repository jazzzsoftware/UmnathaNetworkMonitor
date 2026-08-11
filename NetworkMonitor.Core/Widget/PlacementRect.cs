namespace NetworkMonitor.Core.Widget
{
    // A plain rectangle in physical pixels. Deliberately not Windows.Graphics.RectInt32: that type
    // comes from the Windows SDK projections and is unavailable to a net10.0 library, which is the
    // whole reason the placement arithmetic could not be tested where it lived.
    public readonly record struct PlacementRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;

        public int Bottom => Y + Height;
    }
}
