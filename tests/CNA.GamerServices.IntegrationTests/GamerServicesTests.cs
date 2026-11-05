using CNA.Integration.Tests;
using CNA.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.GamerServices;
using Xunit;
using Xunit.Abstractions;

namespace CnaDotnet.GamerServices.IntegrationTests;

[Collection(GamerServicesCollection.Name)]
public class GamerServicesTests(GamerServicesGameFixture fixture, ITestOutputHelper output)
{
    [NativeFact]
    public unsafe void NativeGamerCursor_NullCurrentAndMutationRefusalFollowAbi059()
    {
        fixture.InsideAFrame(_ =>
        {
            Assert.Equal(CnaResult.Success, Native.cna_friend_collection_create_ext(null, 0, out CnaHandle collection));
            CnaHandle cursor = CnaHandle.Zero;
            try
            {
                Assert.Equal(CnaResult.Success, Native.cna_gamer_collection_create_enumerator(collection, out cursor));
                Assert.Equal(CnaResult.Success, Native.cna_gamer_enumerator_get_current(cursor, out CnaHandle current));
                Assert.True(current.IsNull);
                Assert.Equal(CnaResult.Success, Native.cna_gamer_enumerator_move_next(cursor, out byte more));
                Assert.Equal((byte)0, more);
                Assert.Equal(CnaResult.Success, Native.cna_gamer_enumerator_get_current(cursor, out current));
                Assert.True(current.IsNull);
                Assert.Equal(CnaResult.Success, Native.cna_gamer_enumerator_reset(cursor));

                // Even an empty Clear changes the collection version without changing its count.
                Assert.Equal(CnaResult.Success, Native.cna_gamer_collection_clear(collection));
                more = 0xA5;
                Assert.Equal(CnaResult.InvalidState, Native.cna_gamer_enumerator_move_next(cursor, out more));
                Assert.Equal((byte)0xA5, more);
                Assert.Equal(CnaResult.InvalidState, Native.cna_gamer_enumerator_reset(cursor));
                Assert.Equal(CnaResult.Success, Native.cna_gamer_enumerator_get_current(cursor, out current));
                Assert.True(current.IsNull);
            }
            finally
            {
                if (!cursor.IsNull)
                {
                    Assert.Equal(CnaResult.Success, Native.cna_gamer_enumerator_destroy(cursor));
                }
                Assert.Equal(CnaResult.Success, Native.cna_gamer_collection_destroy(collection));
            }
        });
    }

    [NativeFact]
    public void Dispatcher_IsInitializedOnceByTheComponent()
    {
        fixture.InsideAFrame(game =>
        {
            Assert.True(GamerServicesDispatcher.IsInitialized);
            // XNA's order: already initialized is refused before the provider is even looked at.
            Assert.Throws<InvalidOperationException>(() => GamerServicesDispatcher.Initialize(null!));
            Assert.Throws<InvalidOperationException>(() => GamerServicesDispatcher.Initialize(game.Services));
            Assert.Equal(game.Window.Handle, GamerServicesDispatcher.WindowHandle);
        });
    }

    [NativeFact]
    public void AutoSignedInProfile_IsOneManagedGamerWithItsOwnState()
    {
        fixture.InsideAFrame(_ =>
        {
            SignedInGamer gamer = Gamer.SignedInGamers[PlayerIndex.One];
            Assert.NotNull(gamer);
            output.WriteLine($"signed in: {gamer.Gamertag} ({Gamer.SignedInGamers.Count} gamer(s))");
            Assert.Equal("CnaTester", gamer.Gamertag);
            Assert.Equal("CnaTester", gamer.ToString());
            Assert.Equal(PlayerIndex.One, gamer.PlayerIndex);
            Assert.False(gamer.IsSignedInToLive);
            Assert.False(gamer.IsGuest);
            Assert.False(gamer.IsDisposed);

            // One object per native gamer: the indexer, the list and the SignedIn event agree, and
            // state hung off Tag survives the next lookup.
            Assert.Same(gamer, Gamer.SignedInGamers[0]);
            Assert.Same(gamer, Assert.Single(fixture.SignedInEvents));
            gamer.Tag = "player-state";
            Assert.Equal("player-state", Gamer.SignedInGamers[PlayerIndex.One].Tag);
            Assert.Null(Gamer.SignedInGamers[PlayerIndex.Four]);

            Assert.Same(gamer.Presence, gamer.Presence);
            gamer.Presence.PresenceMode = GamerPresenceMode.AtMenu;
            gamer.Presence.PresenceValue = 7;
            Assert.Equal(GamerPresenceMode.AtMenu, gamer.Presence.PresenceMode);
            Assert.Equal(7, gamer.Presence.PresenceValue);

            Assert.NotNull(gamer.Privileges);
            Assert.NotNull(gamer.GameDefaults);
            Assert.True(gamer.PartySize >= 0);
        });
    }

