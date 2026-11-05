using CNA.XnaCompat.Extensions;
using System.Reflection;
using Microsoft.Xna.Framework;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA, for the reason CompatLayerIntegrationTests records.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>
/// A Windows Phone title sets <c>graphics.IsFullScreen = true</c> to hide the phone's status bar
/// (SoccerPitch, CSSAMPLE-073). On a desktop that request stays the game's own state and never
/// reaches the display (cna-cs CSX-094): forwarded, it asked for a 480x800 full-screen mode.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatPhoneFullScreenTests
{
    private sealed class PhoneTitle : XnaGame
    {
        public PhoneTitle(string runtimeProfileLine)
        {
            Graphics = (GraphicsDeviceManager)typeof(GraphicsDeviceManager)
                .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(XnaGame), typeof(string)])!
                .Invoke([this, runtimeProfileLine]);
            Graphics.PreferredBackBufferWidth = 480;
            Graphics.PreferredBackBufferHeight = 800;
            Graphics.IsFullScreen = true;
        }

        public GraphicsDeviceManager Graphics { get; }

        public bool? PresentedFullScreen { get; private set; }

        protected override void Update(GameTime gameTime)
        {
            PresentedFullScreen = GraphicsDevice.PresentationParameters.IsFullScreen;
            Exit();
            base.Update(gameTime);
        }
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void PhoneTitle_OnADesktop_KeepsIsFullScreenWithoutAFullScreenDevice()
    {
        using var game = new PhoneTitle("WindowsPhone.v4.0.Reach");
        game.RunFrameEXT();

        Assert.True(game.Graphics.IsFullScreen);
        Assert.False(game.PresentedFullScreen);

        game.Graphics.ToggleFullScreen();
        Assert.False(game.Graphics.IsFullScreen);
    }
}
