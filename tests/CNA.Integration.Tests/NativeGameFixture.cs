using CNA.Graphics;
using Xunit;

namespace CNA.Integration.Tests;

/// <summary>
/// One native game, shared by every test in the assembly.
///
/// Native allows exactly one C-owned game at a time, and a game refuses to be destroyed while a
/// resource created against it is still alive. Both are reasonable rules and together they make
/// "a game per test" unworkable: any test that leaves a native-backed wrapper to its finalizer --
/// which is most of them, since XNA's <c>EffectTechnique</c>, <c>EffectPass</c> and friends are not
/// <c>IDisposable</c> and no ported game disposes them -- can leave the slot occupied, and then
/// every *later* test fails to create a game.
///
/// That was not hypothetical. Each of these tests passed alone; run together, between six and
/// thirty-eight failed depending on GC timing and test order, all with the same misleading "only
/// one C-owned CNA game may be active" message pointing at an innocent test.
///
/// Forcing finalizers before the destroy narrows the window and does not close it, because
/// reachability is not something a test can promise. One shared game removes the question: there is
/// no second create to fail, and cleanup ordering stops being load-bearing.
///
/// This mirrors how a real game runs -- one game, many frames -- so it is closer to the thing under
/// test, not a concession.
/// </summary>
public sealed class NativeGameFixture : IDisposable
{
    private readonly HostGame? _game;
    private readonly CNA.GraphicsDeviceManager? _graphics;

    public NativeGameFixture()
    {
        if (CnaNativeProbe.SkipReason is not null)
        {
            return;
        }

        _game = new HostGame();
        // DrawableGameComponent.Initialize requires IGraphicsDeviceService, as XNA does.
        // Keep its manager alive for the entire shared game's lifetime.
        _graphics = new CNA.GraphicsDeviceManager(_game);

        // One frame up front so Initialize and LoadContent have run and a GraphicsDevice exists
        // before any test body asks for one.
        _game.RunFrameEXT();
    }

    /// <summary>
    /// How many native <c>Update</c> callbacks the most recent <see cref="InsideAFrame"/> received.
    ///
    /// It is not always one. A fixed-timestep loop runs catch-up updates when more than one tick of
    /// wall time has passed since the previous frame, and between two tests in this assembly a lot
    /// of wall time can pass. A test that counts per-update effects must divide by this rather than
    /// assume a single update, or it asserts how fast the host machine ran the suite.
    /// </summary>
    public int LastFrameUpdateCount { get; private set; }

    /// <summary>Runs <paramref name="body"/> inside a real frame and surfaces whatever it threw as
    /// a test failure, rather than letting it unwind through native -- an exception crossing an
    /// <c>UnmanagedCallersOnly</c> boundary is undefined behaviour.</summary>
    public void InsideAFrame(Action<CNA.Game> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        Assert.NotNull(_game);

        _game!.Pending = body;
        _game.Failure = null;
        _game.Ran = false;
        _game.Updates = 0;

        for (int i = 0; i < 4 && !_game.Ran; i++)
        {
            _game.RunFrameEXT();
        }

        LastFrameUpdateCount = _game.Updates;
        _game.Pending = null;

        if (_game.Failure is { } failure)
        {
            // A skip is a decision, not a failure, and a test body is where the decision is usually
            // made -- "this renderer has no system cursors", "this one has no Texture3D". Wrapping
            // it in a XunitException reported the environment's answer as a defect, which is the
            // opposite of what a skip is for.
            if (failure is Xunit.Sdk.SkipException)
            {
                throw failure;
            }

            throw new Xunit.Sdk.XunitException($"The body threw inside the frame: {failure}");
        }

        Assert.True(_game.Ran, "The frame never ran, so nothing was exercised.");
    }

    /// <summary>The same, for a body that only needs the device. Named rather than overloaded:
    /// a lambda with an implicitly-typed parameter is ambiguous between the two, and every call
    /// site here uses one.</summary>
    public void InsideAFrameWithDevice(Action<GraphicsDevice> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        InsideAFrame(game => body(game.GraphicsDevice));
    }

    public void Dispose()
    {
        try
        {
            _graphics?.Dispose();
        }
        finally
        {
            _game?.Dispose();
        }
    }

    private sealed class HostGame : CNA.Game
    {
        public Action<CNA.Game>? Pending { get; set; }

        public Exception? Failure { get; set; }

        public bool Ran { get; set; }

        public int Updates { get; set; }

        /// <summary>Never calls <c>Exit</c>: the game outlives every individual test, and exiting
        /// would end the run for whatever comes next.</summary>
        protected override void Update(GameTime gameTime)
        {
            Updates++;

            if (Pending is { } body && !Ran)
            {
                Ran = true;
                try
                {
                    body(this);
                }
                catch (Exception ex)
                {
                    Failure = ex;
                }
            }

            base.Update(gameTime);
        }
    }
}

/// <summary>
/// Binds every test class in this assembly to the one shared game.
///
/// A collection rather than a class fixture: a class fixture is per test class, which would give
/// one game per class and reintroduce exactly the create/destroy sequence this exists to avoid.
/// </summary>
[CollectionDefinition(Name)]
public sealed class NativeGameCollection : ICollectionFixture<NativeGameFixture>
{
    public const string Name = "native-game";
}

/// <summary>
/// The tests that build their own games, kept out of the shared one's way.
///
/// They cannot borrow the shared game -- construction and destruction are the thing under test --
/// and native allows one at a time. A separate collection is enough because parallelisation is off
/// assembly-wide, so collections run in sequence and a collection fixture is disposed before the
/// next collection starts. No suspend-and-rebuild dance required; an earlier attempt at one was
/// both more code and consistently worse.
/// </summary>
[CollectionDefinition(Name)]
public sealed class OwnGameCollection
{
    public const string Name = "own-game";
}
