using CNA.Interop;

namespace CNA.Audio;

/// <summary>
/// A playable instance of a <see cref="SoundEffect"/>, created via <see cref="SoundEffect.CreateInstance"/>
/// -- matching real XNA, where <c>SoundEffectInstance</c> has no public constructor of its own.
/// Now matches the real, shipped openeggbert/cna C API (<c>audio.h</c>) rather than a self-designed
/// guess -- see <c>NEXT.md</c>'s native-ABI-migration entry, step 9. The real ABI has no individual
/// <c>cna_sound_effect_instance_get_state</c>/<c>_get_volume</c>/<c>_get_pitch</c>/<c>_get_pan</c>/
/// <c>_get_is_looped</c> getters at all -- only one combined snapshot,
/// <c>cna_sound_effect_instance_get_info</c> -- so every getter below now routes through that one
/// native round trip instead of its own. The five individual setters this project originally
/// guessed *do* still exist individually, an asymmetric shape confirmed directly against the real
/// header, not assumed.
/// </summary>
public class SoundEffectInstance : IDisposable
{
    private readonly NativeResourceHandle _handle;
    private bool _hasBeenPlayed;

    /// <summary><c>protected internal</c>, not <c>public</c> -- matches real XNA (no public
    /// constructor) as closely as C#'s accessibility model allows, while still letting
    /// <c>CNA.XnaCompat</c>'s <c>SoundEffectInstance</c> subclass forward to it -- same
    /// "protected internal raw-handle constructor" pattern <c>Texture2D</c> already uses.</summary>
    protected internal SoundEffectInstance(ulong nativeHandleValue)
    {
        _handle = new NativeResourceHandle(nativeHandleValue, h => Native.cna_sound_effect_instance_destroy(new CnaHandle(h)).IsSuccess());
    }

    /// <summary><c>private protected</c> rather than <c>private</c> since Phase 8 WP11b, so
    /// <see cref="DynamicSoundEffectInstance"/> -- a real subclass in real XNA -- can reach its own
    /// handle for the dynamic-only native calls.</summary>
    private protected ulong NativeHandleValue => _handle.DangerousGetHandle();

    /// <summary>Internal handle-sharing seam for CNA.XnaCompat. The compat public hierarchy cannot
    /// inherit <see cref="DynamicSoundEffectInstance"/>, but its internal adapter still needs the
    /// one owned instance handle for dynamic-only C ABI calls.</summary>
    internal ulong NativeHandleValueForCompatibility => NativeHandleValue;

