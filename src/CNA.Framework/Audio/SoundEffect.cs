using CNA.Interop;

namespace CNA.Audio;

/// <summary>
/// A loaded, native-backed sound resource, now matching the real, shipped openeggbert/cna C API
/// (<c>audio.h</c>) rather than a self-designed guess -- see <c>NEXT.md</c>'s native-ABI-migration
/// entry, step 9. Creation now needs a game handle (<see cref="CnaAmbientGame.Current"/>, the
/// design this project selected back in step 2 specifically for this) -- no parameterless audio
/// route exists anywhere in the real ABI.
///
/// <see cref="Play()"/> and <see cref="Play(float,float,float)"/> are real. They were previously
/// absent, on the recorded grounds that fire-and-forget playback "relies on XNA's internal instance
/// pool ... which this repository has no equivalent for" -- but <c>audio.h:483</c> and <c>:499</c>
/// are exactly that pool, including the <c>out_played</c> flag that carries the "instance limit
/// reached, returns false" behaviour the note named as the missing part. A header audit found it.
/// The same audit found the four process-wide settings
/// (<see cref="MasterVolume"/>/<see cref="DistanceScale"/>/<see cref="DopplerScale"/>/
/// <see cref="SpeedOfSound"/>) sitting unbound at <c>audio.h:408-471</c>.
/// </summary>
public class SoundEffect : IDisposable
{
    private readonly NativeResourceHandle _handle;
    private TimeSpan? _disposedDuration;

    public SoundEffect(byte[] buffer, int sampleRate, AudioChannels channels)
        : this(ValidateBasicBuffer(buffer), 0, buffer?.Length ?? 0, sampleRate, channels, loopStart: 0, loopLength: 0)
    {
    }

    /// <summary>
    /// <paramref name="buffer"/> must be headerless, little-endian, signed 16-bit PCM samples --
    /// not a WAV/RIFF file and not an XNB asset, matching real XNA's own constructor and the real
    /// ABI's own documented requirement for <c>cna_sound_effect_create_pcm16_range_ext</c> exactly
    /// (real XNA's own 7-argument constructor and this real ABI's "canonical seven-argument
    /// constructor" turned out to already match). Only validates <paramref name="sampleRate"/> is
    /// positive -- real XNA additionally restricts it to the 8,000-48,000 Hz range with its own
    /// <see cref="ArgumentOutOfRangeException"/>, which isn't reproduced here (lower confidence in
    /// the exact bounds than in the rest of this constructor's validation, so this deliberately
    /// validates less rather than risk enforcing the wrong limits). <paramref name="loopStart"/>/
    /// <paramref name="loopLength"/> are only checked for being non-negative, not for actually
    /// fitting within the sample count implied by <paramref name="count"/> -- the native side is
    /// the one place that can validate a loop region's own units (samples) against the buffer
    /// without duplicating its channel/bit-depth interpretation here.
    /// </summary>
    public unsafe SoundEffect(
        byte[] buffer, int offset, int count, int sampleRate, AudioChannels channels, int loopStart, int loopLength)
    {
        // Owned SoundEffect handle. XNA validates in this exact order before it allocates a voice:
        // format, whole-buffer alignment, range, then loop region. Keep this managed so failures
        // are deterministic even when no native game exists yet.
        ValidatePcmArguments(buffer, offset, count, sampleRate, channels, loopStart, loopLength);

        var createInfo = new CnaSoundEffectCreateInfo
        {
            SampleRate = (uint)sampleRate,
            Channels = (uint)channels,
        };

        fixed (byte* basePtr = buffer)
        {
            CnaResult result = Native.cna_sound_effect_create_pcm16_range_ext(
                CnaAmbientGame.Current, in createInfo, basePtr, (ulong)buffer.Length, offset, count, loopStart, loopLength,
                out CnaHandle handle);
            CnaException.ThrowIfFailed(result, nameof(SoundEffect));
            _handle = new NativeResourceHandle(handle.Value, ReleaseNative);
        }
    }

    /// <summary>Wraps an already-created native handle -- used by <c>ContentManager.Load&lt;T&gt;</c>,
    /// same pattern as <c>Texture2D</c>'s equivalent constructor.</summary>
    protected internal SoundEffect(ulong nativeHandleValue)
    {
        _handle = new NativeResourceHandle(nativeHandleValue, ReleaseNative);
    }

