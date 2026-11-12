using CNA.Interop;

namespace CNA.Input.Touch;

/// <summary>
/// Matches real XNA's <c>TouchPanel</c>: a static snapshot/queue facade over the touch device,
/// the same shape <see cref="Keyboard"/>/<see cref="Mouse"/>/<see cref="GamePad"/> already use
/// here.
///
/// Like those, every call passes <see cref="CnaAmbientGame.Current"/> rather than taking a game
/// parameter -- real XNA's <c>TouchPanel</c> is static with no game argument, while the ABI's
/// touch functions all require a game handle, so the ambient handle is what bridges the two. See
/// <see cref="Keyboard"/>, which established this pattern.
/// </summary>
public static class TouchPanel
{
    public static TouchCollection GetState()
    {
        var state = new CnaTouchState();
        CnaResult result = Native.cna_touch_get_state(CnaAmbientGame.Current, ref state);
        CnaException.ThrowIfFailed(result, nameof(GetState));
        return TouchCollection.FromNative(in state);
    }

    /// <summary>
    /// Matches real XNA's <c>GetCapabilities</c>: what the touch device actually reports.
    ///
    /// This used to call <c>cna_touch_capabilities_init</c>, which only fills a *default* value and
    /// touches no device at all -- so it answered "no touch device" on a machine that has one. The
    /// real query, <c>cna_touch_get_capabilities</c>, was sitting unbound; a sweep of unbound header
    /// functions found it. The two names are one word apart, which is presumably how it happened.
    /// </summary>
    public static TouchPanelCapabilities GetCapabilities()
    {
        var capabilities = new CnaTouchCapabilities();
        CnaResult result = Native.cna_touch_get_capabilities(CnaAmbientGame.Current, ref capabilities);
        CnaException.ThrowIfFailed(result, nameof(GetCapabilities));
        return TouchPanelCapabilities.FromNative(in capabilities);
    }

    /// <summary>The window touch input is bound to. Matches real XNA's
    /// <c>TouchPanel.WindowHandle</c>. Read-only here: the ABI exposes the getter only, and the
    /// window is the game's rather than something a caller reassigns.</summary>
    /// <summary>
    /// CNA's own switch (off by default, as in XNA and FNA): left-mouse-button input is also
    /// reported as touch input. Internal: the XNA facade turns it on for a Windows Phone title off
    /// a phone, where the emulator made the mouse the finger (cna-cs CSX-098).
    /// </summary>
    internal static bool MouseTouchEmulationEnabled
    {
        get
        {
            CnaResult result = Native.cna_touch_panel_get_mouse_touch_emulation_enabled_ext(CnaAmbientGame.Current, out byte enabled);
            CnaException.ThrowIfFailed(result, nameof(MouseTouchEmulationEnabled));
            return enabled != 0;
        }
        set
        {
            CnaResult result = Native.cna_touch_panel_set_mouse_touch_emulation_enabled_ext(CnaAmbientGame.Current, value ? (byte)1 : (byte)0);
            CnaException.ThrowIfFailed(result, nameof(MouseTouchEmulationEnabled));
        }
    }

    public static nint WindowHandle
    {
        get
        {
            CnaResult result = Native.cna_touch_panel_get_window_handle(CnaAmbientGame.Current, out ulong window);
            CnaException.ThrowIfFailed(result, nameof(WindowHandle));
            return unchecked((nint)window);
        }
    }

    public static int DisplayWidth
    {
        get
        {
            CnaResult result = Native.cna_touch_panel_get_display_width(CnaAmbientGame.Current, out int value);
            CnaException.ThrowIfFailed(result, nameof(DisplayWidth));
            return value;
        }
        set
        {
            CnaResult result = Native.cna_touch_panel_set_display_width(CnaAmbientGame.Current, value);
            CnaException.ThrowIfFailed(result, nameof(DisplayWidth));
        }
    }

    public static int DisplayHeight
    {
        get
        {
            CnaResult result = Native.cna_touch_panel_get_display_height(CnaAmbientGame.Current, out int value);
            CnaException.ThrowIfFailed(result, nameof(DisplayHeight));
            return value;
        }
        set
        {
            CnaResult result = Native.cna_touch_panel_set_display_height(CnaAmbientGame.Current, value);
            CnaException.ThrowIfFailed(result, nameof(DisplayHeight));
        }
    }

    public static DisplayOrientation DisplayOrientation
    {
        get
        {
            CnaResult result = Native.cna_touch_panel_get_display_orientation(CnaAmbientGame.Current, out uint value);
            CnaException.ThrowIfFailed(result, nameof(DisplayOrientation));
            return (DisplayOrientation)value;
        }
        set
        {
            CnaResult result = Native.cna_touch_panel_set_display_orientation(CnaAmbientGame.Current, (uint)value);
            CnaException.ThrowIfFailed(result, nameof(DisplayOrientation));
        }
    }

    /// <summary>Which gestures <see cref="ReadGesture"/> will report. Nothing is queued until this
    /// is set -- matching real XNA, where an unset <c>EnabledGestures</c> means
    /// <see cref="IsGestureAvailable"/> never becomes true.</summary>
    public static GestureType EnabledGestures
    {
        get
        {
            CnaResult result = Native.cna_touch_panel_get_enabled_gestures(CnaAmbientGame.Current, out uint value);
            CnaException.ThrowIfFailed(result, nameof(EnabledGestures));
            return (GestureType)value;
        }
        set
        {
            CnaResult result = Native.cna_touch_panel_set_enabled_gestures(CnaAmbientGame.Current, (uint)value);
            CnaException.ThrowIfFailed(result, nameof(EnabledGestures));
        }
    }

    public static bool IsGestureAvailable
    {
        get
        {
            CnaResult result = Native.cna_touch_panel_get_is_gesture_available(CnaAmbientGame.Current, out byte value);
            CnaException.ThrowIfFailed(result, nameof(IsGestureAvailable));
            return value != 0;
        }
    }

    /// <summary>Dequeues the next gesture. Throws <see cref="InvalidOperationException"/> when the
    /// queue is empty, matching real XNA -- callers are expected to guard with
    /// <see cref="IsGestureAvailable"/>. The native call reports this as
    /// <see cref="CnaResult.InvalidState"/>, which is translated here rather than surfaced as a
    /// <see cref="CnaException"/>, so XNA source that catches the documented exception still
    /// works.</summary>
    public static GestureSample ReadGesture()
    {
        var sample = new CnaGestureSample();
        CnaResult result = Native.cna_touch_panel_read_gesture(CnaAmbientGame.Current, ref sample);

        if (result == CnaResult.InvalidState)
        {
            throw new InvalidOperationException(CnaError.GetLastErrorMessage());
        }

        CnaException.ThrowIfFailed(result, nameof(ReadGesture));
        return GestureSample.FromNative(in sample);
    }
}
