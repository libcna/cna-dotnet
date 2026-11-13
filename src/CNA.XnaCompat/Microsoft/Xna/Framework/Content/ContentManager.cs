namespace Microsoft.Xna.Framework.Content;

/// <summary>
/// XNA 4.0-compatible content-manager facade. Its public type system is independent of
/// <c>CNA.Content</c>; native loading is delegated through an internal backend instead.
/// </summary>
public class ContentManager : IDisposable
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        CNA.Content.ContentManager,
        ContentManager> BackendFacades = new();

    private readonly IServiceProvider _serviceProvider;
    private WeakReference<byte[]>? _weakTextureReadBuffer;
    // Keep the buffer strong through nested reads, and weak between outermost reads.
    private byte[]? _textureReadBuffer;
    private int _textureReadDepth;
    private readonly Dictionary<string, object> _loadedAssets = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDisposable> _disposableAssets = [];
    private CNA.Content.ContentManager? _backend;
    private readonly bool _ownsBackend;
    private string _rootDirectory;
    private bool _disposed;

    public ContentManager(IServiceProvider serviceProvider)
        : this(serviceProvider, string.Empty)
    {
    }

    public ContentManager(IServiceProvider serviceProvider, string rootDirectory)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(rootDirectory);

        _serviceProvider = serviceProvider;
        _rootDirectory = rootDirectory;
        _ownsBackend = true;
    }

    /// <summary>Wraps the borrowed manager owned by a <see cref="Game"/>. The facade never
    /// destroys this backend; the game remains its sole native owner.</summary>
    internal ContentManager(CNA.Content.ContentManager backend, IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _backend = backend;
        _serviceProvider = serviceProvider;
        _rootDirectory = backend.RootDirectory;
        _ownsBackend = false;
        BackendFacades.Add(backend, this);
    }

    // XNA reuses a sufficiently large content buffer: capacity is not the next level's count.
    internal byte[] RetainTextureReadBuffer(byte[] data)
    {
        byte[]? buffer = _textureReadBuffer;
        if (buffer is null)
        {
            _weakTextureReadBuffer?.TryGetTarget(out buffer);
        }
        if (buffer is not null && buffer.Length >= data.Length)
        {
            data.CopyTo(buffer, 0);
            _textureReadBuffer = buffer;
            return buffer;
        }

        _textureReadBuffer = data;
        _weakTextureReadBuffer = new WeakReference<byte[]>(data);
        return data;
    }

    public string RootDirectory
    {
        get => _backend is null ? _rootDirectory : _backend.RootDirectory;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            ThrowIfDisposed();
            if (_loadedAssets.Count > 0)
            {
                throw new InvalidOperationException(
                    "The content root directory cannot be changed after an asset has been loaded.");
            }

            _rootDirectory = value;
            if (_backend is not null)
            {
                _backend.RootDirectory = value;
            }
        }
    }

    public IServiceProvider ServiceProvider => _serviceProvider;

    public virtual T Load<T>(string assetName)
    {
        if (string.IsNullOrEmpty(assetName))
        {
            throw new ArgumentNullException(nameof(assetName));
        }

        ThrowIfDisposed();

        // XNA let a game load on its own worker thread (a loading screen); CNA's handles belong to
        // the game thread, so the load runs there while the worker waits.
        return CNA.GameThread.IsCurrent ? LoadCached<T>(assetName) : CNA.GameThread.Invoke(() => LoadCached<T>(assetName));
    }

    private T LoadCached<T>(string assetName)
    {
        string key = assetName.Replace('\\', '/');
        if (_loadedAssets.TryGetValue(key, out object? cached))
        {
            if (cached is T typed)
            {
                return typed;
            }

            throw new ContentLoadException(
                $"Content asset '{assetName}' was already loaded as {cached.GetType()}, not {typeof(T)}.");
        }

        T result = ReadAsset<T>(assetName, RecordDisposableObject);
        _loadedAssets[key] = result!;
        return result;
    }

    public virtual void Unload()
    {
        ThrowIfDisposed();

        try
        {
            foreach (IDisposable disposable in _disposableAssets)
            {
                disposable.Dispose();
            }

            _backend?.Unload();
        }
        finally
        {
            // XNA clears both collections even when one disposable throws. This prevents a later
            // Unload from disposing the same prefix again and leaves the manager reusable.
            _disposableAssets.Clear();
            _loadedAssets.Clear();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (disposing)
            {
                // Unload before setting the disposed flag because Unload deliberately validates it.
                Unload();
            }
        }
        finally
        {
            _disposed = true;

            try
            {
                if (_ownsBackend)
                {
                    _backend?.Dispose();
                }
            }
            finally
            {
                _backend = null;
            }
        }
    }

    /// <summary>
    /// Opens an asset's <c>.xnb</c>, matching XNA's own <c>OpenStream</c> including the branch it
    /// takes for an absolute <see cref="RootDirectory"/>.
    ///
    /// That branch is not an edge case and used to be missing. XNA supports a root outside the
    /// title -- <c>RootDirectory</c>'s setter detects it and <c>OpenStream</c> then opens a plain
    /// <see cref="FileStream"/> instead of going through <see cref="TitleContainer"/>, whose whole
    /// job is to refuse paths that leave the title. Routing every open through
    /// <c>TitleContainer</c> made an absolute root fail with "takes a path relative to the title",
    /// which is correct advice for <c>TitleContainer</c> and wrong for <c>ContentManager</c>. It was
    /// previously recorded as a defect in the content survey, which happens to be the caller that
    /// uses an absolute root; the survey was right and this was wrong.
    /// </summary>
    protected virtual Stream OpenStream(string assetName)
    {
        ArgumentNullException.ThrowIfNull(assetName);

        try
        {
            string path = CNA.Content.XnaContentPath.ToFilePath(RootDirectory, assetName, ".xnb");
            return CNA.TitleContainer.IsPathAbsolute(RootDirectory)
                ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                : TitleContainer.OpenStream(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ContentLoadException($"Error loading \"{assetName}\". File not found.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new ContentLoadException($"Error loading \"{assetName}\". Cannot open file.", exception);
        }
    }

    protected T ReadAsset<T>(string assetName, Action<IDisposable>? recordDisposableObject)
    {
        if (string.IsNullOrEmpty(assetName))
        {
            throw new ArgumentNullException(nameof(assetName));
        }

        ThrowIfDisposed();

        // Derived loaders may call ReadAsset directly; serialize buffer reuse and uploads too.
        if (!CNA.GameThread.IsCurrent)
        {
            return CNA.GameThread.Invoke(() => ReadAsset<T>(assetName, recordDisposableObject));
        }

        _textureReadDepth++;
        try
        {
            bool managedXnb = !IsNativeBackedBuiltIn(typeof(T)) || OpensItsOwnStreams;
            T result = LoadCore<T>(assetName, recordDisposableObject);
            if (!managedXnb && result is IDisposable disposable)
            {
                recordDisposableObject?.Invoke(disposable);
            }

            return result;
        }
        catch (ContentLoadException)
        {
            throw;
        }
        catch (CNA.Content.ContentLoadException exception)
        {
            throw new ContentLoadException(exception.Message, exception);
        }
        catch (CNA.CnaException exception) when (exception.CanonicalExceptionType is
            "System.NotSupportedException" or
            "System.InvalidOperationException" or "System.ArgumentException" or "System.ArgumentOutOfRangeException"
            && AssetFileExists(assetName))
        {
            // XNA's ReadAsset wraps IO failures, but lets constructor/SetData failures through. It
            // opens the asset's stream first (OpenStream), so a missing file is always the
            // ContentLoadException below, whatever a native loader reports for it.
            throw XnaExceptions.Translate(exception);
        }
        catch (CNA.CnaException exception)
        {
            // Keep the content wrapper for remaining failures, including missing assets (CSX-133).
            throw new ContentLoadException(
                AssetFileExists(assetName)
                    ? $"Error loading \"{assetName}\"."
                    : $"Error loading \"{assetName}\". File not found.",
                exception);
        }
        finally
        {
            if (--_textureReadDepth == 0)
            {
                _textureReadBuffer = null;
            }
        }
    }

    private bool AssetFileExists(string assetName)
    {
        string path = CNA.Content.XnaContentPath.ToFilePath(RootDirectory, assetName, ".xnb");
        return File.Exists(CNA.TitleContainer.IsPathAbsolute(RootDirectory)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path));
    }

    private T LoadCore<T>(string assetName, Action<IDisposable>? recordDisposableObject)
    {
        // XNA content is table-driven. The native CNA loaders remain the best route for the
        // native-backed built-ins below, but a user-defined T has no CNA type identity to dispatch
        // on. Read those XNB files here through the real public ContentReader/ContentTypeReader
        // contract instead of requiring a non-XNA LoadForeign<T>() escape hatch.
        //
        // XNA reads every asset through OpenStream, so a manager overriding it -- ResourceContentManager,
        // or a game's own reading packed or encrypted content -- decides where a font or a texture
        // comes from as well (CSX-129). CNA's loader takes a path, so those read through the
        // managed readers too.
        if (!IsNativeBackedBuiltIn(typeof(T)) || OpensItsOwnStreams)
        {
            using Stream stream = OpenStream(assetName);
            return ManagedXnbContentLoader.Load<T>(this, stream, assetName, recordDisposableObject);
        }

        CNA.Content.ContentManager backend = GetBackend();

        if (typeof(T) == typeof(Graphics.Texture2D))
        {
            return (T)(object)new Graphics.Texture2D(
                RequireGraphicsDevice<T>(backend, assetName), backend.LoadNativeTexture2DHandle(assetName));
        }

        if (typeof(T) == typeof(Graphics.SpriteFont))
        {
            CNA.Content.ContentManager.SpriteFontData data = backend.LoadSpriteFontData(assetName);
            return (T)(object)new Graphics.SpriteFont(
                new Graphics.Texture2D(RequireGraphicsDevice<T>(backend, assetName), data.TextureHandle),
                Convert(data.GlyphBounds),
                Convert(data.Cropping),
                data.Characters,
                data.LineSpacing,
                data.Spacing,
                Convert(data.Kerning),
                data.DefaultCharacter);
        }

        if (typeof(T) == typeof(Graphics.TextureCube))
        {
            return (T)(object)new Graphics.TextureCube(
                RequireGraphicsDevice<T>(backend, assetName), backend.LoadNativeTextureCubeHandle(assetName));
        }

        if (typeof(T) == typeof(Audio.SoundEffect))
        {
            return (T)(object)new Audio.SoundEffect(backend.LoadNativeSoundEffectHandle(assetName));
        }

        if (typeof(T) == typeof(Graphics.Model))
        {
            Graphics.Model? model;
            try
            {
                model = LoadCompatModel(backend, assetName);
            }
            catch (CNA.Content.Xnb.XnbUnsupportedReaderException)
            {
                // CNA.Framework's parser knows XNA's readers only, and refuses before it creates
                // anything. A model naming one of the game's own -- a Tag of the game's type, as
                // HeightmapCollision's HeightMapInfo -- is read through the managed ContentReader,
                // which resolves it as XNA does.
                model = null;
            }

            if (model is null)
            {
                using Stream stream = OpenStream(assetName);
                return ManagedXnbContentLoader.Load<T>(this, stream, assetName, recordDisposableObject);
            }

            recordDisposableObject?.Invoke(model.OwnedResources);
            return (T)(object)model;
        }

        if (typeof(T) == typeof(Graphics.Effect))
        {
            Graphics.GraphicsDevice device = RequireGraphicsDevice<T>(backend, assetName);
            return (T)(object)Graphics.Effect.Adopt(
                device, new CNA.Graphics.Effect(device.Framework, backend.LoadNativeEffectHandle(assetName)));
        }

        throw new ContentLoadException($"Unsupported built-in content type {typeof(T)}.");
    }

    /// <summary>
    /// Whether an asset whose root type reader has this name loads through CNA's own content
    /// loader rather than the managed XNB path.
    ///
    /// For <c>tools/content-survey</c>. Such an asset needs no managed reader for anything in its
    /// table, so counting its nested readers as unresolvable would report a model or a font as
    /// unreadable when it loads perfectly well. Derived from the same list
    /// <see cref="IsNativeBackedBuiltIn"/> uses, so the two cannot disagree.
    /// </summary>
    internal static bool IsNativeBackedRootReaderForSurvey(string canonicalReaderName) =>
        canonicalReaderName switch
        {
            "Microsoft.Xna.Framework.Content.Texture2DReader" => IsNativeBackedBuiltIn(typeof(Graphics.Texture2D)),
            "Microsoft.Xna.Framework.Content.SpriteFontReader" => IsNativeBackedBuiltIn(typeof(Graphics.SpriteFont)),
            "Microsoft.Xna.Framework.Content.TextureCubeReader" => IsNativeBackedBuiltIn(typeof(Graphics.TextureCube)),
            "Microsoft.Xna.Framework.Content.SoundEffectReader" => IsNativeBackedBuiltIn(typeof(Audio.SoundEffect)),
            "Microsoft.Xna.Framework.Content.ModelReader" => IsNativeBackedBuiltIn(typeof(Graphics.Model)),
            "Microsoft.Xna.Framework.Content.EffectReader" => IsNativeBackedBuiltIn(typeof(Graphics.Effect)),
            _ => false,
        };

    private bool OpensItsOwnStreams =>
        ((Func<string, Stream>)OpenStream).Method.DeclaringType != typeof(ContentManager);

    private static bool IsNativeBackedBuiltIn(Type type) =>
        type == typeof(Graphics.Texture2D) ||
        type == typeof(Graphics.SpriteFont) ||
        type == typeof(Graphics.TextureCube) ||
        type == typeof(Audio.SoundEffect) ||
        type == typeof(Graphics.Model) ||
        type == typeof(Graphics.Effect);

    private CNA.Content.ContentManager GetBackend()
    {
        if (_backend is not null)
        {
            return _backend;
        }

        object? service = _serviceProvider.GetService(typeof(Graphics.IGraphicsDeviceService));
        if (service is not Graphics.IGraphicsDeviceService graphicsDeviceService ||
            graphicsDeviceService.GraphicsDevice is null)
        {
            throw new ContentLoadException(
                "No Microsoft.Xna.Framework.Graphics.IGraphicsDeviceService is available for content loading.");
        }

        _backend = CNA.Content.ContentManager.CreateOwned(graphicsDeviceService.GraphicsDevice.Framework, _rootDirectory);
        BackendFacades.Add(_backend, this);
        return _backend;
    }

    internal static ContentManager FromBackend(CNA.Content.ContentManager backend) =>
        BackendFacades.TryGetValue(backend, out ContentManager? facade)
            ? facade
            : throw new NotSupportedException(
                "The CNA content backend was not created through Microsoft.Xna.Framework.Content.ContentManager.");

    private static Graphics.GraphicsDevice RequireGraphicsDevice<T>(
        CNA.Content.ContentManager backend,
        string assetName) =>
        Graphics.GraphicsDevice.FromFramework(backend.GraphicsDevice) ?? throw new ContentLoadException(
            $"Cannot load {typeof(T).Name} '{assetName}': no compatible GraphicsDevice is available.");

    /// <returns>The model, or null when its tags hold values only the managed
    /// <see cref="ContentReader"/> constructs as XNA's types (see
    /// <see cref="Graphics.XnbCompatModelBuilder.TagsAreXnaTyped"/>).</returns>
    private Graphics.Model? LoadCompatModel(CNA.Content.ContentManager backend, string assetName)
    {
        Graphics.GraphicsDevice graphicsDevice = RequireGraphicsDevice<Graphics.Model>(backend, assetName);

        if (File.Exists(CNA.Content.XnaContentPath.ToTitleFilePath(RootDirectory, assetName, ".xnb")))
        {
            CNA.Content.Xnb.XnbModelData data = backend.LoadXnbModelData(assetName);
            return Graphics.XnbCompatModelBuilder.TagsAreXnaTyped(data)
                ? Graphics.XnbCompatModelBuilder.Build(graphicsDevice, data, this)
                : null;
        }

        if (File.Exists(CNA.Content.XnaContentPath.ToTitleFilePath(RootDirectory, assetName, ".cnj")))
        {
            return Graphics.CnjCompatModelBuilder.Build(graphicsDevice, backend.LoadCnjModelData(assetName));
        }

        throw new ContentLoadException(
            $"Content file '{assetName}' was not found (tried '{assetName}.xnb' and '{assetName}.cnj').");
    }

    private void RecordDisposableObject(IDisposable disposable)
    {
        // XNA records every reader result independently. If a malformed/custom reader returns the
        // same IDisposable for two object records, Unload consequently calls Dispose twice.
        _disposableAssets.Add(disposable);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static Rectangle[] Convert(IReadOnlyList<CNA.Rectangle> rectangles)
    {
        var result = new Rectangle[rectangles.Count];
        for (int i = 0; i < rectangles.Count; i++)
        {
            result[i] = rectangles[i].ToCompat();
        }

        return result;
    }

    private static Vector3[] Convert(IReadOnlyList<CNA.Vector3> vectors)
    {
        var result = new Vector3[vectors.Count];
        for (int i = 0; i < vectors.Count; i++)
        {
            result[i] = vectors[i].ToCompat();
        }

        return result;
    }
}
