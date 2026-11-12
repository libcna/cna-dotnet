namespace Microsoft.Xna.Framework.Content;

using System.Buffers.Binary;
using Microsoft.Xna.Framework.Audio;

/// <summary>
/// A compiled <c>SoundEffect</c>: a <c>WAVEFORMATEX</c> block, the PCM that follows it, a loop
/// region and the duration the pipeline measured.
///
/// <b>Two reasons this exists.</b> A sound nested inside another asset -- a game's own audio bank
/// class, a settings type holding its cues -- reaches the managed path, where there was nothing to
/// meet it. And a top-level <c>Load&lt;SoundEffect&gt;</c> now has two possible routes rather than
/// one, which is what makes it possible to tell a content-loading fault from a playback fault by
/// swapping them.
///
/// <b>Only linear PCM.</b> <c>wFormatTag</c> 1 is what the XNA pipeline emits for a wav, and it is
/// what this reads. ADPCM, WMA and the rest are refused by name rather than reinterpreted as PCM,
/// which would produce noise that sounds like a decoder bug somewhere else entirely.
/// </summary>
internal sealed class SoundEffectContentReader : ContentTypeReader<SoundEffect>
{
    private const int WaveFormatPcm = 1;

    protected internal override SoundEffect Read(ContentReader input, SoundEffect existingInstance)
    {
        ArgumentNullException.ThrowIfNull(input);

        int formatSize = input.ReadInt32();
        byte[] format = ContentTextureLevels.ReadExact(input, formatSize, "sound format block");

        int dataSize = input.ReadInt32();
        byte[] data = ContentTextureLevels.ReadExact(input, dataSize, "sound data");

        _ = input.ReadInt32();  // loop start, in samples
        _ = input.ReadInt32();  // loop length, in samples
        int durationMilliseconds = input.ReadInt32();

        if (format.Length < 16)
        {
            throw new ContentLoadException(
                $"Content asset '{input.AssetName}' has a {format.Length}-byte wave format block; " +
                "WAVEFORMATEX is at least sixteen.");
        }

        var block = format.AsSpan();
        int formatTag = BinaryPrimitives.ReadUInt16LittleEndian(block);
        int channels = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        int sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        int bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(block[14..]);

        if (formatTag != WaveFormatPcm)
        {
            throw new ContentLoadException(
                $"Content asset '{input.AssetName}' is wave format {formatTag}, and this reader " +
                "handles linear PCM (1) only.");
        }

        if (bitsPerSample != 16)
        {
            throw new ContentLoadException(
                $"Content asset '{input.AssetName}' is {bitsPerSample}-bit PCM, and XNA's SoundEffect " +
                "buffer contract is sixteen.");
        }

        if (channels is not (1 or 2))
        {
            throw new ContentLoadException(
                $"Content asset '{input.AssetName}' has {channels} channels; a SoundEffect is mono or stereo.");
        }

        return new SoundEffect(data, sampleRate, (AudioChannels)channels,
            TimeSpan.FromMilliseconds(durationMilliseconds));
    }
}
