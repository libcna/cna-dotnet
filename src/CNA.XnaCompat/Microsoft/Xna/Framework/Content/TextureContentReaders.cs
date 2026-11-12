namespace Microsoft.Xna.Framework.Content;

using Microsoft.Xna.Framework.Graphics;

/// <summary>
/// The texture readers, for a texture that arrives <em>inside</em> another asset.
///
/// A top-level <c>Load&lt;Texture2D&gt;</c> goes to CNA's own content loader and never reaches
/// here. A texture nested in something else does: a game's reflectively serialized settings class
/// with a <c>Texture2D</c> field, a custom model that carries its own textures, a sky box. Measured
/// against the compiled content of the XNA 4.0 sample collection, two of the samples whose root
/// asset is a game type need a nested <c>Texture2DReader</c>, and without one the whole asset fails.
///
/// The formats are transcribed from the decompiled XNA 4.0 readers. Mip levels are a byte count
/// followed by that many bytes, per level -- and for a cube, per face then per level, in that
/// order, which is the part worth stating because the other order also produces a plausible cube.
/// </summary>
internal sealed class Texture2DContentReader : ContentTypeReader<Texture2D>
{
    protected internal override Texture2D Read(ContentReader input, Texture2D existingInstance)
    {
        ArgumentNullException.ThrowIfNull(input);

        var format = (SurfaceFormat)input.ReadInt32();
        int width = input.ReadInt32();
        int height = input.ReadInt32();
        int levelCount = ContentTextureLevels.Count(input, input.ReadInt32());

        var texture = new Texture2D(
            GraphicsContentHelper.GraphicsDeviceFromContentReader(input),
            width,
            height,
            levelCount > 1,
            format);

        for (int level = 0; level < levelCount; level++)
        {
            byte[] data = ContentTextureLevels.ReadLevel(input);
            texture.SetData(level, null, data, 0, data.Length);
        }

        return texture;
    }
}

/// <summary>See <see cref="Texture2DContentReader"/>.</summary>
internal sealed class TextureCubeContentReader : ContentTypeReader<TextureCube>
{
    protected internal override TextureCube Read(ContentReader input, TextureCube existingInstance)
    {
        ArgumentNullException.ThrowIfNull(input);

        var format = (SurfaceFormat)input.ReadInt32();
        int size = input.ReadInt32();
        int levelCount = ContentTextureLevels.Count(input, input.ReadInt32());

        var texture = new TextureCube(
            GraphicsContentHelper.GraphicsDeviceFromContentReader(input),
            size,
            levelCount > 1,
            format);

        // Face-major, then level. Reversing the two loops reads the same bytes into the wrong
        // faces and produces a cube that looks built but is scrambled.
        for (CubeMapFace face = CubeMapFace.PositiveX; face <= CubeMapFace.NegativeZ; face++)
        {
            for (int level = 0; level < levelCount; level++)
            {
                byte[] data = ContentTextureLevels.ReadLevel(input);
                texture.SetData(face, level, null, data, 0, data.Length);
            }
        }

        return texture;
    }
}

/// <summary>See <see cref="Texture2DContentReader"/>. The box shrinks with each mip level, which is
/// why the dimensions are recomputed rather than read again.</summary>
internal sealed class Texture3DContentReader : ContentTypeReader<Texture3D>
{
    protected internal override Texture3D Read(ContentReader input, Texture3D existingInstance)
    {
        ArgumentNullException.ThrowIfNull(input);

        var format = (SurfaceFormat)input.ReadInt32();
        int width = input.ReadInt32();
        int height = input.ReadInt32();
        int depth = input.ReadInt32();
        int levelCount = ContentTextureLevels.Count(input, input.ReadInt32());

        var texture = new Texture3D(
            GraphicsContentHelper.GraphicsDeviceFromContentReader(input),
            width,
            height,
            depth,
            levelCount > 1,
            format);

        for (int level = 0; level < levelCount; level++)
        {
            byte[] data = ContentTextureLevels.ReadLevel(input);
            texture.SetData(level, 0, 0, width, height, 0, depth, data, 0, data.Length);
            width = Math.Max(width >> 1, 1);
            height = Math.Max(height >> 1, 1);
            depth = Math.Max(depth >> 1, 1);
        }

        return texture;
    }
}

/// <summary>Shared level reading, so the three texture readers agree on what a corrupt one looks
/// like.</summary>
internal static class ContentTextureLevels
{
    internal static int Count(ContentReader input, int levelCount)
    {
        if (levelCount > 32)
        {
            throw new ContentLoadException(
                $"Content asset '{input.AssetName}' declares {levelCount} mip levels, which is not a possible chain.");
        }

        return levelCount;
    }

    internal static byte[] ReadLevel(ContentReader input) =>
        ReadExact(input, input.ReadInt32(), "mip level");

    /// <summary>
    /// Reads exactly <paramref name="byteCount"/> bytes, or reports the asset as truncated.
    ///
    /// <see cref="BinaryReader.ReadBytes(int)"/> returns a short array on a truncated stream rather
    /// than throwing, so without this a corrupt asset produces an undersized buffer that the next
    /// reader treats as data.
    /// </summary>
    internal static byte[] ReadExact(ContentReader input, int byteCount, string what)
    {
        if (byteCount < 0)
        {
            throw new ContentLoadException(
                $"Content asset '{input.AssetName}' declares a negative {what} size {byteCount}.");
        }

        byte[] data = input.ReadBytes(byteCount);
        if (data.Length != byteCount)
        {
            throw new ContentLoadException(
                $"Content asset '{input.AssetName}' is truncated: a {what} declared {byteCount} bytes and " +
                $"{data.Length} were available.");
        }

        return data;
    }
}
