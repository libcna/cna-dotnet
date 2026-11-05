using CNA.Interop;

namespace CNA.Graphics;

/// <summary>
/// Every method resolves a fresh native device handle via <see cref="ResolveNativeDeviceHandle"/>
/// before using it, rather than caching one -- the real, shipped openeggbert/cna C API documents
/// the device handle <c>cna_game_get_graphics_device</c> returns as valid only until the lifecycle
/// callback that fetched it returns (confirmed directly against the real test suite, not just the
/// header doc comment: calling it outside a callback fails, and a handle captured during one
/// callback goes stale once a later, separate callback begins -- see
/// <c>Game.GetNativeGraphicsDeviceHandle</c>'s own doc comment for the concrete evidence). Every
/// <see cref="GraphicsDevice"/> method is only ever called from within a game's own
/// <c>Update</c>/<c>Draw</c>/<c>LoadContent</c>/<c>Initialize</c> override -- themselves real
/// lifecycle callbacks -- so resolving fresh, synchronously, at the top of each method stays inside
/// the same callback invocation the whole time, which is exactly what the real ABI requires.
/// </summary>
public class GraphicsDevice : IDisposable
{
    /// <summary>What <see cref="SetRenderTargets"/> last bound -- see
    /// <see cref="GetRenderTargets"/> for why the answer is kept here rather than read back.</summary>
    private RenderTargetBinding[] _boundRenderTargets = [];

    /// <summary>What <see cref="SetVertexBuffer"/>/<see cref="SetVertexBuffers"/> last bound, for
    /// the reason <see cref="GetVertexBuffers"/> gives -- the same shape
    /// <see cref="_boundRenderTargets"/> already uses.</summary>
    private VertexBufferBinding[] _boundVertexBuffers = [];

    /// <summary>Whether at least one vertex stream was successfully bound through this facade.
    /// Used by the strict XNA facade to reproduce XNA's managed pre-dispatch validation without
    /// asking native code to translate a missing-data failure back into an arbitrary exception.</summary>
    internal bool HasBoundVertexBuffers => _boundVertexBuffers.Length != 0;

    /// <summary>
    /// The raw native *game* handle value -- not the device handle; see this class's own doc
    /// comment for why holding a cached device handle across calls is unsafe under the real ABI.
    /// <c>protected internal</c> (not <c>internal</c>) so CNA.XnaCompat's <c>GraphicsDevice</c>
    /// subclass constructor can forward it to <c>base()</c> without CNA.XnaCompat ever naming
    /// <see cref="CnaHandle"/> -- see docs/architecture.md. Also read directly by every other
    /// native-backed <c>CNA.Framework</c> resource type (<c>VertexBuffer</c>, <c>IndexBuffer</c>,
    /// <c>Texture2D</c>, <c>RenderTarget2D</c>, <c>SpriteBatch</c>, <c>BasicEffect</c>) through
    /// <see cref="ResolveNativeDeviceHandle"/> rather than a cached field of their own, for the same
    /// reason.
    /// </summary>
    protected internal ulong NativeGameHandleValue { get; }

    /// <summary>
    /// Set only for a device this object created and owns. Null for the ordinary case, where the
    /// device belongs to a game and is borrowed for the duration of a lifecycle callback.
    ///
    /// This is the whole of the difference between the two kinds. Everything else -- resource
    /// creation, state, drawing -- goes through <see cref="ResolveNativeDeviceHandle"/> and cannot
    /// tell them apart, which is what the header promises: the created handle "is accepted
    /// everywhere a borrowed device handle is".
    /// </summary>
    private readonly NativeResourceHandle? _ownedDevice;

    protected internal GraphicsDevice(ulong nativeGameHandleValue)
    {
        NativeGameHandleValue = nativeGameHandleValue;
    }

    /// <summary>
    /// Creates an independent device the caller owns, matching real XNA's
    /// <c>GraphicsDevice(GraphicsAdapter, GraphicsProfile, PresentationParameters)</c> constructor.
    ///
    /// Every other device in this binding is a game's, borrowed for one callback. CNA 0.19.0 added
    /// <c>cna_graphics_device_create</c>, which takes XNA's own three constructor arguments and
    /// hands back a handle the caller destroys. Several may exist at once, one may be destroyed
    /// while another is live, and -- unlike a game's resources -- resources created on it do not
    /// gate <c>cna_game_destroy</c>, because they belong to this device and are released with it.
    ///
    /// Before that route existed this constructor could only borrow the running game's device and
    /// overwrite its presentation parameters, or refuse when no game was running. Both were wrong:
    /// XNA's constructor creates a device, and a second XNA device does not disturb the first.
    ///
    /// The device is not attached to a game, so <see cref="NativeGameHandleValue"/> stays zero and
    /// nothing here may resolve through the game route.
    ///
    /// <b>Creating one while a game is running is safe as of CNA 0.21.0.</b> It was not before:
    /// <c>cna_graphics_device_create</c> made its own GL context current and did not restore the
    /// game's, so the game's next frame failed in <c>SwapBuffers</c> with "the specified window has
    /// not been made current". Upstream now saves and restores the caller's binding around device
    /// creation, and the runtime probe's <c>devicelifecycle.cross_device</c> pair -- gated to
    /// <c>not-run</c> for exactly this reason -- runs ungated and reports <c>ok</c> for both, with
    /// the game surviving every later observation in the same process.
    /// </summary>
    public GraphicsDevice(
        GraphicsAdapter adapter,
        GraphicsProfile graphicsProfile,
        PresentationParameters presentationParameters)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(presentationParameters);

        CnaPresentationParameters parameters = presentationParameters.ToNative();
        CnaResult result = Native.cna_graphics_device_create(
            adapter.AdapterIndex, (uint)graphicsProfile, in parameters, out CnaHandle device);
        CnaException.ThrowIfFailed(result, nameof(GraphicsDevice));

