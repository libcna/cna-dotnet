namespace Microsoft.Xna.Framework;

/// <summary>
/// XNA's graphics-device manager. The native manager remains private; publishing it as a base
/// class previously made every XNA game inherit CNA-only members and interfaces.
/// </summary>
public class GraphicsDeviceManager : Graphics.IGraphicsDeviceService, IGraphicsDeviceManager, IDisposable
{
    /// <summary>Minimum default back-buffer width used by the XNA device manager.</summary>
    public static readonly int DefaultBackBufferWidth = 800;

    /// <summary>Minimum default back-buffer height used by the XNA device manager.</summary>
    public static readonly int DefaultBackBufferHeight = 480;

    private readonly CNA.GraphicsDeviceManager _backend;

    /// <summary>The CNA manager behind this facade, for <c>CNA.XnaCompat.Extensions</c>. Internal
    /// for the same reason <c>GraphicsDevice.Framework</c> is: the strict facade's public surface is
    /// checked member for member against XNA's own metadata.</summary>
    internal CNA.GraphicsDeviceManager Framework => _backend;
    private bool _disposed;
    private EventHandler<PreparingDeviceSettingsEventArgs>? _preparingDeviceSettings;

    // A Windows Phone title on a desktop (cna-cs CSX-094). On the phone, IsFullScreen hides the
    // status bar and never changes the display mode; the nearest thing on a desktop is the window
    // the emulator gave it. Forwarded, it asked the desktop for a 480x800 full-screen mode.
    private readonly bool _phoneTitleWindowed;
    private bool _phoneTitleFullScreen;

    public GraphicsDeviceManager(Game game)
        : this(game, ReadRuntimeProfileLine(game?.GetType().Assembly))
    {
    }

    internal GraphicsDeviceManager(Game game, string? runtimeProfileLine)
    {
        ArgumentNullException.ThrowIfNull(game);
        Game = game;
        _phoneTitleWindowed = PhoneTitle.IsOffAPhone(runtimeProfileLine);
        _backend = new CNA.GraphicsDeviceManager(game.Backend);
        _backend.GraphicsProfile = (CNA.Graphics.GraphicsProfile)(int)ParseGraphicsProfile(runtimeProfileLine);
        _backend.DeviceCreated += (_, args) => OnDeviceCreated(this, args);
        _backend.DeviceDisposing += (_, args) => OnDeviceDisposing(this, args);
        _backend.DeviceReset += (_, args) => OnDeviceReset(this, args);
        _backend.DeviceResetting += (_, args) => OnDeviceResetting(this, args);
        _backend.PreparingDeviceSettings += OnPreparingDeviceSettings;
        game.RegisterGraphicsDeviceManager(this);
        game.Services.AddService(typeof(IGraphicsDeviceManager), this);
        game.Services.AddService(typeof(Graphics.IGraphicsDeviceService), this);
    }

    internal Game Game { get; }

    /// <summary>
    /// XNA's default profile: the game assembly's <c>Microsoft.Xna.Framework.RuntimeProfile</c>
    /// resource (written from the project's <c>XnaProfile</c>, build/CNA.XnaCompat.targets), whose
    /// first line ends in <c>Reach</c> or <c>HiDef</c>; Reach when there is no such resource or line.
    /// </summary>
    internal static Graphics.GraphicsProfile ReadDefaultGraphicsProfile(System.Reflection.Assembly assembly) =>
        ParseGraphicsProfile(ReadRuntimeProfileLine(assembly));

    /// <summary>The first line of the game assembly's <c>Microsoft.Xna.Framework.RuntimeProfile</c>
    /// resource, <c>&lt;platform&gt;.v4.0.&lt;profile&gt;</c>, or null when there is none.</summary>
    internal static string? ReadRuntimeProfileLine(System.Reflection.Assembly? assembly)
    {
        using Stream? stream = assembly?.GetManifestResourceStream("Microsoft.Xna.Framework.RuntimeProfile");
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadLine();
    }

