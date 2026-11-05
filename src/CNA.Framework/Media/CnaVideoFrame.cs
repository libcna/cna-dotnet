using CNA.Graphics;
using CNA.Interop;

namespace CNA.Media;

/// <summary>
/// One decoded video frame, together with the identity that says whether it is the same frame as
/// last time.
///
/// <b>Why this exists.</b> XNA's <c>VideoPlayer</c> owns two stable <c>Texture2D</c> frame buffers
/// and hands the same objects back over and over, so a game can compare references to notice a new
/// frame. CNA's <c>GetTexture</c> returns a borrowed alias valid only until the next call on the
/// player, and no reference comparison means anything against it -- which was recorded as a native
/// blocker for XNA parity.
///
/// <see cref="Generation"/> is CNA's answer to the second half of that: not stable identity, but an
/// explicit validity generation. Equal across two reads means the same pixels; higher means the
/// frame advanced. It is monotonic for the player's whole life and is never restarted -- not by
/// <c>Stop</c>, not by playing a different video -- because restarting it would give the first
/// frame of a second playback the same value as the first frame of the first, and inequality would
/// stop meaning "changed".
///
/// <b>This does not make a frame cacheable.</b> The texture is borrowed on exactly the terms
/// <c>GetTexture</c> documents and expires on the next call to the player, generation or no
/// generation. What the generation removes is the need to re-upload or re-compare an unchanged
/// frame.
/// </summary>
public readonly struct CnaVideoFrame
{
    internal CnaVideoFrame(Texture? texture, ulong generation, double presentationTime, bool available)
    {
        Texture = texture;
        Generation = generation;
        PresentationTime = presentationTime;
        IsAvailable = available;
    }

    /// <summary>The frame texture, borrowed from the player, or <see langword="null"/> when no
    /// frame exists.</summary>
    public Texture? Texture { get; }

    /// <summary>Frames decoded since the player was created; zero before the first.</summary>
    public ulong Generation { get; }

    /// <summary>The held frame's presentation timestamp in seconds, or negative when there is
    /// none.</summary>
    public double PresentationTime { get; }

    /// <summary>Whether a frame texture exists at all. Distinct from <see cref="Texture"/> being
    /// non-null, which also requires a video to have been played.</summary>
    public bool IsAvailable { get; }
}

/// <summary>
/// CNA's additions to <see cref="VideoPlayer"/>, outside the strict XNA contract.
///
/// XNA's <c>VideoPlayer</c> has no frame generation, so this cannot live on the strict facade --
/// the metadata verifier compares that type member for member against XNA's own.
/// </summary>
public static class CnaVideoPlayerExtensions
{
    /// <summary>
    /// Reads the current frame and its generation in one call.
    ///
    /// One call rather than a texture getter plus a generation getter, because the two must describe
    /// the same instant: the texture expires on the next call to the player, so asking separately
    /// would mean the generation could belong to a frame the caller no longer holds.
    /// </summary>
    /// <exception cref="InvalidOperationException">No video has successfully played.</exception>
    /// <exception cref="CnaException">Other native frame-read failures, including a released player.</exception>
    public static CnaVideoFrame GetCnaFrame(this VideoPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.ReadFrame();
    }
}
