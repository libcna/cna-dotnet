using System.IO;

namespace Microsoft.Xna.Framework.Content;

/// <summary>
/// Reads one XNB object graph for <see cref="ContentManager"/>.
///
/// XNA exposes this as a sealed <see cref="BinaryReader"/> subclass, not as an adapter around a
/// backend reader. That distinction matters to user supplied <see cref="ContentTypeReader"/>
/// implementations: they inherit the complete binary primitive surface and can recursively read
/// objects from the same stream without crossing an implementation namespace.
/// </summary>
public sealed class ContentReader : BinaryReader
{
    private readonly ContentManager _contentManager;
    private readonly string _assetName;
    private readonly int _version;
    private readonly char _platform;
    private readonly Action<IDisposable>? _recordDisposableObject;

    private ContentTypeReader[] _typeReaders = [];
    private int[] _typeReaderVersions = [];
    private int _sharedResourceCount;
    private readonly List<KeyValuePair<int, Action<object>>> _sharedResourceFixups = [];

    /// <summary>Constructed by the content manager after the XNB container header was read.</summary>
    internal ContentReader(
        ContentManager contentManager,
        Stream input,
        string assetName,
        int version,
        char platform,
        Action<IDisposable>? recordDisposableObject)
        : base(input)
    {
        ArgumentNullException.ThrowIfNull(contentManager);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(assetName);

        _contentManager = contentManager;
        _assetName = assetName;
        _version = version;
        _platform = platform;
        _recordDisposableObject = recordDisposableObject;
    }

    /// <summary>Gets the content manager that is loading this asset.</summary>
    public ContentManager ContentManager => _contentManager;

    /// <summary>Gets the logical name of the asset currently being read.</summary>
    public string AssetName => _assetName;

    // XNA redeclares these BinaryReader primitives so custom readers see the same virtual slots
    // through ContentReader metadata as they do in the original framework.
    public override double ReadDouble() => base.ReadDouble();

    public override float ReadSingle() => base.ReadSingle();

    /// <summary>Reads a color in XNA's packed RGBA byte order.</summary>
    public Color ReadColor() => new(ReadByte(), ReadByte(), ReadByte(), ReadByte());

