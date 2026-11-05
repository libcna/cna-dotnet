namespace Microsoft.Xna.Framework;

/// <summary>
/// XNA's game facade. The private <see cref="BackendGame"/> owns CNA's native callback and
/// lifetime machinery; this type owns the public XNA hierarchy and callback contract.
/// </summary>
public class Game : IDisposable
{
    private readonly BackendGame _backend;
    private Content.ContentManager _content;
    private GameComponentCollection? _components;
    private GameWindow? _window;
    private GraphicsDeviceManager? _graphicsDeviceManager;
    private bool _disposed;
    private bool _disposeWhenRunEnds;

    public Game()
    {
        NetFxNumberSymbols.Apply();
        PhoneTitle.Active = PhoneTitle.IsOffAPhone(GraphicsDeviceManager.ReadRuntimeProfileLine(GetType().Assembly));
        PhoneTitle.Title = PhoneTitle.Active ? this : null;
        PhoneTitle.PumpsGamerServices = false;
        Services = new GameServiceContainer();
        // The game's device is created with the native game: a device that cannot be created is
        // XNA's NoSuitableGraphicsDeviceException, as GraphicsDeviceManager.CreateDevice throws it.
        _backend = XnaExceptions.Guard(() => new BackendGame(this));
        // XNA let a game load on a thread of its own; its calls run on the game thread (CSX-101).
        _backend.ServeForeignThreadCalls();
        _content = new Content.ContentManager(_backend.Content, Services);

        _backend.Activated += (_, args) => OnActivated(this, args);
        _backend.Deactivated += (_, args) => OnDeactivated(this, args);
        _backend.Exiting += (_, args) => OnExiting(this, args);
        _backend.Disposed += (_, args) => Disposed?.Invoke(this, args);
    }

    internal CNA.Game Backend => _backend;

    /// <summary>
    /// Raised once per run, after <c>Initialize</c> and <c>LoadContent</c> and before the first
    /// <c>Update</c>: where a Windows Phone title's <c>PhoneApplicationService.Launching</c> arrived
    /// on the phone (CNA.PhoneCompat, cna-cs CSX-097). Not XNA API.
    /// </summary>
    internal static event Action<Game>? Started;

    private bool _startedRaised;

    /// <summary>
    /// The XNA manager is owned by its game for native-lifetime purposes. CNA's native game tears
    /// down devices while it is being destroyed; dispose the facade's subscriptions before that
    /// boundary so native can never call a freed managed event context during game destruction.
    /// </summary>
    internal void RegisterGraphicsDeviceManager(GraphicsDeviceManager manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        if (_graphicsDeviceManager is not null && !ReferenceEquals(_graphicsDeviceManager, manager))
        {
            throw new ArgumentException("A Game may have only one GraphicsDeviceManager.", nameof(manager));
        }

        _graphicsDeviceManager = manager;
    }

    internal void UnregisterGraphicsDeviceManager(GraphicsDeviceManager manager)
    {
        if (ReferenceEquals(_graphicsDeviceManager, manager))
        {
            _graphicsDeviceManager = null;
        }
    }

    public GameComponentCollection Components =>
        _components ??= new GameComponentCollection(this, _backend.Components);

