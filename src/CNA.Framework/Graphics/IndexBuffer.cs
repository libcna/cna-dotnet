using CNA.Interop;

namespace CNA.Graphics;

/// <summary>
/// A native-backed GPU index buffer, now matching the real, shipped openeggbert/cna C API
/// (<c>index_resources.h</c>) rather than a self-designed guess -- see <c>NEXT.md</c>'s
/// native-ABI-migration entry, step 4. Both the <see cref="IndexElementSize"/>-taking and real
/// XNA's <c>Type</c>-taking constructors are implemented, the latter deriving element size from
/// <c>typeof(short)</c>/<c>typeof(ushort)</c>/<c>typeof(int)</c>/<c>typeof(uint)</c>, same
/// "convenience sugar over the same native call" shape as <see cref="VertexBuffer"/>'s equivalent.
///
/// Unlike <see cref="VertexBuffer"/>, <see cref="SetData{T}(T[])"/>/<see cref="GetData{T}(T[])"/>
/// stay fully generic for any 2- or 4-byte unmanaged <c>T</c> -- confirmed directly with
/// <c>cnabinding</c>: <c>cna_index_buffer_set_data</c>/<c>_get_data</c> only ever select a 16- or
/// 32-bit width, with no built-in-type enumeration the way vertex transfer has, because CNA's own
/// C++ <c>IndexBuffer</c> only ever stores <c>uint16_t</c>/<c>uint32_t</c> elements -- a width
/// selector really is the whole story for an index type, not a narrowing the way it is for
/// vertices. Neither real function has an offset parameter into the *native* buffer, though (only
/// a window into the *caller's* own array) -- always operating on native index zero -- so, same as
/// <see cref="VertexBuffer.SetData{T}(int,T[],int,int,int)"/>, a nonzero
/// <c>offsetInBytes</c>-equivalent throws.
/// </summary>
public class IndexBuffer : IDisposable
{
    private readonly NativeResourceHandle _handle;

    public IndexBuffer(GraphicsDevice graphicsDevice, Type indexType, int indexCount, BufferUsage bufferUsage)
        : this(graphicsDevice, SizeForType(indexType), indexCount, bufferUsage)
    {
    }

    public IndexBuffer(GraphicsDevice graphicsDevice, IndexElementSize indexElementSize, int indexCount, BufferUsage bufferUsage)
        : this(graphicsDevice, indexElementSize, indexCount, bufferUsage, dynamic: false)
    {
    }

    /// <summary>The one real constructor -- see <see cref="VertexBuffer"/>'s equivalent for why
    /// <paramref name="dynamic"/> is a constructor flag rather than a separate native
    /// resource.</summary>
    protected internal IndexBuffer(
        GraphicsDevice graphicsDevice, IndexElementSize indexElementSize, int indexCount, BufferUsage bufferUsage, bool dynamic)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(indexCount);

        IndexElementSize = indexElementSize;
        IndexCount = indexCount;
        BufferUsage = bufferUsage;

        var createInfo = new CnaIndexBufferCreateInfo
        {
            IndexCount = indexCount,
            IndexElementSize = (uint)indexElementSize,
            BufferUsage = (uint)bufferUsage,
            Dynamic = (byte)(dynamic ? 1 : 0),
        };

        CnaResult result = Native.cna_index_buffer_create(graphicsDevice.ResolveNativeDeviceHandle(), in createInfo, out CnaHandle handle);
        CnaException.ThrowIfFailed(result, nameof(IndexBuffer));

