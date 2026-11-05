using Xunit;
using Xunit.Abstractions;

namespace CNA.Integration.Tests;

/// <summary>
/// The first tests in this repository that construct a real native object and drive it.
///
/// Everything else here is managed-only, and managed-only tests cannot see the failure modes that
/// actually matter in a P/Invoke binding: a struct whose managed layout does not match the C one, a
/// handle freed while native still holds it, a callback whose calling convention is wrong, an
/// <c>out</c> where the ABI wanted a caller-initialised <c>ref</c>. Every one of those compiles,
/// passes a unit test, and corrupts memory at run time.
///
/// Deliberately ordered from cheapest to most demanding, so a failure says how far the stack got
/// rather than just "it broke": load the library, read the ABI version, construct a Game, run
/// frames, shut down.
/// </summary>
[Collection(OwnGameCollection.Name)]
public class GameLifecycleTests(ITestOutputHelper output)
{
    /// <summary>A game that records what it was asked to do, and asks to exit after a fixed number
    /// of frames so the test cannot hang.</summary>
    private sealed class ProbeGame : CNA.Game
    {
        private readonly int _framesToRun;

        public ProbeGame(int framesToRun)
        {
            _framesToRun = framesToRun;
        }

        public int Initializes { get; private set; }

        public int LoadContents { get; private set; }

        public int Updates { get; private set; }

        public int Draws { get; private set; }

        public int UnloadContents { get; private set; }

        public List<string> Order { get; } = [];

        public Exception? Failure { get; private set; }

        protected override void Initialize()
        {
            Record("Initialize", () => Initializes++);
            base.Initialize();
        }

        protected override void LoadContent()
        {
            Record("LoadContent", () => LoadContents++);
            base.LoadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            Record("Update", () =>
            {
                Updates++;
                if (Updates >= _framesToRun)
                {
                    Exit();
                }
            });

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            Record("Draw", () => Draws++);
            base.Draw(gameTime);
        }

        protected override void UnloadContent()
        {
            Record("UnloadContent", () => UnloadContents++);
            base.UnloadContent();
        }

        /// <summary>
        /// An exception thrown inside a native callback has nowhere useful to go -- it unwinds
        /// through C, which is undefined behaviour. Capturing it and letting the frame finish means
        /// a failure shows up as a readable assertion in the test rather than as a crashed test
        /// host with no output.
        /// </summary>
        private void Record(string phase, Action action)
        {
            if (Order.Count < 64)
            {
                Order.Add(phase);
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                Failure ??= ex;
            }
        }
    }

    [NativeFact]
    public void NativeLibrary_Loads_AndReportsACompatibleAbiVersion()
    {
        (int major, int minor, int patch) = CnaAbi.Decode(CnaNativeProbe.NativeVersion);
        (int expectedMajor, int expectedMinor, int expectedPatch) = CnaAbi.Decode(CnaAbi.ExpectedVersion);

        output.WriteLine($"native ABI {major}.{minor}.{patch}, binding expects {expectedMajor}.{expectedMinor}.{expectedPatch}");

        // EnsureCompatible is what Game's constructor calls. Its first import triggers the
        // resolver's exact reviewed-version, all-symbol, signature, and shape admission contract.
        CnaAbi.EnsureCompatible();
    }

    /// <summary>
    /// Construct and dispose without ever running. This is the narrowest possible test of
    /// <c>cna_game_create</c> plus <c>cna_game_set_frame_hooks_ext</c> plus <c>cna_game_destroy</c>,
    /// and of the <c>GCHandle</c> round trip that carries the managed instance through native as a
    /// <c>void*</c> context. If the callback struct's layout is wrong, this is where it shows.
    /// </summary>
    [NativeFact]
    public void Game_ConstructsAndDisposes_WithoutRunning()
    {
        using var game = new ProbeGame(framesToRun: 1);

        Assert.Equal(0, game.Updates);
    }