    public Content.ContentManager Content
    {
        get => _content;
        set => _content = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>XNA (IL): the device of the game's <c>IGraphicsDeviceService</c>, null until that
    /// service has created it -- in a constructor, before <c>Run</c> -- and an
    /// <see cref="InvalidOperationException"/> only when the game has no such service. CNA creates a
    /// device for every game, so one that registered no service still gets it once it runs.</summary>
    public Graphics.GraphicsDevice GraphicsDevice =>
        _backend.CompatGraphicsDevice
        ?? (Services.GetService(typeof(Graphics.IGraphicsDeviceService)) is not null
            ? null!
            : throw new InvalidOperationException(
                "This property requires a graphics device service in the game service container."));

    /// <summary>The device once created, or null: what <c>GraphicsDeviceManager.GraphicsDevice</c>
    /// returns.</summary>
    internal Graphics.GraphicsDevice? CreatedGraphicsDevice => _backend.CompatGraphicsDevice;

    public TimeSpan InactiveSleepTime
    {
        get => _backend.InactiveSleepTime;
        set => _backend.InactiveSleepTime = value;
    }

    public bool IsActive => _backend.IsActive;

    public bool IsFixedTimeStep
    {
        get => _backend.IsFixedTimeStep;
        set => _backend.IsFixedTimeStep = value;
    }

    public bool IsMouseVisible
    {
        get => _backend.IsMouseVisible;
        set => _backend.IsMouseVisible = value;
    }

    public LaunchParameters LaunchParameters => new(_backend.LaunchParameters);

    public GameServiceContainer Services { get; }

    public TimeSpan TargetElapsedTime
    {
        get => _backend.TargetElapsedTime;
        set => _backend.TargetElapsedTime = value;
    }

    public GameWindow Window => _window ??= new NativeGameWindow(_backend.Window);

    public event EventHandler<EventArgs>? Activated;

    public event EventHandler<EventArgs>? Deactivated;

    public event EventHandler<EventArgs>? Disposed;

    public event EventHandler<EventArgs>? Exiting;

    public void ResetElapsedTime() => _backend.ResetElapsedTime();

    /// <summary>BeginRun and EndRun arrive from the run itself, where XNA calls them: after
    /// Initialize and LoadContent, and after the last frame.</summary>
    public void Run()
    {
        if (OperatingSystem.IsBrowser())
        {
            // A page cannot block here, so the run goes on in the browser's animation frames and
            // ends later; a Dispose the caller asks for meanwhile waits for that end.
            _backend.RunInBrowser(DisposeIfRequested);
            return;
        }

        _backend.Run();
    }

    private void DisposeIfRequested()
    {
        if (_disposeWhenRunEnds)
        {
            Dispose();
        }
    }

    /// <summary>Ticks once without initializing the game, as XNA does.</summary>
    public void RunOneFrame() => _backend.RunOneFrame();

    public void Exit() => _backend.Exit();

    public void SuppressDraw() => _backend.SuppressDraw();

    public void Tick() => _backend.Tick();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~Game()
    {
        Dispose(false);
    }

    protected virtual bool BeginDraw() => true;

    protected virtual void BeginRun()
    {
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        // See Run: in a browser the run outlives the call, and so does the game.
        if (disposing && _backend is not null && _backend.IsRunningInBrowser)
        {
            _disposeWhenRunEnds = true;
            return;
        }

        _disposed = true;
        try
        {
            if (disposing)
            {
                try
                {
                    _content?.Dispose();
                }
                finally
                {
                    try
                    {
                        _backend?.DisposeCompatGraphicsDevice();
                    }
                    finally
                    {
                        _graphicsDeviceManager?.DisposeFromGame();
                    }
                }
            }
        }
        finally
        {
            // A managed disposal handler may throw, but native game teardown must still run.
            _backend?.Dispose();
        }
    }

    /// <summary>
    /// XNA's <c>Game.Update</c>: the enabled components, in <c>UpdateOrder</c>, at the call (XNA
    /// IL), so a game's work after <c>base.Update</c> sees them updated. CNA's own pass follows the
    /// game's override; <see cref="ComponentsUpdatedThisFrame"/> makes it a no-op. The backend
    /// pumps the framework dispatcher once after this callback; pumping here would do it twice.
    /// </summary>
    protected virtual void Update(GameTime gameTime)
    {
        ComponentsUpdatedThisFrame = true;
        _updating.Clear();
        foreach (IGameComponent component in Components)
        {
            if (component is IUpdateable updateable)
            {
                InsertInOrder(_updating, updateable, static item => item.UpdateOrder);
            }
        }

        foreach (IUpdateable updateable in _updating)
        {
            if (updateable.Enabled)
            {
                updateable.Update(gameTime);
            }
        }
    }

    /// <summary>
    /// XNA's <c>Game.Draw</c>: the visible components, in <c>DrawOrder</c>, at the call (XNA IL) --
    /// a game that draws its HUD after <c>base.Draw</c> draws it over them. CNA's own pass follows
    /// the game's override; <see cref="ComponentsDrawnThisFrame"/> makes it a no-op.
    /// </summary>
    protected virtual void Draw(GameTime gameTime)
    {
        ComponentsDrawnThisFrame = true;
        _drawing.Clear();
        foreach (IGameComponent component in Components)
        {
            if (component is IDrawable drawable)
            {
                InsertInOrder(_drawing, drawable, static item => item.DrawOrder);
            }
        }

        foreach (IDrawable drawable in _drawing)
        {
            if (drawable.Visible)
            {
                drawable.Draw(gameTime);
            }
        }
    }

    // Copies, as XNA iterates: a component that adds or removes another while updating or drawing
    // does not disturb this frame's pass. Reused, so a frame allocates nothing.
    private readonly List<IUpdateable> _updating = [];
    private readonly List<IDrawable> _drawing = [];

    /// <summary>Inserts after every item whose order is not greater: stable, so equal orders keep
    /// the collection's order.</summary>
    private static void InsertInOrder<T>(List<T> list, T item, Func<T, int> order)
    {
        int key = order(item);
        int index = list.Count;
        while (index > 0 && order(list[index - 1]) > key)
        {
            index--;
        }

        list.Insert(index, item);
    }

    /// <summary>Whether this frame's <c>base.Update</c> has already updated the components.</summary>
    internal bool ComponentsUpdatedThisFrame { get; private set; }

    /// <summary>Whether this frame's <c>base.Draw</c> has already drawn the components.</summary>
    internal bool ComponentsDrawnThisFrame { get; private set; }

    protected virtual void EndDraw()
    {
    }

    protected virtual void EndRun()
    {
    }

    /// <summary>
    /// XNA's <c>Game.Initialize</c> ends by calling <see cref="LoadContent"/>, and a great deal of
    /// real XNA code depends on it. The pattern is everywhere in the sample collection:
    ///
    /// <code>
    /// protected override void Initialize()
    /// {
    ///     base.Initialize();
    ///     personPosition.X = (safeBounds.Width - personTexture.Width) / 2;   // loaded by now
    /// }
    /// </code>
    ///
    /// <b>This used to be empty</b>, and content was loaded only by the separate native
    /// <c>load_content</c> callback, which arrives after <c>initialize</c>. Every game written that
    /// way therefore read a null texture and died inside the initialize callback with nothing but
    /// "Object reference not set to an instance of an object" to go on. `cna-cs-samples`
    /// CSSAMPLE-019, the unmodified original RectangleCollision, is the case in point.
    ///
    /// FNA is the authority (`src/Game.cs:623`): its <c>Initialize</c> initializes the components
    /// and then calls <c>LoadContent()</c> directly when a graphics device already exists, or
    /// defers it to <c>DeviceCreated</c> when one does not. Here the device is guaranteed --
    /// <c>CNA.Game.RunInitializeOnce</c> calls <c>EnsureGraphicsDevice()</c> before this runs, and
    /// <see cref="OnInitializeFromBackend"/> touches <see cref="GraphicsDevice"/> again -- so the
    /// direct call is the whole of it.
    ///
    /// The once-guard is what keeps the native callback from loading a second time, and it is also
    /// what keeps a game that does <b>not</b> call <c>base.Initialize()</c> working exactly as
    /// before: content then loads at the native callback, as it always did.
    ///
    /// This lives in the XNA facade rather than in <c>CNA.Game</c> on purpose. The lifecycle
    /// <c>CNA.Game</c> presents is the C ABI's own -- initialize, load_content, begin_run, update,
    /// draw, each delivered separately -- and that is a coherent contract for a CNA-first game. XNA's
    /// contract is a different one, and this is the layer that owes it.
    /// </summary>
    /// <summary>
    /// XNA's <c>Game.Initialize</c>: every component not yet initialized, in collection order --
    /// one a component adds while initializing included -- then <see cref="LoadContent"/> (XNA IL).
    /// A game's override that reads a component's content right after <c>base.Initialize()</c>
    /// sees it. CNA's own <c>Game::Initialize</c> runs after the override returns and reaches the
    /// same components; <see cref="InitializeComponentOnce"/> makes that the no-op it is in XNA.
    /// </summary>
    protected virtual void Initialize()
    {
        for (int index = 0; index < Components.Count; index++)
        {
            InitializeComponentOnce(Components[index]);
        }

        EnsureContentLoaded();
    }

    private readonly HashSet<IGameComponent> _initializedComponents = new(ReferenceEqualityComparer.Instance);

    /// <summary>Initializes a component unless this game already has since it was added.</summary>
    internal void InitializeComponentOnce(IGameComponent component)
    {
        if (_initializedComponents.Add(component))
        {
            component.Initialize();
        }
    }

    /// <summary>A removed component is initialized again when it is added again, as in XNA.</summary>
    internal void ForgetComponentInitialization(IGameComponent component) =>
        _initializedComponents.Remove(component);

    protected virtual void LoadContent()
    {
    }

    private bool _contentLoaded;

    /// <summary>Runs <see cref="LoadContent"/> exactly once, whichever of the two paths reaches it
    /// first: <see cref="Initialize"/> through a game's <c>base.Initialize()</c>, or the native
    /// <c>load_content</c> callback for a game that does not call it.</summary>
    private void EnsureContentLoaded()
    {
        if (_contentLoaded)
        {
            return;
        }

        _contentLoaded = true;
        LoadContent();
    }

    protected virtual void OnActivated(object sender, EventArgs args) => Activated?.Invoke(sender, args);

    protected virtual void OnDeactivated(object sender, EventArgs args) => Deactivated?.Invoke(sender, args);

    protected virtual void OnExiting(object sender, EventArgs args) => Exiting?.Invoke(sender, args);

    protected virtual bool ShowMissingRequirementMessage(Exception exception) => false;

    protected virtual void UnloadContent()
    {
    }

    private void OnInitializeFromBackend()
    {
        _ = GraphicsDevice;

        // Before Initialize, which is where XNA has already named the window: its window type sets
        // the default title in its own constructor, so a game's Initialize can read Window.Title
        // and see it.
        Window.ApplyDefaultTitle();

        Initialize();
    }

    private void OnDrawFromBackend(GameTime gameTime)
    {
        if (!BeginDraw())
        {
            return;
        }

        try
        {
            ComponentsDrawnThisFrame = false;
            Draw(gameTime);
        }
        finally
        {
            EndDraw();
        }
    }

    private sealed class BackendGame : CNA.Game
    {
        private readonly Game _owner;
        private Graphics.GraphicsDevice? _compatGraphicsDevice;

        internal BackendGame(Game owner)
        {
            _owner = owner;
        }

        internal Graphics.GraphicsDevice? CompatGraphicsDevice => _compatGraphicsDevice;

        internal void DisposeCompatGraphicsDevice()
        {
            _compatGraphicsDevice?.DisposeFromOwningGame();
            _compatGraphicsDevice = null;
        }

        protected override void OnGraphicsDeviceRetired() => DisposeCompatGraphicsDevice();

        protected override CNA.Graphics.GraphicsDevice CreateGraphicsDevice() =>
            (_compatGraphicsDevice ??= new Graphics.GraphicsDevice(NativeHandle)).Framework;

        protected override void Initialize() => _owner.OnInitializeFromBackend();

        protected override void BeginRun() => _owner.BeginRun();

        protected override void EndRun() => _owner.EndRun();

        // Through the guard, not straight to the override: a game whose Initialize called
        // base.Initialize() has already loaded, and XNA loads content once.
        protected override void LoadContent() => _owner.EnsureContentLoaded();

        protected override void Update(CNA.GameTime gameTime)
        {
            if (!_owner._startedRaised)
            {
                _owner._startedRaised = true;
                if (PhoneTitle.Active)
                {
                    PhoneTitle.OnStarted();
                }

                Started?.Invoke(_owner);
            }

            if (PhoneTitle.PumpsGamerServices)
            {
                GamerServices.GamerServicesDispatcher.Update();
            }

            _owner.ComponentsUpdatedThisFrame = false;
            _owner.Update(GameTime.FromFramework(gameTime));
        }

        protected override void Draw(CNA.GameTime gameTime) =>
            _owner.OnDrawFromBackend(GameTime.FromFramework(gameTime));

        protected override void UnloadContent() => _owner.UnloadContent();
    }

    private sealed class NativeGameWindow : GameWindow
    {
        private readonly CNA.GameWindow _backend;

        internal NativeGameWindow(CNA.GameWindow backend)
        {
            _backend = backend;
            Attach(backend);
            backend.ClientSizeChanged += (_, _) => OnClientSizeChanged();
            backend.OrientationChanged += (_, _) => OnOrientationChanged();
            backend.ScreenDeviceNameChanged += (_, _) => OnScreenDeviceNameChanged();
        }

        public override bool AllowUserResizing
        {
            get => _backend.AllowUserResizing;
            set => _backend.AllowUserResizing = value;
        }

        public override Rectangle ClientBounds => _backend.ClientBounds.ToCompat();

        public override DisplayOrientation CurrentOrientation =>
            (DisplayOrientation)(int)_backend.CurrentOrientation;

        public override IntPtr Handle => _backend.Handle;

        public override string ScreenDeviceName => _backend.ScreenDeviceName;

        public override void BeginScreenDeviceChange(bool willBeFullScreen) =>
            _backend.BeginScreenDeviceChange(willBeFullScreen);

        public override void EndScreenDeviceChange(string screenDeviceName, int clientWidth, int clientHeight) =>
            _backend.EndScreenDeviceChange(screenDeviceName, clientWidth, clientHeight);

        protected internal override void SetSupportedOrientations(DisplayOrientation orientations)
        {
        }

        protected override void SetTitle(string title) => _backend.Title = title;
    }
}