    /// <summary>
    /// Every lookup gets a fresh native handle to an already-wrapped gamer, which the facade releases
    /// at once. A signed-in gamer's handle is its own native kind and only its own release route
    /// takes it; the plain gamer route used to be called and refused silently, leaking a handle per
    /// gamer per lookup. A refused release now throws, so this loop fails on the old route.
    /// </summary>
    [NativeFact]
    public void SignedInGamerLookups_ReleaseEveryDuplicateHandle()
    {
        fixture.InsideAFrame(_ =>
        {
            SignedInGamer first = Gamer.SignedInGamers[PlayerIndex.One];
            for (int lookup = 0; lookup < 200; lookup++)
            {
                Assert.Same(first, Gamer.SignedInGamers[PlayerIndex.One]);
                Assert.Same(first, Gamer.SignedInGamers[0]);
            }
        });
    }

    /// <summary>
    /// A refusal native raises from an exception arrives as that exception (ABI 0.37.0 names it):
    /// the Guide's own argument checks throw XNA's argument exceptions with their parameter names,
    /// not a category's nearest guess.
    /// </summary>
    [NativeFact]
    public void Guide_RefusesWithXnasOwnArgumentExceptions()
    {
        fixture.InsideAFrame(_ =>
        {
            ArgumentOutOfRangeException focus = Assert.Throws<ArgumentOutOfRangeException>(() =>
                Guide.BeginShowMessageBox("Title", "Text", ["OK"], 5, MessageBoxIcon.None, null!, null!));
            Assert.Equal("focusButton", focus.ParamName);

            ArgumentException title = Assert.Throws<ArgumentException>(() =>
                Guide.BeginShowMessageBox(string.Empty, "Text", ["OK"], 0, MessageBoxIcon.None, null!, null!));
            Assert.IsType<ArgumentException>(title);
            Assert.Equal("title", title.ParamName);
        });
    }

    [NativeFact]
    public void Profile_AndAchievements_AreReadable()
    {
        fixture.InsideAFrame(_ =>
        {
            SignedInGamer gamer = Gamer.SignedInGamers[PlayerIndex.One];
            using GamerProfile profile = gamer.GetProfile();
            output.WriteLine($"zone={profile.GamerZone} score={profile.GamerScore} motto='{profile.Motto}'");
            Assert.True(profile.GamerScore >= 0);
            profile.Dispose();
            Assert.True(profile.IsDisposed);

            IAsyncResult pending = gamer.BeginGetProfile(null!, "state");
            Assert.True(pending.IsCompleted);
            Assert.Equal("state", pending.AsyncState);
            using GamerProfile again = gamer.EndGetProfile(pending);
            Assert.Throws<InvalidOperationException>(() => gamer.EndGetProfile(pending));

            using AchievementCollection achievements = gamer.GetAchievements();
            output.WriteLine($"achievements: {achievements.Count}");
            Assert.Throws<NotSupportedException>(
                () => ((ICollection<Achievement>)achievements).Add(null!));
        });
    }

