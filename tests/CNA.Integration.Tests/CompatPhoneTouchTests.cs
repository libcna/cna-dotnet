using CNA.XnaCompat.Extensions;
using System.Reflection;
using Microsoft.Xna.Framework;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA, for the reason CompatLayerIntegrationTests records.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>
/// The phone emulator made the mouse the finger, so a Windows Phone title off a phone gets CNA's
/// mouse-as-touch bridge once it runs (cna-cs CSX-098); a Windows title keeps XNA's default, touch
/// from a digitizer only.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatPhoneTouchTests
{
    private static readonly PropertyInfo PhoneTitleActive = typeof(XnaGame).Assembly
        .GetType("Microsoft.Xna.Framework.PhoneTitle", throwOnError: true)!
        .GetProperty("Active", BindingFlags.NonPublic | BindingFlags.Static)!;

    private sealed class Probe : XnaGame
    {
        public Probe(bool phoneTitle)
        {
            _ = new GraphicsDeviceManager(this);
            PhoneTitleActive.SetValue(null, phoneTitle);
        }

        public bool? Emulated { get; private set; }

        public bool Restore { get; init; }

        protected override void Update(GameTime gameTime)
        {
            Emulated = global::CNA.Input.Touch.TouchPanel.MouseTouchEmulationEnabled;
            if (Restore)
            {
                global::CNA.Input.Touch.TouchPanel.MouseTouchEmulationEnabled = false;
            }

            Exit();
            base.Update(gameTime);
        }
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void PhoneTitle_GetsTheMouseAsAFinger_AWindowsTitleDoesNot()
    {
        try
        {
            using (var windows = new Probe(phoneTitle: false))
            {
                windows.RunFrameEXT();
                Assert.False(windows.Emulated);
            }

            using var phone = new Probe(phoneTitle: true) { Restore = true };
            phone.RunFrameEXT();
            Assert.True(phone.Emulated);
        }
        finally
        {
            PhoneTitleActive.SetValue(null, false);
        }
    }
}
