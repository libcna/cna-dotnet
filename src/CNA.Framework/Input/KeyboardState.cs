using CNA.Interop;

namespace CNA.Input;

/// <summary>
/// A full-keyboard snapshot taken by <see cref="Keyboard.GetState()"/>. One native call per
/// snapshot, then every <see cref="IsKeyDown"/> check is local -- see the "input as snapshots"
/// guidance in libcna/cna's docs/binding.md and docs/c-api/INPUT_SNAPSHOTS.md.
/// </summary>
public readonly struct KeyboardState
{
    private readonly CnaKeyboardState _native;

    internal KeyboardState(CnaKeyboardState native)
    {
        _native = native;
    }

    public bool IsKeyDown(Keys key) => _native.IsKeyDown((int)key);

    public bool IsKeyUp(Keys key) => !IsKeyDown(key);

    /// <summary>Matches real XNA's indexer. Landed with <see cref="KeyState"/> in the WP16
    /// re-audit -- both were missing, and this is the only member that consumes that enum.</summary>
    public KeyState this[Keys key] => IsKeyDown(key) ? KeyState.Down : KeyState.Up;

    /// <summary>Every key currently down. Real XNA's <c>GetPressedKeys</c>; allocates a fresh array
    /// per call, as XNA's does.</summary>
    public Keys[] GetPressedKeys()
    {
        var pressed = new List<Keys>();
        foreach (Keys key in Enum.GetValues<Keys>())
        {
            if (key != Keys.None && IsKeyDown(key))
            {
                pressed.Add(key);
            }
        }

        return [.. pressed];
    }
}
