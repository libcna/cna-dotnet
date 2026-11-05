using CNA.Interop;

namespace CNA.Graphics;

/// <summary>
/// Matches real XNA's <c>EffectMaterial</c>: a per-material clone of an existing
/// <see cref="Effect"/>, carrying its own parameter values while sharing the source's shader.
///
/// Note the native constructor takes an effect to clone, not a device
/// (<c>cna_effect_material_create</c>) -- which is exactly what the type means, and why this has no
/// device-taking constructor unlike every other effect here.
/// </summary>
public class EffectMaterial : StockEffect
{
    public EffectMaterial(Effect cloneSource)
        : base(RequireDevice(cloneSource), CreateNative(cloneSource))
    {
    }

    private static GraphicsDevice RequireDevice(Effect cloneSource)
    {
        ArgumentNullException.ThrowIfNull(cloneSource);
        return cloneSource.GraphicsDevice;
    }

    private static CnaHandle CreateNative(Effect cloneSource)
    {
        ArgumentNullException.ThrowIfNull(cloneSource);

        // Reads the handle through Effect's own accessor rather than requiring a StockEffect:
        // CNA.XnaCompat's effects are natively backed but reach their handle by overriding that
        // accessor rather than by deriving from StockEffect, so a type test here would reject them
        // with a message claiming they have no native effect -- which a code-review pass caught.
        // A genuinely unbacked effect still fails, from the accessor's own NotSupportedException.
        CnaResult result = Native.cna_effect_material_create(
            new CnaHandle(cloneSource.NativeEffectHandleValue), out CnaHandle effect);
        CnaException.ThrowIfFailed(result, nameof(EffectMaterial));
        return effect;
    }

    /// <summary>ABI 0.60 documents EffectMaterial's inherited native base Effect clone.
    /// Preserve its techniques/parameters without claiming the result is another material.</summary>
    public override Effect Clone() => new Effect(GraphicsDevice, CloneNativeHandle().Value);
}
