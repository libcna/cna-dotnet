using CNA.Graphics;
using CNA.Interop;

namespace CNA.Media;

/// <summary>
/// Matches real XNA's <c>VideoPlayer</c>: plays a <see cref="Video"/> and hands each decoded frame
/// back as a texture through <see cref="GetTexture"/>, which the game draws itself (typically with
/// <see cref="SpriteBatch"/>) -- XNA never composites video for you.
///
/// Created against the *game*, not a graphics device (<c>cna_video_player_create</c> takes a game
/// handle), which is why the constructor takes no argument and uses the ambient game handle, the
/// same way <c>Keyboard</c>/<c>Mouse</c>/<c>TouchPanel</c> do.
/// </summary>
public class VideoPlayer : IDisposable
{
    // Owned player handle. GetTexture returns a ParentOwned borrowed alias, never another owner;
    // the ABI guarantees that alias only until the next call made on this player.
    private readonly NativeResourceHandle _handle;
    private Video? _video;

    public VideoPlayer()
    {
        CnaResult result = Native.cna_video_player_create(CnaAmbientGame.Current, out CnaHandle player);
        CnaException.ThrowIfFailed(result, nameof(VideoPlayer));

        _handle = new NativeResourceHandle(player.Value, h => Native.cna_video_player_destroy(new CnaHandle(h)).IsSuccess());
    }

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

    /// <summary>The video currently loaded, or <see langword="null"/> before the first
    /// <see cref="Play"/>. Tracked managed-side: the native getter reports a handle this project
    /// cannot map back to the managed <see cref="Video"/> wrapper that owns it -- the same
    /// limitation <c>TextureCollection</c> documents -- so returning the object the caller passed
    /// to <see cref="Play"/> is both correct and what XNA callers expect (reference
    /// equality).</summary>
    public Video? Video => _video;

    public MediaState State
    {
        get
        {
            CnaResult result = Native.cna_video_player_get_state(NativeHandle, out uint value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(State));
            return (MediaState)value;
        }
    }