        _ownedDevice = new NativeResourceHandle(
            device.Value, handle => Native.cna_graphics_device_destroy(new CnaHandle(handle)).IsSuccess());
    }

    /// <summary>Whether this device was created by <see cref="GraphicsDevice(GraphicsAdapter,
    /// GraphicsProfile, PresentationParameters)"/> rather than borrowed from a game. Callers need
    /// this to know who is responsible for destroying it.</summary>
    public bool IsCallerOwned => _ownedDevice is not null;

    /// <summary>
    /// Whether this build of CNA has its CNAEXT engine layer.
    ///
    /// Static, and not a device query, because the answer is a property of the linked library. The
    /// engine layer's whole surface is exported by every build and answers <c>NOT_SUPPORTED</c>
    /// when the layer is absent, so a resolved symbol proves nothing and this is what to ask.
    /// </summary>
    /// <summary>
    /// Tells every content-losable resource on this device that its content is gone.
    ///
    /// <b>A test hook, not a game route.</b> A real device loses content when the platform takes it
    /// away, and the subscription that matters is the one a game already has on
    /// <c>RenderTarget2D.ContentLost</c>. The problem is that the renderers available here do not
    /// lose devices, so nothing ever fires and the subscription is untested -- and an event that
    /// has never fired is an event nobody knows is wired up.
    ///
    /// Calling this from an ordinary game path would tell every render target its content is gone
    /// while it demonstrably is not, which is the one thing a game must be able to trust here.
    ///
    /// Native iterates a snapshot rather than the live list, because a subscriber is free to
    /// dispose the resource it is being told about.
    /// </summary>
    public void NotifyContentLostResourcesForTesting()
    {
        CnaResult result = Native.cna_graphics_device_notify_content_lost_resources_ext(
            ResolveNativeDeviceHandle());
        CnaException.ThrowIfFailed(result, nameof(NotifyContentLostResourcesForTesting));
    }

    /// <summary>
    /// What this renderer can do with one surface format, and how much of that it has classified.
    ///
    /// CNA's own query, with no XNA counterpart: XNA had <c>GraphicsAdapter.CheckDeviceFormat</c>
    /// against a fixed set of D3D9 usages, and this is a different and larger question. It is here
    /// rather than on the strict facade for that reason.
    ///
    /// <b>Two masks, not one boolean.</b> A bit absent from <see cref="CnaSurfaceFormatSupport.Known"/>
    /// means the renderer has not classified that usage, which is not the same as refusing it --
    /// <c>graphics.h</c> says so explicitly and forbids inferring one from the other. A caller
    /// choosing among several encodings of one asset needs that distinction: "unclassified" is a
    /// reason to prefer another encoding, and "refused" is a reason to rule this one out.
    /// </summary>
    public CnaSurfaceFormatSupport GetCnaSurfaceFormatSupport(SurfaceFormat format)
    {
        CnaResult result = Native.cna_graphics_device_get_surface_format_support_ext(
            ResolveNativeDeviceHandle(), (uint)format,
            out CnaRendererFormatUsage known, out CnaRendererFormatUsage supported);
        CnaException.ThrowIfFailed(result, nameof(GetCnaSurfaceFormatSupport));
        return new CnaSurfaceFormatSupport((CnaSurfaceFormatUsage)known, (CnaSurfaceFormatUsage)supported);
    }

    /// <summary>
    /// Creates the native-backed implementation used by the XNA facade. Keeping this factory
    /// internal lets the facade compose the device without widening this implementation's public
    /// constructor surface or exposing an interop handle.
    /// </summary>
    internal static GraphicsDevice CreateFacadeBackend(ulong nativeGameHandleValue) =>
        new(nativeGameHandleValue);

    /// <summary>Resolves a fresh, currently-valid native device handle for this call only -- see
    /// this class's own doc comment. Every other native-backed resource type in
    /// <c>CNA.Framework</c> that needs a device handle to create itself calls this too (through the
    /// owning <see cref="GraphicsDevice"/> instance), rather than reading a cached handle of its
    /// own.</summary>
    internal CnaHandle ResolveNativeDeviceHandle()
    {
        ObjectDisposedException.ThrowIf(_retiredByManager, this);
        // A caller-owned device has no game to resolve through, and its handle does not expire at a
        // callback boundary -- that lifetime rule is a property of the borrow, not of devices.
        if (_ownedDevice is { } owned)
        {
            var handle = new CnaHandle(owned.DangerousGetHandle());
            GC.KeepAlive(this);
            return handle;
        }

        CnaResult result = Native.cna_game_get_graphics_device(new CnaHandle(NativeGameHandleValue), out CnaHandle device);
        CnaException.ThrowIfFailed(result, "cna_game_get_graphics_device");
        return device;
    }

    /// <summary>
    /// Real XNA's <c>Clear(Color)</c>: <c>Clear(DefaultClearOptions, color, 1f, 0)</c>, read from
    /// the XNA 4.0 IL. The planes are the ones the active target actually has, and the depth is
    /// 1.0 whatever the viewport's range -- CNA's C++ <c>GraphicsDevice::Clear(Color)</c> is the
    /// same (SOFTWARE-333/334).
    ///
    /// It used to pass all three planes and <c>Viewport.MaxDepth</c>, FNA's form, which relies on
    /// the device masking planes it lacks. Since C ABI 0.35 <c>cna_graphics_device_clear_options</c>
    /// refuses a plane the target does not have, as XNA's explicit overload does, so that form threw
    /// on any depth-less back buffer or render target. Clearing the depth plane at all still matters
    /// (`cna-cs-samples` CSSAMPLE-001 went black when only <c>Target</c> was cleared).
    /// </summary>
    public void Clear(Color color) => Clear(DefaultClearOptions, color, 1f, 0);

    /// <summary>
    /// XNA's private <c>DefaultClearOptions</c>: <c>Target</c>, plus <c>DepthBuffer</c> when the
    /// active depth format is not <see cref="DepthFormat.None"/>, plus <c>Stencil</c> for
    /// <see cref="DepthFormat.Depth24Stencil8"/>. The active format is the first bound render
    /// target's, or the back buffer's when none is bound.
    ///
    /// For the back buffer a plane also has to exist, as in CNA's C++
    /// <c>GraphicsDevice::GetDefaultClearOptions</c>: a renderer without
    /// <see cref="GraphicsCapability.DepthStencilBuffer"/> (SDL_RENDERER) keeps the requested
    /// format in its presentation parameters but has no depth plane, and
    /// <c>cna_graphics_device_clear_options</c> refuses a clear of one. Asking by the format alone
    /// made every <c>Clear(Color)</c> throw there (CNA plans/plan_apple_m4.md AM4-231).
    /// </summary>
    private ClearOptions DefaultClearOptions
    {
        get
        {
            if (_boundRenderTargets.Length > 0)
            {
                return OptionsFor(_boundRenderTargets[0].RenderTarget switch
                {
                    RenderTarget2D target => target.DepthStencilFormat,
                    RenderTargetCube cube => cube.DepthStencilFormat,
                    _ => DepthFormat.None,
                }, hasDepthPlane: true, hasStencilPlane: true);
            }

            DepthFormat format = PresentationParameters.DepthStencilFormat;
            return format == DepthFormat.None
                ? ClearOptions.Target
                : OptionsFor(format,
                    hasDepthPlane: SupportsCapability(GraphicsCapability.DepthStencilBuffer),
                    hasStencilPlane: SupportsCapability(GraphicsCapability.StencilBuffer));

            static ClearOptions OptionsFor(DepthFormat format, bool hasDepthPlane, bool hasStencilPlane)
            {
                ClearOptions options = ClearOptions.Target;
                if (format != DepthFormat.None && hasDepthPlane)
                    options |= ClearOptions.DepthBuffer;
                if (format == DepthFormat.Depth24Stencil8 && hasStencilPlane)
                    options |= ClearOptions.Stencil;
                return options;
            }
        }
    }

    /// <summary>Matches real XNA's full <c>Clear(ClearOptions, Color, float, int)</c> overload --
    /// unlike the simple <see cref="Clear(Color)"/> overload above, <paramref name="options"/> is
    /// the caller's own bitmask (cast straight across, since <see cref="ClearOptions"/>'s values
    /// match <see cref="CnaClearOptions"/> exactly) instead of a hardcoded
    /// <see cref="CnaClearOptions.Target"/>-only value.</summary>
    public void Clear(ClearOptions options, Color color, float depth, int stencil)
    {
        CnaResult result = Native.cna_graphics_device_clear_options(
            ResolveNativeDeviceHandle(), (CnaClearOptions)options, color.ToNative(), depth, stencil);
        CnaException.ThrowIfFailed(result, nameof(Clear));
    }

    /// <summary>
    /// Whether the active renderer supports <paramref name="capability"/>.
    ///
    /// <b>Added because a template got the wrong answer by guessing.</b> <c>cna-dotnet-template</c>
    /// probed for this method through <c>dynamic</c>, caught the resulting binder failure, and fell
    /// back to <c>true</c> -- so on SDL_RENDERER, a renderer that prints "2D-only" during startup,
    /// it reported "3D pipeline: yes", built a <see cref="BasicEffect"/>, and died in
    /// <c>DrawUserPrimitives</c>. The fallback was the problem, but the method not existing is what
    /// made a fallback necessary.
    ///
    /// A recognized but unavailable capability is a successful query answering <see langword="false"/>,
    /// not an error. Operations needing it fail with <c>NOT_SUPPORTED</c> rather than substituting
    /// other behaviour -- which is exactly why asking first is worth doing.
    ///
    /// Not an XNA member: XNA had fixed Reach/HiDef profiles where every conforming device
    /// supported everything. CNA's renderers genuinely differ, so <see cref="GraphicsProfile"/>
    /// cannot answer this.
    /// </summary>
    public bool SupportsCapability(GraphicsCapability capability)
    {
        CnaResult result = Native.cna_graphics_device_supports_capability(
            ResolveNativeDeviceHandle(), (uint)capability, out byte supported);
        CnaException.ThrowIfFailed(result, nameof(SupportsCapability));
        return supported != 0;
    }

    /// <summary>The active renderer's own name (<c>SOFTWARE</c>, <c>SDL_RENDERER</c>,
    /// <c>OPENGLES3</c>, …). Useful in a bug report; not something to branch on -- use
    /// <see cref="SupportsCapability"/> for that, since a name does not tell you what a renderer
    /// can do.</summary>
    public unsafe string RendererName =>
        NativeStringReader.Read(
            static (CnaHandle device, out ulong bytes) =>
                Native.cna_graphics_device_get_renderer_name_size(device, out bytes),
            static (CnaHandle device, byte* destination, ulong capacity, out ulong bytes) =>
                Native.cna_graphics_device_copy_renderer_name(device, destination, capacity, out bytes),
            ResolveNativeDeviceHandle(),
            nameof(RendererName));

    /// <summary>
    /// The dialect a source-based <see cref="Effect"/>'s text must be written in.
    ///
    /// Not an XNA member; XNA had one shader language. Ask before supplying source rather than
    /// inferring from <see cref="RendererName"/> -- the header is explicit that the renderer
    /// identity is not a safe way to infer this, and the inference fails precisely in a build
    /// carrying more than one backend.
    ///
    /// <see cref="ShaderDialect.Unknown"/> means the renderer declared none, and is an answer
    /// rather than a failure: supplying text for a dialect nobody named is how a shader compiles on
    /// one machine and not another.
    ///
    /// This was briefly reported here as a route the headers declared and no library exported.
    /// That was wrong, and the error is worth keeping: the name I bound,
    /// <c>cna_graphics_device_get_shading_dialect</c>, appears in no header. It was taken from the
    /// prose above the real declaration rather than from the declaration, so the library was right
    /// to lack it. The real route is <c>cna_graphics_device_get_shader_dialect_ext</c> and has
    /// worked all along.
    /// </summary>
    public ShaderDialect ShadingDialect
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_shader_dialect_ext(
                ResolveNativeDeviceHandle(), out uint dialect);
            CnaException.ThrowIfFailed(result, nameof(ShadingDialect));
            return (ShaderDialect)dialect;
        }
    }

    public Viewport Viewport
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_viewport(ResolveNativeDeviceHandle(), out CnaViewport native);
            CnaException.ThrowIfFailed(result, nameof(Viewport));
            return Viewport.FromNative(native);
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_set_viewport(ResolveNativeDeviceHandle(), value.ToNative());
            CnaException.ThrowIfFailed(result, nameof(Viewport));
        }
    }

    /// <summary>The adapter this device renders with. Real XNA also has a static
    /// <c>GraphicsAdapter.Adapters</c>; see <see cref="Graphics.GraphicsAdapter"/>'s own doc
    /// comment for why the enumeration entry points here take a device instead.</summary>
    public GraphicsAdapter Adapter => CreateAdapter(GetAdapterIndex());

    /// <summary>The index of the adapter *this device* renders with.
    /// <c>cna_graphics_device_get_adapter_index</c> is the authoritative answer -- deliberately
    /// not a scan for the <c>is_default_adapter</c> flag, which answers "the machine's default
    /// adapter", a different question this device need not agree with.
    ///
    /// Separate from <see cref="CreateAdapter"/> so the override below receives the index as a
    /// parameter rather than having to look it up: an override written as
    /// <c>new GraphicsAdapter(this, base.Adapter.AdapterIndex)</c> would recurse forever, since
    /// <see cref="Adapter"/> dispatches straight back into the override.</summary>
    protected uint GetAdapterIndex()
    {
        CnaResult result = Native.cna_graphics_device_get_adapter_index(ResolveNativeDeviceHandle(), out uint adapterIndex);
        CnaException.ThrowIfFailed(result, nameof(Adapter));
        return adapterIndex;
    }

    /// <summary>Covariant-return factory hook, same pattern as <see cref="QueryBlendState"/>.</summary>
    protected virtual GraphicsAdapter CreateAdapter(uint adapterIndex) => new(this, adapterIndex);

    /// <summary>The device's current presentation configuration. The setter applies it through
    /// <c>cna_graphics_device_set_presentation_parameters</c>; mutating the object a getter
    /// returned does nothing until it is assigned back, matching real XNA.</summary>
    public PresentationParameters PresentationParameters
    {
        get
        {
            var native = new CnaPresentationParameters();
            CnaResult result = Native.cna_graphics_device_get_presentation_parameters(ResolveNativeDeviceHandle(), ref native);
            CnaException.ThrowIfFailed(result, nameof(PresentationParameters));
            return WrapPresentationParameters(PresentationParameters.FromNative(native));
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CnaResult result = Native.cna_graphics_device_set_presentation_parameters(ResolveNativeDeviceHandle(), value.ToNative());
            CnaException.ThrowIfFailed(result, nameof(PresentationParameters));
        }
    }

    /// <summary>Re-typing hook for CNA.XnaCompat -- takes and returns the public type rather than
    /// the interop struct, for the same CS0050 reason
    /// <see cref="SamplerStateCollection.Wrap"/> documents.</summary>
    protected virtual PresentationParameters WrapPresentationParameters(PresentationParameters parameters) => parameters;

    /// <summary>Whether this device has been disposed.</summary>
    public bool IsDisposed
    {
        get
        {
            if (_retiredByManager)
            {
                return true;
            }

            CnaResult result = Native.cna_graphics_device_get_is_disposed(ResolveNativeDeviceHandle(), out byte disposed);
            CnaException.ThrowIfFailed(result, nameof(IsDisposed));
            return disposed != 0;
        }
    }

    /// <summary>Whether the device is usable, lost, or lost and not yet reset. What an XNA game
    /// checks before drawing, and the reason <see cref="DeviceLostException"/> exists.</summary>
    public GraphicsDeviceStatus GraphicsDeviceStatus
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_status(ResolveNativeDeviceHandle(), out uint status);
            CnaException.ThrowIfFailed(result, nameof(GraphicsDeviceStatus));
            return (GraphicsDeviceStatus)status;
        }
    }

    /// <summary>The scissor test rectangle. Only applied while the active
    /// <see cref="RasterizerState"/> enables scissor testing.</summary>
    public Rectangle ScissorRectangle
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_scissor_rectangle(ResolveNativeDeviceHandle(), out CnaRect rect);
            CnaException.ThrowIfFailed(result, nameof(ScissorRectangle));
            return new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_set_scissor_rectangle(
                ResolveNativeDeviceHandle(), new CnaRect(value.X, value.Y, value.Width, value.Height));
            CnaException.ThrowIfFailed(result, nameof(ScissorRectangle));
        }
    }

    /// <summary>The constant colour a <see cref="Blend.BlendFactor"/> blend multiplies by.</summary>
    public Color BlendFactor
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_blend_factor(ResolveNativeDeviceHandle(), out CnaColor color);
            CnaException.ThrowIfFailed(result, nameof(BlendFactor));
            return Color.FromNative(color);
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_set_blend_factor(ResolveNativeDeviceHandle(), value.ToNative());
            CnaException.ThrowIfFailed(result, nameof(BlendFactor));
        }
    }

    /// <summary>Which multisample samples are written. All bits set by default.</summary>
    public int MultiSampleMask
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_multi_sample_mask(ResolveNativeDeviceHandle(), out int mask);
            CnaException.ThrowIfFailed(result, nameof(MultiSampleMask));
            return mask;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_set_multi_sample_mask(ResolveNativeDeviceHandle(), value);
            CnaException.ThrowIfFailed(result, nameof(MultiSampleMask));
        }
    }

    /// <summary>The value the stencil test compares against.</summary>
    public int ReferenceStencil
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_reference_stencil(ResolveNativeDeviceHandle(), out int stencil);
            CnaException.ThrowIfFailed(result, nameof(ReferenceStencil));
            return stencil;
        }
        set
        {
            CnaResult result = Native.cna_graphics_device_set_reference_stencil(ResolveNativeDeviceHandle(), value);
            CnaException.ThrowIfFailed(result, nameof(ReferenceStencil));
        }
    }

    /// <summary>Presents the back buffer. A game running the CNA loop never needs this -- the loop
    /// presents -- but a host driving <see cref="Game.Tick"/> itself does.</summary>
    public void Present()
    {
        CnaResult result = Native.cna_graphics_device_present(ResolveNativeDeviceHandle());
        CnaException.ThrowIfFailed(result, nameof(Present));
    }

    /// <summary>Recreates the device with its current presentation parameters.</summary>
    public void Reset()
    {
        CnaResult result = Native.cna_graphics_device_reset(ResolveNativeDeviceHandle());
        CnaException.ThrowIfFailed(result, nameof(Reset));
    }

    /// <summary>Recreates the device with new presentation parameters, keeping the current
    /// adapter.</summary>
    public unsafe void Reset(PresentationParameters presentationParameters)
    {
        ArgumentNullException.ThrowIfNull(presentationParameters);

        CnaPresentationParameters native = presentationParameters.ToNative();

        // A null adapter index is how the ABI spells "keep the current adapter", which is what real
        // XNA's parameters-only overload means.
        CnaResult result = Native.cna_graphics_device_reset_with_parameters(
            ResolveNativeDeviceHandle(), in native, null);
        CnaException.ThrowIfFailed(result, nameof(Reset));
    }

    /// <summary>Recreates the device on a specific adapter.</summary>
    public unsafe void Reset(PresentationParameters presentationParameters, GraphicsAdapter graphicsAdapter)
    {
        ArgumentNullException.ThrowIfNull(presentationParameters);
        ArgumentNullException.ThrowIfNull(graphicsAdapter);

        CnaPresentationParameters native = presentationParameters.ToNative();
        uint adapterIndex = graphicsAdapter.AdapterIndex;
        CnaResult result = Native.cna_graphics_device_reset_with_parameters(
            ResolveNativeDeviceHandle(), in native, &adapterIndex);
        GC.KeepAlive(graphicsAdapter);
        CnaException.ThrowIfFailed(result, nameof(Reset));
    }

    /// <summary>Matches real XNA's <c>DrawInstancedPrimitives</c>: draws the bound indexed geometry
    /// <paramref name="instanceCount"/> times, with per-instance data supplied by a second vertex
    /// buffer bound at an instance frequency.</summary>
    public void DrawInstancedPrimitives(
        PrimitiveType primitiveType,
        int baseVertex,
        int minVertexIndex,
        int numVertices,
        int startIndex,
        int primitiveCount,
        int instanceCount)
    {
        CnaResult result = Native.cna_graphics_device_draw_instanced_primitives(
            ResolveNativeDeviceHandle(), (uint)primitiveType, baseVertex, minVertexIndex,
            numVertices, startIndex, primitiveCount, instanceCount);
        CnaException.ThrowIfFailed(result, nameof(DrawInstancedPrimitives));
    }

    /// <summary>Raised as the device is disposed. See
    /// <see cref="GraphicsDeviceManager.DeviceCreated"/> for why the native subscription is taken on
    /// the first <c>+=</c> and held until disposal.
    ///
    /// These four are the *device's* own events, released with
    /// <c>cna_graphics_device_unsubscribe</c> -- a different registration family from the manager's
    /// identically-named ones, which use <c>cna_game_unsubscribe</c>. Both exist, and a game may
    /// subscribe to either.</summary>
    public event EventHandler<EventArgs>? Disposing
    {
        add { EnsureDeviceSubscribed(CnaGraphicsDeviceEvent.Disposing); _disposingEvent += value; }
        remove => _disposingEvent -= value;
    }

    public event EventHandler<EventArgs>? DeviceLost
    {
        add { EnsureDeviceSubscribed(CnaGraphicsDeviceEvent.DeviceLost); _deviceLost += value; }
        remove => _deviceLost -= value;
    }

    public event EventHandler<EventArgs>? DeviceReset
    {
        add { EnsureDeviceSubscribed(CnaGraphicsDeviceEvent.DeviceReset); _deviceReset += value; }
        remove => _deviceReset -= value;
    }

    public event EventHandler<EventArgs>? DeviceResetting
    {
        add { EnsureDeviceSubscribed(CnaGraphicsDeviceEvent.DeviceResetting); _deviceResetting += value; }
        remove => _deviceResetting -= value;
    }

    private EventHandler<EventArgs>? _disposingEvent;
    private EventHandler<EventArgs>? _deviceLost;
    private EventHandler<EventArgs>? _deviceReset;
    private EventHandler<EventArgs>? _deviceResetting;

    private readonly NativeEventBridge?[] _deviceEventBridges =
        new NativeEventBridge?[(int)CnaGraphicsDeviceEvent.DeviceResetting + 1];

    private void EnsureDeviceSubscribed(CnaGraphicsDeviceEvent which)
    {
        int index = (int)which;
        if (_deviceEventBridges[index] is not null)
        {
            return;
        }

        // CNA_GraphicsDeviceEventCallback is (graphics_device, context), not the one-argument
        // callback used by audio and manager events. Using Subscribe here made native's device
        // handle look like a GCHandle during Reset and could access-violate the process.
        _deviceEventBridges[index] = NativeEventBridge.SubscribeWithSender(
            () => RaiseDeviceEvent(which),
            (callback, context) =>
            {
                CnaResult result = Native.cna_graphics_device_subscribe_event(
                    ResolveNativeDeviceHandle(), (uint)which, callback, context, out CnaHandle registration);
                CnaException.ThrowIfFailed(result, nameof(EnsureDeviceSubscribed));
                return registration;
            },
            registration => Native.cna_graphics_device_unsubscribe(registration));
    }

    /// <summary>
    /// Copies the back buffer's pixels. Matches real XNA's <c>GetBackBufferData</c>.
    ///
    /// The ABI reads into a <c>CNA_Color*</c>, so the readback is RGBA8 and four bytes wide per
    /// pixel. That does not make the element type <see cref="Color"/>: XNA's signature is generic
    /// and its transfer is an untyped byte copy, so <c>GetBackBufferData&lt;uint&gt;</c> and
    /// <c>&lt;byte&gt;</c> are ordinary, and this used to refuse both. Any element type whose size
    /// divides four now reads the same four bytes per pixel; see <see cref="TextureTransferPlan"/>.
    /// </summary>
    /// <remarks>
    /// The constraint is <c>struct</c> rather than <c>unmanaged</c> because the strict facade's
    /// signature must match XNA's exactly, and <c>unmanaged</c> emits a modreq XNA's metadata does
    /// not have -- the api-compat gate reports it as a generic-constraint mismatch. The element
    /// type is therefore checked at run time and the array is pinned through a handle, which is the
    /// same shape the texture transfer helpers already use.
    /// </remarks>
    public void GetBackBufferData<T>(T[] data) where T : struct
        => GetBackBufferData(null, data, 0, data?.Length ?? 0);

    /// <summary>See <see cref="GetBackBufferData{T}(T[])"/>.</summary>
    public void GetBackBufferData<T>(T[] data, int startIndex, int elementCount)
        where T : struct
        => GetBackBufferData(null, data, startIndex, elementCount);

    /// <summary>See <see cref="GetBackBufferData{T}(T[])"/>.</summary>
    public unsafe void GetBackBufferData<T>(Rectangle? rect, T[] data, int startIndex, int elementCount)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(elementCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(elementCount, data.Length - startIndex);

        if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new ArgumentException(
                $"Back-buffer element type {typeof(T)} contains managed references.", nameof(data));
        }

        TextureTransferPlan plan = TextureTransferPlan.Rgba8(
            System.Runtime.CompilerServices.Unsafe.SizeOf<T>(), startIndex, elementCount, data.Length);

        var readback = new CnaBackBufferReadback
        {
            HasSourceRectangle = rect is null ? (byte)0 : (byte)1,
            SourceRectangle = rect is { } r ? new CnaRect(r.X, r.Y, r.Width, r.Height) : default,
            StartIndex = plan.StartIndex,
            ElementCount = plan.ElementCount,
        };

        System.Runtime.InteropServices.GCHandle pinned =
            System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            CnaResult result = Native.cna_graphics_device_get_backbuffer_data_window(
                ResolveNativeDeviceHandle(), in readback,
                (CnaColor*)pinned.AddrOfPinnedObject(), plan.Capacity);
            CnaException.ThrowIfFailed(result, nameof(GetBackBufferData));
        }
        finally
        {
            pinned.Free();
        }
    }

    /// <summary>How many vertex buffers are currently bound. Real XNA's <c>GetVertexBuffers</c>
    /// returns the bindings themselves; native answers only a count and bare handles, which cannot
    /// be mapped back to their managed wrappers -- the limitation
    /// <see cref="TextureCollection"/> documents. The count is the part that is honestly
    /// answerable.</summary>
    public int VertexBufferCount
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_vertex_buffer_count(
                ResolveNativeDeviceHandle(), out ulong count);
            CnaException.ThrowIfFailed(result, nameof(VertexBufferCount));
            return (int)count;
        }
    }

    /// <summary>
    /// Matches real XNA's <c>GetVertexBuffers</c>: the bindings currently in effect.
    ///
    /// <b>This threw until the render-target precedent was noticed.</b> The message said the
    /// question "cannot be answered" because <c>cna_graphics_device_copy_vertex_buffers</c> reports
    /// bare handles that cannot be mapped back to their managed <see cref="VertexBuffer"/>. All of
    /// that is true and none of it makes the question unanswerable -- <see cref="GetRenderTargets"/>
    /// has the identical limitation and solves it the same way, twenty lines up in this file:
    /// answer from what this object bound, and cross-check the count against native so a rebind
    /// from elsewhere is reported rather than papered over.
    ///
    /// The cross-check is what makes it honest rather than a guess. If something bound vertex
    /// buffers outside this object -- canonical CNA code, which the header notes reports
    /// <c>CNA_INVALID_HANDLE</c> for exactly that reason -- the counts disagree and this throws
    /// instead of returning a stale array.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the device reports a different number of
    /// bound buffers than this object last set.</exception>
    public VertexBufferBinding[] GetVertexBuffers()
    {
        CnaResult result = Native.cna_graphics_device_get_vertex_buffer_count(
            ResolveNativeDeviceHandle(), out ulong count);
        CnaException.ThrowIfFailed(result, nameof(GetVertexBuffers));

        if (count == 0 || _boundVertexBuffers.Length == 0)
        {
            return [];
        }

        if ((ulong)_boundVertexBuffers.Length != count)
        {
            throw new InvalidOperationException(
                $"The device reports {count} bound vertex buffer(s) but this object last bound " +
                $"{_boundVertexBuffers.Length}. Something rebound them outside SetVertexBuffer(s).");
        }

        return (VertexBufferBinding[])_boundVertexBuffers.Clone();
    }

    /// <summary>Raised when a graphics resource is created on this device. Native subscription is
    /// deliberately disabled: ABI 0.6 invokes a three-argument payload callback, while the old
    /// managed bridge was two-argument and could interpret the payload address as a
    /// <see cref="System.Runtime.InteropServices.GCHandle"/>. More importantly, the ABI reports
    /// presence only and cannot supply XNA's actual managed resource object. Handlers are retained
    /// safely until the ABI can represent the contract.</summary>
    public event EventHandler<ResourceCreatedEventArgs>? ResourceCreated
    {
        add => _resourceCreated += value;
        remove => _resourceCreated -= value;
    }

    /// <summary>Raised when a graphics resource is destroyed. See
    /// <see cref="ResourceCreated"/>.</summary>
    public event EventHandler<ResourceDestroyedEventArgs>? ResourceDestroyed
    {
        add => _resourceDestroyed += value;
        remove => _resourceDestroyed -= value;
    }

    private EventHandler<ResourceCreatedEventArgs>? _resourceCreated;
    private EventHandler<ResourceDestroyedEventArgs>? _resourceDestroyed;

    private void RaiseDeviceEvent(CnaGraphicsDeviceEvent which)
    {
        EventHandler<EventArgs>? handler = which switch
        {
            CnaGraphicsDeviceEvent.Disposing => _disposingEvent,
            CnaGraphicsDeviceEvent.DeviceLost => _deviceLost,
            CnaGraphicsDeviceEvent.DeviceReset => _deviceReset,
            CnaGraphicsDeviceEvent.DeviceResetting => _deviceResetting,
            _ => null,
        };

        handler?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The display mode this device is currently presenting in.</summary>
    public DisplayMode DisplayMode
    {
        get
        {
            var native = new CnaDisplayMode();
            CnaResult result = Native.cna_graphics_device_get_display_mode(ResolveNativeDeviceHandle(), ref native);
            CnaException.ThrowIfFailed(result, nameof(DisplayMode));
            return Graphics.DisplayMode.FromNative(in native);
        }
    }

    public GraphicsProfile GraphicsProfile
    {
        get
        {
            CnaResult result = Native.cna_graphics_device_get_graphics_profile(ResolveNativeDeviceHandle(), out CnaGraphicsProfile native);
            CnaException.ThrowIfFailed(result, nameof(GraphicsProfile));
            return (GraphicsProfile)native;
        }
    }

    /// <summary>
    /// Matches real XNA's <c>DrawUserPrimitives&lt;T&gt;</c>, for any
    /// <see cref="IVertexType"/>-implementing <typeparamref name="T"/>.
    ///
    /// The four types <c>CNA_UserVertexSource</c> names by identity take that route, which needs no
    /// declaration at all. Anything else goes through <c>CNA_USER_VERTEX_SOURCE_RAW_STREAM</c> with
    /// a declaration built from the type -- which is what the header intends, and what
    /// <see cref="VertexBuffer"/> has always done for its own creation path.
    ///
    /// It used to throw <see cref="NotSupportedException"/> for every other <typeparamref name="T"/>,
    /// on the recorded grounds that the raw route "would need a native vertex-declaration resource
    /// this project doesn't have". A header audit found otherwise:
    /// <c>cna_vertex_declaration_create_with_stride</c> was already bound and already in use one
    /// file over.
    /// </summary>
    public unsafe void DrawUserPrimitives<T>(PrimitiveType primitiveType, T[] vertexData, int vertexOffset, int primitiveCount)
        where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(vertexData);

        UserVertexSource? typedSource = TypedVertexSourceFor<T>();

        fixed (T* vertexDataPtr = vertexData)
        {
            if (typedSource is UserVertexSource source)
            {
                DrawUserPrimitivesRaw(primitiveType, vertexDataPtr, source, vertexOffset, primitiveCount);
                return;
            }

            DrawUserPrimitivesRaw(
                primitiveType, vertexDataPtr, UserVertexSource.RawStream, vertexOffset, primitiveCount,
                DeclarationFor<T>());
        }
    }

    /// <summary>Derives the declaration for a vertex type that has no
    /// <c>CNA_UserVertexSource</c> identity of its own. Rejects a non-<see cref="IVertexType"/>
    /// <typeparamref name="T"/> here rather than letting
    /// <see cref="VertexDeclaration.FromType"/>'s reflection failure surface as something less
    /// obvious.</summary>
    private static VertexDeclaration DeclarationFor<T>() where T : unmanaged
    {
        if (!typeof(IVertexType).IsAssignableFrom(typeof(T)))
        {
            throw new NotSupportedException(
                $"DrawUserPrimitives<{typeof(T).Name}> needs {typeof(T).Name} to implement IVertexType, so its " +
                "vertex declaration can be derived. Only VertexPositionColor, VertexPositionColorTexture, " +
                "VertexPositionTexture and VertexPositionNormalTexture are drawable without one.");
        }

        return VertexDeclaration.FromType(typeof(T));
    }

    /// <summary><see langword="null"/> when <typeparamref name="T"/> is not one of the four types
    /// the ABI names directly -- which is a fall-through to the raw-stream route, not a
    /// failure.</summary>
    private static UserVertexSource? TypedVertexSourceFor<T>() where T : unmanaged
    {
        if (typeof(T) == typeof(VertexPositionColor))
        {
            return UserVertexSource.PositionColor;
        }

        if (typeof(T) == typeof(VertexPositionColorTexture))
        {
            return UserVertexSource.PositionColorTexture;
        }

        if (typeof(T) == typeof(VertexPositionTexture))
        {
            return UserVertexSource.PositionTexture;
        }

        if (typeof(T) == typeof(VertexPositionNormalTexture))
        {
            return UserVertexSource.PositionNormalTexture;
        }

        return null;
    }

    /// <summary>Matches real XNA's <c>DrawUserIndexedPrimitives&lt;T&gt;</c>: the same
    /// caller-supplied vertex array as <see cref="DrawUserPrimitives{T}"/>, plus a caller-supplied
    /// index array. <typeparamref name="TIndex"/> must be a 16- or 32-bit integer, which is the
    /// whole of what an index element can be (see <see cref="IndexElementSize"/>).</summary>
    public unsafe void DrawUserIndexedPrimitives<TVertex, TIndex>(
        PrimitiveType primitiveType,
        TVertex[] vertexData,
        int vertexOffset,
        int numVertices,
        TIndex[] indexData,
        int indexOffset,
        int primitiveCount)
        where TVertex : unmanaged
        where TIndex : unmanaged
    {
        ArgumentNullException.ThrowIfNull(vertexData);
        ArgumentNullException.ThrowIfNull(indexData);

        UserVertexSource? typedSource = TypedVertexSourceFor<TVertex>();
        IndexElementSize indexElementSize = IndexBuffer.SizeForType(typeof(TIndex));

        fixed (TVertex* vertexDataPtr = vertexData)
        fixed (TIndex* indexDataPtr = indexData)
        {
            DrawUserIndexedPrimitivesRaw(
                primitiveType,
                vertexDataPtr,
                typedSource ?? UserVertexSource.RawStream,
                vertexOffset,
                numVertices,
                indexDataPtr,
                indexElementSize,
                indexOffset,
                primitiveCount,
                typedSource is null ? DeclarationFor<TVertex>() : null);
        }
    }

    /// <summary>The pointer-and-identity level <see cref="DrawUserIndexedPrimitives{TVertex,TIndex}"/>
    /// builds on, <c>protected</c> for the same reason
    /// <see cref="DrawUserPrimitivesRaw"/> is -- CNA.XnaCompat's vertex structs are separate types
    /// and must reach the same native call.</summary>
    internal unsafe void DrawUserIndexedPrimitivesRaw(
        PrimitiveType primitiveType,
        void* vertexData,
        UserVertexSource vertexSource,
        int vertexOffset,
        int numVertices,
        void* indexData,
        IndexElementSize indexElementSize,
        int indexOffset,
        int primitiveCount,
        VertexDeclaration? vertexDeclaration = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(numVertices);
        ArgumentOutOfRangeException.ThrowIfNegative(indexOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primitiveCount);

        CnaHandle declaration = vertexDeclaration?.CreateNativeHandle() ?? CnaHandle.Zero;
        try
        {
            var primitives = new CnaUserPrimitives
            {
                PrimitiveType = (uint)primitiveType,
                VertexSource = (CnaUserVertexSource)vertexSource,
                VertexData = vertexData,
                VertexDeclaration = declaration,
                VertexOffset = vertexOffset,

                // Meaningful only on this route -- the non-indexed draw ignores it, per the header.
                NumVertices = numVertices,
                PrimitiveCount = primitiveCount,
            };

            var indices = new CnaUserIndices
            {
                IndexElementSize = (uint)indexElementSize,
                IndexOffset = indexOffset,
                IndexData = indexData,
            };

            CnaResult result = Native.cna_graphics_device_draw_user_indexed_primitives(
                ResolveNativeDeviceHandle(), in primitives, in indices);
            CnaException.ThrowIfFailed(result, nameof(DrawUserIndexedPrimitives));
        }
        finally
        {
            // The draw reads the declaration during the call and keeps nothing, so it is released
            // immediately -- in a finally, because a failed draw must not leak it.
            if (declaration.Value != 0)
            {
                Native.cna_vertex_declaration_destroy(declaration);
            }
        }
    }

    /// <summary>The pointer-and-identity level <see cref="DrawUserPrimitives{T}"/> builds on --
    /// <c>protected</c>, not <c>private</c>, so CNA.XnaCompat's <c>GraphicsDevice</c> override can
    /// reach it for its own, separately-typed compat vertex structs (structs can't share a type
    /// across the CNA/XnaCompat boundary the way <see cref="RenderTarget2D"/>-style reference types
    /// do -- see <see cref="Graphics.UserVertexSource"/>'s own doc comment) without ever naming a
    /// <c>CNA.Interop</c> type.
    ///
    /// <paramref name="vertexDeclaration"/> is optional and only meaningful for
    /// <see cref="UserVertexSource.RawStream"/>: the header states that a raw stream without one
    /// uses the implicit <c>VertexPositionColor</c> layout, and that a typed source without one
    /// uses its own type's declaration.</summary>
    internal unsafe void DrawUserPrimitivesRaw(
        PrimitiveType primitiveType,
        void* vertexData,
        UserVertexSource vertexSource,
        int vertexOffset,
        int primitiveCount,
        VertexDeclaration? vertexDeclaration = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(vertexOffset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primitiveCount);

        CnaHandle declaration = vertexDeclaration?.CreateNativeHandle() ?? CnaHandle.Zero;
        try
        {
            var primitives = new CnaUserPrimitives
            {
                PrimitiveType = (uint)primitiveType,
                VertexSource = (CnaUserVertexSource)vertexSource,
                VertexData = vertexData,
                VertexDeclaration = declaration,
                VertexOffset = vertexOffset,
                PrimitiveCount = primitiveCount,
            };

            CnaResult result = Native.cna_graphics_device_draw_user_primitives(ResolveNativeDeviceHandle(), in primitives);
            CnaException.ThrowIfFailed(result, nameof(DrawUserPrimitives));
        }
        finally
        {
            if (declaration.Value != 0)
            {
                Native.cna_vertex_declaration_destroy(declaration);
            }
        }
    }

    /// <summary>
    /// Sets the active render target, or restores the back buffer when <paramref name="renderTarget"/>
    /// is <c>null</c>. Takes <see cref="Texture2D"/> rather than the stricter
    /// <see cref="RenderTarget2D"/> -- a deliberate, documented looseness (real XNA's signature
    /// is <c>SetRenderTarget(RenderTarget2D)</c>) that lets CNA.XnaCompat's <c>RenderTarget2D</c>
    /// (which inherits from CNA.XnaCompat's own <c>Texture2D</c>, not this project's
    /// <see cref="RenderTarget2D"/> -- see that type's doc comment) upcast straight into this
    /// parameter with no override needed, matching every other XnaCompat <c>Draw</c>/<c>Clear</c>
    /// overload's "inherited unchanged, converts through implicit operators" pattern. Whoever calls
    /// this is expected to pass an actual <see cref="RenderTarget2D"/> (or a compat subclass of
    /// one) -- passing a plain <see cref="Texture2D"/> is a caller error the real native call now
    /// catches on its own (it validates the handle really is a render target), unlike before this
    /// migration when nothing could.
    /// </summary>
    public void SetRenderTarget(Texture2D? renderTarget)
    {
        CnaHandle handle = renderTarget is null ? CnaHandle.Zero : new CnaHandle(renderTarget.NativeHandleValue);
        CnaResult result = Native.cna_graphics_device_set_render_target2d(ResolveNativeDeviceHandle(), handle);
        GC.KeepAlive(renderTarget);
        CnaException.ThrowIfFailed(result, nameof(SetRenderTarget));

        // Load-bearing, not bookkeeping: GetRenderTargets cross-checks its cached array against
        // native's own count, so a single-target bind that left a stale multi-target array behind
        // would make the next GetRenderTargets throw on a perfectly legitimate sequence.
        _boundRenderTargets = renderTarget is null ? [] : [new RenderTargetBinding(renderTarget)];
    }

    /// <summary>Matches real XNA's <c>SetRenderTarget(RenderTargetCube, CubeMapFace)</c>. A
    /// separate method from <see cref="SetRenderTarget(Texture2D?)"/> because the real ABI's
    /// render-target binding is shape-specific: <c>cna_graphics_device_set_render_target_cube</c>
    /// takes the face to render into, which has no 2D equivalent. Passing <see langword="null"/>
    /// restores the back buffer, same sentinel as the 2D overload.</summary>
    public void SetRenderTarget(RenderTargetCube? renderTarget, CubeMapFace cubeMapFace)
    {
        CnaHandle handle = renderTarget is null ? CnaHandle.Zero : new CnaHandle(renderTarget.NativeHandleValue);
        CnaResult result = Native.cna_graphics_device_set_render_target_cube(
            ResolveNativeDeviceHandle(), handle, (uint)cubeMapFace);
        GC.KeepAlive(renderTarget);
        CnaException.ThrowIfFailed(result, nameof(SetRenderTarget));

        // See the 2D overload for why this is not optional.
        _boundRenderTargets = renderTarget is null ? [] : [new RenderTargetBinding(renderTarget, cubeMapFace)];
    }

    /// <summary>
    /// Matches real XNA's <c>SetRenderTargets</c>: binds a whole multiple-render-target array, or
    /// restores the backbuffer when given none.
    ///
    /// Added in the WP16 re-audit along with <see cref="RenderTargetBinding"/> itself -- only the
    /// single-target overloads had been bound, while
    /// <c>cna_graphics_device_set_render_targets</c> (<c>render_target.h:238</c>) had been there
    /// all along.
    /// </summary>
    public unsafe void SetRenderTargets(params RenderTargetBinding[]? renderTargets)
    {
        if (renderTargets is null || renderTargets.Length == 0)
        {
            CnaResult empty = Native.cna_graphics_device_set_render_targets(ResolveNativeDeviceHandle(), null, 0);
            CnaException.ThrowIfFailed(empty, nameof(SetRenderTargets));
            _boundRenderTargets = [];
            return;
        }

        var bindings = new CnaRenderTargetBinding[renderTargets.Length];
        for (int i = 0; i < bindings.Length; i++)
        {
            RenderTargetBinding binding = renderTargets[i];
            if (binding.RenderTarget is null)
            {
                throw new ArgumentException(
                    $"{nameof(renderTargets)}[{i}] is a default-constructed RenderTargetBinding with no target.",
                    nameof(renderTargets));
            }

            bindings[i] = new CnaRenderTargetBinding
            {
                RenderTarget = new CnaHandle(binding.RenderTarget.NativeHandleValue),
                ArraySlice = binding.ArraySlice,
                CubeMapFace = (uint)binding.CubeMapFace,
            };
        }

        fixed (CnaRenderTargetBinding* bindingsPtr = bindings)
        {
            CnaResult result = Native.cna_graphics_device_set_render_targets(
                ResolveNativeDeviceHandle(), bindingsPtr, (ulong)bindings.Length);

            // Keeps every bound target reachable across the call -- their handles were read into
            // the array above, and an unreachable SafeHandle could have been finalized mid-call.
            GC.KeepAlive(renderTargets);
            CnaException.ThrowIfFailed(result, nameof(SetRenderTargets));
        }

        // Recorded only after the bind succeeded, so a failed call leaves the previous answer
        // standing rather than claiming targets that were never bound. Also what keeps the targets
        // reachable for as long as they are bound -- see GetRenderTargets.
        _boundRenderTargets = (RenderTargetBinding[])renderTargets.Clone();
    }

    /// <summary>
    /// The targets last bound through <see cref="SetRenderTargets"/>, or an empty array while the
    /// backbuffer is bound. Matches real XNA's <c>GetRenderTargets</c>.
    ///
    /// Answers from the managed references it was handed, not from
    /// <c>cna_graphics_device_copy_render_targets</c>, for the reason
    /// <see cref="TextureCollection"/> documents at length: native reports bare handles, and this
    /// project has no way to map a handle back to the managed
    /// <see cref="RenderTarget2D"/>/<see cref="RenderTargetCube"/> wrapper that owns it. Re-reading
    /// could therefore not return the objects the caller set. The count is still checked against
    /// native, so a binding dropped underneath this device is reported rather than hidden.
    /// </summary>
    public RenderTargetBinding[] GetRenderTargets()
    {
        CnaResult result = Native.cna_graphics_device_get_render_target_count(
            ResolveNativeDeviceHandle(), out ulong count);
        CnaException.ThrowIfFailed(result, nameof(GetRenderTargets));

        if (count == 0 || _boundRenderTargets.Length == 0)
        {
            return [];
        }

        if ((ulong)_boundRenderTargets.Length != count)
        {
            throw new InvalidOperationException(
                $"The device reports {count} bound render target(s) but this object last bound " +
                $"{_boundRenderTargets.Length}. Something rebound them outside SetRenderTargets.");
        }

        return (RenderTargetBinding[])_boundRenderTargets.Clone();
    }

    public void SetVertexBuffer(VertexBuffer? vertexBuffer)
    {
        CnaHandle handle = vertexBuffer is null ? CnaHandle.Zero : new CnaHandle(vertexBuffer.NativeHandleValue);
        CnaResult result = Native.cna_graphics_device_set_vertex_buffer(ResolveNativeDeviceHandle(), handle);
        GC.KeepAlive(vertexBuffer);
        CnaException.ThrowIfFailed(result, nameof(SetVertexBuffer));

        // Load-bearing, not bookkeeping -- see GetVertexBuffers, which cross-checks this against
        // native's count. Recorded after the call so a failed bind does not rewrite the record.
        _boundVertexBuffers = vertexBuffer is null ? [] : [new VertexBufferBinding(vertexBuffer)];
    }

    /// <summary>Matches real XNA's <c>SetVertexBuffers(params VertexBufferBinding[])</c> -- the
    /// multi-stream form <see cref="SetVertexBuffer"/> is shorthand for. Passing no bindings (or
    /// <see langword="null"/>) unbinds every stream, which is what the native call documents an
    /// empty array as doing.</summary>
    public unsafe void SetVertexBuffers(params VertexBufferBinding[]? vertexBuffers)
    {
        if (vertexBuffers is null || vertexBuffers.Length == 0)
        {
            CnaResult emptyResult = Native.cna_graphics_device_set_vertex_buffers(ResolveNativeDeviceHandle(), null, 0);
            CnaException.ThrowIfFailed(emptyResult, nameof(SetVertexBuffers));
            _boundVertexBuffers = [];
            return;
        }

        var native = new CnaVertexBufferBinding[vertexBuffers.Length];
        for (int i = 0; i < native.Length; i++)
        {
            native[i] = vertexBuffers[i].ToNative();
        }

        fixed (CnaVertexBufferBinding* nativePtr = native)
        {
            CnaResult result = Native.cna_graphics_device_set_vertex_buffers(
                ResolveNativeDeviceHandle(), nativePtr, (ulong)native.Length);

            // The native array holds bare handles; vertexBuffers is what keeps the VertexBuffer
            // objects (and so their SafeHandles) reachable across the call.
            GC.KeepAlive(vertexBuffers);
            CnaException.ThrowIfFailed(result, nameof(SetVertexBuffers));
            _boundVertexBuffers = (VertexBufferBinding[])vertexBuffers.Clone();
        }
    }

    private BlendState? _blendState;

    /// <summary>Lazily queries the device's current state on first read (via
    /// <c>cna_graphics_device_get_blend_state</c>) rather than defaulting to
    /// <see cref="Graphics.BlendState.Opaque"/> locally -- the real ABI is the source of truth for
    /// what a freshly created device actually starts with, not an assumption made here. The query
    /// itself is split into <see cref="QueryBlendState"/>, a <see langword="protected virtual"/>
    /// hook -- CNA.XnaCompat's <c>GraphicsDevice</c> overrides it to return a compat-typed
    /// <c>BlendState</c> instead, so <c>this.GraphicsDevice.BlendState</c> never throws
    /// <see cref="InvalidCastException"/> on a first read that happens before any explicit
    /// <see langword="set"/> -- the same class of bug <see cref="Indices"/>'s own doc comment
    /// describes, but for a property whose base default is a real, non-null constructed value
    /// rather than <see langword="null"/>.</summary>
    public BlendState BlendState
    {
        get => _blendState ??= QueryBlendState();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CnaResult result = Native.cna_graphics_device_set_blend_state(ResolveNativeDeviceHandle(), value.ToNative());
            CnaException.ThrowIfFailed(result, nameof(BlendState));
            _blendState = value;
        }
    }

    /// <summary>Drops the cached blend, depth-stencil and rasterizer states after native code --
    /// a <see cref="SpriteBatch"/> -- changed them, so the next read asks the device again.</summary>
    internal void ForgetRenderStates()
    {
        _blendState = null;
        _depthStencilState = null;
        _rasterizerState = null;
    }

    protected virtual BlendState QueryBlendState()
    {
        var native = CnaBlendState.Versioned();
        CnaResult queryResult = Native.cna_graphics_device_get_blend_state(
                ResolveNativeDeviceHandle(), ref native);
        CnaException.ThrowIfFailed(queryResult, nameof(BlendState));
        return BlendState.FromNative(native);
    }

    private DepthStencilState? _depthStencilState;

    /// <summary>See <see cref="BlendState"/>'s own doc comment for why the default-construction
    /// path is a separate, overridable <see cref="QueryDepthStencilState"/> hook.</summary>
    public DepthStencilState DepthStencilState
    {
        get => _depthStencilState ??= QueryDepthStencilState();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CnaResult result = Native.cna_graphics_device_set_depth_stencil_state(ResolveNativeDeviceHandle(), value.ToNative());
            CnaException.ThrowIfFailed(result, nameof(DepthStencilState));
            _depthStencilState = value;
        }
    }

    protected virtual DepthStencilState QueryDepthStencilState()
    {
        var native = CnaDepthStencilState.Versioned();
        CnaResult queryResult = Native.cna_graphics_device_get_depth_stencil_state(
                ResolveNativeDeviceHandle(), ref native);
        CnaException.ThrowIfFailed(queryResult, nameof(DepthStencilState));
        return DepthStencilState.FromNative(native);
    }

    private RasterizerState? _rasterizerState;

    /// <summary>See <see cref="BlendState"/>'s own doc comment for why the default-construction
    /// path is a separate, overridable <see cref="QueryRasterizerState"/> hook.</summary>
    public RasterizerState RasterizerState
    {
        get => _rasterizerState ??= QueryRasterizerState();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CnaResult result = Native.cna_graphics_device_set_rasterizer_state(ResolveNativeDeviceHandle(), value.ToNative());
            CnaException.ThrowIfFailed(result, nameof(RasterizerState));
            _rasterizerState = value;
        }
    }

    protected virtual RasterizerState QueryRasterizerState()
    {
        var native = CnaRasterizerState.Versioned();
        CnaResult queryResult = Native.cna_graphics_device_get_rasterizer_state(
                ResolveNativeDeviceHandle(), ref native);
        CnaException.ThrowIfFailed(queryResult, nameof(RasterizerState));
        return RasterizerState.FromNative(native);
    }

    private SamplerStateCollection? _samplerStates;
    private SamplerStateCollection? _vertexSamplerStates;

    /// <summary>The pixel shader's sampler collection. Unlike
    /// <see cref="BlendState"/>/<see cref="DepthStencilState"/>/<see cref="RasterizerState"/>,
    /// what is cached here is the *collection object*, not any state value -- the collection
    /// itself reads and writes through to native on every access (see its own doc comment).</summary>
    public SamplerStateCollection SamplerStates => _samplerStates ??= CreateSamplerStateCollection(vertexStage: false);

    public SamplerStateCollection VertexSamplerStates => _vertexSamplerStates ??= CreateSamplerStateCollection(vertexStage: true);

    /// <summary>Covariant-return factory hook, same pattern as <see cref="QueryBlendState"/> --
    /// CNA.XnaCompat overrides it to build its own compat-typed collection.</summary>
    protected virtual SamplerStateCollection CreateSamplerStateCollection(bool vertexStage) => new(this, vertexStage);

    private TextureCollection? _textures;
    private TextureCollection? _vertexTextures;

    /// <summary>The pixel shader's texture bindings. Unlike <see cref="SamplerStates"/>, the
    /// collection object here is genuinely stateful -- see <see cref="TextureCollection"/>'s own
    /// doc comment for why native cannot answer its getter.</summary>
    public TextureCollection Textures => _textures ??= CreateTextureCollection(vertexStage: false);

    public TextureCollection VertexTextures => _vertexTextures ??= CreateTextureCollection(vertexStage: true);

    protected virtual TextureCollection CreateTextureCollection(bool vertexStage) => new(this, vertexStage);

    private IndexBuffer? _indices;

    public IndexBuffer? Indices
    {
        get => _indices;
        set
        {
            CnaHandle handle = value is null ? CnaHandle.Zero : new CnaHandle(value.NativeHandleValue);
            CnaResult result = Native.cna_graphics_device_set_index_buffer(ResolveNativeDeviceHandle(), handle);
            GC.KeepAlive(value);
            CnaException.ThrowIfFailed(result, nameof(Indices));
            _indices = value;
        }
    }

    public void DrawPrimitives(PrimitiveType primitiveType, int startVertex, int primitiveCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startVertex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primitiveCount);

        CnaResult result = Native.cna_graphics_device_draw_primitives(
            ResolveNativeDeviceHandle(), (uint)primitiveType, startVertex, primitiveCount);
        CnaException.ThrowIfFailed(result, nameof(DrawPrimitives));
    }

    /// <summary><paramref name="minVertexIndex"/>/<paramref name="numVertices"/> now forward to
    /// native code -- the real <c>cna_graphics_device_draw_indexed_primitives</c> takes exactly
    /// real XNA's own full 7-argument signature (confirmed by reading <c>graphics_device.h</c>
    /// directly), unlike the old guessed 5-argument shape this method used to call, which silently
    /// dropped these two.</summary>
    public void DrawIndexedPrimitives(
        PrimitiveType primitiveType, int baseVertex, int minVertexIndex, int numVertices, int startIndex, int primitiveCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseVertex);
        ArgumentOutOfRangeException.ThrowIfNegative(minVertexIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(numVertices);
        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(primitiveCount);

        CnaResult result = Native.cna_graphics_device_draw_indexed_primitives(
            ResolveNativeDeviceHandle(), (uint)primitiveType, baseVertex, minVertexIndex, numVertices, startIndex, primitiveCount);
        CnaException.ThrowIfFailed(result, nameof(DrawIndexedPrimitives));
    }

    /// <summary>
    /// Disposes the device, matching real XNA where <c>GraphicsDevice</c> is
    /// <see cref="IDisposable"/>.
    ///
    /// Releases the event subscriptions this object took, then asks native to dispose. A borrowed
    /// device's handle is <em>not</em> released here: it is the game's, resolved through
    /// <see cref="ResolveNativeDeviceHandle"/> rather than owned, and destroying it would pull the
    /// device out from under a game that is still running.
    ///
    /// A device created through
    /// <see cref="GraphicsDevice(GraphicsAdapter, GraphicsProfile, PresentationParameters)"/> is
    /// the caller's, so this destroys it -- after the native dispose, in that order, because the
    /// dispose call needs the handle it is about to release.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_deviceDisposed)
        {
            return;
        }

        _deviceDisposed = true;

        ReleaseEventSubscriptions();

        if (!disposing)
        {
            return;
        }

        // Deliberately unchecked: Dispose must not throw, and a device already torn down by its
        // game makes this an ordinary, harmless failure.
        Native.cna_graphics_device_dispose(ResolveNativeDeviceHandle());

        _ownedDevice?.Dispose();
    }

    /// <summary>
    /// Releases the managed roots used by native device-event subscriptions. A game's device handle
    /// is borrowable during lifecycle callbacks only, but the subscriptions must be detached before
    /// its owner destroys the game outside one. The XNA facade uses this exact path during game
    /// teardown; ordinary explicit device disposal still follows <see cref="Dispose(bool)"/>.
    /// </summary>
    internal void ReleaseEventSubscriptions()
    {
        for (int i = 0; i < _deviceEventBridges.Length; i++)
        {
            _deviceEventBridges[i]?.Dispose();
            _deviceEventBridges[i] = null;
        }
    }

    // Native device loans are renewed per call. Once the manager replaces the device, an old
    // managed wrapper must not renew its loan against the replacement.
    internal void RetireFromManager()
    {
        _retiredByManager = true;
        _deviceDisposed = true;
        ReleaseEventSubscriptions();
    }

    private bool _retiredByManager;
    private bool _deviceDisposed;
}
