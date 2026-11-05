using CNA.Audio;
using Xunit;
using Xunit.Abstractions;

namespace CNA.Integration.Tests;

/// <summary>
/// Game components, driven by native rather than by a direct call.
///
/// In the own-game collection because a component takes its game in the constructor, so the host
/// game has to be built here rather than borrowed from the shared fixture.
/// </summary>
[Collection(OwnGameCollection.Name)]
public class GameComponentTests(ITestOutputHelper output)
{
    [NativeFact]
    public void FailedUpdate_SkipsFrameworkDispatcherPump()
    {
        using var game = new FailingUpdateHost();

        // The game's own exception, not a CnaException wrapping its message: in XNA it unwinds out
        // of the call that drives the frame.
        var thrown = Assert.Throws<InvalidOperationException>(() => { game.RunFrameEXT(); });
        Assert.Equal("update-failure", thrown.Message);
        Assert.Equal(0, game.BufferNeededEvents);
    }

    /// <summary>
    /// A component's exception surfaces from the driving call with its own type and stack. Component
    /// handlers have no error channel, so it used to be kept on the component and rethrown from the
    /// next unrelated member access -- ParticleSample's showed up from a DrawOrder setter -- while
    /// the game ran on.
    /// </summary>
    [NativeFact]
    public void ComponentException_SurfacesFromTheDrivingCall()
    {
        using var game = new ThrowingComponentHost();

        var thrown = Assert.Throws<FormatException>(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                game.RunFrameEXT();
            }
        });
        Assert.Equal("component-update-failure", thrown.Message);
        Assert.Contains(nameof(ThrowingComponent.Update), thrown.StackTrace);
    }

    /// <summary>
    /// Adding a component to a game that has already initialized initializes it inside the add, so
    /// an exception from its Initialize comes out of <c>Components.Add</c>, as in XNA -- and the
    /// game keeps running when the caller catches it.
    /// </summary>
    [NativeFact]
    public void ComponentInitializeException_SurfacesFromAdd()
    {
        using var game = new ComponentHost();
        game.RunFrameEXT();

        var thrown = Assert.Throws<ArithmeticException>(
            () => game.Components.Add(new ThrowingInitializeComponent(game)));
        Assert.Equal("component-initialize-failure", thrown.Message);

        game.RunFrameEXT();
    }

    private sealed class ThrowingInitializeComponent(CNA.Game game) : CNA.GameComponent(game)
    {
        public override void Initialize() =>
            throw new ArithmeticException("component-initialize-failure");
    }

    private sealed class ThrowingComponent(CNA.Game game) : CNA.GameComponent(game)
    {
        public override void Update(GameTime gameTime) =>
            throw new FormatException("component-update-failure");
    }

    private sealed class ThrowingComponentHost : CNA.Game
    {
        public ThrowingComponentHost() => Components.Add(new ThrowingComponent(this));
    }

    /// <summary>
    /// A component added to Game.Components must actually tick.
    ///
    /// `CnaCApiRuntime.cpp`'s CGame overrides `Update` and `Draw` as
    /// `Invoke(callbacks_.update, ...)` with no `Game::Update(gameTime)` after it -- the only two
    /// overrides in that class that do not chain to the base. `Game::Update` is what walks
    /// `updateableComponents_` and updates each enabled one, so a component added through
    /// `cna_game_components_add` is created, initialized, and then never ticks. Measured: 6 frames,
    /// `initialized=True`, `updated 0 times`. Reported as the sibling of CBIND-063.
    ///
    /// The same omission also skips `FrameworkDispatcher::Update()`, the last statement of
    /// `Game::Update` -- which in XNA is what pumps DynamicSoundEffectInstance buffer refills and
    /// MediaPlayer song transitions. So that is silently dead for every C consumer too.
    ///
    /// <b>Fixed upstream in CBIND-068</b>, which chained both overrides to their base. This test
    /// was skipped rather than rewritten to assert the broken behaviour -- a test that encodes a
    /// defect as expected passes forever and stops being a question -- and un-skipping it is what
    /// verified the fix from this side.
    ///
    /// Not worked around here at the time, deliberately: a managed fallback driving components
    /// itself would have double-updated them the moment the fix landed.
    ///
    /// One consequence of the fix worth knowing: the base pass is skipped when a callback has
    /// already failed, because recording a callback failure calls Exit. So "the frame ran" and
    /// "components ticked" differ in exactly that case and no other.
    /// </summary>
    [NativeFact]
    public void GameComponents_AddedComponentReceivesUpdate()
    {
        using var game = new ComponentHost();

        for (int i = 0; i < 6 && game.Component.Updates == 0; i++)
        {
            game.RunFrameEXT();
        }

        output.WriteLine(
            $"component updated {game.Component.Updates} time(s), initialized={game.Component.Initialized}");
        Assert.True(game.Component.Initialized, "The component was never initialized.");
        Assert.True(game.Component.Updates > 0, "The component was added but never updated.");
    }

    /// <summary>
    /// cna-cs-samples CNA-REPORT-004: a drawable component reads <c>GraphicsDevice</c> from the
    /// callbacks the game itself drives -- LoadContent from <c>Game.Initialize</c>, Update and Draw
    /// from the base passes -- which is where XNA code uses it.
    ///
    /// Native opened the device borrow only around the game's own callback, and the base passes run
    /// after it returns, so every one of these threw <c>InvalidState</c>. Fixed in CNA CBIND-128
    /// (`RuntimeComponentDeviceBorrow.c` is the C-level twin); nothing here caches a device handle.
    /// </summary>
    [NativeFact]
    public void DrawableComponent_UsesGraphicsDeviceFromGameDrivenCallbacks()
    {
        using var game = new DeviceComponentHost();
        using var graphics = new CNA.GraphicsDeviceManager(game);

        for (int i = 0; i < 4; i++)
        {
            game.RunFrameEXT();
        }

        DeviceComponent component = game.Component;
        output.WriteLine(
            $"load {component.Loads}, update {component.Updates}, draw {component.Draws}; " +
            $"viewport {component.LastViewportWidth}; failure: {component.Failure?.Message ?? "none"}");
        Assert.Null(component.Failure);
        Assert.Equal(1, component.Loads);
        Assert.True(component.Updates > 0, "The component was never updated.");
        Assert.True(component.Draws > 0, "The component was never drawn.");
        Assert.True(component.LastViewportWidth > 0, "The borrowed device answered no viewport.");
    }

    private sealed class DeviceComponent(CNA.Game game) : CNA.DrawableGameComponent(game)
    {
        public int Loads { get; private set; }

        public int Updates { get; private set; }

        public int Draws { get; private set; }

        public int LastViewportWidth { get; private set; }

        public Exception? Failure { get; private set; }

        protected internal override void LoadContent()
        {
            Loads++;
            Touch();
        }

        public override void Update(GameTime gameTime)
        {
            Updates++;
            Touch();
        }

        public override void Draw(GameTime gameTime)
        {
            Draws++;
            Touch();
        }

        // Recorded rather than thrown: an exception must not unwind through the native frame.
        private void Touch()
        {
            try
            {
                LastViewportWidth = GraphicsDevice.Viewport.Width;
            }
            catch (Exception exception)
            {
                Failure ??= exception;
            }
        }
    }

    private sealed class DeviceComponentHost : CNA.Game
    {
        public DeviceComponentHost()
        {
            Component = new DeviceComponent(this);
            Components.Add(Component);
        }

        public DeviceComponent Component { get; }
    }

    private sealed class CountingComponent(CNA.Game game) : CNA.GameComponent(game)
    {
        public int Updates { get; private set; }

        public bool Initialized { get; private set; }

        public override void Initialize() => Initialized = true;

        public override void Update(GameTime gameTime) => Updates++;
    }

    /// <summary>A component takes its game in the constructor, so the host has to build it after
    /// base construction rather than receive one -- which is XNA's own shape.</summary>
    private sealed class ComponentHost : CNA.Game
    {
        public ComponentHost()
        {
            Component = new CountingComponent(this);
            Components.Add(Component);
        }

        public CountingComponent Component { get; }

        private int _frames;

        protected override void Update(GameTime gameTime)
        {
            // Deliberately not exiting on the first frame: a component is updated by the game's own
            // update pass, so exiting immediately could cut it off before it ever ran.
            if (++_frames >= 3)
            {
                Exit();
            }

            base.Update(gameTime);
        }
    }

    private sealed class FailingUpdateHost : CNA.Game
    {
        private DynamicSoundEffectInstance? _dynamic;
        private bool _failed;

        public int BufferNeededEvents { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            if (!_failed)
            {
                _failed = true;
                // Construct inside the active native frame. This both avoids competing ambient
                // games when xUnit collections overlap and proves that framework work queued by a
                // callback which then fails is not pumped by the native post-callback base pass.
                _dynamic = new DynamicSoundEffectInstance(8000, AudioChannels.Mono);
                _dynamic.SubmitBuffer(new byte[16_000]);
                _dynamic.BufferNeeded += (_, _) => BufferNeededEvents++;
                _dynamic.Play();
                throw new InvalidOperationException("update-failure");
            }

            base.Update(gameTime);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _dynamic?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
