using CNA.XnaCompat.Extensions;
using CNA.Integration.Tests;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Xunit;

namespace CnaDotnet.GamerServices.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class GamerServicesCollection : ICollectionFixture<GamerServicesGameFixture>
{
    public const string Name = "GamerServices";
}

/// <summary>
/// One XNA-style game with a <see cref="GamerServicesComponent"/>, kept for the whole process, the
/// way an XNA game is. Its first frames initialize the dispatcher, after which the auto-signed-in
/// local profile appears and raises SignedIn.
/// </summary>
public sealed class GamerServicesGameFixture : IDisposable
{
    private readonly HostGame? _game;

    public GamerServicesGameFixture()
    {
        if (CnaNativeProbe.SkipReason is not null)
        {
            return;
        }

        SignedInGamer.SignedIn += (_, e) => SignedInEvents.Add(e.Gamer);
        _game = new HostGame();
        for (int frame = 0; frame < 30 && Gamer.SignedInGamers.Count == 0; frame++)
        {
            _game.RunFrameEXT();
        }
    }

    public List<SignedInGamer> SignedInEvents { get; } = new();

    public Game Game => _game!;

    /// <summary>Runs <paramref name="body"/> inside a real update and surfaces what it threw.</summary>
    public void InsideAFrame(Action<Game> body)
    {
        Assert.NotNull(_game);
        _game!.Pending = body;
        _game.Failure = null;
        _game.RunFrameEXT();
        if (_game.Failure is { } failure)
        {
            throw new Xunit.Sdk.XunitException($"The body threw inside the frame: {failure}");
        }
    }

    public void RunFrames(int count)
    {
        for (int frame = 0; frame < count; frame++)
        {
            _game!.RunFrameEXT();
        }
    }

    public void Dispose() => _game?.Dispose();

    private sealed class HostGame : Game
    {
        public HostGame()
        {
            _ = new GraphicsDeviceManager(this);
            Components.Add(new GamerServicesComponent(this));
        }

        public Action<Game>? Pending { get; set; }

        public Exception? Failure { get; set; }

        protected override void Update(GameTime gameTime)
        {
            if (Pending is { } body)
            {
                Pending = null;
                try
                {
                    body(this);
                }
                catch (Exception exception)
                {
                    Failure = exception;
                }
            }

            base.Update(gameTime);
        }
    }
}
