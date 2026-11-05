using CNA.Interop;

namespace CNA.Graphics;

/// <summary>
/// A shader effect. Concrete, as in XNA -- it can be constructed directly from compiled Effect
/// Framework bytecode, and it is also the base for the stock effects.
///
/// This said "custom user-authored <c>.fx</c> shader loading is still not implemented, matching the
/// real openeggbert/cna C++ engine itself" until <c>cna_effect_create_compiled</c> was actually
/// tried. It works. The claim traced back to a header sentence -- "NOT_SUPPORTED while native CNA
/// bytecode loading is unavailable" -- that had outlived its implementation, and this comment
/// repeated it as settled fact. It was recorded as this binding's single largest functional
/// blocker on that basis. Eleventh entry in the same pattern; see plan.md's Corrections table.
///
/// Phase 8 WP4a made the reflection surface real. <see cref="CurrentTechnique"/>,
/// <see cref="Techniques"/> and <see cref="Parameters"/> now read the effect's actual native
/// objects (<c>cna_effect_get_current_technique</c>/<c>_get_techniques</c>/<c>_get_parameters</c>)
/// instead of the single fabricated one-pass technique this class used to hand out. That fake
/// existed only so <c>CurrentTechnique.Passes[0].Apply()</c> would compile and route back to
/// <see cref="Apply"/>; it reported a made-up name, always exactly one pass, and no parameters at
/// all, none of which a caller could distinguish from truth.
///
/// <see cref="Apply"/> remains a documented deviation from standard XNA (which exposes only
/// <c>EffectPass.Apply()</c>) that this project's own C++ engine already makes -- both paths reach
/// the same native code.
/// </summary>
public class Effect : IDisposable
{
    private readonly NativeResourceHandle? _ownedHandle;

    protected Effect(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        GraphicsDevice = graphicsDevice;
    }

    /// <summary>
    /// Builds an effect from compiled Effect Framework bytecode -- real XNA's
    /// <c>Effect(GraphicsDevice, byte[])</c>.
    ///
    /// This class was <see langword="abstract"/> until the bytecode route was bound, which was
    /// itself an XNA divergence: XNA's <c>Effect</c> is concrete and this constructor is the usual
    /// way a game loads a custom shader it read from disk itself. It could not be written while
    /// <c>cna_effect_create_compiled</c> was believed to answer <c>NOT_SUPPORTED</c> -- a belief
    /// that came from a header sentence which had outlived its implementation.
    /// </summary>
    /// <exception cref="CnaException">If the bytes are not a structurally valid Effect Framework
    /// binary, or the active renderer reports <c>COMPILED_EFFECTS</c> as false. Branch on the
    /// result rather than the file name: only the compiled shape depends on that capability.</exception>
    public unsafe Effect(GraphicsDevice graphicsDevice, byte[] effectCode)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(effectCode);

        if (effectCode.Length == 0)
        {
            throw new ArgumentException("Effect bytecode is empty.", nameof(effectCode));
        }

        GraphicsDevice = graphicsDevice;

        CnaHandle handle;
        fixed (byte* code = effectCode)
        {
            CnaResult result = Native.cna_effect_create_compiled(
                graphicsDevice.ResolveNativeDeviceHandle(), code, (ulong)effectCode.Length, out handle);
            CnaException.ThrowIfFailed(result, nameof(Effect));
        }

