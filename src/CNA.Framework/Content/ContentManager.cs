using CNA.Audio;
using CNA.Content.Cnj;
using CNA.Content.Xnb;
using CNA.Graphics;
using CNA.Interop;

namespace CNA.Content;

/// <summary>
/// The native C ABI cannot expose C# generics directly, so <see cref="Load{T}"/> dispatches by
/// runtime type -- see libcna/cna's docs/binding.md, "Generic APIs dispatch in the language".
/// CNA.XnaCompat's
/// <c>ContentManager</c> overrides this same method to additionally recognize its own compat
/// content types, reusing <see cref="LoadNativeTexture2DHandle"/> so it never has to touch
/// CNA.Interop directly (see docs/architecture.md).
///
/// <see cref="Load{T}"/>'s <see cref="Model"/> case is genuinely different from every other case
/// here: real XNA's own <c>Content.Load&lt;Model&gt;()</c> reads a compiled <c>.xnb</c> binary
/// asset -- pure C#/BCL logic with no native ABI dependency at all, unlike
/// <see cref="Texture2D"/>/<see cref="SoundEffect"/>/<see cref="SpriteFont"/> (which this project's
/// native CNA engine loads on their behalf). See <c>CNA.Content.Xnb</c>'s own types for the actual
/// <c>.xnb</c> reader (confirmed byte-for-byte against the real openeggbert/cna C++ engine's own
/// reference implementation and a real MonoGame-compiled fixture -- see <c>NEXT.md</c>) -- real,
/// uncompressed <c>.xnb</c> files and real, LZX-compressed <c>.xnb</c> files (<see cref="XnbLzxDecompression"/>/
/// <see cref="LzxDecoder"/>, a direct port of the real C++ engine's own <c>LzxDecoder</c>) are both
/// supported; MonoGame's own Lz4 extension remains out of scope (no local format grounding exists
/// to implement it correctly -- see <see cref="XnbCompression"/>'s own doc comment).
/// <see cref="LoadModel"/> also recognizes a real, minimal-scope subset of the real
/// engine's own <c>.cnj</c> format (<c>CNA.Content.Cnj</c> -- JSON envelope + flat mesh list,
/// <c>BasicEffect</c> only, vertex strides 16/20/24/32 only; see that namespace's own types for the
/// full scope-cut list), tried only when no <c>.xnb</c> file of the same asset name exists, matching
/// the real engine's own dispatch order. Runtime glTF (<c>.gltf</c>/<c>.glb</c>) remains entirely out
/// of scope (see <c>plan.md</c>/<c>NEXT.md</c>). Building the final, real <see cref="Model"/> still needs a real
/// <see cref="Graphics.GraphicsDevice"/> (to construct native-backed <see cref="VertexBuffer"/>/
/// <see cref="IndexBuffer"/> instances), so <see cref="GraphicsDevice"/> below is set by
/// <see cref="Game"/> once its own device becomes available -- <em>that</em> part is native-ABI-blocked,
/// same as the rest of this class's content types.
/// </summary>
public class ContentManager : IDisposable
{
    private ulong _nativeHandleValue;
    private readonly NativeResourceHandle? _ownedHandle;

    /// <summary>
    /// <c>protected internal</c> so CNA.XnaCompat's <c>ContentManager</c> subclass constructor
    /// can forward to it without naming <see cref="CnaHandle"/> -- see docs/architecture.md.
    /// </summary>
    protected internal ContentManager(ulong nativeHandleValue)
        : this(nativeHandleValue, ownsNativeHandle: false)
    {
    }

    internal ContentManager(ulong nativeHandleValue, bool ownsNativeHandle)
    {
        _nativeHandleValue = nativeHandleValue;
        if (ownsNativeHandle)
        {
            _ownedHandle = new NativeResourceHandle(
                nativeHandleValue,
                handle => Native.cna_content_manager_destroy(new CnaHandle(handle)).IsSuccess());
        }
    }

    internal ulong NativeHandleValue =>
        _ownedHandle is null ? _nativeHandleValue : _ownedHandle.DangerousGetHandle();

    /// <summary>Creates an independently owned native manager. This is used by the strict XNA
    /// facade's public service-provider constructors once a callback-scoped graphics device is
    /// available. The game-provided manager continues to use the borrowed constructor above.</summary>
    internal static ContentManager CreateOwned(GraphicsDevice graphicsDevice, string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(rootDirectory);

        CnaHandle contentManager = CnaHandle.Zero;
        CnaResult result = CnaStringMarshal.WithStringView(
            rootDirectory,
            view =>
            {
                var createInfo = new CnaContentManagerCreateInfo(view);
                return Native.cna_content_manager_create(
                    graphicsDevice.ResolveNativeDeviceHandle(), in createInfo, out contentManager);
            });
        CnaException.ThrowIfFailed(result, nameof(CreateOwned));

        return new ContentManager(contentManager.Value, ownsNativeHandle: true)
        {
            GraphicsDevice = graphicsDevice,
        };
    }