        _handle = new NativeResourceHandle(handle.Value, h => Native.cna_index_buffer_destroy(new CnaHandle(h)).IsSuccess());
    }

    /// <summary>Matches real XNA/MonoGame's own <c>Type</c>-to-<see cref="IndexElementSize"/>
    /// mapping exactly (message text recalled from memory, not independently verified -- same
    /// flag as <see cref="VertexDeclaration.FromType"/>'s own). <c>internal</c> rather than
    /// <c>private</c> specifically so it's directly testable without needing a real
    /// <c>cna-native</c> to exercise the constructor it feeds.</summary>
    internal static IndexElementSize SizeForType(Type indexType)
    {
        ArgumentNullException.ThrowIfNull(indexType);

        if (indexType == typeof(short) || indexType == typeof(ushort))
        {
            return IndexElementSize.SixteenBits;
        }

        if (indexType == typeof(int) || indexType == typeof(uint) ||
            indexType == typeof(float) || indexType == typeof(bool))
        {
            return IndexElementSize.ThirtyTwoBits;
        }

        throw new ArgumentOutOfRangeException(
            nameof(indexType), indexType,
            "Index buffers can only be created for types that are sixteen or thirty two bits in length.");
    }

    internal ulong NativeHandleValue => _handle.DangerousGetHandle();

    public IndexElementSize IndexElementSize { get; }

    public int IndexCount { get; }

    public BufferUsage BufferUsage { get; }

    public void SetData<T>(T[] data) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        SetData(0, data, 0, data.Length);
    }

    public unsafe void SetData<T>(int offsetInBytes, T[] data, int startIndex, int elementCount) where T : struct
        => SetDataWithOptions(offsetInBytes, data, startIndex, elementCount, 0);

    internal unsafe void SetDataWithOptions<T>(
        int offsetInBytes,
        T[] data,
        int startIndex,
        int elementCount,
        uint options)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offsetInBytes);
        if (data.Length == 0)
        {
            throw new ArgumentNullException(nameof(data));
        }

        BufferRangeValidation.ValidateRange(data.Length, startIndex, elementCount);

        if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new ArgumentException($"Index element type {typeof(T)} contains managed references.", nameof(data));
        }

        int sourceElementSize = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        int nativeElementSize = IndexElementSize == CNA.Graphics.IndexElementSize.SixteenBits ? 2 : 4;
        long byteCount = (long)sourceElementSize * elementCount;
        long capacity = (long)nativeElementSize * IndexCount;
        if ((long)offsetInBytes + byteCount > capacity)
        {
            throw new InvalidOperationException("The data does not fit in this IndexBuffer.");
        }

        if (byteCount % nativeElementSize != 0 || offsetInBytes % nativeElementSize != 0)
        {
            throw new NotSupportedException(
                "The CNA IndexBuffer ABI can transfer only complete native 16-bit or 32-bit indices.");
        }

        ulong nativeElementCount = (ulong)(byteCount / nativeElementSize);

        // A windowed upload carries no streaming hint, because upstream refuses one: "a windowed
        // upload preserves the rest of the buffer, so it accepts no SetDataOptions other than
        // None". XNA does accept both there, and refusing the call outright -- which is what this
        // did -- breaks the commonest use of the overload there is, a sprite or geometry batcher
        // rewriting one slice per frame with NoOverwrite.
        //
        // Dropping the hint on that route changes cost, not result. NoOverwrite promises only that
        // the caller will not touch what the GPU is still reading, so ignoring it may stall and
        // cannot corrupt. Discard says the rest of the buffer may become undefined, and this route
        // preserves it instead -- a stronger guarantee than the caller asked for. The vertex family
        // documents the same trade in its own header.
        uint windowedOptions = offsetInBytes == 0 ? options : 0;
        var transfer = new CnaIndexBufferTransfer
        {
            IndexElementSize = (uint)IndexElementSize,
            Options = windowedOptions,
            StartIndex = 0,
            ElementCount = nativeElementCount,
        };

        System.Runtime.InteropServices.GCHandle pinned =
            System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            byte* source = (byte*)pinned.AddrOfPinnedObject() + ((long)startIndex * sourceElementSize);
            // A nonzero offsetInBytes threw here until cna_index_buffer_set_data_at landed. The
            // plain route replaces the buffer's whole contents -- which is what XNA's
            // SetData(T[], int, int) does -- so the two are different calls, not one with a default.
            //
            // Note what stays the same across both: transfer.StartIndex indexes the *caller's*
            // array, as everywhere else in this ABI. Only offsetInBytes indexes the buffer. Getting
            // those two confused is how a slice update silently writes the wrong region.
            CnaResult result;
            if (offsetInBytes == 0)
            {
                result = Native.cna_index_buffer_set_data(
                    new CnaHandle(NativeHandleValue), in transfer, source, nativeElementCount);
            }
            else
            {
                CnaIndexBufferTransfer windowed = transfer;
                result = Native.cna_index_buffer_set_data_at(
                    new CnaHandle(NativeHandleValue), (ulong)offsetInBytes, &windowed,
                    source, nativeElementCount);
            }

            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(SetData));
        }
        finally
        {
            pinned.Free();
        }
    }

    public void GetData<T>(T[] data) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        GetData(0, data, 0, data.Length);
    }

    public unsafe void GetData<T>(int offsetInBytes, T[] data, int startIndex, int elementCount) where T : struct
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offsetInBytes);
        if (data.Length == 0)
        {
            throw new ArgumentNullException(nameof(data));
        }

        BufferRangeValidation.ValidateRange(data.Length, startIndex, elementCount);

        if (System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            throw new ArgumentException($"Index element type {typeof(T)} contains managed references.", nameof(data));
        }

        int destinationElementSize = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        int nativeElementSize = IndexElementSize == CNA.Graphics.IndexElementSize.SixteenBits ? 2 : 4;
        long byteCount = (long)destinationElementSize * elementCount;
        long capacity = (long)nativeElementSize * IndexCount;
        if ((long)offsetInBytes + byteCount > capacity)
        {
            throw new InvalidOperationException("The requested data does not fit in this IndexBuffer.");
        }

        if (byteCount % nativeElementSize != 0 || offsetInBytes % nativeElementSize != 0)
        {
            throw new NotSupportedException(
                "The CNA IndexBuffer ABI can transfer only complete native 16-bit or 32-bit indices.");
        }

        ulong nativeElementCount = (ulong)(byteCount / nativeElementSize);
        ulong nativeSkip = (ulong)(offsetInBytes / nativeElementSize);
        var transfer = new CnaIndexBufferTransfer
        {
            IndexElementSize = (uint)IndexElementSize,
            Options = 0,
            StartIndex = 0,
            ElementCount = nativeElementCount + nativeSkip,
        };

        if (nativeSkip == 0)
        {
            System.Runtime.InteropServices.GCHandle pinned =
                System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                CnaResult result = Native.cna_index_buffer_get_data(
                    new CnaHandle(NativeHandleValue), in transfer,
                    (byte*)pinned.AddrOfPinnedObject() + ((long)startIndex * destinationElementSize),
                    nativeElementCount, out _);
                GC.KeepAlive(this);
                CnaException.ThrowIfFailed(result, nameof(GetData));
            }
            finally
            {
                pinned.Free();
            }

            return;
        }

        // A nonzero offsetInBytes used to throw: cna_index_buffer_get_data always starts at native
        // index zero, so there is no route that begins partway in. Reading the prefix as well and
        // keeping the tail costs one temporary buffer and gets the caller the bytes XNA gives them.
        // The read stays atomic -- the destination is written only after the native call returns.
        var staging = new byte[(long)(nativeElementCount + nativeSkip) * nativeElementSize];
        fixed (byte* stagingPtr = staging)
        {
            CnaResult result = Native.cna_index_buffer_get_data(
                new CnaHandle(NativeHandleValue), in transfer, stagingPtr,
                nativeElementCount + nativeSkip, out _);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(GetData));

            System.Runtime.InteropServices.GCHandle pinned =
                System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                Buffer.MemoryCopy(
                    stagingPtr + offsetInBytes,
                    (byte*)pinned.AddrOfPinnedObject() + ((long)startIndex * destinationElementSize),
                    byteCount,
                    byteCount);
            }
            finally
            {
                pinned.Free();
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>The overridable half of disposal, so <see cref="DynamicIndexBuffer"/> can release its
    /// <c>ContentLost</c> subscription before the buffer handle that subscription is registered
    /// against goes away.</summary>
    protected virtual void Dispose(bool disposing) => _handle.Dispose();
}