        _ownedHandle = new NativeResourceHandle(handle.Value, h => Native.cna_effect_destroy(new CnaHandle(h)).IsSuccess());
    }

    /// <summary>
    /// Adopts an effect native already built -- the <c>Load&lt;Effect&gt;</c> route's landing point.
    /// The handle is owned from here on, matching <c>cna_content_manager_load_effect</c>'s contract.
    ///
    /// Takes a raw <see cref="nint"/> rather than a <c>CnaHandle</c>, and is
    /// <c>protected internal</c>, so CNA.XnaCompat can call it -- it has no
    /// <c>InternalsVisibleTo</c> grant into CNA.Interop and can never name that type (invariant 5).
    /// Same rule <c>Texture2D</c>'s handle constructor already follows.
    /// </summary>
    /// <summary>
    /// A custom effect from shader <em>source</em> -- real XNA has no such constructor, because XNA
    /// had one shader language and a compiled pipeline.
    ///
    /// Separate capability from <see cref="Effect(GraphicsDevice, byte[])"/>: that one needs
    /// <see cref="GraphicsCapability.CompiledEffects"/>, this needs
    /// <see cref="GraphicsCapability.CustomEffects"/>, and they differ in practice -- the SOFTWARE
    /// renderer reports the second true and the first false. A game shipping a compiled <c>.fx</c>
    /// still cannot load it there; a game that can supply source can.
    ///
    /// <b>Ask <see cref="GraphicsDevice.ShadingDialect"/> first.</b> The text is renderer-specific,
    /// and the header is explicit that the renderer's identity is not a safe way to infer the
    /// dialect -- that inference is wrong in a build carrying more than one backend, which is
    /// exactly the case where it looks right.
    ///
    /// <b>Success does not mean the source compiled.</b> Check <see cref="IsSourceValid"/>
    /// afterwards and read it the way the ABI specifies -- false is the strong answer.
    /// </summary>
    /// <exception cref="CnaException">If the renderer has no source-effect support, or either
    /// source is empty.</exception>
    public Effect(GraphicsDevice graphicsDevice, string vertexShaderSource, string fragmentShaderSource)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentException.ThrowIfNullOrEmpty(vertexShaderSource);
        ArgumentException.ThrowIfNullOrEmpty(fragmentShaderSource);

        GraphicsDevice = graphicsDevice;

        CnaHandle handle = CnaHandle.Zero;
        CnaResult result = CnaStringMarshal.WithStringView(vertexShaderSource, vertexView =>
            CnaStringMarshal.WithStringView(fragmentShaderSource, fragmentView =>
                Native.cna_shader_effect_create(
                    graphicsDevice.ResolveNativeDeviceHandle(), vertexView, fragmentView, out handle)));

        CnaException.ThrowIfFailed(result, nameof(Effect));
        _ownedHandle = new NativeResourceHandle(handle.Value, h => Native.cna_effect_destroy(new CnaHandle(h)).IsSuccess());
    }

    /// <summary>
    /// What the renderer concluded about a source-built effect's text.
    ///
    /// <b>Read this asymmetrically.</b> <see langword="false"/> is the strong answer -- a renderer
    /// looked at the source and refused it. <see langword="true"/> means only that nothing rejected
    /// it, which is weaker than "this will draw": the software rasterizer accepts any non-empty
    /// text and reports true for a shader that cannot draw anything.
    ///
    /// The distinction is not pedantry. Constructing from deliberate nonsense succeeds on SOFTWARE
    /// and reports valid, and fails the same check on SDL_RENDERER. A binding that treated
    /// construction as verification -- as this one did until the route was found -- hands a game a
    /// live effect for text that will never render, on exactly the renderer where it is hardest to
    /// notice.
    /// </summary>
    public bool IsSourceValid
    {
        get
        {
            CnaResult result = Native.cna_shader_effect_is_valid(
                new CnaHandle(NativeEffectHandleValue), out byte valid);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsSourceValid));
            return valid != 0;
        }
    }

    protected internal Effect(GraphicsDevice graphicsDevice, ulong nativeHandleValue)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        GraphicsDevice = graphicsDevice;
        _ownedHandle = new NativeResourceHandle(nativeHandleValue, h => Native.cna_effect_destroy(new CnaHandle(h)).IsSuccess());
    }

    public GraphicsDevice GraphicsDevice { get; }

    /// <summary>
    /// The native effect handle a concrete subclass owns. Declared here rather than passed to the
    /// constructor because subclasses create their handle through their own
    /// <c>cna_*_effect_create</c> call, and C# evaluates a <c>base(...)</c> argument before any
    /// instance state exists.
    ///
    /// <see langword="virtual"/> with a throwing default rather than <see langword="abstract"/>:
    /// it is <c>private protected</c>, so a subclass outside this assembly could never override it
    /// and an abstract declaration would make <see cref="Effect"/> impossible to subclass at all
    /// from outside -- which would break, among other things, the test doubles
    /// <c>CNA.Framework.Tests</c> builds to exercise <see cref="ModelMesh"/> without a real
    /// effect. Such a subclass simply has no reflection surface, which the message says plainly
    /// instead of failing as a null handle deeper in native code -- unless this instance owns a
    /// handle of its own, which one built from bytecode or loaded through the content manager does.
    ///
    /// Typed <see cref="nint"/>, not <see cref="CnaHandle"/>, and
    /// <c>protected internal</c> rather than <c>private protected</c> -- both so CNA.XnaCompat can
    /// take part. Phase 8 WP4c made <c>Microsoft.Xna.Framework.Graphics.Effect</c> a real base
    /// class of the compat stock effects, which therefore hold a <c>CNA.Graphics</c> effect by
    /// composition and must forward this handle out of it; the <c>internal</c> half of
    /// <c>protected internal</c> is what lets them read it off that inner instance (CNA.Framework
    /// grants <c>InternalsVisibleTo</c> to CNA.XnaCompat), and the <c>protected</c> half is what
    /// lets them override it. Same "raw <see cref="nint"/> across the assembly boundary" rule
    /// <c>GraphicsDevice.NativeGameHandleValue</c> and <c>Texture2D</c>'s handle constructor
    /// already follow -- see docs/architecture.md.</summary>
    protected internal virtual ulong NativeEffectHandleValue =>
        _ownedHandle is not null
        ? _ownedHandle.DangerousGetHandle()
        : throw new NotSupportedException(
            $"{GetType().Name} is not backed by a native CNA effect, so Parameters, Techniques and " +
            "CurrentTechnique are unavailable. Only this project's own stock effects (BasicEffect, " +
            "AlphaTestEffect, DualTextureEffect, EnvironmentMapEffect, SkinnedEffect, EffectMaterial) " +
            "provide one.");

    private CnaHandle NativeEffectHandle => new(NativeEffectHandleValue);

    private EffectParameterCollection? _parameters;
    private EffectTechniqueCollection? _techniques;

    /// <summary>Cached and owned for the same reason as the two above: each read of
    /// <c>cna_effect_get_current_technique</c> mints a new owned handle, and an XNA game reads
    /// <c>CurrentTechnique</c> once per frame without ever disposing what it gets back.</summary>
    private EffectTechnique? _currentTechnique;

    /// <summary>Cached: each read of the underlying native call mints a new owned handle (see
    /// <see cref="EffectParameterCollection"/>), so re-resolving per access would churn native
    /// handles for a collection whose identity never changes over an effect's life.</summary>
    public EffectParameterCollection Parameters
    {
        get
        {
            if (_parameters is null)
            {
                CnaResult result = Native.cna_effect_get_parameters(NativeEffectHandle, out CnaHandle collection);
                GC.KeepAlive(this);
                CnaException.ThrowIfFailed(result, nameof(Parameters));
                _parameters = new EffectParameterCollection(collection, GraphicsDevice);
            }

            return _parameters;
        }
    }

    /// <summary>Cached for the same reason as <see cref="Parameters"/>.</summary>
    public EffectTechniqueCollection Techniques
    {
        get
        {
            if (_techniques is null)
            {
                CnaResult result = Native.cna_effect_get_techniques(NativeEffectHandle, out CnaHandle collection);
                GC.KeepAlive(this);
                CnaException.ThrowIfFailed(result, nameof(Techniques));
                _techniques = new EffectTechniqueCollection(collection);
            }

            return _techniques;
        }
    }

    public EffectTechnique CurrentTechnique
    {
        get
        {
            if (_currentTechnique is not null)
            {
                return _currentTechnique;
            }

            CnaResult result = Native.cna_effect_get_current_technique(NativeEffectHandle, out CnaHandle technique);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(CurrentTechnique));
            _currentTechnique = new EffectTechnique(technique);
            return _currentTechnique;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CnaResult result = Native.cna_effect_set_current_technique(NativeEffectHandle, value.NativeHandle);
            GC.KeepAlive(value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(CurrentTechnique));

            // The cached wrapper described the *previous* technique, so it is released rather than
            // reused: keeping it would answer the old technique's name and passes forever.
            _currentTechnique?.Dispose();
            _currentTechnique = null;
        }
    }

    public void Apply() => OnApply();

    /// <summary>
    /// An independent copy of this effect, matching real XNA's <c>Effect.Clone</c>.
    ///
    /// Native chooses the clone's concrete type: stock effects preserve theirs, while
    /// EffectMaterial inherits the base Effect clone. Derived wrappers select the corresponding
    /// managed type; an unbacked effect must implement its own cloning.
    /// </summary>
    public virtual Effect Clone() =>
        _ownedHandle is not null
        ? new Effect(GraphicsDevice, CloneNativeHandle().Value)
        : throw new NotSupportedException(
            $"{GetType().Name} does not implement Clone. The native clone route exists " +
            "(cna_effect_clone), but rewrapping its result requires the concrete effect type to " +
            "construct the matching managed class.");

    /// <summary>Clones the native effect and hands back the new handle for a subclass to wrap.
    /// The caller owns it.</summary>
    private protected CnaHandle CloneNativeHandle()
    {
        CnaResult result = Native.cna_effect_clone(new CnaHandle(NativeEffectHandleValue), out CnaHandle clone);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Clone));
        return clone;
    }

    /// <summary>
    /// Selects this effect on its device. <see langword="virtual"/> with a working default rather
    /// than <see langword="abstract"/>, since this class became concrete: an effect that owns a
    /// handle -- one built from bytecode or loaded through the content manager -- applies through
    /// the same <c>cna_effect_apply</c> every stock effect uses. One with no handle says so.
    /// </summary>
    protected virtual void OnApply()
    {
        CnaResult result = Native.cna_effect_apply(new CnaHandle(NativeEffectHandleValue));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Apply));
    }

    public virtual void Dispose()
    {
        _parameters?.Dispose();
        _techniques?.Dispose();
        _currentTechnique?.Dispose();
        _ownedHandle?.Dispose();
    }
}
