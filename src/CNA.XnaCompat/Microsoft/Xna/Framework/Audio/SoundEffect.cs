namespace Microsoft.Xna.Framework.Audio;

/// <summary>XNA 4.0-compatible sound-effect facade backed by one CNA sound resource.</summary>
public sealed class SoundEffect : IDisposable
{
    private readonly CNA.Audio.SoundEffect _soundEffect;
    private readonly TimeSpan? _contentDuration;
    private readonly List<WeakReference<SoundEffectInstance>> _children = new();
    private bool _disposed;

    public SoundEffect(byte[] buffer, int sampleRate, AudioChannels channels)
    {
        _soundEffect = new CNA.Audio.SoundEffect(buffer, sampleRate, (CNA.Audio.AudioChannels)(int)channels);
    }

    public SoundEffect(
        byte[] buffer,
        int offset,
        int count,
        int sampleRate,
        AudioChannels channels,
        int loopStart,
        int loopLength)
    {
        _soundEffect = new CNA.Audio.SoundEffect(
            buffer, offset, count, sampleRate, (CNA.Audio.AudioChannels)(int)channels, loopStart, loopLength);
    }

    internal SoundEffect(
        byte[] buffer, int sampleRate, AudioChannels channels, TimeSpan duration)
        : this(buffer, sampleRate, channels)
    {
        _contentDuration = duration;
    }

    internal SoundEffect(ulong nativeHandleValue)
    {
        _soundEffect = new CNA.Audio.SoundEffect(nativeHandleValue);
    }

    ~SoundEffect()
    {
        Dispose(false);
    }

    public bool IsDisposed => _disposed || _soundEffect.IsDisposed;

    public string Name
    {
        get => _soundEffect.Name;
        set => _soundEffect.Name = value;
    }

    public TimeSpan Duration => _contentDuration ?? _soundEffect.Duration;

    public static float MasterVolume
    {
        get => CNA.Audio.SoundEffect.MasterVolume;
        set
        {
            ValidateRange(value, 0f, 1f, nameof(value));
            CNA.Audio.SoundEffect.MasterVolume = value;
        }
    }

    public static float SpeedOfSound
    {
        get => CNA.Audio.SoundEffect.SpeedOfSound;
        set
        {
            if (value <= 0f || float.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            CNA.Audio.SoundEffect.SpeedOfSound = value;
        }
    }

    public static float DopplerScale
    {
        get => CNA.Audio.SoundEffect.DopplerScale;
        set
        {
            if (value < 0f || float.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            CNA.Audio.SoundEffect.DopplerScale = value;
        }
    }

    public static float DistanceScale
    {
        get => CNA.Audio.SoundEffect.DistanceScale;
        set
        {
            if (value < 0f || float.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            CNA.Audio.SoundEffect.DistanceScale = Math.Max(value, float.Epsilon);
        }
    }

    public static SoundEffect FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using CNA.Audio.SoundEffect decoded =
            XnaExceptions.Guard(() => CNA.Audio.SoundEffect.FromStream(stream));
        return new SoundEffect(decoded.DetachNativeHandle());
    }

    public SoundEffectInstance CreateInstance()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var instance = new SoundEffectInstance(XnaExceptions.Guard(_soundEffect.CreateNativeInstanceHandle), this);
        lock (_children)
        {
            _children.Add(new WeakReference<SoundEffectInstance>(instance));
        }

        return instance;
    }

    public bool Play() => Play(1f, 0f, 0f);

    public bool Play(float volume, float pitch, float pan)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateRange(volume, 0f, 1f, nameof(volume));
        ValidateRange(pitch, -1f, 1f, nameof(pitch));
        ValidateRange(pan, -1f, 1f, nameof(pan));
        return XnaExceptions.Guard(() => _soundEffect.Play(volume, pitch, pan));
    }

    public static TimeSpan GetSampleDuration(int sizeInBytes, int sampleRate, AudioChannels channels) =>
        CNA.Audio.SoundEffect.GetSampleDuration(sizeInBytes, sampleRate, (CNA.Audio.AudioChannels)(int)channels);

    public static int GetSampleSizeInBytes(TimeSpan duration, int sampleRate, AudioChannels channels) =>
        CNA.Audio.SoundEffect.GetSampleSizeInBytes(duration, sampleRate, (CNA.Audio.AudioChannels)(int)channels);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        _ = disposing;
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // XNA keeps weak child references on the effect while each instance keeps its parent
        // strongly rooted. The parent disposes every still-live child before releasing its own
        // native effect. This also satisfies CNA's native dependency order.
        lock (_children)
        {
            foreach (WeakReference<SoundEffectInstance> child in _children)
            {
                if (child.TryGetTarget(out SoundEffectInstance? instance))
                {
                    instance.Dispose();
                }
            }

            _children.Clear();
        }

        _soundEffect?.Dispose();
    }

    private static void ValidateRange(float value, float minimum, float maximum, string parameterName)
    {
        if (value < minimum || value > maximum || float.IsNaN(value))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