    public void Play()
    {
        CnaResult result = Native.cna_sound_effect_instance_play(new CnaHandle(NativeHandleValue));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Play));
        _hasBeenPlayed = true;
    }

    public void Pause()
    {
        CnaResult result = Native.cna_sound_effect_instance_pause(new CnaHandle(NativeHandleValue));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Pause));
    }

    public void Resume()
    {
        CnaResult result = Native.cna_sound_effect_instance_resume(new CnaHandle(NativeHandleValue));
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Resume));
    }

    public void Stop() => Stop(immediate: true);

    /// <summary><paramref name="immediate"/> matching the real ABI's own documented semantics:
    /// <c>true</c> cuts playback off immediately; <c>false</c> allows a release tail to finish
    /// (only meaningful for effect types this repository doesn't implement yet, but the parameter
    /// itself is part of real XNA's public API shape).</summary>
    public void Stop(bool immediate)
    {
        CnaResult result = Native.cna_sound_effect_instance_stop(new CnaHandle(NativeHandleValue), immediate ? (byte)1 : (byte)0);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Stop));
    }

    private CnaSoundEffectInstanceInfo GetInfo()
    {
        var info = new CnaSoundEffectInstanceInfo();
        CnaResult result = Native.cna_sound_effect_instance_get_info(new CnaHandle(NativeHandleValue), ref info);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, "cna_sound_effect_instance_get_info");
        return info;
    }

    public SoundState State => (SoundState)GetInfo().State;

    /// <summary>Range [0, 1], but -- matching the real ABI's own documented behavior ("passed
    /// through by CNA without clamping") -- this setter does not validate or clamp; an
    /// out-of-range value is the caller's responsibility, not an error here.</summary>
    public float Volume
    {
        get => GetInfo().Volume;
        set
        {
            CnaResult result = Native.cna_sound_effect_instance_set_volume(new CnaHandle(NativeHandleValue), value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(Volume));
        }
    }

    /// <summary>Range [-1, 1] (one octave down to one octave up). Native CNA now refuses
    /// nonfinite or out-of-range assignments instead of clamping them.</summary>
    public float Pitch
    {
        get => GetInfo().Pitch;
        set
        {
            CnaResult result = Native.cna_sound_effect_instance_set_pitch(new CnaHandle(NativeHandleValue), value);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(Pitch));
        }
    }

    /// <summary>Range [-1 (full left), 1 (full right)] -- unlike <see cref="Volume"/>/<see cref="Pitch"/>,
    /// the real ABI's setter *does* validate this range and returns a documented failure; that
    /// validation is reproduced here in managed code too (matching where the real C++
    /// implementation itself performs it) rather than left entirely to a native
    /// <see cref="CnaResult"/> failure.</summary>
    public float Pan
    {
        get => GetInfo().Pan;
        set
        {
            if (value is < -1f or > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Must be between -1 and 1.");
            }

            CnaResult result = Native.cna_sound_effect_instance_set_pan(new CnaHandle(NativeHandleValue), value);

            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(Pan));
        }
    }

    /// <summary>Matching the real ABI's own documented behavior: fails with a documented
    /// <see cref="CnaResult"/> if set after playback has begun. Also checked here in managed code
    /// before reaching native (matching where the real C++ implementation itself performs the same
    /// check), so the failure surfaces as a clear <see cref="InvalidOperationException"/> rather
    /// than a native result the caller has to interpret.</summary>
    public bool IsLooped
    {
        get => GetInfo().IsLooped != 0;
        set
        {
            if (_hasBeenPlayed)
            {
                throw new InvalidOperationException($"{nameof(IsLooped)} cannot be changed after the instance has already been played.");
            }

            CnaResult result = Native.cna_sound_effect_instance_set_is_looped(new CnaHandle(NativeHandleValue), value ? (byte)1 : (byte)0);

            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsLooped));
        }
    }

    /// <summary>Whether this instance has been disposed. Real XNA API, read from native rather
    /// than tracked here.</summary>
    public bool IsDisposed
    {
        get
        {
            CnaResult result = Native.cna_sound_effect_instance_get_is_disposed(
                new CnaHandle(NativeHandleValue), out byte disposed);
            GC.KeepAlive(this);
            CnaException.ThrowIfFailed(result, nameof(IsDisposed));
            return disposed != 0;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>The overridable half of disposal. Added in WP15 for
    /// <see cref="DynamicSoundEffectInstance"/>, which holds a native event subscription that must
    /// be released *before* the instance handle it is registered against.</summary>
    protected virtual void Dispose(bool disposing) => _handle.Dispose();

    /// <summary>Matches real XNA's <c>Apply3D</c>: positions this instance in 3D relative to a
    /// listener. Applies to the *instance*, not the effect -- which is why
    /// <c>SoundEffect.Play</c>-style fire-and-forget playback has no 3D
    /// form and <c>SoundEffect.Apply3D</c> goes through
    /// <see cref="SoundEffect.CreateInstance"/>.</summary>
    public void Apply3D(AudioListener listener, AudioEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(emitter);

        CnaAudioListener nativeListener = listener.ToNative();
        CnaAudioEmitter nativeEmitter = emitter.ToNative();
        CnaResult result = Native.cna_sound_effect_instance_apply_3d(
            new CnaHandle(NativeHandleValue), in nativeListener, in nativeEmitter);
        GC.KeepAlive(this);
        CnaException.ThrowIfFailed(result, nameof(Apply3D));
    }

    /// <summary>Routes XNA's listener-array overload through the native ABI as one atomic request.
    ///
    /// CNA's C ABI carried the whole array from 0.6.0, but until CABI-6 the implementation behind it
    /// refused every count other than one. CNA 0.19.0 accepts any count of one or more and applies
    /// the dominant listener, which is what XNA does; a runtime that still refuses reports
    /// <see cref="CnaResult.NotSupported"/> and is surfaced as <see cref="NotSupportedException"/>
    /// rather than approximated. Either way this never loops over the array applying one listener
    /// at a time, which would silently retain only the last.</summary>
    internal unsafe void Apply3D(AudioListener[] listeners, AudioEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(listeners);
        ArgumentNullException.ThrowIfNull(emitter);

        var nativeListeners = new CnaAudioListener[listeners.Length];
        for (int i = 0; i < listeners.Length; i++)
        {
            ArgumentNullException.ThrowIfNull(listeners[i]);
            nativeListeners[i] = listeners[i].ToNative();
        }

        CnaAudioEmitter nativeEmitter = emitter.ToNative();
        fixed (CnaAudioListener* listenersPtr = nativeListeners)
        {
            CnaResult result = Native.cna_sound_effect_instance_apply_3d_multi_ext(
                new CnaHandle(NativeHandleValue), listenersPtr, (ulong)nativeListeners.Length, in nativeEmitter);
            GC.KeepAlive(this);
            if (result == CnaResult.NotSupported)
            {
                throw new NotSupportedException("Only one audio listener is supported by the current CNA runtime.");
            }

            CnaException.ThrowIfFailed(result, nameof(Apply3D));
        }
    }
}
