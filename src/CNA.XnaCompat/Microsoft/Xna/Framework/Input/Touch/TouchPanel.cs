namespace Microsoft.Xna.Framework.Input.Touch;

/// <summary>XNA 4.0-compatible <c>TouchPanel</c>. A thin re-typing facade over
/// <see cref="CNA.Input.Touch.TouchPanel"/> -- static classes cannot be subclassed, so every
/// member forwards, exactly as <see cref="Microsoft.Xna.Framework.Input.Keyboard"/> already
/// does.</summary>
public static class TouchPanel
{
    private static nint? _windowHandleOverride;

    public static TouchCollection GetState() => TouchCollection.FromFramework(CNA.Input.Touch.TouchPanel.GetState());

    public static TouchPanelCapabilities GetCapabilities() =>
        TouchPanelCapabilities.FromFramework(CNA.Input.Touch.TouchPanel.GetCapabilities());

    public static int DisplayWidth
    {
        get => CNA.Input.Touch.TouchPanel.DisplayWidth;
        set => CNA.Input.Touch.TouchPanel.DisplayWidth = value;
    }

    public static int DisplayHeight
    {
        get => CNA.Input.Touch.TouchPanel.DisplayHeight;
        set => CNA.Input.Touch.TouchPanel.DisplayHeight = value;
    }

    public static DisplayOrientation DisplayOrientation
    {
        get => (DisplayOrientation)(int)CNA.Input.Touch.TouchPanel.DisplayOrientation;
        set => CNA.Input.Touch.TouchPanel.DisplayOrientation = (CNA.DisplayOrientation)(int)value;
    }

    public static GestureType EnabledGestures
    {
        get => (GestureType)(int)CNA.Input.Touch.TouchPanel.EnabledGestures;
        set => CNA.Input.Touch.TouchPanel.EnabledGestures = (CNA.Input.Touch.GestureType)(int)value;
    }

    public static bool IsGestureAvailable =>
        XnaExceptions.Guard(() => CNA.Input.Touch.TouchPanel.IsGestureAvailable);

    public static GestureSample ReadGesture() => GestureSample.FromFramework(CNA.Input.Touch.TouchPanel.ReadGesture());

    public static nint WindowHandle
    {
        get => _windowHandleOverride ?? CNA.Input.Touch.TouchPanel.WindowHandle;
        set => _windowHandleOverride = value;
    }
}
