using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CNA.Graphics;
using CNA.Interop;

namespace CNA;

/// <summary>
/// Matches real XNA's <c>GraphicsDeviceManager</c>: the object a game constructs to state its
/// back-buffer, full-screen and profile preferences, then applies with
/// <see cref="ApplyChanges"/>.
///
/// Phase 8 WP6 replaced a placeholder that had only a <see cref="Game"/> property (everything else
/// was recorded as deferred) with a real binding over <c>runtime_graphics_manager.h</c>.
///
/// The constructor really does create the native manager, rather than deferring: the C API
/// documents <c>cna_graphics_device_manager_create</c> as registering the new manager as the
/// game's graphics device manager *and* its graphics device service, and as refusing a second
/// manager for the same game. That is exactly real XNA's own
/// <c>new GraphicsDeviceManager(this)</c> contract, so the two map one-to-one and a second
/// construction fails loudly here just as it does there.
///
/// Every property is a live round trip to native, not a cached value -- so a preference read back
/// after <see cref="ApplyChanges"/> reports what the engine actually settled on, which may differ
/// from what was asked for (a requested back-buffer size the display cannot provide, for example).
/// Real XNA behaves the same way.
/// </summary>
public class GraphicsDeviceManager : IGraphicsDeviceService, IGraphicsDeviceManager, IDisposable
{
    private readonly NativeResourceHandle _handle;

    /// <summary>One slot per <c>CNA_GRAPHICS_DEVICE_MANAGER_EVENT_*</c> identity, indexed by that
    /// identity's own value -- see <see cref="EnsureSubscribed"/>. Null until first subscribed.
    /// Sized from the enum so adding an identity cannot silently overflow it.</summary>
    private readonly NativeEventBridge?[] _eventBridges =
        new NativeEventBridge?[(int)CnaGraphicsDeviceManagerEvent.DeviceResetting + 1];

    private bool _disposed;

    public GraphicsDeviceManager(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);
        Game = game;

