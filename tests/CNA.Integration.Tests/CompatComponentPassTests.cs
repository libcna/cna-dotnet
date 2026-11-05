using CNA.XnaCompat.Extensions;
using Microsoft.Xna.Framework;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA, for the reason CompatLayerIntegrationTests records.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>
/// XNA's <c>Game.Update</c> and <c>Game.Draw</c> run the components at the call (cna-cs CSX-090):
/// a game drawing its HUD after <c>base.Draw</c> draws it over them, as DistortionSample does.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatComponentPassTests
{
    private sealed class CountingComponent(XnaGame game) : DrawableGameComponent(game)
    {
        public int Updates { get; private set; }

        public int Draws { get; private set; }

        public override void Update(GameTime gameTime) => Updates++;

        public override void Draw(GameTime gameTime) => Draws++;
    }

    private sealed class PassProbe : XnaGame
    {
        private readonly bool _callBaseUpdate;
        private int _frames;

        public PassProbe(bool callBaseUpdate)
        {
            _callBaseUpdate = callBaseUpdate;
            _ = new GraphicsDeviceManager(this);
            Component = new CountingComponent(this);
            Components.Add(Component);
        }

        public CountingComponent Component { get; }

        public List<(int Before, int After)> UpdateCalls { get; } = [];

        public List<(int Before, int After)> DrawCalls { get; } = [];

        protected override void Update(GameTime gameTime)
        {
            int before = Component.Updates;
            if (_callBaseUpdate)
            {
                base.Update(gameTime);
            }

            UpdateCalls.Add((before, Component.Updates));
            if (++_frames >= 3)
            {
                Exit();
            }
        }

        protected override void Draw(GameTime gameTime)
        {
            int before = Component.Draws;
            base.Draw(gameTime);
            DrawCalls.Add((before, Component.Draws));
        }
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void BaseUpdateAndBaseDraw_RunTheComponentsAtTheCall_OncePerFrame()
    {
        using var game = new PassProbe(callBaseUpdate: true);
        for (int i = 0; i < 5; i++)
        {
            game.RunFrameEXT();
        }

        Assert.NotEmpty(game.UpdateCalls);
        Assert.All(game.UpdateCalls, call => Assert.Equal(call.Before + 1, call.After));
        Assert.Equal(game.UpdateCalls.Count, game.Component.Updates);

        Assert.NotEmpty(game.DrawCalls);
        Assert.All(game.DrawCalls, call => Assert.Equal(call.Before + 1, call.After));
        Assert.Equal(game.DrawCalls.Count, game.Component.Draws);
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void AGameThatSkipsBaseUpdate_StillHasItsComponentsUpdatedByCna()
    {
        using var game = new PassProbe(callBaseUpdate: false);
        for (int i = 0; i < 5; i++)
        {
            game.RunFrameEXT();
        }

        // XNA would not update them at all; CNA's own pass keeps the lenience CNA.NET had.
        Assert.NotEmpty(game.UpdateCalls);
        Assert.All(game.UpdateCalls, call => Assert.Equal(call.Before, call.After));
        Assert.Equal(game.UpdateCalls.Count, game.Component.Updates);
    }
}