    private static bool ReleaseNative(ulong handleValue) =>
        Native.cna_sound_effect_destroy(new CnaHandle(handleValue)).IsSuccess();

    internal ulong NativeHandleValue => _handle.DangerousGetHandle();

    /// <summary>Hands this effect's handle to a new owner, leaving this object inert. Used by
    /// CNA.XnaCompat's own <c>FromStream</c>, which cannot reuse the base factory's result because
    /// a compat <c>SoundEffect</c> is a separate class -- see
    /// <see cref="NativeResourceHandle.Detach"/> for why transferring beats sharing.</summary>
    internal ulong DetachNativeHandle() => _handle.Detach();

    /// <summary>
    /// A caller-set label. Real XNA's <c>SoundEffect.Name</c>, and unlike
    /// <c>GraphicsResource.Name</c> this one really does cross the ABI --
    /// <c>cna_sound_effect_set_name</c> exists and this type holds its own handle, so there is
    /// nothing in the way.
    /// </summary>
    public unsafe string Name
    {
        get
        {
            string value = NativeStringReader.Read(
                Native.cna_sound_effect_get_name_size,
                Native.cna_sound_effect_copy_name,
                new CnaHandle(NativeHandleValue),
                nameof(Name));
            GC.KeepAlive(this);
            return value;
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            CnaResult result = CnaStringMarshal.WithStringView(
                value, view => Native.cna_sound_effect_set_name(new CnaHandle(NativeHandleValue), view));
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(Name));
        }
    }

    /// <summary>Whether this effect has been disposed. Read from native rather than tracked here,
    /// so it stays true when something else disposed the underlying effect.</summary>
    public bool IsDisposed
    {
        get
        {
            CnaResult result = Native.cna_sound_effect_get_is_disposed(new CnaHandle(NativeHandleValue), out byte disposed);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsDisposed));
            return disposed != 0;
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (_disposedDuration is { } saved)
            {
                return saved;
            }

            CnaResult result = Native.cna_sound_effect_get_duration_ticks(new CnaHandle(NativeHandleValue), out long ticks);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(Duration));
            return TimeSpan.FromTicks(ticks);
        }
    }

    /// <summary>
    /// Matches real XNA's <c>SoundEffect.FromStream</c>: decodes a strict PCM WAV stream
    /// (8/16-bit, mono/stereo, 8000-48000 Hz) into a sound effect. ABI 0.59 retired the
    /// permissive decoder extension; other encoded formats are refused by the native parser.
    ///
    /// The whole stream is read into memory first, because
    /// <c>cna_sound_effect_create_from_encoded_ext</c> decodes from a contiguous block rather than
    /// from a reader -- the same shape <c>Texture2D.FromStream</c> has.
    /// </summary>
    public static unsafe SoundEffect FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] encoded = buffer.ToArray();

        CnaHandle soundEffect;
        fixed (byte* encodedPtr = encoded)
        {
            CnaResult result = Native.cna_sound_effect_create_from_encoded_ext(
                CnaAmbientGame.Current, encodedPtr, (ulong)encoded.Length, out soundEffect);
            CnaException.ThrowIfFailed(result, nameof(FromStream));
        }

        return new SoundEffect(soundEffect.Value);
    }

    public SoundEffectInstance CreateInstance() => new(CreateNativeInstanceHandle());

    /// <summary>
    /// Plays once, with no instance to control it. Returns <see langword="false"/> when the engine
    /// already has too many instances playing, matching real XNA -- that is an ordinary answer, not
    /// an error, and so is a disposed effect answering <see langword="false"/>.
    /// </summary>
    public bool Play()
    {
        CnaResult result = Native.cna_sound_effect_play(new CnaHandle(NativeHandleValue), out byte played);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Play));
        return played != 0;
    }

    /// <summary>
    /// Plays once with explicit settings. The canonical asymmetry is preserved and is native's:
    /// <paramref name="pitch"/> is clamped, while <paramref name="pan"/> outside [-1, 1] is
    /// rejected. Reproducing the pan check here as well would give the caller a
    /// <see cref="ArgumentOutOfRangeException"/> rather than a native result, which is what
    /// <see cref="SoundEffectInstance.Pan"/> already does for the same reason.
    /// </summary>
    public bool Play(float volume, float pitch, float pan)
    {
        if (pan is < -1f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(pan), pan, "Must be between -1 and 1.");
        }

        CnaResult result = Native.cna_sound_effect_play_with_settings(
            new CnaHandle(NativeHandleValue), volume, pitch, pan, out byte played);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Play));
        return played != 0;
    }

    /// <summary>The process-wide playback volume, in [0, 1].</summary>
    public static float MasterVolume
    {
        get => GetGlobal(Native.cna_sound_effect_get_master_volume, nameof(MasterVolume));
        set => SetGlobal(Native.cna_sound_effect_set_master_volume, value, nameof(MasterVolume));
    }

    /// <summary>Scales the distance between an <see cref="AudioListener"/> and an
    /// <see cref="AudioEmitter"/> for 3D attenuation.</summary>
    public static float DistanceScale
    {
        get => GetGlobal(Native.cna_sound_effect_get_distance_scale, nameof(DistanceScale));
        set => SetGlobal(Native.cna_sound_effect_set_distance_scale, value, nameof(DistanceScale));
    }

    /// <summary>Scales the Doppler effect applied to 3D playback.</summary>
    public static float DopplerScale
    {
        get => GetGlobal(Native.cna_sound_effect_get_doppler_scale, nameof(DopplerScale));
        set => SetGlobal(Native.cna_sound_effect_set_doppler_scale, value, nameof(DopplerScale));
    }

    /// <summary>The speed of sound used for Doppler, in units per second.</summary>
    public static float SpeedOfSound
    {
        get => GetGlobal(Native.cna_sound_effect_get_speed_of_sound, nameof(SpeedOfSound));
        set => SetGlobal(Native.cna_sound_effect_set_speed_of_sound, value, nameof(SpeedOfSound));
    }

    private delegate CnaResult GetGlobalFunc(CnaHandle game, out float outValue);

    private delegate CnaResult SetGlobalFunc(CnaHandle game, float value);

    /// <summary>These four are static in XNA but game-addressed in the ABI, so they read the
    /// ambient game the same way <c>Keyboard</c>/<c>Mouse</c> do -- see
    /// <c>CnaAmbientGame</c>.</summary>
    private static float GetGlobal(GetGlobalFunc getter, string propertyName)
    {
        CnaResult result = getter(CnaAmbientGame.Current, out float value);
        CnaException.ThrowIfFailed(result, propertyName);
        return value;
    }

    private static void SetGlobal(SetGlobalFunc setter, float value, string propertyName)
    {
        CnaResult result = setter(CnaAmbientGame.Current, value);
        CnaException.ThrowIfFailed(result, propertyName);
    }

    /// <summary>
    /// Creates the native playable-instance handle without wrapping it. <c>internal</c> (visible
    /// to CNA.XnaCompat via the assembly's <c>InternalsVisibleTo</c> grant) so CNA.XnaCompat's
    /// <c>SoundEffect.CreateInstance</c> override can wrap the *same* native call's result in its
    /// own <c>SoundEffectInstance</c> type -- calling <see cref="CreateInstance"/> itself and then
    /// re-wrapping its return value would wrap the same handle twice, double-releasing it on
    /// disposal. Same pattern as <c>RenderTarget2D.CreateNativeHandle</c>.
    /// </summary>
    internal ulong CreateNativeInstanceHandle()
    {
        CnaResult result = Native.cna_sound_effect_create_instance(new CnaHandle(NativeHandleValue), out CnaHandle instance);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(CreateInstance));
        return instance.Value;
    }

    /// <summary>Pure arithmetic (16-bit-PCM byte size to <see cref="TimeSpan"/>), no native call --
    /// real, testable today, same as the math value types. Rounds down to a whole sample count
    /// (a partial trailing sample has no duration of its own), matching how PCM sample counting
    /// actually works.</summary>
    public static TimeSpan GetSampleDuration(int sizeInBytes, int sampleRate, AudioChannels channels)
    {
        if (sizeInBytes < 0)
        {
            throw new ArgumentException("The audio buffer size cannot be negative.", nameof(sizeInBytes));
        }

        ValidateSampleRate(sampleRate);
        ValidateChannels(channels);

        if (sizeInBytes == 0)
        {
            return TimeSpan.Zero;
        }

        int blockAlign = 2 * (int)channels;
        int sampleCount = sizeInBytes / blockAlign;
        float milliseconds = (float)sampleCount * 1000f / (float)sampleRate;
        return TimeSpan.FromMilliseconds((double)milliseconds);
    }

    /// <summary><see cref="AudioChannels"/> is a plain enum, so C# does not itself reject a cast
    /// like <c>(AudioChannels)0</c> -- without this check, that value would make
    /// <see cref="GetSampleDuration"/> divide by zero and <see cref="GetSampleSizeInBytes"/>
    /// silently return 0 for any duration, instead of failing with a clear argument error.
    /// </summary>
    private static void ValidateChannels(AudioChannels channels)
    {
        if (channels is not (AudioChannels.Mono or AudioChannels.Stereo))
        {
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Must be Mono or Stereo.");
        }
    }

    /// <summary>The inverse of <see cref="GetSampleDuration"/> -- also pure arithmetic. Rounds the
    /// sample count down, then back up to a whole block (sample-aligned across all channels),
    /// matching 16-bit PCM's fixed per-sample byte size.</summary>
    public static int GetSampleSizeInBytes(TimeSpan duration, int sampleRate, AudioChannels channels)
    {
        double totalMilliseconds = duration.TotalMilliseconds;
        if (totalMilliseconds < 0d || totalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        ValidateSampleRate(sampleRate);
        ValidateChannels(channels);

        if (duration == TimeSpan.Zero)
        {
            return 0;
        }

        try
        {
            int sampleCount = checked((int)(totalMilliseconds * ((float)sampleRate / 1000f)));
            int blockAlign = 2 * (int)channels;
            return checked((sampleCount + sampleCount % (int)channels) * blockAlign);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }
    }

    private static byte[] ValidateBasicBuffer(byte[]? buffer)
    {
        if (buffer is null || buffer.Length == 0)
        {
            throw new ArgumentException("The audio buffer cannot be null or empty.");
        }

        return buffer;
    }

    private static void ValidateSampleRate(int sampleRate)
    {
        if (sampleRate is < 8000 or > 48000)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }
    }

    private static void ValidatePcmArguments(
        byte[]? buffer,
        int offset,
        int count,
        int sampleRate,
        AudioChannels channels,
        int loopStart,
        int loopLength)
    {
        ValidateSampleRate(sampleRate);
        ValidateChannels(channels);

        int blockAlign = 2 * (int)channels;
        if (buffer is null || buffer.Length == 0 || buffer.Length % blockAlign != 0)
        {
            throw new ArgumentException("The audio buffer is invalid.");
        }

        if (offset < 0 || offset >= buffer.Length || offset % blockAlign != 0)
        {
            throw new ArgumentException("The audio buffer offset is invalid.");
        }

        int end;
        try
        {
            end = checked(offset + count);
        }
        catch (OverflowException)
        {
            throw new ArgumentException("The offset and count do not describe a valid buffer range.");
        }

        if (count <= 0 || end > buffer.Length || count % blockAlign != 0)
        {
            throw new ArgumentException("The offset and count do not describe a valid buffer range.");
        }

        int loopEnd;
        try
        {
            loopEnd = checked(loopStart + loopLength);
        }
        catch (OverflowException)
        {
            throw new ArgumentException("The loop region is invalid.");
        }

        int sampleCount = count / blockAlign;
        if (loopStart < 0 || loopLength < 0 || loopEnd > sampleCount)
        {
            throw new ArgumentException("The loop region is invalid.");
        }
    }

    public void Dispose()
    {
        if (!_handle.IsClosed)
        {
            CnaResult result = Native.cna_sound_effect_get_duration_ticks(
                new CnaHandle(NativeHandleValue), out long ticks);
            GC.KeepAlive(this);
            if (result.IsSuccess())
            {
                _disposedDuration = TimeSpan.FromTicks(ticks);
            }
        }

        _handle.Dispose();
        GC.SuppressFinalize(this);
    }
}