        CnaResult result = Native.cna_graphics_device_manager_create(new CnaHandle(game.NativeHandle), out CnaHandle manager);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(GraphicsDeviceManager));

        _handle = new NativeResourceHandle(manager.Value, ReleaseNative);

        // Real XNA's GraphicsDeviceManager registers itself as the game's IGraphicsDeviceService
        // from its own constructor, which is how components find the device without depending on
        // the manager type. The C API's create call does the same registration natively (its own
        // doc calls it "the game's graphics device manager and graphics device service"), so doing
        // it here keeps the managed service container agreeing with native rather than being a
        // second, separate registry.
        game.Services.AddService(typeof(IGraphicsDeviceService), this);
        // ABI 0.60 replaces the device on profile changes and repeated CreateDevice calls.
        // Track identity before forwarding events to clients, even if none subscribe themselves.
        DeviceDisposing += (_, _) => game.RetireGraphicsDevice();
        DeviceCreated += (_, _) => game.EnsureGraphicsDevice();
    }

    public Game Game { get; }

    /// <summary>
    /// The native handle, read out of the owning <see cref="NativeResourceHandle"/>. Every caller
    /// pairs it with <see cref="GC.KeepAlive(object)"/> after the native call: once the handle
    /// value has been read this object can be unreachable, and an unreachable
    /// <see cref="System.Runtime.InteropServices.SafeHandle"/> may have its critical finalizer run
    /// <c>destroy</c> while the call is still in flight. Defeating exactly that is what
    /// <see cref="System.Runtime.InteropServices.SafeHandle"/> is for, so reading the handle
    /// without keeping its owner alive gives the guarantee up -- see <c>plan.md</c> WP17.
    /// </summary>
    private CnaHandle NativeHandle => new(_handle.DangerousGetHandle());

    /// <summary>
    /// How the back buffer is fitted into the window.
    ///
    /// Not XNA. XNA 4.0 presents the back buffer stretched to the client area and offers no say in
    /// it; CNA's renderers can letterbox, overscan, stretch, present at the native back-buffer size,
    /// or hold the height and let the width follow the window. A game that wants a fixed aspect
    /// ratio has to letterbox by hand under XNA, and does not here.
    /// </summary>
    public PresentationMode PreferredPresentationMode
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_preferred_presentation_mode_ext(
                NativeHandle, out uint mode);
            CnaException.ThrowIfFailed(result, nameof(PreferredPresentationMode));
            return (PresentationMode)mode;
        }

        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_preferred_presentation_mode_ext(
                NativeHandle, (uint)value);
            CnaException.ThrowIfFailed(result, nameof(PreferredPresentationMode));
        }
    }

    private static bool ReleaseNative(ulong handleValue) =>
        Native.cna_graphics_device_manager_destroy(new CnaHandle(handleValue)).IsSuccess();

    public int PreferredBackBufferWidth
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_preferred_back_buffer_width(NativeHandle, out int value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferredBackBufferWidth));
            return value;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_preferred_back_buffer_width(NativeHandle, value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferredBackBufferWidth));
        }
    }

    public int PreferredBackBufferHeight
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_preferred_back_buffer_height(NativeHandle, out int value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferredBackBufferHeight));
            return value;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_preferred_back_buffer_height(NativeHandle, value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferredBackBufferHeight));
        }
    }

    public SurfaceFormat PreferredBackBufferFormat
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_preferred_back_buffer_format(NativeHandle, out uint value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferredBackBufferFormat));
            return (SurfaceFormat)value;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_preferred_back_buffer_format(NativeHandle, (uint)value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferredBackBufferFormat));
        }
    }

    public DepthFormat PreferredDepthStencilFormat
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_preferred_depth_stencil_format(NativeHandle, out uint value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferredDepthStencilFormat));
            return (DepthFormat)value;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_preferred_depth_stencil_format(NativeHandle, (uint)value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferredDepthStencilFormat));
        }
    }

    public bool IsFullScreen
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_is_full_screen(NativeHandle, out byte value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsFullScreen));
            return value != 0;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_is_full_screen(NativeHandle, (byte)(value ? 1 : 0));
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsFullScreen));
        }
    }

    public bool PreferMultiSampling
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_prefer_multi_sampling(NativeHandle, out byte value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferMultiSampling));
            return value != 0;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_prefer_multi_sampling(NativeHandle, (byte)(value ? 1 : 0));
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PreferMultiSampling));
        }
    }

    public bool SynchronizeWithVerticalRetrace
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_synchronize_with_vertical_retrace(NativeHandle, out byte value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(SynchronizeWithVerticalRetrace));
            return value != 0;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_synchronize_with_vertical_retrace(NativeHandle, (byte)(value ? 1 : 0));
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(SynchronizeWithVerticalRetrace));
        }
    }

    public GraphicsProfile GraphicsProfile
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_graphics_profile(NativeHandle, out CnaGraphicsProfile value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(GraphicsProfile));
            return (GraphicsProfile)value;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_graphics_profile(NativeHandle, (CnaGraphicsProfile)value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(GraphicsProfile));
        }
    }

    public DisplayOrientation SupportedOrientations
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_manager_get_supported_orientations(NativeHandle, out uint value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(SupportedOrientations));
            return (DisplayOrientation)value;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_manager_set_supported_orientations(NativeHandle, (uint)value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(SupportedOrientations));
        }
    }

    /// <summary>Applies every preference set since the last call. Until this runs, the properties
    /// above are only requests -- matching real XNA, where setting
    /// <see cref="PreferredBackBufferWidth"/> alone changes nothing.</summary>
    public void ApplyChanges()
    {
        CnaResult result = Native.cna_graphics_device_manager_apply_changes(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(ApplyChanges));
    }

    public void ToggleFullScreen()
    {
        CnaResult result = Native.cna_graphics_device_manager_toggle_full_screen(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(ToggleFullScreen));
    }

    /// <summary>The device this manager configures. Deliberately reads
    /// <see cref="CNA.Game.GraphicsDevice"/> rather than
    /// <c>cna_graphics_device_manager_get_graphics_device</c>: the game already owns the single
    /// managed <see cref="Graphics.GraphicsDevice"/> wrapper for this game, and building a second
    /// wrapper around the same native device would give callers two objects whose cached state
    /// (<c>BlendState</c>, <c>Indices</c>, the sampler/texture collections) could disagree.</summary>
    public GraphicsDevice GraphicsDevice => Game.GraphicsDevice;

    /// <summary>
    /// Raised after the device is created -- and genuinely raised, by native. WP15 replaced the
    /// four inert placeholder events with real subscriptions over
    /// <c>cna_graphics_device_manager_subscribe</c>, whose event identities
    /// (<c>CNA_GRAPHICS_DEVICE_MANAGER_EVENT_*</c>, <c>runtime_graphics_manager.h:65-73</c>) map
    /// one-to-one onto XNA's four <c>IGraphicsDeviceService</c> events.
    ///
    /// The native subscription is taken on the first <c>+=</c> and kept until
    /// <see cref="Dispose()"/>, not dropped again on the last <c>-=</c>. Native registration is not
    /// free, and a game that subscribes and unsubscribes per screen would otherwise churn a native
    /// registration every transition; holding it costs one idle callback that finds a null
    /// invocation list.
    /// </summary>
    private GCHandle _preparingSelf;
    private CnaHandle _preparingRegistration;
    private EventHandler<PreparingDeviceSettingsEventArgs>? _preparingDeviceSettings;

    /// <summary>
    /// Raised while the device is being prepared, with settings the handler may change. Real XNA's
    /// <c>PreparingDeviceSettings</c>, and the way a game requests MSAA, picks a back-buffer format
    /// or chooses an adapter before the device exists.
    ///
    /// <b>Missing entirely until now, and recorded as unfixable.</b> This project listed it as a
    /// blocker on the strength of the observation-only route's header, which said the event
    /// delivers its argument as a <c>const</c> reference so even a C++ subscriber cannot reach the
    /// mutable accessor -- and called that canonical rather than introduced. It was fixed at the
    /// source instead: the argument holds its settings by pointer, so the mutable accessor needs no
    /// cast, and <c>_subscribe_preparing_device_settings_ext</c> forwards it. The <c>_ext</c>
    /// suffix marks the shape, not weaker behaviour.
    ///
    /// What the handler writes is what the device is created from -- adapter, back-buffer format
    /// and size, depth-stencil format, multisample count, presentation interval. Native validates
    /// the result and <em>ignores</em> an invalid one rather than half-applying it, so a handler
    /// cannot produce a configuration that fails device creation for an unrelated-looking reason.
    /// </summary>
    public unsafe event EventHandler<PreparingDeviceSettingsEventArgs>? PreparingDeviceSettings
    {
        add
        {
            EnsurePreparingSubscribed();
            _preparingDeviceSettings += value;
        }
        remove => _preparingDeviceSettings -= value;
    }

    private unsafe void EnsurePreparingSubscribed()
    {
        if (_preparingSelf.IsAllocated)
        {
            return;
        }

        _preparingSelf = GCHandle.Alloc(this);

        CnaResult result = Native.cna_graphics_device_manager_subscribe_preparing_device_settings_ext(
            NativeHandle,
            (nint)(delegate* unmanaged[Cdecl]<CnaGraphicsDeviceInformation*, nint, void>)&OnPreparingDeviceSettings,
            GCHandle.ToIntPtr(_preparingSelf),
            out _preparingRegistration);

        if (result.IsFailure())
        {
            _preparingSelf.Free();
            CnaException.ThrowIfFailed(result, nameof(PreparingDeviceSettings));
        }
    }

    /// <summary>
    /// Reads the candidate settings out, runs the handlers, and writes back whatever they changed.
    ///
    /// Catches rather than unwinding -- an exception crossing an <c>UnmanagedCallersOnly</c>
    /// boundary is undefined behaviour -- and the callback returns <c>void</c>, so there is nowhere
    /// to report one. A handler that throws therefore leaves the settings as native supplied them,
    /// which is the same outcome as a handler that changed nothing.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnPreparingDeviceSettings(CnaGraphicsDeviceInformation* information, nint context)
    {
        if (information is null || context == 0)
        {
            return;
        }

        if (GCHandle.FromIntPtr(context).Target is not GraphicsDeviceManager manager)
        {
            return;
        }

        EventHandler<PreparingDeviceSettingsEventArgs>? handlers = manager._preparingDeviceSettings;
        if (handlers is null)
        {
            return;
        }

        try
        {
            var settings = new GraphicsDeviceInformation
            {
                GraphicsProfile = (GraphicsProfile)information->GraphicsProfile,
                PresentationParameters = PresentationParameters.FromNative(information->PresentationParameters),
            };

            handlers(manager, new PreparingDeviceSettingsEventArgs(settings));

            information->GraphicsProfile = (uint)settings.GraphicsProfile;
            information->PresentationParameters = settings.PresentationParameters.ToNative();
        }
        catch (Exception)
        {
        }
    }

    public event EventHandler<EventArgs>? DeviceCreated
    {
        add { EnsureSubscribed(CnaGraphicsDeviceManagerEvent.DeviceCreated); _deviceCreated += value; }
        remove => _deviceCreated -= value;
    }

    /// <summary>Raised before the device is disposed. See <see cref="DeviceCreated"/> for how the
    /// native subscription is managed.</summary>
    public event EventHandler<EventArgs>? DeviceDisposing
    {
        add { EnsureSubscribed(CnaGraphicsDeviceManagerEvent.DeviceDisposing); _deviceDisposing += value; }
        remove => _deviceDisposing -= value;
    }

    /// <summary>Raised after the device finishes resetting. See <see cref="DeviceCreated"/> for how
    /// the native subscription is managed.</summary>
    public event EventHandler<EventArgs>? DeviceReset
    {
        add { EnsureSubscribed(CnaGraphicsDeviceManagerEvent.DeviceReset); _deviceReset += value; }
        remove => _deviceReset -= value;
    }

    /// <summary>Raised before the device resets. See <see cref="DeviceCreated"/> for how the native
    /// subscription is managed.</summary>
    public event EventHandler<EventArgs>? DeviceResetting
    {
        add { EnsureSubscribed(CnaGraphicsDeviceManagerEvent.DeviceResetting); _deviceResetting += value; }
        remove => _deviceResetting -= value;
    }

    private EventHandler<EventArgs>? _deviceCreated;
    private EventHandler<EventArgs>? _deviceDisposing;
    private EventHandler<EventArgs>? _deviceReset;
    private EventHandler<EventArgs>? _deviceResetting;

    /// <summary>Subscribes to <paramref name="which"/> exactly once. Indexed by the native event
    /// identity so the array position and the value handed to native cannot drift apart.</summary>
    private void EnsureSubscribed(CnaGraphicsDeviceManagerEvent which)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int index = (int)which;
        if (_eventBridges[index] is not null)
        {
            return;
        }

        _eventBridges[index] = NativeEventBridge.Subscribe(
            () => Raise(which),
            (callback, context) =>
            {
                CnaResult result = Native.cna_graphics_device_manager_subscribe(
                    NativeHandle, (uint)which, callback, context, out CnaHandle registration);
                GC.KeepAlive(this);
                CnaException.ThrowIfFailed(result, nameof(EnsureSubscribed));
                return registration;
            },
            registration => Native.cna_game_unsubscribe(registration));
    }

    private void Raise(CnaGraphicsDeviceManagerEvent which)
    {
        EventHandler<EventArgs>? handler = which switch
        {
            CnaGraphicsDeviceManagerEvent.DeviceCreated => _deviceCreated,
            CnaGraphicsDeviceManagerEvent.DeviceDisposing => _deviceDisposing,
            CnaGraphicsDeviceManagerEvent.DeviceReset => _deviceReset,
            CnaGraphicsDeviceManagerEvent.DeviceResetting => _deviceResetting,
            _ => null,
        };

        handler?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Rethrows the first exception any event handler threw. These callbacks return
    /// <c>void</c> to native, so an exception cannot be reported where it happens without unwinding
    /// into native code; <see cref="NativeEventBridge"/> captures it and this surfaces it at the
    /// next managed-initiated call, the same bargain <see cref="GameComponent"/> makes.</summary>
    private void ThrowPendingCallbackException()
    {
        foreach (NativeEventBridge? bridge in _eventBridges)
        {
            bridge?.ThrowPendingException();
        }
    }

    /// <summary>Matches <c>IGraphicsDeviceManager.BeginDraw</c>: <see langword="false"/> tells the
    /// game to skip this frame's drawing.</summary>
    public bool BeginDraw()
    {
        ThrowPendingCallbackException();
        CnaResult result = Native.cna_graphics_device_manager_begin_draw(NativeHandle, out byte shouldDraw);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(BeginDraw));
        return shouldDraw != 0;
    }

    public void CreateDevice()
    {
        CnaResult result = Native.cna_graphics_device_manager_create_device(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(CreateDevice));
    }

    public void EndDraw()
    {
        ThrowPendingCallbackException();
        CnaResult result = Native.cna_graphics_device_manager_end_draw(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(EndDraw));
    }

    public void Dispose()
    {
        // Unsubscribe before freeing the root, the rule NativeEventBridge already follows: an
        // in-flight callback must not reach a GCHandle that has been released.
        if (_preparingSelf.IsAllocated)
        {
            Native.cna_game_unsubscribe(_preparingRegistration);
            _preparingSelf.Free();
        }

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Unsubscribes every native event before releasing the manager. Order matters: a
    /// live registration outliving this object would leave native calling into a freed
    /// <see cref="System.Runtime.InteropServices.GCHandle"/> context. Any handler failure captured
    /// but never surfaced is rethrown last, so a game that subscribed and then only ever ran the
    /// frame loop still hears about it rather than losing it silently.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Exception? pending = null;
        for (int i = 0; i < _eventBridges.Length; i++)
        {
            NativeEventBridge? bridge = _eventBridges[i];
            _eventBridges[i] = null;
            if (bridge is null)
            {
                continue;
            }

            try
            {
                bridge.ThrowPendingException();
            }
            catch (Exception ex)
            {
                pending ??= ex;
            }

            bridge.Dispose();
        }

        _handle.Dispose();
        Game.RetireGraphicsDevice();

        if (pending is not null)
        {
            throw pending;
        }
    }
}
