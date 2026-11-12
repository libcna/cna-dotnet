namespace Microsoft.Xna.Framework.Audio;

/// <summary>XNA 4.0-compatible <c>Cue</c>. A thin re-typing wrapper rather than a subclass,
/// because <see cref="CNA.Audio.Cue"/>'s only constructor is internal (cues come from a sound
/// bank lookup).</summary>
public sealed class Cue : IDisposable
{
    private readonly CNA.Audio.Cue _cue;
    private bool _disposed;

    internal Cue(CNA.Audio.Cue cue)
    {
        _cue = cue;
    }

    ~Cue()
    {
        Dispose(false);
    }

    public string Name => _cue.Name;

    public bool IsCreated => _cue.IsCreated;

    public bool IsDisposed => _disposed || _cue.IsDisposed;

    public bool IsPaused => _cue.IsPaused;

    public bool IsPlaying => _cue.IsPlaying;

    public bool IsPrepared => _cue.IsPrepared;

    public bool IsPreparing => _cue.IsPreparing;

    public bool IsStopped => _cue.IsStopped;

    public bool IsStopping => _cue.IsStopping;

    public void Play() => _cue.Play();

    public void Pause() => _cue.Pause();

    public void Resume() => _cue.Resume();

    public void Stop(AudioStopOptions options) => _cue.Stop((CNA.Audio.AudioStopOptions)(int)options);

    public event EventHandler<EventArgs>? Disposing;

    public void Apply3D(AudioListener listener, AudioEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(emitter);
        _cue.Apply3D(listener.ToFramework(), emitter.ToFramework());
    }

    // An unknown or engine-global name is XNA's IndexOutOfRangeException (Helpers.GetExceptionFromResult
    // IL_0189-0193); CNA's C API reports it as INVALID_ARGUMENT with that canonical type.
    public float GetVariable(string name) => XnaExceptions.Guard(() => _cue.GetVariable(name));

    public void SetVariable(string name, float value) => XnaExceptions.Guard(() => _cue.SetVariable(name, value));

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cue?.Dispose();
        if (disposing)
        {
            Disposing?.Invoke(this, EventArgs.Empty);
        }
    }
}