    [NativeFact]
    public void Guide_PropertiesRoundTrip()
    {
        fixture.InsideAFrame(_ =>
        {
            Assert.False(Guide.IsVisible);

            NotificationPosition position = Guide.NotificationPosition;
            Guide.NotificationPosition = NotificationPosition.TopLeft;
            Assert.Equal(NotificationPosition.TopLeft, Guide.NotificationPosition);
            Guide.NotificationPosition = position;

            bool screenSaver = Guide.IsScreenSaverEnabled;
            Guide.IsScreenSaverEnabled = !screenSaver;
            Assert.Equal(!screenSaver, Guide.IsScreenSaverEnabled);
            Guide.IsScreenSaverEnabled = screenSaver;

            Guide.SimulateTrialMode = true;
        });

        // XNA latches IsTrialMode from the license state at each dispatcher update, so the change
        // shows on the next frame, not on the line after the setter.
        fixture.RunFrames(1);
        fixture.InsideAFrame(_ =>
        {
            Assert.True(Guide.SimulateTrialMode);
            Assert.True(Guide.IsTrialMode);
            Guide.SimulateTrialMode = false;
        });

        fixture.RunFrames(1);
        fixture.InsideAFrame(_ => Assert.False(Guide.IsTrialMode));
    }

    /// <summary>
    /// The message box is genuinely asynchronous: it stays open across frames, refuses a second
    /// Guide screen, and completes -- running the caller's callback -- when a button is chosen.
    /// </summary>
    [NativeFact]
    public void Guide_MessageBoxCompletesWithTheChosenButton()
    {
        IAsyncResult? pending = null;
        int callbacks = 0;
        fixture.InsideAFrame(_ =>
        {
            pending = Guide.BeginShowMessageBox(
                PlayerIndex.One, "Title", "Text", ["Yes", "No"], 0, MessageBoxIcon.Alert,
                result => callbacks++, "message-box");
            Assert.False(pending.IsCompleted);
            Assert.Equal("message-box", pending.AsyncState);
        });

        fixture.RunFrames(2);
        fixture.InsideAFrame(_ =>
        {
            Assert.True(Guide.IsVisible);
            Assert.Throws<GuideAlreadyVisibleException>(() => Guide.BeginShowMessageBox(
                "Again", "Text", ["OK"], 0, MessageBoxIcon.None, null!, null!));
            Assert.Equal(CnaResult.Success, Native.cna_guide_simulate_message_box_click_ext(1));
        });

        fixture.RunFrames(2);
        fixture.InsideAFrame(_ =>
        {
            Assert.True(pending!.IsCompleted);
            Assert.Equal(1, callbacks);
            Assert.Equal(1, Guide.EndShowMessageBox(pending));
            Assert.False(Guide.IsVisible);
        });
    }

    [NativeFact]
    public void Guide_CancelledKeyboardInputAnswersNull()
    {
        IAsyncResult? pending = null;
        fixture.InsideAFrame(_ =>
        {
            pending = Guide.BeginShowKeyboardInput(PlayerIndex.One, "Name", "Enter a name", "default", null!, null!);
            Assert.False(pending.IsCompleted);
        });

        fixture.RunFrames(2);
        fixture.InsideAFrame(_ =>
        {
            Assert.True(Guide.IsVisible);
            Assert.Equal(CnaResult.Success, Native.cna_guide_simulate_keyboard_input_cancel_ext());
        });

        fixture.RunFrames(2);
        fixture.InsideAFrame(_ =>
        {
            Assert.True(pending!.IsCompleted);
            Assert.Null(Guide.EndShowKeyboardInput(pending));
        });
    }

    [NativeFact]
    public void LeaderboardIdentity_UsesTheKeyName()
    {
        LeaderboardIdentity identity = LeaderboardIdentity.Create(LeaderboardKey.BestScoreRecent, 3);
        Assert.Equal("BestScoreRecent", identity.Key);
        Assert.Equal(3, identity.GameMode);
        Assert.Equal(0, LeaderboardIdentity.Create(LeaderboardKey.BestTimeLifeTime).GameMode);
    }
}
