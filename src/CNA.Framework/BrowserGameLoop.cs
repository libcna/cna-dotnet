using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

namespace CNA;

/// <summary>
/// <see cref="Game.Run"/> in a browser. A page cannot block, so the run goes on in the browser's
/// animation frames: each one runs one frame of a host-driven native run
/// (<c>cna_game_run_frame_ext</c>), and the frame that finds the game exited ends the run --
/// delivering <c>Exiting</c> and <c>EndRun</c> as <see cref="Game.Run"/> does -- and calls the
/// ending action, which is where a <c>Dispose</c> requested while the run was live happens. The
/// frames are requested from here, so a page needs nothing beyond <c>runMain()</c>.
/// </summary>
[SupportedOSPlatform("browser")]
internal static partial class BrowserGameLoop
{
    private static readonly Action<double> Frame = OnAnimationFrame;
    private static Game? _game;
    private static Action? _ended;

    internal static bool IsRunning(Game game) => ReferenceEquals(_game, game);

    internal static void Start(Game game, Action ended)
    {
        if (_game is not null)
        {
            throw new InvalidOperationException("Another game is already running in this page.");
        }

        _game = game;
        _ended = ended;
        if (AppContext.TryGetSwitch("CNA.Browser.Threads", out bool threads) && threads)
        {
            unsafe
            {
                EmscriptenSetMainLoopArg(&OnWorkerFrame, 0, 0, 0);
            }
        }
        else
        {
            RequestAnimationFrame(Frame);
        }
    }

    // A threaded bundle (WasmEnableThreads; eng/browser/CNA.Browser.targets sets the switch) runs C#
    // on a worker, .NET's deputy thread. A JSImport made there runs on the page's thread, which may
    // not call C# back synchronously ("Cannot call synchronous C# methods"), so the frames come from
    // Emscripten's main loop on the deputy itself. Between frames the deputy is back in its own event
    // loop, which is how the page's input events, handed to it as queued calls, ever reach SDL: a
    // loop that never returned there had no keyboard. The import binds by the one library name
    // everything in this static link answers to.
    [LibraryImport("cna-native", EntryPoint = "emscripten_set_main_loop_arg")]
    private static unsafe partial void EmscriptenSetMainLoopArg(
        delegate* unmanaged[Cdecl]<nint, void> frame, nint arg, int fps, int simulateInfiniteLoop);

    [LibraryImport("cna-native", EntryPoint = "emscripten_cancel_main_loop")]
    private static partial void EmscriptenCancelMainLoop();

    /// <summary>A frame of a threaded bundle's run. An exception cannot cross back into the main
    /// loop, so one a game callback threw ends the run and the process, as an unhandled exception
    /// does on a desktop.</summary>
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static void OnWorkerFrame(nint arg)
    {
        _ = arg;
        try
        {
            if (!RunOneFrame(0))
            {
                EmscriptenCancelMainLoop();
            }
        }
        catch (Exception ex)
        {
            EmscriptenCancelMainLoop();
            Environment.FailFast("Unhandled exception in the game's frame.", ex);
        }
    }

    [JSImport("globalThis.requestAnimationFrame")]
    private static partial int RequestAnimationFrame(
        [JSMarshalAs<JSType.Function<JSType.Number>>] Action<double> callback);

    /// <summary>A frame that throws -- a game callback did -- ends the run before the exception
    /// reaches the page, which reports it; the game does not go on drawing after it.</summary>
    private static void OnAnimationFrame(double timestamp)
    {
        if (RunOneFrame(timestamp))
        {
            RequestAnimationFrame(Frame);
        }
    }

    /// <summary>One frame of the run; false once it has ended.</summary>
    private static bool RunOneFrame(double timestamp)
    {
        _ = timestamp;
        Game game = _game!;
        bool running = false;
        try
        {
            running = game.RunFrameEXT();
            return running;
        }
        finally
        {
            if (!running)
            {
                Action ended = _ended!;
                _game = null;
                _ended = null;
                // A game that exits leaves its last frame on the canvas; there is no window to
                // close. Say so where a page and its developer can see it.
                Console.WriteLine("CNA: the game's run has ended.");
                ended();
            }
        }
    }
}