    public bool IsLooped
    {
        get
        {
            CnaResult result = Native.cna_video_player_get_is_looped(NativeHandle, out byte value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsLooped));
            return value != 0;
        }
        set
        {
            CnaResult result = Native.cna_video_player_set_is_looped(NativeHandle, (byte)(value ? 1 : 0));
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsLooped));
        }
    }

    public bool IsMuted
    {
        get
        {
            CnaResult result = Native.cna_video_player_get_is_muted(NativeHandle, out byte value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsMuted));
            return value != 0;
        }
        set
        {
            CnaResult result = Native.cna_video_player_set_is_muted(NativeHandle, (byte)(value ? 1 : 0));
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsMuted));
        }
    }

    public float Volume
    {
        get
        {
            CnaResult result = Native.cna_video_player_get_volume(NativeHandle, out float value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(Volume));
            return value;
        }
        set
        {
            CnaResult result = Native.cna_video_player_set_volume(NativeHandle, value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(Volume));
        }
    }

    public TimeSpan PlayPosition
    {
        get
        {
            CnaResult result = Native.cna_video_player_get_play_position_ticks(NativeHandle, out long ticks);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(PlayPosition));
            return TimeSpan.FromTicks(ticks);
        }
    }

    public void Play(Video video)
    {
        ArgumentNullException.ThrowIfNull(video);
        CnaResult result = Native.cna_video_player_play(NativeHandle, video.NativeHandle);
        GC.KeepAlive(video);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Play));
        _video = video;
    }

    public void Stop()
    {
        CnaResult result = Native.cna_video_player_stop(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Stop));
    }

    public void Pause()
    {
        CnaResult result = Native.cna_video_player_pause(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Pause));
    }

    public void Resume()
    {
        CnaResult result = Native.cna_video_player_resume(NativeHandle);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Resume));
    }

    /// <summary>
    /// The current frame, or <see langword="null"/> when an opened video has no frame available.
    /// Reading before successful playback throws <see cref="InvalidOperationException"/>, as
    /// XNA does. Stop retains the last decoded frame; it does not restore the pre-play state.
    ///
    /// The returned texture wraps a handle the *player* owns. The C API states it is "valid only
    /// until the next call on this player", so it must not be cached across frames -- and, more
    /// importantly, the wrapper is deliberately <em>non-owning</em>: an owning
    /// <see cref="Texture2D"/> here would let <see cref="System.Runtime.InteropServices.SafeHandle"/>'s
    /// critical finalizer destroy the player's own frame texture on the next GC, whether or not the
    /// caller disposed it. A code-review pass caught exactly that.
    ///
    /// Returns the <see cref="Texture"/> base rather than <see cref="Texture2D"/>, unlike real
    /// XNA. That is forced by WP3's texture reparenting: <c>CNA.Graphics.Texture2D</c> and
    /// <c>Microsoft.Xna.Framework.Graphics.Texture2D</c> are now siblings under
    /// <see cref="Texture"/>, not one deriving from the other, so no single <c>Texture2D</c> type
    /// can be the return type both namespaces need. CNA.XnaCompat's own <c>VideoPlayer</c> narrows
    /// this back to its <c>Texture2D</c>, so XNA-shaped game code is unaffected.
    /// </summary>
    public Texture? GetTexture()
    {
        CnaResult result = Native.cna_video_player_get_texture(NativeHandle, out CnaHandle texture, out byte available);
        GC.KeepAlive(this);
        ThrowIfFrameReadFailed(result, nameof(GetTexture));

        // _video is non-null whenever native reports a frame: a frame only exists after Play(),
        // which is the only thing that sets it.
        if (available == 0 || _video is null)
        {
            return null;
        }

        // The device comes from the Video the caller handed to Play() -- deliberately not a fresh
        // wrapper around the ambient game's device, which would give the frame texture a second
        // GraphicsDevice object with its own cached state (see Video.GraphicsDevice).
        return CreateFrameTexture(_video!.GraphicsDevice, texture.Value);
    }

    /// <summary>
    /// The frame and its generation, behind <see cref="CnaVideoPlayerExtensions.GetCnaFrame"/>.
    ///
    /// <c>internal</c> and reached through an extension method rather than being a public member
    /// here: this type is the strict XNA <c>VideoPlayer</c>'s implementation and the metadata
    /// verifier compares it member for member, so a frame generation -- which XNA has no notion of
    /// -- cannot appear on it.
    /// </summary>
    internal CnaVideoFrame ReadFrame()
    {
        CnaVideoFrameExt frame = ReadNativeFrame();
        Texture? texture = frame.Available != 0 && _video is not null
            ? CreateFrameTexture(_video.GraphicsDevice, frame.Texture.Value)
            : null;

        return new CnaVideoFrame(texture, frame.Generation, frame.PresentationTime, frame.Available != 0);
    }

    private CnaVideoFrameExt ReadNativeFrame()
    {
        var frame = CnaVideoFrameExt.Versioned();
        CnaResult result = Native.cna_video_player_get_frame_ext(NativeHandle, ref frame);
        GC.KeepAlive(this);
        ThrowIfFrameReadFailed(result, nameof(ReadFrame));
        return frame;
    }

    private static void ThrowIfFrameReadFailed(CnaResult result, string operation)
    {
        try
        {
            CnaException.ThrowIfFailed(result, operation);
        }
        catch (CnaException failure) when (failure.CanonicalExceptionType == "System.InvalidOperationException")
        {
            // Use the native exception identity, not every InvalidState result: other frame
            // failures (including a released player token) retain their existing diagnostics.
            throw new InvalidOperationException(failure.NativeMessage ?? failure.Message, failure);
        }
    }

    /// <summary>Covariant-return factory hook so CNA.XnaCompat can hand back its own
    /// <c>Texture2D</c> -- same pattern as <c>GraphicsDevice.QueryBlendState</c>.</summary>
    protected virtual Texture CreateFrameTexture(GraphicsDevice graphicsDevice, ulong nativeHandleValue) =>
        Texture2D.CreateBorrowed(graphicsDevice, nativeHandleValue);

    /// <summary>Whether this player has been disposed. Read from native rather than tracked here,
    /// so it stays true when something else disposed the underlying player.</summary>
    public bool IsDisposed
    {
        get
        {
            CnaResult result = Native.cna_video_player_get_is_disposed(NativeHandle, out byte disposed);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsDisposed));
            return disposed != 0;
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
        GC.SuppressFinalize(this);
    }
}
