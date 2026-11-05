using CNA.Interop;

namespace CNA.Graphics;

/// <summary>
/// Matches real XNA's <c>OcclusionQuery</c>: counts the pixels that survived depth testing between
/// <see cref="Begin"/> and <see cref="End"/>, so a game can skip work for geometry that turned out
/// to be hidden.
///
/// The result is not available immediately -- the GPU finishes the query some frames later, which
/// is why <see cref="IsComplete"/> exists and why reading <see cref="PixelCount"/> before it is
/// true is a caller error rather than a blocking wait.
/// </summary>
public class OcclusionQuery : IDisposable
{
    private readonly NativeResourceHandle _handle;

    public OcclusionQuery(GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        CnaResult result = Native.cna_occlusion_query_create(graphicsDevice.ResolveNativeDeviceHandle(), out CnaHandle query);
        CnaException.ThrowIfFailed(result, nameof(OcclusionQuery));

        GraphicsDevice = graphicsDevice;
        _handle = new NativeResourceHandle(query.Value, h => Native.cna_occlusion_query_destroy(new CnaHandle(h)).IsSuccess());
    }

    public GraphicsDevice GraphicsDevice { get; }

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

    public void Begin()
    {
        CnaResult result = Native.cna_occlusion_query_begin(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Begin));
    }

    public void End()
    {
        CnaResult result = Native.cna_occlusion_query_end(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(End));
    }

    public bool IsComplete
    {
        get
        {
            // Dispose releases the C token as well as the native GPU query. ABI 0.60/XNA
            // define this one post-disposal answer as false; never query a released token.
            if (_handle.IsClosed)
            {
                return false;
            }

            CnaResult result = Native.cna_occlusion_query_get_is_complete(NativeHandle, out byte value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsComplete));
            return value != 0;
        }
    }

    /// <summary>Only meaningful once <see cref="IsComplete"/> is <see langword="true"/>; the
    /// native call reports a failure otherwise rather than blocking, matching real XNA's own
    /// "throws if the query is not complete" contract.</summary>
    public int PixelCount
    {
        get
        {
            CnaResult result = Native.cna_occlusion_query_get_pixel_count(NativeHandle, out int value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PixelCount));
            return value;
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
        GC.SuppressFinalize(this);
    }
}