    /// <summary>Two games in sequence: the second must not inherit the first's ambient handle or
    /// trip over its freed <c>GCHandle</c>. A leaked root or a stale <c>CnaAmbientGame.Current</c>
    /// shows up here and nowhere in the managed tests.</summary>
    [NativeFact]
    public void Game_ConstructAndDispose_IsRepeatable()
    {

        for (int i = 0; i < 3; i++)
        {
            using var game = new ProbeGame(framesToRun: 1);
            Assert.NotNull(game.Content);
        }
    }

    [NativeFact]
    public void Game_RunOneFrameTicksWithoutInitializing()
    {
        using var game = new ProbeGame(framesToRun: 10);
        game.RunOneFrame();
        Assert.Equal(0, game.Initializes);
        Assert.Equal(0, game.LoadContents);
        Assert.DoesNotContain("Initialize", game.Order);
        Assert.DoesNotContain("LoadContent", game.Order);
        Assert.True(game.Updates > 0);
    }

    /// <summary>
    /// The one that matters: a real game loop. <c>RunFrameEXT</c> rather than <c>Run</c> so the
    /// test cannot hang if <c>Exit</c> is not honoured -- <c>Run</c> would block forever on a
    /// broken exit path, and a hung CI job is a much worse failure report than an assertion.
    /// </summary>
    [NativeFact]
    public void Game_RunsFrames_AndCallsTheLifecycleCallbacks()
    {
        using var game = new ProbeGame(framesToRun: 3);

        for (int i = 0; i < 5 && game.Updates < 3; i++)
        {
            game.RunFrameEXT();
        }

        output.WriteLine("callback order: " + string.Join(" -> ", game.Order));

        if (game.Failure is { } failure)
        {
            Assert.Fail($"A lifecycle callback threw: {failure}");
        }

        Assert.True(game.Updates > 0, "Update was never called -- the frame hooks are not firing.");
        Assert.True(game.Draws > 0, "Draw was never called.");
    }

    /// <summary>
    /// The lifecycle order, measured rather than assumed.
    ///
    /// This test found a real defect on its first run: native delivered
    /// <c>LoadContent -> Initialize -> Update</c>, because the C glue called the canonical
    /// <c>Game::Initialize()</c> -- which ends by calling <c>LoadContent()</c>, exactly as XNA does
    /// -- before invoking the <c>initialize</c> frame hook. The header had promised the opposite
    /// all along. Fixed upstream in CBIND-063.
    ///
    /// It survived as long as it did because the native suite's lifecycle assertions counted
    /// callbacks, and a count cannot see an order. That is the whole reason this asserts the
    /// *sequence* and not just the totals: exactly the same three counts hold for both orderings.
    /// </summary>
    [NativeFact]
    public void Game_DeliversTheLifecycleCallbacksInXnaOrder()
    {
        using var game = new ProbeGame(framesToRun: 2);

        for (int i = 0; i < 4 && game.Updates < 2; i++)
        {
            game.RunFrameEXT();
        }

        output.WriteLine("callback order: " + string.Join(" -> ", game.Order));

        Assert.Equal(1, game.Initializes);
        Assert.Equal(1, game.LoadContents);

        // Initialize then LoadContent, and both before any Update. In XNA, LoadContent runs from
        // inside base.Initialize(), so a game routinely reads Initialize-assigned fields there.
        Assert.Equal(["Initialize", "LoadContent"], game.Order.Take(2));
        Assert.Equal("Update", game.Order.Skip(2).First());
    }

    /// <summary>Properties that round-trip through native on every access. A mismatched struct or a
    /// wrong marshalling direction turns these into garbage, and they are cheap to check.</summary>
    [NativeFact]
    public void Game_TimingProperties_RoundTripThroughNative()
    {
        using var game = new ProbeGame(framesToRun: 1);

        game.IsFixedTimeStep = true;
        Assert.True(game.IsFixedTimeStep);

        game.IsFixedTimeStep = false;
        Assert.False(game.IsFixedTimeStep);

        var interval = TimeSpan.FromMilliseconds(20);
        game.TargetElapsedTime = interval;
        Assert.Equal(interval, game.TargetElapsedTime);
    }
}