    /// <summary>Set by <see cref="Game"/> once its own <see cref="Graphics.GraphicsDevice"/>
    /// becomes available (real XNA content loading generally only ever happens from
    /// <c>LoadContent()</c> onward, by which point this is always set) -- <see langword="null"/>
    /// only before that point, or if this <see cref="ContentManager"/> was hand-built outside the
    /// normal <see cref="Game"/> lifecycle. Only <see cref="Load{T}"/>'s <see cref="Model"/> case
    /// needs this today.</summary>
    public GraphicsDevice? GraphicsDevice { get; set; }

    /// <summary>
    /// Where assets are loaded from, relative to the title.
    ///
    /// The getter asks native rather than answering from the value last set, which it used to do.
    /// That cache was wrong in one reachable case and right by accident otherwise: a manager whose
    /// root was set by something other than this property -- native's own default, or another
    /// wrapper over the same handle -- reported an empty string.
    /// </summary>
    public unsafe string RootDirectory
    {
        get
        {
            string value = NativeStringReader.Read(
                Native.cna_content_manager_get_root_directory_size,
                Native.cna_content_manager_copy_root_directory,
                new CnaHandle(NativeHandleValue),
                nameof(RootDirectory));
            GC.KeepAlive(this);
            return value;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CnaResult result = CnaStringMarshal.WithStringView(
                value, view => Native.cna_content_manager_set_root_directory(new CnaHandle(NativeHandleValue), view));
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(RootDirectory));
        }
    }

    /// <summary>
    /// The service container assets are resolved through. Matches real XNA's
    /// <c>ServiceProvider</c>.
    ///
    /// Always <see langword="null"/>. <c>content.h</c> documents both manager constructors as
    /// creating one "with a null service provider", and exposes only
    /// <c>cna_content_manager_get_has_service_provider</c> -- a boolean, with no route to obtain the
    /// provider itself. Present rather than omitted so ported XNA source compiles; reporting null is
    /// what the ABI actually says is there.
    /// </summary>
    public IServiceProvider? ServiceProvider => null;

    /// <summary>Unloads every asset. A game-provided native manager remains borrowed; a manager
    /// created for the strict service-provider constructor also releases its independently owned
    /// native handle. Matches real XNA's observable <c>ContentManager.Dispose</c> contract while
    /// respecting the two distinct CNA ownership routes.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>The overridable half, for a subclass that owns more than the base does.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (disposing)
            {
                Unload();

                // Native's .cnj table goes with the manager, so this is the moment the roots it holds
                // stop being reachable from native and can be freed.
                foreach (CnjLoaderRegistration registration in _cnjLoaders)
                {
                    registration.ReleaseRoots();
                }

                _cnjLoaders.Clear();
            }
        }
        finally
        {
            if (_ownedHandle is not null)
            {
                _ownedHandle.Dispose();
                _nativeHandleValue = 0;
            }
        }
    }

    /// <summary>
    /// What this manager has loaded, keyed by canonical asset name.
    ///
    /// <b>This class documented a cache before it had one.</b> <see cref="Unload"/>'s comment said
    /// "releases every asset this manager loaded", and <see cref="LoadForeign{T}"/>'s said "results
    /// are cached by asset name exactly as every other load is" -- and no load cached anything.
    /// Measured before this was written: two <c>Load&lt;SpriteFont&gt;</c> calls for one name
    /// returned two fonts with two textures, <c>Unload</c> disposed neither, and a game looping over
    /// <c>Load</c> accumulated a GPU texture per call that nothing would ever free. So this is a
    /// missing implementation rather than a deliberate CNA-native divergence, which is the question
    /// that had to be settled before it could be fixed.
    ///
    /// Case-insensitive over backslash-normalised names, matching <c>CNA.XnaCompat</c>'s facade and
    /// XNA before it. The two layers agreeing matters more than either spelling: a game that ports
    /// from one to the other should not find its content identity changing underneath it.
    /// </summary>
    private readonly Dictionary<string, object> _loadedAssets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The subset of <see cref="_loadedAssets"/> this manager must dispose, in load order.
    ///
    /// Separate from the cache because the two answer different questions -- the cache is "have I
    /// loaded this name", and this is "what do I own". A native-backed asset returned twice under
    /// two names would otherwise be disposed twice.
    ///
    /// Ownership is the half that makes caching worth having here. CNA's own header is explicit
    /// that a load hands back an <em>independently owned</em> handle which "must still be destroyed
    /// explicitly" and which <c>cna_content_manager_unload</c> does not touch. Somebody has to be
    /// that owner; XNA's answer is the manager, and adopting it is what turns an accumulating pile
    /// of textures into a lifetime.
    /// </summary>
    private readonly List<IDisposable> _ownedAssets = [];

    private bool _disposed;

    /// <summary>
    /// Releases every asset this manager loaded. Matches real XNA's <c>Unload</c>: the manager stays
    /// usable and can load again afterwards.
    ///
    /// Disposal runs before the native unload and the collections are cleared in a <c>finally</c>,
    /// so a handler that throws cannot leave this manager holding references it has already
    /// disposed -- a second <c>Unload</c> would then dispose the same prefix again. That is XNA's
    /// own ordering and the compat facade's.
    /// </summary>
    public virtual void Unload()
    {
        try
        {
            foreach (IDisposable owned in _ownedAssets)
            {
                owned.Dispose();
            }
        }
        finally
        {
            _ownedAssets.Clear();
            _loadedAssets.Clear();
        }

        CnaResult result = Native.cna_content_manager_unload(new CnaHandle(NativeHandleValue));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Unload));
    }

    /// <summary>
    /// Loads an asset, or returns the one already loaded under that name.
    ///
    /// <b>Cached, and the manager owns what it caches.</b> A second call for the same name returns
    /// the same instance, and <see cref="Unload"/> disposes it. See <see cref="_loadedAssets"/> for
    /// why that is a fix rather than a design change.
    ///
    /// A failed load caches nothing, so a later call retries. Caching the failure would make a
    /// missing file that has since appeared stay missing for the life of the manager.
    /// </summary>
    /// <exception cref="ContentLoadException">The name is already loaded as a different type.
    /// Reported here rather than as an <c>InvalidCastException</c> deeper in the caller, because
    /// only this frame knows which asset caused it.</exception>
    public virtual T Load<T>(string assetName)
    {
        ArgumentException.ThrowIfNullOrEmpty(assetName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        string key = CanonicalAssetName(assetName);
        if (_loadedAssets.TryGetValue(key, out object? cached))
        {
            if (cached is T typed)
            {
                return typed;
            }

            throw new ContentLoadException(
                $"Content asset '{assetName}' was already loaded as {cached.GetType()}, not {typeof(T)}.");
        }

        // Disposables created on the way to the asset are collected here rather than recorded as
        // they appear, so a load that fails halfway leaves nothing behind. XNA leaks them; there is
        // no compatibility reason to copy that, and the alternative is a texture nobody can reach.
        var pending = new List<IDisposable>();
        T loaded;
        try
        {
            loaded = ReadAsset<T>(assetName, pending);
        }
        catch
        {
            foreach (IDisposable partial in pending)
            {
                partial.Dispose();
            }

            throw;
        }

        Track(key, loaded, pending);
        return loaded;
    }

    /// <summary>
    /// The canonical form of an asset name, for cache identity.
    ///
    /// Backslashes become forward slashes and comparison is case-insensitive, which is what XNA does
    /// and what <c>CNA.XnaCompat</c> already did. Nothing more: no rooting, no <c>.</c>/<c>..</c>
    /// collapsing. That is deliberate -- XNA does not do it either, and a manager that decided
    /// <c>./hero</c> and <c>hero</c> were one asset would be inventing an identity the format does
    /// not have.
    /// </summary>
    private static string CanonicalAssetName(string assetName) => assetName.Replace('\\', '/');

    /// <summary>
    /// Records a loaded asset in the cache, and everything it owns in the owned list.
    ///
    /// Called only on success, so a failure caches nothing and owns nothing. The asset itself is
    /// added only when it is not already among <paramref name="pending"/>, because a composite
    /// asset that recorded itself would otherwise be disposed twice.
    /// </summary>
    private void Track(string key, object? loaded, List<IDisposable> pending)
    {
        if (loaded is null)
        {
            return;
        }

        _loadedAssets[key] = loaded;
        _ownedAssets.AddRange(pending);

        if (loaded is IDisposable disposable && !pending.Contains(disposable))
        {
            _ownedAssets.Add(disposable);
        }
    }

    /// <summary>
    /// Materialises the asset. Anything disposable created along the way that is <em>not</em> the
    /// returned object goes into <paramref name="pending"/>.
    ///
    /// <c>SpriteFont</c> is the case that makes this parameter necessary and is why it exists.
    /// XNA's <c>SpriteFont</c> is not <c>IDisposable</c> -- so is this one -- while the texture
    /// behind it very much is, and a manager that recorded only the returned object would leave a
    /// font atlas alive after <see cref="Unload"/>. Measured: it did.
    /// </summary>
    private T ReadAsset<T>(string assetName, List<IDisposable> pending)
    {
        if (typeof(T) == typeof(Texture2D))
        {
            return (T)(object)new Texture2D(RequireGraphicsDevice<T>(assetName), LoadNativeTexture2DHandle(assetName));
        }

        if (typeof(T) == typeof(SpriteFont))
        {
            SpriteFontData data = LoadSpriteFontData(assetName);
            var atlas = new Texture2D(RequireGraphicsDevice<T>(assetName), data.TextureHandle);
            pending.Add(atlas);
            return (T)(object)new SpriteFont(
                atlas,
                data.GlyphBounds,
                data.Cropping,
                data.Characters,
                data.LineSpacing,
                data.Spacing,
                data.Kerning,
                data.DefaultCharacter);
        }

        if (typeof(T) == typeof(TextureCube))
        {
            return (T)(object)new TextureCube(RequireGraphicsDevice<T>(assetName), LoadNativeTextureCubeHandle(assetName));
        }

        if (typeof(T) == typeof(SoundEffect))
        {
            return (T)(object)new SoundEffect(LoadNativeSoundEffectHandle(assetName));
        }

        if (typeof(T) == typeof(Model))
        {
            return (T)(object)LoadModel(assetName);
        }

        if (typeof(T) == typeof(Effect))
        {
            return (T)(object)new Effect(RequireGraphicsDevice<T>(assetName), LoadNativeEffectHandle(assetName));
        }

        throw new NotSupportedException($"Unsupported content type {typeof(T)}.");
    }

    /// <summary>Parses a real <c>.xnb</c> <see cref="Model"/> asset's bytes (uncompressed or
    /// LZX-compressed) from <see cref="RootDirectory"/> into an intermediate, native-free
    /// <see cref="XnbModelData"/> -- deliberately split out from <see cref="LoadModel"/> (which needs a real
    /// <see cref="Graphics.GraphicsDevice"/> to finish the job) so <c>CNA.XnaCompat</c>'s own
    /// <c>ContentManager</c> can reuse this exact parsing step to build its own compat-typed
    /// <see cref="Model"/>, without duplicating any <c>.xnb</c> format logic -- the same "reuse the
    /// shared low-level parsing/helper, reimplement only the thin native-backed assembly around it"
    /// pattern <c>CNA.XnaCompat.MediaLibrary</c> already established for
    /// <see cref="Media.SavedPictureStore"/>. <c>internal</c>, not <c>protected</c> like
    /// <see cref="LoadNativeTexture2DHandle"/>'s own -- <see cref="XnbModelData"/> is itself
    /// <c>internal</c>, and a <c>protected</c> member's signature must be visible to *any*
    /// subclass in *any* assembly, not just the one (<c>CNA.XnaCompat</c>) this project's own
    /// <c>InternalsVisibleTo</c> grant actually covers (a real <c>CS0050</c> compiler error caught
    /// this during implementation) -- <c>internal</c> matches <see cref="Media.SavedPictureStore"/>'s
    /// own accessibility for the identical reason.</summary>
    internal XnbModelData LoadXnbModelData(string assetName)
    {
        object? root = ReadXnbRootObject(assetName);

        if (root is not XnbModelData modelData)
        {
            throw new ContentLoadException(
                $"'{assetName}' is not a Model asset (its .xnb root object's type reader was not ModelReader).");
        }

        return modelData;
    }

    /// <summary>Parses a real <c>.xnb</c> <c>SpriteFont</c> asset into a native-free
    /// <see cref="XnbSpriteFontData"/>. Same split, and same <c>internal</c> accessibility, as
    /// <see cref="LoadXnbModelData"/> -- see its doc comment for both.</summary>
    internal XnbSpriteFontData LoadXnbSpriteFontData(string assetName)
    {
        object? root = ReadXnbRootObject(assetName);

        if (root is not XnbSpriteFontData spriteFontData)
        {
            throw new ContentLoadException(
                $"'{assetName}' is not a SpriteFont asset (its .xnb root object's type reader was not SpriteFontReader).");
        }

        return spriteFontData;
    }

    /// <summary>Opens a <c>.xnb</c> file, decompresses it when the header says so, and reads its
    /// root object. Extracted when the <c>SpriteFont</c> reader landed: the container half is
    /// identical for every asset type and only the root-object type check differs, so duplicating
    /// it would have meant two copies of the LZX handling below.</summary>
    private object? ReadXnbRootObject(string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetName);

        return XnbContainer.Read(
            ResolveXnbAssetPath(assetName),
            assetName,
            static reader => reader.ReadRootObjectAndResolveSharedResources());
    }

    /// <summary>
    /// Loads an external reference whose type the referring file does not state, which is what
    /// XNA's <c>ReadExternalReference&lt;object&gt;</c> did through <c>Load&lt;object&gt;</c>.
    ///
    /// The referenced file names its own root reader, so the type is read rather than guessed --
    /// see <see cref="XnbContainer.RootReaderName"/>. Only the texture kinds are dispatched,
    /// because the one place this is reached from is an <c>EffectMaterial</c> parameter, and XNA's
    /// own parameter chain assigns a loaded reference only when it is a <c>Texture</c> and
    /// otherwise ignores it. Anything else therefore reports itself rather than being silently
    /// dropped, since a material naming a non-texture parameter reference is content this reader has
    /// not seen and should not pretend to understand.
    /// </summary>
    internal Texture LoadReferencedTexture(string assetName) =>
        XnbContainer.RootReaderName(RootDirectory, assetName) switch
        {
            "Microsoft.Xna.Framework.Content.Texture2DReader" => Load<Texture2D>(assetName),
            "Microsoft.Xna.Framework.Content.TextureCubeReader" => Load<TextureCube>(assetName),
            var other => throw new ContentLoadException(
                $"External reference '{assetName}' is a {other ?? "null object"}, which this project's " +
                ".xnb reader cannot load as a texture."),
        };

    /// <summary>Builds the final, real, native-backed <see cref="Model"/> -- see this type's own doc
    /// comment for why only this assembly step (not the parsing <see cref="LoadXnbModelData"/>/
    /// <see cref="LoadCnjModelData"/> do) is native-ABI-blocked. Dispatch order matches the real
    /// engine's own: a real <c>.xnb</c> file always wins first if one exists for
    /// <paramref name="assetName"/>, only falling back to <c>.cnj</c> when it doesn't -- so a real
    /// <c>.xnb</c> asset always shadows a <c>.cnj</c> file of the same name sitting next to it.
    /// Two of the real engine's own further fallbacks are deliberately <b>not</b> ported: resolving
    /// an <paramref name="assetName"/> that already carries its own extension as-is (a rarely-used
    /// convenience with no precedent anywhere in this project's own <c>.xnb</c>-loading code, which
    /// always appends the extension itself), and runtime glTF (<c>.gltf</c>/<c>.glb</c>, hard out of
    /// scope -- see <c>CNA.Content.Cnj</c>'s own doc comments).</summary>
    /// <summary>
    /// The device every <see cref="GraphicsResource"/>-derived asset now needs, or a
    /// <see cref="ContentLoadException"/> naming the asset if none has been assigned yet.
    ///
    /// Added by Phase 8 WP3, which reparented <see cref="Texture2D"/> onto
    /// <see cref="GraphicsResource"/> and so made a device mandatory where texture loading
    /// previously needed none. Deliberately the same failure shape <see cref="LoadModel"/> has used
    /// since it was written -- a <see cref="ContentLoadException"/> naming the asset, not a bare
    /// <see cref="NullReferenceException"/> from somewhere deeper -- since the cause is identical
    /// (content loaded before <see cref="Game"/> assigns the device).
    /// </summary>
    private GraphicsDevice RequireGraphicsDevice<T>(string assetName) =>
        GraphicsDevice ?? throw new ContentLoadException(
            $"Cannot load {typeof(T).Name} '{assetName}': no GraphicsDevice is available yet (ContentManager.GraphicsDevice is null).");

    protected Model LoadModel(string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetName);

        if (GraphicsDevice is null)
        {
            throw new ContentLoadException(
                $"Cannot load Model '{assetName}': no GraphicsDevice is available yet (ContentManager.GraphicsDevice is null).");
        }

        if (File.Exists(XnaContentPath.ToTitleFilePath(RootDirectory, assetName, ".xnb")))
        {
            return XnbModelBuilder.Build(GraphicsDevice, LoadXnbModelData(assetName), this);
        }

        if (File.Exists(XnaContentPath.ToTitleFilePath(RootDirectory, assetName, ".cnj")))
        {
            return CnjModelBuilder.Build(GraphicsDevice, LoadCnjModelData(assetName));
        }

        throw new ContentLoadException($"Content file '{assetName}' was not found (tried '{assetName}.xnb' and '{assetName}.cnj').");
    }

    /// <summary>Parses a real, minimal-scope <c>.cnj</c> <see cref="Model"/> asset's JSON (plus its
    /// vertex/index sidecar files) from <see cref="RootDirectory"/> into an intermediate,
    /// native-free <see cref="CnjModelData"/> -- same split, same reuse rationale, and the same
    /// <c>internal</c>-not-<c>protected</c> accessibility reasoning (a real <c>CS0050</c> compiler
    /// error, since <see cref="CnjModelData"/> is itself <c>internal</c>) as
    /// <see cref="LoadXnbModelData"/>'s own doc comment already explains for the <c>.xnb</c> side.</summary>
    internal CnjModelData LoadCnjModelData(string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetName);

        string path = XnaContentPath.ToTitleFilePath(RootDirectory, assetName, ".cnj");
        if (!File.Exists(path))
        {
            throw new ContentLoadException($"Content file '{path}' was not found.");
        }

        string json = File.ReadAllText(path);
        return CnjModelReader.Read(json, assetName, XnaContentPath.ToTitlePath(RootDirectory));
    }

    private string ResolveXnbAssetPath(string assetName)
    {
        string path = XnaContentPath.ToTitleFilePath(RootDirectory, assetName, ".xnb");
        if (!File.Exists(path))
        {
            throw new ContentLoadException($"Content file '{path}' was not found.");
        }

        return path;
    }

    internal ulong LoadNativeTexture2DHandle(string assetName)
    {
        CnaHandle texture = CnaHandle.Zero;
        CnaResult result = CnaStringMarshal.WithStringView(
            assetName, view => Native.cna_content_manager_load_texture2d(new CnaHandle(NativeHandleValue), view, out texture));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Load));
        return texture.Value;
    }

    /// <summary>The texture-cube loader. <c>cna_content_manager_load_texture_cube</c> was unbound
    /// until a sweep of unbound header functions found it, so <c>Load&lt;TextureCube&gt;</c> threw
    /// "unsupported content type" for an asset the C API could load all along.</summary>
    internal ulong LoadNativeTextureCubeHandle(string assetName)
    {
        CnaHandle texture = CnaHandle.Zero;
        CnaResult result = CnaStringMarshal.WithStringView(
            assetName, view => Native.cna_content_manager_load_texture_cube(new CnaHandle(NativeHandleValue), view, out texture));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Load));
        return texture.Value;
    }

    internal ulong LoadNativeSoundEffectHandle(string assetName)
    {
        CnaHandle soundEffect = CnaHandle.Zero;
        CnaResult result = CnaStringMarshal.WithStringView(
            assetName, view => Native.cna_content_manager_load_sound_effect(new CnaHandle(NativeHandleValue), view, out soundEffect));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Load));
        return soundEffect.Value;
    }

    private readonly List<CnjLoaderRegistration> _cnjLoaders = [];

    /// <summary>
    /// Registers a <see cref="CnjLoader"/> for descriptors whose <c>"type"</c> is
    /// <paramref name="typeName"/>, then load them through <see cref="LoadForeign{T}"/>.
    ///
    /// The <c>.cnj</c> half of custom content. <see cref="ContentTypeReaderRegistration"/> covers
    /// compiled <c>.xnb</c> assets; this covers loose descriptors, and
    /// <see cref="LoadForeign{T}"/> takes either without being told which produced the object --
    /// which also closes the limit that route's own header used to carry, that only compiled assets
    /// could reach a registered reader.
    ///
    /// <b>Registered against this manager, and not revocable.</b> Native's table belongs to the
    /// content manager and dies with it, so there is no unregister to expose and the returned
    /// object is not disposable. <see cref="Dispose(bool)"/> releases the managed roots at the same
    /// moment native drops the table.
    /// </summary>
    /// <exception cref="CnaException">If <paramref name="typeName"/> is already registered on this
    /// manager. A descriptor naming nothing registered fails its load rather than falling back --
    /// the same rule a compiled asset naming an unregistered reader follows.</exception>
    public CnjLoaderRegistration RegisterCnjLoader(string typeName, CnjLoader loader)
    {
        CnjLoaderRegistration registration =
            CnjLoaderRegistration.Register(NativeHandleValue, typeName, loader);
        GC.KeepAlive(this);

        _cnjLoaders.Add(registration);
        return registration;
    }

    /// <summary>
    /// Loads an asset whose root reader was registered through
    /// <see cref="ContentTypeReaderRegistration.Register"/> -- the only route that reaches a
    /// caller-supplied reader.
    ///
    /// Separate from <see cref="Load{T}"/> rather than another case inside it, because the ABI
    /// route genuinely takes no type: <c>cna_content_manager_load_foreign_ext</c> lets the asset's
    /// own type-reader table decide which reader runs, since a custom content type is by definition
    /// not one of the C++ types the typed loaders name. Folding it into <see cref="Load{T}"/> would
    /// mean claiming to dispatch on <c>T</c> while ignoring it.
    ///
    /// <b>Only compiled <c>.xnb</c> assets reach a registered reader.</b> A loose file or a
    /// <c>.cnj</c> descriptor is dispatched by requested C++ type instead, and there is none here,
    /// so such an asset fails rather than being read by the wrong reader.
    ///
    /// Results are cached by asset name exactly as every other load is, so a second call for the
    /// same name returns the same instance. <see cref="Unload"/> drops the cache.
    /// </summary>
    /// <typeparam name="T">The type the registered reader produces.</typeparam>
    /// <exception cref="ContentLoadException">If the reader produced something else -- a wrong
    /// registration is far easier to make than to notice, and an invalid cast deeper in the caller
    /// would not say which asset caused it.</exception>
    public T LoadForeign<T>(string assetName)
        where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(assetName);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // One cache with Load<T>, which is what the sentence above always claimed and what a caller
        // would assume: a custom reader's asset is an asset, and loading it twice under one name
        // should not produce two.
        string key = CanonicalAssetName(assetName);
        if (_loadedAssets.TryGetValue(key, out object? alreadyLoaded))
        {
            if (alreadyLoaded is T typedCache)
            {
                return typedCache;
            }

            throw new ContentLoadException(
                $"Content asset '{assetName}' was already loaded as {alreadyLoaded.GetType()}, not {typeof(T)}.");
        }

        nint produced = 0;
        CnaResult result = CnaStringMarshal.WithStringView(
            assetName,
            view => Native.cna_content_manager_load_foreign_ext(
                new CnaHandle(NativeHandleValue), view, out produced));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(LoadForeign));

        object? value = ContentTypeReaderRegistration.Resolve(produced);

        T typed = value as T ?? throw new ContentLoadException(
            value is null
            ? $"'{assetName}' loaded, but its reader produced no object."
            : $"'{assetName}' produced a {value.GetType()}, not a {typeof(T)}.");

        // A foreign reader's object is the whole asset: there is no intermediate
        // resource to collect, so the pending list is empty by construction.
        Track(key, typed, []);
        return typed;
    }

    /// <summary>
    /// The <c>Load&lt;Effect&gt;</c> route, added once <c>cna_content_manager_load_effect</c>
    /// existed. It reads all three shapes CNA supports -- a compiled <c>.xnb</c> Effect asset, a
    /// <c>.cnj</c> descriptor naming a stock effect, and a <c>.cnj</c> descriptor carrying custom
    /// shader source -- so the caller does not choose between them; the asset does.
    ///
    /// This was the largest single functional blocker on the "will an XNA game run" list: without
    /// it, every ported 3D game with a custom shader stopped at its first
    /// <c>Content.Load&lt;Effect&gt;</c>. <c>internal</c> rather than <c>protected</c> because
    /// Returns a raw <see cref="nint"/>, not a <c>CnaHandle</c>, and is <c>protected</c> like its
    /// three sibling loaders: CNA.XnaCompat has no <c>InternalsVisibleTo</c> grant into CNA.Interop
    /// and so can never name that type -- invariant 5. An <c>internal</c> member returning
    /// <c>CnaHandle</c> compiles here and is unusable from compat, which is how the first draft of
    /// this method failed.
    /// </summary>
    internal ulong LoadNativeEffectHandle(string assetName)
    {
        CnaHandle effect = CnaHandle.Zero;
        CnaResult result = CnaStringMarshal.WithStringView(
            assetName, view => Native.cna_content_manager_load_effect(new CnaHandle(NativeHandleValue), view, out effect));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Load));
        return effect.Value;
    }

    /// <summary>
    /// The raw pieces of a loaded <c>SpriteFont</c> asset, in exactly the shape
    /// <see cref="Graphics.SpriteFont"/>'s public constructor wants -- returned rather than an
    /// already-built <see cref="Graphics.SpriteFont"/> so <c>CNA.XnaCompat</c>'s
    /// <c>ContentManager</c> can build its own namespace's <c>SpriteFont</c> from the same native
    /// fetch, the same "return raw pieces, let each layer wrap its own type" split
    /// <see cref="LoadNativeTexture2DHandle"/> already uses for <c>Texture2D</c>.
    /// </summary>
    internal readonly record struct SpriteFontData(
        ulong TextureHandle,
        IReadOnlyList<Rectangle> GlyphBounds,
        IReadOnlyList<Rectangle> Cropping,
        IReadOnlyList<char> Characters,
        int LineSpacing,
        float Spacing,
        IReadOnlyList<Vector3> Kerning,
        char? DefaultCharacter);

    /// <summary>
    /// Parses a <c>SpriteFont</c> asset and uploads its atlas.
    ///
    /// This used to call <c>cna_content_load_spritefont</c>, a P/Invoke that names a function
    /// present in no header -- so every <c>Load&lt;SpriteFont&gt;</c> would have died with an
    /// <c>EntryPointNotFoundException</c>, and the doc comment above it asserted "there is nothing
    /// real to match" while <c>sprite_font.h</c> shipped an eight-function SpriteFont resource. A
    /// header audit found it.
    ///
    /// The C API then genuinely lacked a font *loader*, so the container was parsed here. It has
    /// one now -- <c>cna_content_manager_load_sprite_font</c> -- and that is the primary path,
    /// with the managed <c>.xnb</c> parser kept as the fallback.
    ///
    /// Native first, for a reason worth stating: parsing the container here meant the font the
    /// engine holds and the font this binding draws from were two objects, and only one of them
    /// could be authoritative for layout. Reading the glyph table back out of native's own font
    /// makes them the same numbers. The fallback still matters -- it handles a font this build of
    /// the engine cannot open, and it is the only path with no ABI dependency at all -- so it is a
    /// fallback rather than a deletion.
    ///
    /// The 256-glyph cap the old fabricated native shape imposed is gone either way; a real
    /// <c>.xnb</c> font is limited only by the file.
    /// </summary>
    internal SpriteFontData LoadSpriteFontData(string assetName)
    {
        if (TryLoadNativeSpriteFontData(assetName, out SpriteFontData native))
        {
            return native;
        }

        XnbSpriteFontData data = LoadXnbSpriteFontData(assetName);
        Texture2D texture = BuildAtlas(assetName, data.Texture);

        // Detached, not read: this method's contract is to hand back a raw handle its caller wraps
        // (so each layer can build its own namespace's Texture2D), and `texture` is a throwaway that
        // existed only to run SetData. Reading NativeHandleValue instead would leave two owners of
        // one handle, and the throwaway's critical finalizer would destroy a texture the SpriteFont
        // is still drawing from -- a use-after-free that only shows up at whatever GC happens to
        // collect it.
        return new SpriteFontData(
            texture.DetachNativeHandle(),
            data.GlyphBounds,
            data.Cropping,
            data.Characters,
            data.LineSpacing,
            data.Spacing,
            data.Kerning,
            data.DefaultCharacter);
    }

    /// <summary>
    /// Loads a font through <c>cna_content_manager_load_sprite_font</c> and reads its glyph table
    /// back out, or reports that this build could not.
    ///
    /// <b>Two owned handles come back, and the font is destroyed before returning.</b> That is
    /// deliberate, not a leak of the shorter-lived one: the atlas is retained for as long as a font
    /// uses it, so <c>cna_texture2d_destroy</c> refuses with <c>INVALID_STATE</c> while the font is
    /// alive. Keeping the native font would mean this binding's <see cref="Graphics.SpriteFont"/>
    /// -- which is a managed glyph table plus a texture -- had to own and order two handles for no
    /// gain, since every number it needs has already been copied out by then. Destroying the font
    /// releases the retention and leaves the atlas owned solely by the returned handle.
    ///
    /// Returns <see langword="false"/> rather than throwing when native cannot open the asset, so
    /// the managed <c>.xnb</c> parser still gets its turn -- a font this build of the engine does
    /// not understand is exactly the case the fallback exists for. A failure *after* the font
    /// loaded is a different matter and does throw: at that point the asset is readable and
    /// something else is wrong, and falling back would hide it.
    /// </summary>
    private unsafe bool TryLoadNativeSpriteFontData(string assetName, out SpriteFontData data)
    {
        data = default;

        CnaHandle font = CnaHandle.Zero;
        CnaHandle texture = CnaHandle.Zero;
        CnaResult load = CnaStringMarshal.WithStringView(
            assetName,
            view => Native.cna_content_manager_load_sprite_font(
                new CnaHandle(NativeHandleValue), view, out font, out texture));
        GC.KeepAlive(this);

        if (load.IsFailure())
        {
            return false;
        }

        try
        {
            var info = CnaSpriteFontInfo.Versioned();
            CnaResult infoResult = Native.cna_sprite_font_get_info(font, ref info);
            CnaException.ThrowIfFailed(infoResult, nameof(LoadSpriteFontData));

            int count = checked((int)info.CharacterCount);

            var glyphs = new CnaSpriteFontGlyph[count];
            for (int i = 0; i < count; i++)
            {
                glyphs[i] = CnaSpriteFontGlyph.Versioned();
            }

            var characters = new char[count];

            if (count > 0)
            {
                fixed (CnaSpriteFontGlyph* glyphPtr = glyphs)
                {
                    CnaResult glyphResult = Native.cna_sprite_font_copy_glyphs(
                        font, glyphPtr, (ulong)count, out _);
                    CnaException.ThrowIfFailed(glyphResult, nameof(LoadSpriteFontData));
                }

                fixed (char* charPtr = characters)
                {
                    CnaResult charResult = Native.cna_sprite_font_copy_characters(
                        font, (ushort*)charPtr, (ulong)count, out _);
                    CnaException.ThrowIfFailed(charResult, nameof(LoadSpriteFontData));
                }
            }

            var bounds = new Rectangle[count];
            var cropping = new Rectangle[count];
            var kerning = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                CnaSpriteFontGlyph glyph = glyphs[i];
                bounds[i] = new Rectangle(
                    glyph.GlyphBounds.X, glyph.GlyphBounds.Y, glyph.GlyphBounds.Width, glyph.GlyphBounds.Height);
                cropping[i] = new Rectangle(
                    glyph.Cropping.X, glyph.Cropping.Y, glyph.Cropping.Width, glyph.Cropping.Height);
                kerning[i] = new Vector3(glyph.Kerning.X, glyph.Kerning.Y, glyph.Kerning.Z);
            }

            data = new SpriteFontData(
                texture.Value,
                bounds,
                cropping,
                characters,
                info.LineSpacing,
                info.Spacing,
                kerning,
                info.HasDefaultCharacter != 0 ? (char)info.DefaultCharacter : null);

            return true;
        }
        finally
        {
            // Before the caller can ever destroy the atlas, and unconditionally: an exception on
            // the way out must not leave the font holding the texture hostage.
            Native.cna_sprite_font_destroy(font);
        }
    }

    /// <summary>
    /// Uploads a parsed atlas as a real <see cref="Texture2D"/>.
    ///
    /// Only mip level 0 is uploaded. <see cref="Texture2D"/> has no per-level <c>SetData</c>
    /// overload in this binding, and a font atlas is sampled at its native size by
    /// <c>SpriteBatch.DrawString</c>, so the lower levels would never be read. Stated here rather
    /// than silently dropped, because a font compiled with mipmaps is a real input.
    ///
    /// The returned wrapper is a throwaway whose handle the caller detaches -- see
    /// <see cref="LoadSpriteFontData"/>.
    /// </summary>
    private Texture2D BuildAtlas(string assetName, XnbTextureData data)
    {
        GraphicsDevice device = RequireGraphicsDevice<Graphics.SpriteFont>(assetName);

        if (data.Format != SurfaceFormat.Color)
        {
            throw new ContentLoadException(
                $"'{assetName}' has a {data.Format} font atlas. Only {SurfaceFormat.Color} is supported here, " +
                "because that is the only format this binding can upload through Texture2D.SetData.");
        }

        var texture = new Texture2D(device, data.Width, data.Height);
        texture.SetData(data.MipLevels[0]);
        return texture;
    }
}
