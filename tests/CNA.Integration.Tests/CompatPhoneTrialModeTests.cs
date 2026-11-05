using CNA.XnaCompat.Extensions;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Xunit;
using XnaGame = Microsoft.Xna.Framework.Game;

// NOT under CNA, for the reason CompatLayerIntegrationTests records.
namespace CnaDotnet.Integration.Tests.Compat;

/// <summary>
/// CSX-137: a Windows Phone title's <c>Guide.IsTrialMode</c> is the phone's license check, which
/// needs no GamerServicesComponent: a title its developer deployed is no trial unless
/// <c>Guide.SimulateTrialMode</c> makes it one. <i>Windows Phone 7 Recipes</i>' trial sample reads it
/// in <c>Initialize</c>, sets <c>SimulateTrialMode</c> only in its Debug build, and without a component
/// CNA.NET answered as XNA on Windows does before its dispatcher first updates: true.
/// </summary>
[Collection(global::CNA.Integration.Tests.OwnGameCollection.Name)]
public class CompatPhoneTrialModeTests
{
    private sealed class Title : XnaGame
    {
        private readonly bool _simulate;

        public Title(string runtimeProfileLine, bool simulate)
        {
            _simulate = simulate;
            _ = (GraphicsDeviceManager)typeof(GraphicsDeviceManager)
                .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(XnaGame), typeof(string)])!
                .Invoke([this, runtimeProfileLine]);

            // A title's own build marks it in its RuntimeProfile resource, which this test assembly
            // lacks: mark it as Game's constructor would have.
            SetPhoneTitle(runtimeProfileLine.StartsWith("WindowsPhone.", StringComparison.Ordinal));
        }

        public bool? TrialModeInInitialize { get; private set; }

        protected override void Initialize()
        {
            Guide.SimulateTrialMode = _simulate;
            TrialModeInInitialize = Guide.IsTrialMode;
            base.Initialize();
        }

        protected override void Update(GameTime gameTime)
        {
            Exit();
            base.Update(gameTime);
        }
    }

    private static void SetPhoneTitle(bool active) =>
        typeof(GraphicsDeviceManager).Assembly.GetType("Microsoft.Xna.Framework.PhoneTitle", throwOnError: true)!
            .GetProperty("Active", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, active);

    private static bool? TrialMode(string runtimeProfileLine, bool simulate)
    {
        try
        {
            using var game = new Title(runtimeProfileLine, simulate);
            game.RunFrameEXT();
            return game.TrialModeInInitialize;
        }
        finally
        {
            Guide.SimulateTrialMode = false;
            SetPhoneTitle(false);
        }
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void APhoneTitle_IsNoTrial()
    {
        Assert.False(TrialMode("WindowsPhone.v4.0.Reach", simulate: false));
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void APhoneTitle_SimulatingTrialMode_IsATrial()
    {
        Assert.True(TrialMode("WindowsPhone.v4.0.Reach", simulate: true));
    }

    [global::CNA.Integration.Tests.NativeFact]
    public void AWindowsTitle_WithoutGamerServices_KeepsXnasStartingAnswer()
    {
        // XNA's IL: Guide.isTrialMode starts true and only the dispatcher's update latches it.
        Assert.True(TrialMode("Windows.v4.0.Reach", simulate: false));
    }
}
