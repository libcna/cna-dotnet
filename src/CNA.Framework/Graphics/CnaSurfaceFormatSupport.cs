namespace CNA.Graphics;

/// <summary>
/// What a renderer can do with one surface format, and how much of that it has actually classified.
///
/// The pair is the whole point. CNA answers a *known* mask and a *supported* mask, and
/// <c>graphics.h</c> is explicit that a bit absent from the known mask means **unknown**, not
/// unsupported: "Callers must not infer support or rejection for such a usage from the renderer name
/// or another usage bit." Collapsing the two into one boolean would turn "this renderer has not
/// classified BC7" into "this renderer refuses BC7" -- and the second is a much more confident claim
/// than CNA made.
/// </summary>
/// <param name="Known">Usages whose support has been classified.</param>
/// <param name="Supported">Usages that are supported; always a subset of <paramref name="Known"/>.</param>
public readonly record struct CnaSurfaceFormatSupport(CnaSurfaceFormatUsage Known, CnaSurfaceFormatUsage Supported)
{
    /// <summary>Whether <paramref name="usage"/> is classified *and* supported. A usage nobody has
    /// classified answers <see langword="false"/> here and <see langword="false"/> from
    /// <see cref="IsRefused"/> too -- the two questions are different and neither is the negation of
    /// the other.</summary>
    public bool IsSupported(CnaSurfaceFormatUsage usage) =>
        (Known & usage) == usage && (Supported & usage) == usage;

    /// <summary>Whether <paramref name="usage"/> is classified and *not* supported.</summary>
    public bool IsRefused(CnaSurfaceFormatUsage usage) =>
        (Known & usage) == usage && (Supported & usage) != usage;
}

/// <summary>What a renderer can do with a surface format -- CNA's own
/// <c>CNA_RENDERER_FORMAT_USAGE_*</c> bits.</summary>
[Flags]
public enum CnaSurfaceFormatUsage : uint
{
    None = 0,
    TextureStorage = 1u << 0,
    Sampled = 1u << 1,
    Filterable = 1u << 2,
    RenderTarget = 1u << 3,
    Blendable = 1u << 4,

    // Bits 5-7 named the storage-image usages of CNA's modern GPU layer. CNA 0.47 retired that
    // layer; the bits stay reserved and no renderer reports them.

    TransferSource = 1u << 8,
    TransferDestination = 1u << 9,
    Mipmapped = 1u << 10,
    MultiSample = 1u << 11,

    /// <summary>Transferring through a colour-shaped element. Bit 12, and the one this enum was
    /// first written without -- OPENGLES3 reports it in every format's known mask, so an enum that
    /// stopped at bit 11 printed the mask as a bare number.</summary>
    ColorTransfer = 1u << 12,
}
