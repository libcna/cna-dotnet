namespace CNA.Interop;

/// <summary>
/// Mirrors <c>cnb.h</c>'s <c>CNA_CnbTextureFormat</c> exactly (<c>cnb.h:1867-1923</c>): the storage
/// format one representation of a CNB texture is stored in.
///
/// Every identity is declared, including the ones CNA's own encoder never writes. Decoding accepts
/// all of them, and a decoder that only knew the encodable subset would be unable to read a file
/// another tool produced -- which is the whole reason a container format has more formats than one
/// writer emits.
/// </summary>
internal enum CnaCnbTextureFormat : uint
{
    Unknown = 0,
    Rgba8 = 1,
    Bgra8 = 2,
    Rgba8Srgb = 3,
    Bgr565 = 4,
    Bgra5551 = 5,
    Bgra4444 = 6,
    Alpha8 = 7,
    R8 = 8,
    R16 = 9,
    Rg16 = 10,
    Rgba16 = 11,
    Rg8Snorm = 12,
    Rgba8Snorm = 13,
    Rgb10A2 = 14,
    R32Float = 15,
    Rg32Float = 16,
    Rgba32Float = 17,
    R16Float = 18,
    Rg16Float = 19,
    Rgba16Float = 20,
    HdrBlendable = 21,
    Bc1 = 22,
    Bc2 = 23,
    Bc3 = 24,
    Bc3Srgb = 25,
    Bc7 = 26,
    Bc7Srgb = 27,
}

/// <summary>
/// Mirrors <c>graphics.h</c>'s <c>CNA_RENDERER_FORMAT_USAGE_*</c> bits exactly
/// (<c>graphics.h:394-416</c>): what a renderer can do with one surface format.
///
/// Used in pairs. <c>cna_graphics_device_get_surface_format_support_ext</c> answers a *known* mask
/// and a *supported* mask, and the header is explicit that a bit absent from the known mask means
/// unknown rather than unsupported. Collapsing the two would turn "this renderer has not classified
/// BC7" into "this renderer refuses BC7", which is a different and much more confident claim.
/// </summary>
[Flags]
internal enum CnaRendererFormatUsage : uint
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
