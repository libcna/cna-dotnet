namespace CNA;

/// <summary>
/// A single frame's timing snapshot. See libcna/cna's docs/binding.md, "Value types live in the
/// language" --
/// crosses the ABI as a POD value (<see cref="CNA.Interop.CnaGameTime"/>), never as any native
/// time-class instance.
/// </summary>
public readonly struct GameTime
{
    public TimeSpan TotalGameTime { get; }
    public TimeSpan ElapsedGameTime { get; }
    public bool IsRunningSlowly { get; }

    public GameTime(TimeSpan totalGameTime, TimeSpan elapsedGameTime, bool isRunningSlowly = false)
    {
        TotalGameTime = totalGameTime;
        ElapsedGameTime = elapsedGameTime;
        IsRunningSlowly = isRunningSlowly;
    }

    internal static GameTime FromNative(CNA.Interop.CnaGameTime native) => new(
        TimeSpan.FromTicks(native.TotalGameTimeTicks),
        TimeSpan.FromTicks(native.ElapsedGameTimeTicks),
        native.IsRunningSlowly != 0);
}
