namespace NetworkMonitor.Core.Widget
{
    // The invisible resize border around a frameless window, in physical pixels. Windows counts it in
    // both GetWindowRect and AppWindow, but nobody can see it — so a width derived from what the
    // sections need, or a clamp tested against a display edge, has to say which of the two boxes it
    // means. Three separate defects came from not saying.
    public readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
    {
        public int Horizontal => Left + Right;

        public int Vertical => Top + Bottom;
    }
}