    /// <summary>Reads a matrix in row-major field order.</summary>
    public Matrix ReadMatrix() => new(
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle(),
        ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    /// <summary>Reads a quaternion as X, Y, Z, W.</summary>
    public Quaternion ReadQuaternion() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    /// <summary>Reads a two-component vector.</summary>
    public Vector2 ReadVector2() => new(ReadSingle(), ReadSingle());

    /// <summary>Reads a three-component vector.</summary>
    public Vector3 ReadVector3() => new(ReadSingle(), ReadSingle(), ReadSingle());

    /// <summary>Reads a four-component vector.</summary>
    public Vector4 ReadVector4() => new(ReadSingle(), ReadSingle(), ReadSingle(), ReadSingle());

    /// <summary>
    /// Reads a relative external asset reference. An empty reference represents the default value,
    /// as it does in XNA content files.
    ///
    /// The combine-and-clean rule lives in <see cref="CNA.Content.XnaContentPath"/> rather than
    /// here, so this path and <c>CNA.Framework</c>'s own XNB reader resolve a reference to the same
    /// asset name. The approximation this replaced split on separators and dropped empty segments,
    /// which answers <c>a\b</c> for XNA's <c>a\\b</c>, and used <see cref="Path.Combine(string, string)"/>,
    /// whose rooted-path rule differs between Windows and this host.
    /// </summary>
    public T ReadExternalReference<T>()
    {
        string? resolved = CNA.Content.XnaContentPath.Resolve(_assetName, ReadString());
        return resolved is null ? default! : _contentManager.Load<T>(resolved);
    }

    /// <summary>Reads an object selected by the next type-reader table index.</summary>
    public T ReadObject<T>() => InnerReadObject<T>(default!, hasExistingInstance: false);

    /// <summary>Reads an object selected by the next type-reader table index into an existing value.</summary>
    public T ReadObject<T>(T existingInstance) =>
        InnerReadObject(existingInstance, existingInstance is not null);

    /// <summary>
    /// Reads an object using the supplied reader or the stream-selected reader, as appropriate.
    ///
    /// This branched on nothing and always read the body inline, which is right for a value type
    /// and wrong for a reference type: XNA's own <c>ReadObjectInternal</c> takes the polymorphic
    /// route for those, and the object it reads carries a type-index prefix. Reading it inline
    /// consumes the prefix as data and misreads every byte after it. Nothing had caught it because
    /// this overload is reached from collection readers, and there were none.
    /// </summary>
    public T ReadObject<T>(ContentTypeReader typeReader)
    {
        ArgumentNullException.ThrowIfNull(typeReader);
        return typeReader.TargetType.IsValueType
            ? ReadAndRecord<T>(typeReader, default!, hasExistingInstance: false)
            : InnerReadObject<T>(default!, hasExistingInstance: false);
    }

    /// <summary>Reads an object using the supplied reader or the stream-selected reader as appropriate.</summary>
    public T ReadObject<T>(ContentTypeReader typeReader, T existingInstance)
    {
        ArgumentNullException.ThrowIfNull(typeReader);

        // Reference-type objects carry their reader tag in the stream. Value types are written
        // directly by their known reader, which is why XNA's overload branches here.
        //
        // Whether there is an existing instance is a runtime question about the boxed value, not a
        // compile-time one about T: XNA writes `if (existingInstance != null)`, and a caller that
        // passes null through a `T existingInstance` parameter -- which is exactly what the
        // reflective reader does for every writable member -- has no existing instance. Hardcoding
        // true here made the reader assert that an Int32 read had populated a null.
        return typeReader.TargetType.IsValueType
            ? ReadAndRecord(typeReader, existingInstance, existingInstance is not null)
            : InnerReadObject(existingInstance, existingInstance is not null);
    }

    /// <summary>Reads an untagged object body using the type reader for <typeparamref name="T"/>.</summary>
    public T ReadRawObject<T>()
    {
        ContentTypeReader typeReader = FindTypeReader(typeof(T));
        return ReadAndRecord<T>(typeReader, default!, hasExistingInstance: false);
    }

    /// <summary>Reads an untagged object body using an explicitly selected type reader.</summary>
    public T ReadRawObject<T>(ContentTypeReader typeReader)
    {
        ArgumentNullException.ThrowIfNull(typeReader);
        return ReadAndRecord<T>(typeReader, default!, hasExistingInstance: false);
    }

    /// <summary>Reads an untagged object body into an existing instance.</summary>
    public T ReadRawObject<T>(T existingInstance)
    {
        ContentTypeReader typeReader = FindTypeReader(typeof(T));
        return ReadAndRecord(typeReader, existingInstance, existingInstance is not null);
    }

    /// <summary>Reads an untagged object body using an explicit reader and existing instance.</summary>
    public T ReadRawObject<T>(ContentTypeReader typeReader, T existingInstance)
    {
        ArgumentNullException.ThrowIfNull(typeReader);
        return ReadAndRecord(typeReader, existingInstance, existingInstance is not null);
    }

    /// <summary>Records a deferred fix-up for a resource stored after the root object.</summary>
    public void ReadSharedResource<T>(Action<T> fixup)
    {
        ArgumentNullException.ThrowIfNull(fixup);

        int index = Read7BitEncodedInt32();
        if (index == 0)
        {
            return;
        }

        if (index < 1 || index > _sharedResourceCount)
        {
            throw new ContentLoadException(
                $"Content asset '{AssetName}' references shared resource {index}, but only {_sharedResourceCount} exist.");
        }

        _sharedResourceFixups.Add(new KeyValuePair<int, Action<object>>(index - 1, value =>
        {
            if (value is not T typed)
            {
                throw new ContentLoadException(
                    $"Content asset '{AssetName}' shared resource {index} is {value.GetType()}, not {typeof(T)}.");
            }

            fixup(typed);
        }));
    }

    internal T ReadAsset<T>(ContentTypeReaderManager typeReaderManager)
    {
        ArgumentNullException.ThrowIfNull(typeReaderManager);

        _typeReaders = typeReaderManager.LoadAssetReaders(this, out _typeReaderVersions);
        _sharedResourceCount = Read7BitEncodedInt32();
        if (_sharedResourceCount < 0)
        {
            throw new ContentLoadException($"Content asset '{AssetName}' has a negative shared resource count.");
        }

        T root = ReadObject<T>();
        ReadSharedResources();
        return root;
    }

    /// <summary>Available to built-in readers that need the serialized reader version.</summary>
    internal int GetTypeReaderVersion(ContentTypeReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        for (int index = 0; index < _typeReaders.Length; index++)
        {
            if (ReferenceEquals(_typeReaders[index], reader))
            {
                return _typeReaderVersions[index];
            }
        }

        throw new InvalidOperationException("The supplied content type reader is not in this asset's reader table.");
    }

    internal byte[] RetainTextureReadBuffer(byte[] data) =>
        _contentManager.RetainTextureReadBuffer(data);

    internal int Read7BitEncodedInt32() => Read7BitEncodedInt();

    /// <summary>
    /// How many readers this asset's own table declares.
    ///
    /// Exists for the media readers, which have to tell two wire forms apart -- see
    /// <c>SongContentReader</c>. It is the asset's own count, not a capability of this reader, so it
    /// is exposed rather than inferred from what has been read so far.
    /// </summary>
    internal int TypeReaderCount => _typeReaders.Length;

    private T InnerReadObject<T>(T existingInstance, bool hasExistingInstance)
    {
        int typeReaderIndex = Read7BitEncodedInt32();
        if (typeReaderIndex == 0)
        {
            return default!;
        }

        if (typeReaderIndex < 1 || typeReaderIndex > _typeReaders.Length)
        {
            throw new ContentLoadException(
                $"Content asset '{AssetName}' contains invalid type reader index {typeReaderIndex}.");
        }

        return ReadAndRecord(_typeReaders[typeReaderIndex - 1], existingInstance, hasExistingInstance);
    }

    private T ReadAndRecord<T>(ContentTypeReader typeReader, T existingInstance, bool hasExistingInstance)
    {
        T result = CastResult<T>(typeReader.Read(this, existingInstance));
        if (hasExistingInstance)
        {
            if (!ReferenceEquals(existingInstance, result))
            {
                throw new InvalidOperationException(
                    $"Content type reader '{typeReader.GetType()}' constructed a new instance " +
                    "instead of populating the supplied instance.");
            }
        }
        else if (!typeReader.TargetType.IsValueType && result is IDisposable disposable)
        {
            _recordDisposableObject?.Invoke(disposable);
        }

        return result;
    }

    private ContentTypeReader FindTypeReader(Type targetType)
    {
        foreach (ContentTypeReader typeReader in _typeReaders)
        {
            if (typeReader.TargetType == targetType)
            {
                return typeReader;
            }
        }

        throw new NotSupportedException($"The XNB reader table has no reader for '{targetType}'.");
    }

    private void ReadSharedResources()
    {
        if (_sharedResourceCount == 0)
        {
            return;
        }

        var sharedResources = new object?[_sharedResourceCount];
        for (int index = 0; index < sharedResources.Length; index++)
        {
            sharedResources[index] = InnerReadObject<object?>(null, hasExistingInstance: false);
        }

        foreach (KeyValuePair<int, Action<object>> fixup in _sharedResourceFixups)
        {
            object? resource = sharedResources[fixup.Key];
            if (resource is null)
            {
                throw new ContentLoadException(
                    $"Content asset '{AssetName}' shared resource {fixup.Key + 1} is null.");
            }

            fixup.Value(resource);
        }
    }

    private static T CastResult<T>(object? value)
    {
        if (value is T typed)
        {
            return typed;
        }

        if (value is null && default(T) is null)
        {
            return default!;
        }

        throw new ContentLoadException(
            $"A content type reader produced {value?.GetType().ToString() ?? "null"}, not {typeof(T)}.");
    }
}
