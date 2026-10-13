namespace CNA.Graphics;

/// <summary>
/// One capability of the active renderer -- <c>graphics.h</c>'s <c>CNA_GRAPHICS_CAPABILITY_*</c>
/// identities.
///
/// Not an XNA type. XNA had no capability query at all: it had two fixed profiles, Reach and HiDef,
/// and every device that reported a profile supported everything in it. CNA ships renderers that
/// genuinely differ -- SDL_RENDERER is 2D-only and says so -- so "what profile is this" and "can
/// this device draw a triangle" are separate questions here.
///
/// Asking is worth it because the alternative is finding out from a draw call. A recognized but
/// unavailable capability is a <em>successful</em> query that answers false; operations needing it
/// then fail with <c>NOT_SUPPORTED</c> rather than silently substituting something else.
/// </summary>
public enum GraphicsCapability : uint
{
    /// <summary>3D draw submission. False on a 2D-only renderer, where
    /// <c>DrawUserPrimitives</c> and friends throw.</summary>
    ThreeD = 0,

    DepthStencilBuffer = 1,

    MultiSampleAntiAliasing = 2,

    /// <summary>More than one simultaneous render target.</summary>
    MultipleRenderTargets = 3,

    AnisotropicFiltering = 4,

    WireFrame = 5,

    OcclusionQuery = 6,

    /// <summary>Custom effects built from shader <em>source</em>. Separate from
    /// <see cref="CompiledEffects"/>, which is about pre-compiled bytecode.</summary>
    CustomEffects = 7,

    Texture3D = 8,

    MultiStreamVertexInput = 9,

    Instancing = 10,

    StencilBuffer = 11,

    AdditiveBlending = 12,

    /// <summary>Compiled Effect Framework bytecode -- what
    /// <see cref="Effect(GraphicsDevice, byte[])"/> and <c>Load&lt;Effect&gt;</c> need for a
    /// compiled asset. The header recommends asking this in advance rather than branching on a
    /// file name, since only the compiled shape depends on it.</summary>
    CompiledEffects = 13,

    // The three below were added by CNA 0.8, which moved CNA_GRAPHICS_CAPABILITY_MAXIMUM from 13 to
    // 18. That constant move is the reason 0.8 was not a generally additive ABI generation, and
    // this enum stopped at 13 through three ABI admissions afterwards -- a query route was bound,
    // and the things it can answer were unreachable from managed code. None of them has an XNA
    // counterpart; they are here for the same reason the rest of the enum is, which is that a
    // renderer either has them or does not and a game should be able to ask before it depends on
    // one. CNA 0.8 also added compute shaders (17) and indirect draws (18); CNA 0.47 retired both
    // with the rest of its modern GPU layer, and their values stay reserved.

    /// <summary>32-bit-per-channel float render targets.</summary>
    FloatRenderTargets = 14,

    /// <summary>16-bit-per-channel half-float render targets.</summary>
    HalfFloatRenderTargets = 15,

    /// <summary>Linear filtering when sampling a half-float texture, which some renderers that
    /// accept the format still cannot do.</summary>
    HalfFloatTextureLinearFiltering = 16,
}