    /// <summary>
    /// Whether <see cref="IsFullScreen"/> stays the game's own state rather than reaching the
    /// display: a Windows Phone title (XNA's build writes <c>WindowsPhone.v4.0.&lt;profile&gt;</c>)
    /// on a host that is not a phone. On Android and iOS the platform's full screen is the phone's.
    /// </summary>
    internal static bool KeepsFullScreenInTheGame(string? runtimeProfileLine, bool hostIsPhone) =>
        PhoneTitle.IsOffAPhone(runtimeProfileLine, hostIsPhone);

    internal static Graphics.GraphicsProfile ParseGraphicsProfile(string? line)
    {
        if (line is not null && line.EndsWith("Reach", StringComparison.Ordinal))
        {
            return Graphics.GraphicsProfile.Reach;
        }

        if (line is not null && line.EndsWith("HiDef", StringComparison.Ordinal))
        {
            return Graphics.GraphicsProfile.HiDef;
        }

        return Graphics.GraphicsProfile.Reach;
    }

    public int PreferredBackBufferWidth
    {
        get => _backend.PreferredBackBufferWidth;
        set => _backend.PreferredBackBufferWidth = value;
    }

    public int PreferredBackBufferHeight
    {
        get => _backend.PreferredBackBufferHeight;
        set => _backend.PreferredBackBufferHeight = value;
    }

    public Graphics.SurfaceFormat PreferredBackBufferFormat
    {
        get => (Graphics.SurfaceFormat)(int)_backend.PreferredBackBufferFormat;
        set => _backend.PreferredBackBufferFormat = (CNA.Graphics.SurfaceFormat)(int)value;
    }

    public Graphics.DepthFormat PreferredDepthStencilFormat
    {
        get => (Graphics.DepthFormat)(int)_backend.PreferredDepthStencilFormat;
        set => _backend.PreferredDepthStencilFormat = (CNA.Graphics.DepthFormat)(int)value;
    }

    public bool IsFullScreen
    {
        get => _phoneTitleWindowed ? _phoneTitleFullScreen : _backend.IsFullScreen;
        set
        {
            if (_phoneTitleWindowed)
            {
                _phoneTitleFullScreen = value;
            }
            else
            {
                _backend.IsFullScreen = value;
            }
        }
    }

    public bool PreferMultiSampling
    {
        get => _backend.PreferMultiSampling;
        set => _backend.PreferMultiSampling = value;
    }

    public bool SynchronizeWithVerticalRetrace
    {
        get => _backend.SynchronizeWithVerticalRetrace;
        set => _backend.SynchronizeWithVerticalRetrace = value;
    }

    public Graphics.GraphicsProfile GraphicsProfile
    {
        get => (Graphics.GraphicsProfile)(int)_backend.GraphicsProfile;
        set => _backend.GraphicsProfile = (CNA.Graphics.GraphicsProfile)(int)value;
    }

    public DisplayOrientation SupportedOrientations
    {
        get => (DisplayOrientation)(int)_backend.SupportedOrientations;
        set => _backend.SupportedOrientations = (CNA.DisplayOrientation)(int)value;
    }

    /// <summary>XNA (IL): the device this manager created, null until it has -- a game's constructor
    /// sees null (gamealgorithms/defense tests it before applying a resolution).</summary>
    public Graphics.GraphicsDevice GraphicsDevice => Game.CreatedGraphicsDevice!;

    public event EventHandler<PreparingDeviceSettingsEventArgs>? PreparingDeviceSettings
    {
        add => _preparingDeviceSettings += value;
        remove => _preparingDeviceSettings -= value;
    }

    public event EventHandler<EventArgs>? DeviceCreated;

    public event EventHandler<EventArgs>? DeviceDisposing;

    public event EventHandler<EventArgs>? DeviceReset;

    public event EventHandler<EventArgs>? DeviceResetting;

    public event EventHandler<EventArgs>? Disposed;

