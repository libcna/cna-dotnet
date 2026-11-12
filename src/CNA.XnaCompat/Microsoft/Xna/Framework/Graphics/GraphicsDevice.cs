namespace Microsoft.Xna.Framework.Graphics;

/// <summary>
/// XNA 4.0-compatible <c>GraphicsDevice</c>. Its native implementation is private so the public
/// XNA device no longer inherits CNA's members, interfaces, or base type.
/// </summary>
public class GraphicsDevice : IDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        CNA.Graphics.GraphicsDevice,
        GraphicsDevice> FrameworkFacades = new();

    private readonly CNA.Graphics.GraphicsDevice _framework;
    private BlendState _blendState;
    private DepthStencilState _depthStencilState;
    private bool _blendStateDirty;
    private bool _depthStencilStateDirty;
    private RasterizerState _rasterizerState;
    private readonly SamplerStateCollection _samplerStates;
    private readonly SamplerStateCollection _vertexSamplerStates;
    private readonly TextureCollection _textures;
    private readonly TextureCollection _vertexTextures;
    private bool _disposed;

    internal GraphicsDevice(ulong nativeGameHandleValue)
        : this(CNA.Graphics.GraphicsDevice.CreateFacadeBackend(nativeGameHandleValue))
    {
    }

    /// <summary>
    /// XNA's public device constructor, which creates a device rather than borrowing one.
    ///
    /// It used to adopt the running game's device and overwrite its presentation parameters, on the
    /// grounds that "CNA's backend creates devices as part of a live game" -- and refused outright
    /// when the adapter came from the ambient static enumeration rather than from a device. CNA
    /// 0.19.0 added <c>cna_graphics_device_create</c>, so both the adoption and the refusal are
    /// gone: this constructs an independent, caller-owned device that <see cref="Dispose()"/>
    /// destroys.
    ///
    /// Creating one while a game is running used to kill that game: the native create took the GL
    /// context and did not give it back, so the running game died on its next present. CNA 0.21.0
    /// restores the caller's binding and it no longer does. See
    /// <c>CNA.Graphics.GraphicsDevice</c>'s constructor for the measurement.
    /// </summary>
    public GraphicsDevice(
        GraphicsAdapter adapter,
        GraphicsProfile graphicsProfile,
        PresentationParameters presentationParameters)
        : this(new CNA.Graphics.GraphicsDevice(
            (adapter ?? throw new ArgumentNullException(nameof(adapter))).Framework,
            (CNA.Graphics.GraphicsProfile)(int)graphicsProfile,
            (presentationParameters ?? throw new ArgumentNullException(nameof(presentationParameters))).Framework))
    {
    }

    private GraphicsDevice(CNA.Graphics.GraphicsDevice framework)
    {
        ArgumentNullException.ThrowIfNull(framework);
        _framework = framework;
        FrameworkFacades.Add(_framework, this);

        _blendState = BlendState.Opaque;
        _depthStencilState = DepthStencilState.Default;
        _rasterizerState = RasterizerState.CullCounterClockwise;
        _blendState.Bind(this);
        _depthStencilState.Bind(this);
        _rasterizerState.Bind(this);
        SamplerState.LinearWrap.Bind(this);

        const int pixelSlots = 16;
        int vertexSlots = this.GraphicsProfile ==
            global::Microsoft.Xna.Framework.Graphics.GraphicsProfile.Reach ? 0 : 4;
        _samplerStates = new SamplerStateCollection(this, vertexStage: false, pixelSlots);
        _vertexSamplerStates = new SamplerStateCollection(this, vertexStage: true, vertexSlots);
        _textures = new TextureCollection(this, vertexStage: false, pixelSlots);
        _vertexTextures = new TextureCollection(this, vertexStage: true, vertexSlots);
    }

    internal CNA.Graphics.GraphicsDevice Framework => _framework;

    internal static GraphicsDevice? FromFramework(CNA.Graphics.GraphicsDevice? framework) =>
        framework is not null && FrameworkFacades.TryGetValue(framework, out GraphicsDevice? facade)
            ? facade
            : null;

    /// <summary>The composed backend owns the single native index-buffer binding. This facade
    /// validates XNA resource/device lifetime rules and converts the strict wrapper without keeping
    /// a second binding field that could diverge from native state.</summary>
    internal bool SupportsCnaCapabilityCore(CNA.XnaCompat.Extensions.CnaGraphicsCapability capability) =>
        _framework.SupportsCapability((CNA.Graphics.GraphicsCapability)(uint)capability);

    internal string CnaRendererName => _framework.RendererName;

    public IndexBuffer? Indices
    {
        get => IndexBuffer.FromFramework(_framework.Indices);
        set
        {
            ThrowIfDisposedForBinding();
            ThrowIfDisposed(value);
            if (value is not null && !ReferenceEquals(value.GraphicsDevice, this))
            {
                throw new InvalidOperationException("The index buffer was created for a different GraphicsDevice.");
            }

            _framework.Indices = value?.FrameworkBuffer;
        }
    }

    public void SetVertexBuffer(VertexBuffer? vertexBuffer)
    {
        ValidateVertexBuffer(vertexBuffer);
        _framework.SetVertexBuffer(vertexBuffer?.FrameworkBuffer);
    }

    public void SetVertexBuffer(VertexBuffer? vertexBuffer, int vertexOffset)
    {
        ValidateVertexBuffer(vertexBuffer);
        if (vertexBuffer is null)
        {
            _framework.SetVertexBuffer(null);
            return;
        }

        _framework.SetVertexBuffers(new CNA.Graphics.VertexBufferBinding(
            vertexBuffer.FrameworkBuffer,
            vertexOffset));
    }

    public void SetRenderTarget(RenderTarget2D? renderTarget) =>
        _framework.SetRenderTarget(renderTarget?.FrameworkTexture as CNA.Graphics.Texture2D);

    public void SetRenderTarget(RenderTargetCube? renderTarget, CubeMapFace cubeMapFace) =>
        _framework.SetRenderTarget(
            renderTarget?.FrameworkTexture as CNA.Graphics.RenderTargetCube,
            (CNA.Graphics.CubeMapFace)(int)cubeMapFace);

    public void DrawPrimitives(PrimitiveType primitiveType, int startVertex, int primitiveCount)
    {
        ThrowIfDisposedForDraw();
        if (primitiveCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        }

        ValidateBoundDrawInputs(indexed: false);
        _framework.DrawPrimitives(
            (CNA.Graphics.PrimitiveType)(int)primitiveType, startVertex, primitiveCount);
    }

    public void DrawIndexedPrimitives(
        PrimitiveType primitiveType, int baseVertex, int minVertexIndex, int numVertices, int startIndex, int primitiveCount)
    {
        ThrowIfDisposedForDraw();
        if (numVertices <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numVertices));
        }

        if (primitiveCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        }

        ValidateBoundDrawInputs(indexed: true);
        _framework.DrawIndexedPrimitives(
            (CNA.Graphics.PrimitiveType)(int)primitiveType, baseVertex, minVertexIndex,
            numVertices, startIndex, primitiveCount);
    }

    public void Clear(Color color) => _framework.Clear(color.ToFramework());

    public void Clear(ClearOptions options, Color color, float depth, int stencil) =>
        _framework.Clear((CNA.Graphics.ClearOptions)(int)options, color.ToFramework(), depth, stencil);

    public void Clear(ClearOptions options, Vector4 color, float depth, int stencil) =>
        Clear(options, new Color(color.X, color.Y, color.Z, color.W), depth, stencil);

    public Viewport Viewport
    {
        get => QueryLiveDevice(static device => Viewport.FromNative(device._framework.Viewport));
        set => _framework.Viewport = value.ToNative();
    }

    public GraphicsProfile GraphicsProfile => Query(static device => (GraphicsProfile)device._framework.GraphicsProfile);

    public bool IsDisposed => _disposed || _framework.IsDisposed;

    /// <summary>A device query from another thread -- XNA's loading threads read the viewport and
    /// the presentation parameters -- runs on the game thread as one call: the device CNA lends
    /// lasts one callback scope, so resolving it and reading through it cannot be two queued calls
    /// that a frame boundary might fall between (CSX-101). Translate the captured native exception
    /// identity after the call, including when the query ran on another thread.</summary>
    private T Query<T>(Func<GraphicsDevice, T> read) =>
        XnaExceptions.Guard(() =>
            CNA.GameThread.IsCurrent ? read(this) : CNA.GameThread.Invoke(read, this));

    private T QueryLiveDevice<T>(Func<GraphicsDevice, T> read)
    {
        // These native getters call ThrowIfDisposed. The C API refuses to dispose a game's
        // borrowed device, so enforce explicit facade disposal here as well. Cached-state
        // getters use Query directly and keep their native post-disposal read behavior.
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Query(read);
    }

    /// <summary>Overrides one field of the active blend state, so -- as in XNA -- assigning that
    /// same <see cref="BlendState"/> object again applies it again instead of being skipped.</summary>
    public int MultiSampleMask
    {
        get => Query(static device => device._framework.MultiSampleMask);
        set
        {
            _framework.MultiSampleMask = value;
            _blendStateDirty = true;
        }
    }

    /// <summary>The depth-stencil counterpart of <see cref="MultiSampleMask"/>.</summary>
    public int ReferenceStencil
    {
        get => Query(static device => device._framework.ReferenceStencil);
        set
        {
            _framework.ReferenceStencil = value;
            _depthStencilStateDirty = true;
        }
    }

    private EventHandler<EventArgs>? _disposing;
    private EventHandler<EventArgs>? _deviceLost;
    private EventHandler<EventArgs>? _deviceReset;
    private EventHandler<EventArgs>? _deviceResetting;
    private EventHandler<ResourceCreatedEventArgs>? _resourceCreated;
    private EventHandler<ResourceDestroyedEventArgs>? _resourceDestroyed;

    public event EventHandler<EventArgs>? Disposing
    {
        add
        {
            if (_disposing is null)
            {
                _framework.Disposing += OnFrameworkDisposing;
            }

            _disposing += value;
        }
        remove
        {
            _disposing -= value;
            if (_disposing is null)
            {
                _framework.Disposing -= OnFrameworkDisposing;
            }
        }
    }

    public event EventHandler<EventArgs>? DeviceLost
    {
        add
        {
            if (_deviceLost is null)
            {
                _framework.DeviceLost += OnFrameworkDeviceLost;
            }

            _deviceLost += value;
        }
        remove
        {
            _deviceLost -= value;
            if (_deviceLost is null)
            {
                _framework.DeviceLost -= OnFrameworkDeviceLost;
            }
        }
    }

    public event EventHandler<EventArgs>? DeviceReset
    {
        add
        {
            if (_deviceReset is null)
            {
                _framework.DeviceReset += OnFrameworkDeviceReset;
            }

            _deviceReset += value;
        }
        remove
        {
            _deviceReset -= value;
            if (_deviceReset is null)
            {
                _framework.DeviceReset -= OnFrameworkDeviceReset;
            }
        }
    }

    public event EventHandler<EventArgs>? DeviceResetting
    {
        add
        {
            if (_deviceResetting is null)
            {
                _framework.DeviceResetting += OnFrameworkDeviceResetting;
            }

            _deviceResetting += value;
        }
        remove
        {
            _deviceResetting -= value;
            if (_deviceResetting is null)
            {
                _framework.DeviceResetting -= OnFrameworkDeviceResetting;
            }
        }
    }

    public event EventHandler<ResourceCreatedEventArgs>? ResourceCreated
    {
        add => _resourceCreated += value;
        remove => _resourceCreated -= value;
    }

    public event EventHandler<ResourceDestroyedEventArgs>? ResourceDestroyed
    {
        add => _resourceDestroyed += value;
        remove => _resourceDestroyed -= value;
    }

    public void Present() => _framework.Present();

    public void Present(Rectangle? sourceRectangle, Rectangle? destinationRectangle, IntPtr overrideWindowHandle)
    {
        if (sourceRectangle is null && destinationRectangle is null && overrideWindowHandle == IntPtr.Zero)
        {
            _framework.Present();
            return;
        }

        throw new NotSupportedException(
            "The CNA C ABI exposes only parameterless presentation and cannot represent source/destination rectangles or an override window handle.");
    }

    public void Reset() => _framework.Reset();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool arg0)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EventHandler<EventArgs>? disposing = arg0 ? _disposing : null;
        DetachBackendEvents();
        if (arg0)
        {
            try
            {
                _framework.Dispose();
            }
            finally
            {
                // XNA releases the device and sets IsDisposed before raising Disposing. Invoke the
                // strict handler from managed teardown, after detaching the native bridge, so a
                // callback can never unwind across the unmanaged subscription boundary.
                disposing?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    internal void DisposeFromOwningGame()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        EventHandler<EventArgs>? disposing = _disposing;
        DetachBackendEvents();
        _framework.ReleaseEventSubscriptions();
        disposing?.Invoke(this, EventArgs.Empty);
    }

    private void DetachBackendEvents()
    {
        if (_disposing is not null) _framework.Disposing -= OnFrameworkDisposing;
        if (_deviceLost is not null) _framework.DeviceLost -= OnFrameworkDeviceLost;
        if (_deviceReset is not null) _framework.DeviceReset -= OnFrameworkDeviceReset;
        if (_deviceResetting is not null) _framework.DeviceResetting -= OnFrameworkDeviceResetting;
        _disposing = null;
        _deviceLost = null;
        _deviceReset = null;
        _deviceResetting = null;
        _resourceCreated = null;
        _resourceDestroyed = null;
    }

    private void OnFrameworkDisposing(object? sender, EventArgs args) => _disposing?.Invoke(this, args);

    private void OnFrameworkDeviceLost(object? sender, EventArgs args) => _deviceLost?.Invoke(this, args);

    private void OnFrameworkDeviceReset(object? sender, EventArgs args) => _deviceReset?.Invoke(this, args);

    private void OnFrameworkDeviceResetting(object? sender, EventArgs args) => _deviceResetting?.Invoke(this, args);

    ~GraphicsDevice()
    {
        Dispose(false);
    }

    public void SetVertexBuffers(params VertexBufferBinding[]? vertexBuffers)
    {
        ThrowIfDisposedForBinding();
        if (vertexBuffers is null)
        {
            _framework.SetVertexBuffers(null);
            return;
        }

        var converted = new CNA.Graphics.VertexBufferBinding[vertexBuffers.Length];
        for (int i = 0; i < converted.Length; i++)
        {
            if (vertexBuffers[i].VertexBuffer is null)
            {
                throw new ArgumentException("A vertex-buffer binding cannot contain a null buffer.");
            }

            ValidateVertexBuffer(vertexBuffers[i].VertexBuffer);
            converted[i] = vertexBuffers[i].ToFramework();
        }

        _framework.SetVertexBuffers(converted);
    }

    /// <summary><see cref="VertexBufferBinding"/> is a separate value type per namespace. The
    /// composed backend cross-checks its retained wrappers against native's count; this facade
    /// converts that verified result.</summary>
    public VertexBufferBinding[] GetVertexBuffers()
    {
        CNA.Graphics.VertexBufferBinding[] bindings = _framework.GetVertexBuffers();
        var converted = new VertexBufferBinding[bindings.Length];

        for (int i = 0; i < converted.Length; i++)
        {
            converted[i] = VertexBufferBinding.FromFramework(bindings[i]);
        }

        return converted;
    }

    public GraphicsAdapter Adapter => Query(static device => GraphicsAdapter.FromFramework(device._framework.Adapter));

    public PresentationParameters PresentationParameters
    {
        get => Query(static device => new PresentationParameters(device._framework.PresentationParameters));
    }

    public DisplayMode DisplayMode => QueryLiveDevice(static device => DisplayMode.FromFramework(device._framework.DisplayMode));

    public BlendState BlendState
    {
        get => _blendState;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _blendState) && !_blendStateDirty)
            {
                return;
            }

            value.Bind(this);
            _framework.BlendState = value.Framework;
            _blendState = value;
            _blendStateDirty = false;
        }
    }

    public DepthStencilState DepthStencilState
    {
        get => _depthStencilState;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _depthStencilState) && !_depthStencilStateDirty)
            {
                return;
            }

            value.Bind(this);
            _framework.DepthStencilState = value.Framework;
            _depthStencilState = value;
            _depthStencilStateDirty = false;
        }
    }

    public RasterizerState RasterizerState
    {
        get => _rasterizerState;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _rasterizerState))
            {
                return;
            }

            value.Bind(this);
            _framework.RasterizerState = value.Framework;
            _rasterizerState = value;
        }
    }

    /// <summary>
    /// Records the states a <see cref="SpriteBatch"/> has just applied in native code. XNA's
    /// SpriteBatch assigns them through these properties; here native applies them itself, and
    /// without this the setters above would compare against the state set before the batch and
    /// skip the game's next assignment of it -- the ColorReplacement sample's opaque tyres, whose
    /// texels carry alpha 0, then blended away under the batch's AlphaBlend.
    /// </summary>
    internal void NoteSpriteBatchRenderState(
        BlendState blendState,
        DepthStencilState depthStencilState,
        RasterizerState rasterizerState,
        SamplerState samplerState)
    {
        blendState.Bind(this);
        depthStencilState.Bind(this);
        rasterizerState.Bind(this);
        _blendState = blendState;
        _blendStateDirty = false;
        _depthStencilState = depthStencilState;
        _depthStencilStateDirty = false;
        _rasterizerState = rasterizerState;
        _samplerStates.NoteApplied(0, samplerState);
    }

    public SamplerStateCollection SamplerStates => _samplerStates;

    public SamplerStateCollection VertexSamplerStates => _vertexSamplerStates;

    public TextureCollection Textures => _textures;

    public TextureCollection VertexTextures => _vertexTextures;

    /// <summary>Re-typed because <c>Rectangle</c> is a separate struct per namespace.</summary>
    public Rectangle ScissorRectangle
    {
        get => QueryLiveDevice(static device => device._framework.ScissorRectangle.ToCompat());
        set => _framework.ScissorRectangle = value.ToFramework();
    }

    /// <summary>Re-typed: <c>Color</c> is a separate struct per namespace.</summary>
    public Color BlendFactor
    {
        get => Query(static device => device._framework.BlendFactor.ToCompat());
        set
        {
            _framework.BlendFactor = value.ToFramework();
            _blendStateDirty = true;
        }
    }

    /// <summary>
    /// Copies the back buffer into a caller-provided XNA-compatible pixel buffer. The native CNA
    /// ABI exposes this readback as packed <see cref="Color"/> values, so that representation is
    /// currently the supported generic element type. Keeping the XNA generic overload family is
    /// nevertheless essential: existing XNA callers bind to these signatures, not CNA's former
    /// Color-only convenience members.
    /// </summary>
    /// <summary>
    /// Matches real XNA's generic back-buffer readback. It used to accept only
    /// <see cref="Color"/>; see <c>CNA.Graphics.GraphicsDevice.GetBackBufferData</c>.
    /// </summary>
    public void GetBackBufferData<T>(T[] data)
        where T : struct =>
        GetBackBufferData(null, data, 0, data?.Length ?? 0);

    public void GetBackBufferData<T>(T[] data, int startIndex, int elementCount)
        where T : struct =>
        GetBackBufferData(null, data, startIndex, elementCount);

    public void GetBackBufferData<T>(Rectangle? rect, T[] data, int startIndex, int elementCount)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(elementCount, data.Length - startIndex);

        // No element-type restriction and no per-pixel conversion loop. The readback is RGBA8, and
        // this namespace's Color has the same four-byte layout as the CNA one, so the bytes native
        // writes are already the right bytes -- for Color and for every other element type whose
        // size divides four. Converting element by element also meant allocating a second array the
        // size of the back buffer on every call.
        _framework.GetBackBufferData(rect.ToFramework(), data, startIndex, elementCount);
    }

    /// <summary>Re-typed: <c>GraphicsDeviceStatus</c> is a separate enum per namespace.</summary>
    public GraphicsDeviceStatus GraphicsDeviceStatus => QueryLiveDevice(static device => (GraphicsDeviceStatus)(int)device._framework.GraphicsDeviceStatus);

    /// <summary>Re-typed: takes this namespace's own <see cref="PresentationParameters"/>.</summary>
    public void Reset(PresentationParameters presentationParameters)
    {
        ArgumentNullException.ThrowIfNull(presentationParameters);
        _framework.Reset(presentationParameters.Framework);
    }

    /// <summary>Re-typed: takes this namespace's own <see cref="PresentationParameters"/> and
    /// <see cref="GraphicsAdapter"/>.</summary>
    public void Reset(PresentationParameters presentationParameters, GraphicsAdapter graphicsAdapter)
    {
        ArgumentNullException.ThrowIfNull(presentationParameters);
        ArgumentNullException.ThrowIfNull(graphicsAdapter);
        _framework.Reset(presentationParameters.Framework, graphicsAdapter.Framework);
    }

    /// <summary>Re-typed: <c>PrimitiveType</c> is a separate enum per namespace.</summary>
    public void DrawInstancedPrimitives(
        PrimitiveType primitiveType,
        int baseVertex,
        int minVertexIndex,
        int numVertices,
        int startIndex,
        int primitiveCount,
        int instanceCount)
    {
        ThrowIfDisposedForDraw();
        if (numVertices <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numVertices));
        }

        if (primitiveCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        }

        if (instanceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceCount));
        }

        ValidateBoundDrawInputs(indexed: true);
        XnaExceptions.Guard(() => _framework.DrawInstancedPrimitives(
            (CNA.Graphics.PrimitiveType)(int)primitiveType, baseVertex, minVertexIndex,
            numVertices, startIndex, primitiveCount, instanceCount));
    }

    /// <summary>Matches real XNA's <c>SetRenderTargets</c>. The composed backend takes
    /// <c>CNA.Graphics.RenderTargetBinding</c>, while this namespace's binding is a separate struct,
    /// so the array is converted element-wise.</summary>
    public void SetRenderTargets(params RenderTargetBinding[]? renderTargets)
    {
        if (renderTargets is null || renderTargets.Length == 0)
        {
            _framework.SetRenderTargets();
            return;
        }

        var converted = new CNA.Graphics.RenderTargetBinding[renderTargets.Length];
        for (int i = 0; i < converted.Length; i++)
        {
            converted[i] = renderTargets[i].Framework;
        }

        XnaExceptions.Guard(() => _framework.SetRenderTargets(converted));
    }

    public RenderTargetBinding[] GetRenderTargets()
    {
        CNA.Graphics.RenderTargetBinding[] source = _framework.GetRenderTargets();
        var result = new RenderTargetBinding[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            result[i] = RenderTargetBinding.FromFramework(source[i]);
        }

        return result;
    }

    /// <summary>Matches real XNA's <c>DrawUserPrimitives&lt;T&gt;</c>. Reimplements the
    /// type-to-<c>UserVertexSource</c> mapping for this namespace's own vertex structs rather than
    /// forwarding to <see cref="CNA.Graphics.GraphicsDevice.DrawUserPrimitives{T}"/> directly --
    /// compat vertex structs (e.g. <see cref="VertexPositionTexture"/>) are separate types from
    /// their CNA.Graphics counterparts (structs can't be subclassed to share one), so the backend
    /// generic method's own <c>typeof(T) ==</c> checks would never match. Goes through
    /// <see cref="CNA.Graphics.GraphicsDevice.DrawUserPrimitivesRaw"/> instead, the shared
    /// pointer-and-identity level both namespaces build on.
    ///
    /// Any <see cref="IVertexType"/> works, not only the four the ABI names -- see
    /// <see cref="CNA.Graphics.UserVertexSource"/> for why the previous restriction was
    /// self-imposed.</summary>
    public void DrawUserPrimitives<T>(
        PrimitiveType primitiveType,
        T[] vertexData,
        int vertexOffset,
        int primitiveCount)
        where T : struct, IVertexType
    {
        ThrowIfDisposedForDraw();
        ArgumentNullException.ThrowIfNull(vertexData);
        VertexDeclaration? declaration = TypedVertexSourceFor<T>() is null ? DeclarationFor<T>() : null;
        DrawUserPrimitivesCore(
            primitiveType,
            vertexData,
            vertexOffset,
            primitiveCount,
            declaration?.Framework,
            declaration);
    }

    public void DrawUserPrimitives<T>(
        PrimitiveType primitiveType,
        T[] vertexData,
        int vertexOffset,
        int primitiveCount,
        VertexDeclaration vertexDeclaration)
        where T : struct
    {
        ThrowIfDisposedForDraw();
        ArgumentNullException.ThrowIfNull(vertexData);
        ArgumentNullException.ThrowIfNull(vertexDeclaration);
        DrawUserPrimitivesCore(
            primitiveType, vertexData, vertexOffset, primitiveCount,
            vertexDeclaration.Framework, vertexDeclaration);
    }

    /// <summary>Matches real XNA's <c>DrawUserIndexedPrimitives&lt;T&gt;</c>. Reimplements the
    /// vertex-type mapping for the same reason <c>DrawUserPrimitives&lt;T&gt;</c> does -- compat
    /// vertex structs are separate types from their CNA.Graphics counterparts -- and reaches the
    /// shared raw level underneath.</summary>
    public void DrawUserIndexedPrimitives<T>(
        PrimitiveType primitiveType,
        T[] vertexData,
        int vertexOffset,
        int numVertices,
        int[] indexData,
        int indexOffset,
        int primitiveCount)
        where T : struct, IVertexType
    {
        ThrowIfDisposedForDraw();
        VertexDeclaration? declaration = TypedVertexSourceFor<T>() is null ? DeclarationFor<T>() : null;
        DrawUserIndexedPrimitivesCore(
            primitiveType, vertexData, vertexOffset, numVertices, indexData, indexOffset,
            primitiveCount, declaration?.Framework, declaration);
    }

    public void DrawUserIndexedPrimitives<T>(
        PrimitiveType primitiveType,
        T[] vertexData,
        int vertexOffset,
        int numVertices,
        short[] indexData,
        int indexOffset,
        int primitiveCount)
        where T : struct, IVertexType
    {
        ThrowIfDisposedForDraw();
        VertexDeclaration? declaration = TypedVertexSourceFor<T>() is null ? DeclarationFor<T>() : null;
        DrawUserIndexedPrimitivesCore(
            primitiveType, vertexData, vertexOffset, numVertices, indexData, indexOffset,
            primitiveCount, declaration?.Framework, declaration);
    }

    public void DrawUserIndexedPrimitives<T>(
        PrimitiveType primitiveType,
        T[] vertexData,
        int vertexOffset,
        int numVertices,
        int[] indexData,
        int indexOffset,
        int primitiveCount,
        VertexDeclaration vertexDeclaration)
        where T : struct
    {
        ThrowIfDisposedForDraw();
        ValidateIndexedArraysBeforeDeclaration(vertexData, indexData);
        ArgumentNullException.ThrowIfNull(vertexDeclaration);
        DrawUserIndexedPrimitivesCore(
            primitiveType, vertexData, vertexOffset, numVertices, indexData, indexOffset,
            primitiveCount, vertexDeclaration.Framework, vertexDeclaration);
    }

    public void DrawUserIndexedPrimitives<T>(
        PrimitiveType primitiveType,
        T[] vertexData,
        int vertexOffset,
        int numVertices,
        short[] indexData,
        int indexOffset,
        int primitiveCount,
        VertexDeclaration vertexDeclaration)
        where T : struct
    {
        ThrowIfDisposedForDraw();
        ValidateIndexedArraysBeforeDeclaration(vertexData, indexData);
        ArgumentNullException.ThrowIfNull(vertexDeclaration);
        DrawUserIndexedPrimitivesCore(
            primitiveType, vertexData, vertexOffset, numVertices, indexData, indexOffset,
            primitiveCount, vertexDeclaration.Framework, vertexDeclaration);
    }

    private unsafe void DrawUserPrimitivesCore<T>(
        PrimitiveType primitiveType,
        T[] vertexData,
        int vertexOffset,
        int primitiveCount,
        CNA.Graphics.VertexDeclaration? vertexDeclaration,
        VertexDeclaration? facadeDeclaration)
        where T : struct
    {
        ThrowIfDisposedForDraw();
        ArgumentNullException.ThrowIfNull(vertexData);
        if (primitiveCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        }

        if (vertexOffset < 0 || vertexOffset >= vertexData.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexOffset));
        }

        long requiredElements = GetElementCount(primitiveType, primitiveCount);
        if (requiredElements > vertexData.LongLength - vertexOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        }

        ThrowIfDisposed(facadeDeclaration);
        RejectManagedReferences<T>(nameof(vertexData));

        CNA.Graphics.UserVertexSource? typedSource = TypedVertexSourceFor<T>();
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(
            vertexData, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            _framework.DrawUserPrimitivesRaw(
                (CNA.Graphics.PrimitiveType)(int)primitiveType,
                (void*)pin.AddrOfPinnedObject(),
                vertexDeclaration is null && typedSource is { } source
                    ? source
                    : CNA.Graphics.UserVertexSource.RawStream,
                vertexOffset,
                primitiveCount,
                vertexDeclaration);
        }
        finally
        {
            pin.Free();
        }
    }

    private unsafe void DrawUserIndexedPrimitivesCore<TVertex, TIndex>(
        PrimitiveType primitiveType,
        TVertex[] vertexData,
        int vertexOffset,
        int numVertices,
        TIndex[] indexData,
        int indexOffset,
        int primitiveCount,
        CNA.Graphics.VertexDeclaration? vertexDeclaration,
        VertexDeclaration? facadeDeclaration)
        where TVertex : struct
        where TIndex : struct
    {
        ThrowIfDisposedForDraw();
        ValidateIndexedArraysBeforeDeclaration(vertexData, indexData);
        if (numVertices <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numVertices));
        }

        if (primitiveCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        }

        if (vertexOffset < 0 || vertexOffset >= vertexData.LongLength)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexOffset));
        }

        if (indexOffset < 0 || indexOffset >= indexData.LongLength)
        {
            throw new ArgumentOutOfRangeException(nameof(indexOffset));
        }

        long requiredElements = GetElementCount(primitiveType, primitiveCount);
        if (requiredElements > indexData.LongLength - indexOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(primitiveCount));
        }

        if ((long)vertexOffset + numVertices > vertexData.LongLength)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexData));
        }

        ThrowIfDisposed(facadeDeclaration);
        RejectManagedReferences<TVertex>(nameof(vertexData));
        RejectManagedReferences<TIndex>(nameof(indexData));

        CNA.Graphics.UserVertexSource? typedSource = TypedVertexSourceFor<TVertex>();
        CNA.Graphics.IndexElementSize indexElementSize =
            CNA.Graphics.IndexBuffer.SizeForType(typeof(TIndex));
        var vertexPin = System.Runtime.InteropServices.GCHandle.Alloc(
            vertexData, System.Runtime.InteropServices.GCHandleType.Pinned);
        var indexPin = System.Runtime.InteropServices.GCHandle.Alloc(
            indexData, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            _framework.DrawUserIndexedPrimitivesRaw(
                (CNA.Graphics.PrimitiveType)(int)primitiveType,
                (void*)vertexPin.AddrOfPinnedObject(),
                vertexDeclaration is null && typedSource is { } source
                    ? source
                    : CNA.Graphics.UserVertexSource.RawStream,
                vertexOffset,
                numVertices,
                (void*)indexPin.AddrOfPinnedObject(),
                indexElementSize,
                indexOffset,
                primitiveCount,
                vertexDeclaration);
        }
        finally
        {
            indexPin.Free();
            vertexPin.Free();
        }
    }

    private static void RejectManagedReferences<T>(string parameterName) where T : struct
    {
        if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new ArgumentException(
                $"Vertex/index type {typeof(T)} contains managed references.", parameterName);
        }
    }

    private void ThrowIfDisposedForDraw()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(GraphicsDevice));
        }
    }

    private void ThrowIfDisposedForBinding()
    {
        if (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(GraphicsDevice));
        }
    }

    private static void ThrowIfDisposed(GraphicsResource? resource)
    {
        if (resource?.IsDisposed == true)
        {
            throw new ObjectDisposedException(resource.GetType().Name);
        }
    }

    private void ValidateVertexBuffer(VertexBuffer? vertexBuffer)
    {
        ThrowIfDisposedForBinding();
        ThrowIfDisposed(vertexBuffer);
        if (vertexBuffer is not null && !ReferenceEquals(vertexBuffer.GraphicsDevice, this))
        {
            throw new InvalidOperationException("The vertex buffer was created for a different GraphicsDevice.");
        }
    }

    private void ValidateBoundDrawInputs(bool indexed)
    {
        if ((indexed && _framework.Indices is null) || !_framework.HasBoundVertexBuffers)
        {
            throw new InvalidOperationException("A vertex buffer and, for indexed draws, an index buffer must be bound before drawing.");
        }
    }

    private static void ValidateIndexedArraysBeforeDeclaration<TVertex, TIndex>(
        TVertex[]? vertexData,
        TIndex[]? indexData)
    {
        if (vertexData is null || vertexData.Length == 0)
        {
            throw new ArgumentNullException(nameof(vertexData));
        }

        if (indexData is null || indexData.Length == 0)
        {
            throw new ArgumentNullException(nameof(indexData));
        }
    }

    private static long GetElementCount(PrimitiveType primitiveType, int primitiveCount) =>
        primitiveType switch
        {
            PrimitiveType.LineList => (long)primitiveCount * 2,
            PrimitiveType.TriangleList => (long)primitiveCount * 3,
            PrimitiveType.LineStrip => (long)primitiveCount + 1,
            PrimitiveType.TriangleStrip => (long)primitiveCount + 2,
            _ => long.MaxValue,
        };

    /// <summary><see langword="null"/> for a vertex type the ABI does not name directly, which is
    /// a fall-through to the raw-stream route rather than a failure -- see
    /// <see cref="CNA.Graphics.UserVertexSource"/> for the header evidence that the raw route was
    /// never actually blocked.</summary>
    internal static CNA.Graphics.UserVertexSource? TypedVertexSourceFor<T>() where T : struct
    {
        if (typeof(T) == typeof(VertexPositionColor))
        {
            return CNA.Graphics.UserVertexSource.PositionColor;
        }

        if (typeof(T) == typeof(VertexPositionColorTexture))
        {
            return CNA.Graphics.UserVertexSource.PositionColorTexture;
        }

        if (typeof(T) == typeof(VertexPositionTexture))
        {
            return CNA.Graphics.UserVertexSource.PositionTexture;
        }

        if (typeof(T) == typeof(VertexPositionNormalTexture))
        {
            return CNA.Graphics.UserVertexSource.PositionNormalTexture;
        }

        return null;
    }

    /// <summary>Derives the declaration for a compat vertex type with no
    /// <c>CNA_UserVertexSource</c> identity. Goes through this namespace's own
    /// <see cref="VertexDeclaration"/>, which converts implicitly to the CNA one, so a compat
    /// <see cref="IVertexType"/> is read through the compat interface it actually
    /// implements.</summary>
    private static VertexDeclaration DeclarationFor<T>() where T : struct
    {
        if (Activator.CreateInstance<T>() is not IVertexType instance)
        {
            throw new NotSupportedException(
                $"DrawUserPrimitives<{typeof(T).Name}> needs {typeof(T).Name} to implement IVertexType, so its " +
                "vertex declaration can be derived. Only VertexPositionColor, VertexPositionColorTexture, " +
                "VertexPositionTexture and VertexPositionNormalTexture are drawable without one.");
        }

        return instance.VertexDeclaration;
    }
}
