using CNA.XnaCompat.Extensions;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA, for the reason CompatLayerIntegrationTests records.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>
/// XNA initializes a game's components, content included, inside its <c>base.Initialize()</c>
/// (cna-cs CSX-088): SoundAndMusic reads a component's texture right after it.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatComponentInitializationTests
{
    private sealed class CountingComponent(XnaGame game) : DrawableGameComponent(game)
    {
        public int Initialized { get; private set; }

        public int Loaded { get; private set; }

        public int LoadedWhenItsInitializeReturned { get; private set; } = -1;

        public override void Initialize()
        {
            base.Initialize();
            Initialized++;
            LoadedWhenItsInitializeReturned = Loaded;
        }

        protected override void LoadContent() => Loaded++;
    }

    private sealed class ComponentOrderProbe : XnaGame
    {
        public ComponentOrderProbe()
        {
            _ = new GraphicsDeviceManager(this);
            Early = new CountingComponent(this);
            Components.Add(Early);
        }

        public CountingComponent Early { get; }

        public CountingComponent? Late { get; private set; }

        public int EarlyInitializedAfterBase { get; private set; } = -1;

        public int EarlyLoadedAfterBase { get; private set; } = -1;

        private int _frames;

        protected override void Initialize()
        {
            base.Initialize();
            EarlyInitializedAfterBase = Early.Initialized;
            EarlyLoadedAfterBase = Early.Loaded;
        }

        protected override void Update(GameTime gameTime)
        {
            if (++_frames == 1)
            {
                Late = new CountingComponent(this);
                Components.Add(Late);
            }

            if (_frames >= 3)
            {
                Exit();
            }

            base.Update(gameTime);
        }
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void Components_AreInitializedWithTheirContentInsideBaseInitialize_Once()
    {
        using var game = new ComponentOrderProbe();
        for (int i = 0; i < 5; i++)
        {
            game.RunFrameEXT();
        }

        // Inside base.Initialize(), as XNA's Game.Initialize does it (IL): initialized, and the
        // component's content loaded within its own base.Initialize().
        Assert.Equal(1, game.EarlyInitializedAfterBase);
        Assert.Equal(1, game.EarlyLoadedAfterBase);
        Assert.Equal(1, game.Early.LoadedWhenItsInitializeReturned);

        // CNA's own Game::Initialize reaches the same component afterwards; nothing runs twice.
        Assert.Equal(1, game.Early.Initialized);
        Assert.Equal(1, game.Early.Loaded);

        // Added while running: initialized at once, content loaded inside, once.
        Assert.NotNull(game.Late);
        Assert.Equal(1, game.Late!.Initialized);
        Assert.Equal(1, game.Late.Loaded);
        Assert.Equal(1, game.Late.LoadedWhenItsInitializeReturned);
    }
}