    /// <summary>Applies the preferences. XNA's with no device yet creates it (IL: ChangeDevice(false)
    /// -> CreateDevice), so a game's constructor that calls it can read the device after: Kermit's
    /// SKraft sizes its menu from <c>GraphicsDevice.Viewport</c> there (CSX-121).</summary>
    public void ApplyChanges()
    {
        _backend.ApplyChanges();
        Game.Backend.EnsureGraphicsDevice();
    }

    public void ToggleFullScreen()
    {
        if (_phoneTitleWindowed)
        {
            _phoneTitleFullScreen = !_phoneTitleFullScreen;
            return;
        }

        _backend.ToggleFullScreen();
    }

    protected virtual bool CanResetDevice(GraphicsDeviceInformation newDeviceInfo)
    {
        ArgumentNullException.ThrowIfNull(newDeviceInfo);
        return !GraphicsDevice.IsDisposed && GraphicsDevice.GraphicsProfile == newDeviceInfo.GraphicsProfile;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || _disposed)
        {
            return;
        }

        _disposed = true;
        _backend.Dispose();
        // ABI 0.60/XNA retain IGraphicsDeviceManager until game teardown. Only the
        // graphics-device service is removed by explicit manager disposal.
        if (ReferenceEquals(Game.Services.GetService(typeof(Graphics.IGraphicsDeviceService)), this))
        {
            Game.Services.RemoveService(typeof(Graphics.IGraphicsDeviceService));
        }

        Game.UnregisterGraphicsDeviceManager(this);
        Disposed?.Invoke(this, EventArgs.Empty);
    }

    internal void DisposeFromGame() => Dispose(true);

    protected virtual GraphicsDeviceInformation FindBestDevice(bool anySuitableDevice)
    {
        var information = new GraphicsDeviceInformation
        {
            Adapter = Graphics.GraphicsAdapter.DefaultAdapter,
            GraphicsProfile = GraphicsProfile,
            PresentationParameters = new Graphics.PresentationParameters
            {
                BackBufferWidth = PreferredBackBufferWidth,
                BackBufferHeight = PreferredBackBufferHeight,
                BackBufferFormat = PreferredBackBufferFormat,
                DepthStencilFormat = PreferredDepthStencilFormat,
                IsFullScreen = IsFullScreen,
            },
        };

        _ = anySuitableDevice;
        return information;
    }

    protected virtual void OnDeviceCreated(object sender, EventArgs args) =>
        DeviceCreated?.Invoke(sender, args);

    protected virtual void OnDeviceDisposing(object sender, EventArgs args) =>
        DeviceDisposing?.Invoke(sender, args);

    protected virtual void OnDeviceReset(object sender, EventArgs args) =>
        DeviceReset?.Invoke(sender, args);

    protected virtual void OnDeviceResetting(object sender, EventArgs args) =>
        DeviceResetting?.Invoke(sender, args);

    protected virtual void OnPreparingDeviceSettings(object sender, PreparingDeviceSettingsEventArgs args) =>
        _preparingDeviceSettings?.Invoke(sender, args);

    protected virtual void RankDevices(List<GraphicsDeviceInformation> foundDevices)
    {
        ArgumentNullException.ThrowIfNull(foundDevices);
    }

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    bool IGraphicsDeviceManager.BeginDraw() => _backend.BeginDraw();

    void IGraphicsDeviceManager.CreateDevice() => _backend.CreateDevice();

    void IGraphicsDeviceManager.EndDraw() => _backend.EndDraw();

    private void OnPreparingDeviceSettings(object? sender, CNA.PreparingDeviceSettingsEventArgs args)
    {
        GraphicsDeviceInformation information = GraphicsDeviceInformation.FromFramework(args.GraphicsDeviceInformation);
        OnPreparingDeviceSettings(this, new PreparingDeviceSettingsEventArgs(information));
        information.CopyTo(args.GraphicsDeviceInformation);
    }
}
