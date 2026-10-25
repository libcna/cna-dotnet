using CNA.Interop;

namespace CNA.Input;

/// <summary>A mouse snapshot taken by <see cref="Mouse.GetState"/>. See the "input as snapshots"
/// guidance in libcna/cna's docs/binding.md and docs/c-api/INPUT_SNAPSHOTS.md.</summary>
public readonly struct MouseState
{
    private readonly CnaMouseState _native;

    internal MouseState(CnaMouseState native)
    {
        _native = native;
    }

    public int X => _native.X;
    public int Y => _native.Y;
    public Point Position => new(X, Y);
    public int ScrollWheelValue => _native.ScrollWheelValue;
    public int HorizontalScrollWheelValue => _native.HorizontalScrollWheelValue;

    public ButtonState LeftButton => ToButtonState(0);
    public ButtonState MiddleButton => ToButtonState(1);
    public ButtonState RightButton => ToButtonState(2);
    public ButtonState XButton1 => ToButtonState(3);
    public ButtonState XButton2 => ToButtonState(4);

    private ButtonState ToButtonState(int bit) => _native.IsButtonDown(bit) ? ButtonState.Pressed : ButtonState.Released;
}
