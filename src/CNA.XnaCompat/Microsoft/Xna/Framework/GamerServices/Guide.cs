using System.Text;
using CNA.Interop;

namespace Microsoft.Xna.Framework.GamerServices;

/// <summary>
/// XNA's Guide, presented by CNA itself inside the running game once the dispatcher is initialized.
/// The message box and keyboard input are genuinely asynchronous: they complete when the user
/// answers, on the game thread, through the dispatcher update that is running at the time.
/// </summary>
public static class Guide
{
    private static readonly object s_messageBoxOwner = new();
    private static readonly object s_keyboardOwner = new();

    public static bool IsScreenSaverEnabled
    {
        get => Flag(Native.cna_guide_get_is_screen_saver_enabled, nameof(IsScreenSaverEnabled));
        set => GamerServicesInterop.Check(
            Native.cna_guide_set_is_screen_saver_enabled(GamerServicesInterop.Bool(value)), nameof(IsScreenSaverEnabled));
    }

    /// <summary>XNA declares an internal setter here, for its own Guide host; CNA's Guide reports its
    /// own visibility, so nothing in this facade sets it.</summary>
    public static bool IsVisible
    {
        // A phone title whose Guide has not shown itself has no Guide to be visible (CSX-119).
        get => (!PhoneTitle.Active || PhoneTitle.PumpsGamerServices || GamerServicesDispatcher.IsInitialized)
            && Flag(Native.cna_guide_get_is_visible, nameof(IsVisible));
        internal set => throw new InvalidOperationException("The Guide's visibility is CNA's own state.");
    }

    public static NotificationPosition NotificationPosition
    {
        get
        {
            GamerServicesInterop.Check(Native.cna_guide_get_notification_position(out uint value), nameof(NotificationPosition));
            return (NotificationPosition)value;
        }
        set => GamerServicesInterop.Check(Native.cna_guide_set_notification_position((uint)value), nameof(NotificationPosition));
    }

    /// <summary>XNA's internal setter is its licence check's; CNA answers from its own license state
    /// and <see cref="SimulateTrialMode"/>, so nothing in this facade sets it. A Windows Phone title
    /// answers the phone's license check instead, which needed no gamer services: a title its
    /// developer deployed is no trial unless <see cref="SimulateTrialMode"/> makes it one (CSX-137).
    /// On Windows, XNA's answer starts true and the dispatcher's first update sets it.</summary>
    public static bool IsTrialMode
    {
        get => PhoneTitle.Active ? SimulateTrialMode : Flag(Native.cna_guide_get_is_trial_mode, nameof(IsTrialMode));
        internal set => throw new InvalidOperationException("Trial mode is CNA's own license state.");
    }

    public static bool SimulateTrialMode
    {
        get => Flag(Native.cna_guide_get_simulate_trial_mode, nameof(SimulateTrialMode));
        set => GamerServicesInterop.Check(
            Native.cna_guide_set_simulate_trial_mode(GamerServicesInterop.Bool(value)), nameof(SimulateTrialMode));
    }

    public static IAsyncResult BeginShowMessageBox(
        PlayerIndex player, string title, string text, IEnumerable<string> buttons, int focusButton,
        MessageBoxIcon icon, AsyncCallback callback, object state)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(buttons);
        EnsurePhoneGuide();
        ThrowIfVisible();

        string[] captions = buttons.ToArray();
        var result = new GamerServicesAsyncResult(callback, state, s_messageBoxOwner);
        nint context = result.NativeContext();
        CnaResult status = WithStrings(captions, views => GamerServicesInterop.WithString(title, titleView =>
            GamerServicesInterop.WithString(text, textView => BeginMessageBox(
                (uint)player, titleView, textView, views, focusButton, (uint)icon, context))));
        GamerServicesInterop.Check(status, nameof(BeginShowMessageBox));
        return result;
    }

    public static IAsyncResult BeginShowMessageBox(
        string title, string text, IEnumerable<string> buttons, int focusButton, MessageBoxIcon icon,
        AsyncCallback callback, object state) =>
        BeginShowMessageBox(PlayerIndex.One, title, text, buttons, focusButton, icon, callback, state);

    /// <summary>The chosen button, or null when the box closed without a choice.</summary>
    public static int? EndShowMessageBox(IAsyncResult result)
    {
        GamerServicesAsyncResult ours = GamerServicesAsyncResult.ForEnd(result, s_messageBoxOwner);
        _ = ours.AsyncWaitHandle.WaitOne(0); // Reject disposal before entering the native modal pump.
        int button = -1;
        GamerServicesInterop.Check(Native.cna_guide_end_show_message_box(out byte hasChoice, out button), nameof(EndShowMessageBox));
        // Native End pumps a pending dialog first; then honor the event exposed to managed callers.
        ours.AsyncWaitHandle.WaitOne();
        return hasChoice != 0 ? button : null;
    }

    public static void ShowSignIn(int paneCount, bool onlineOnly) =>
        GamerServicesInterop.Check(
            Native.cna_guide_show_sign_in(paneCount, GamerServicesInterop.Bool(onlineOnly)), nameof(ShowSignIn));

    public static IAsyncResult BeginShowKeyboardInput(
        PlayerIndex player, string title, string description, string defaultText, AsyncCallback callback, object state) =>
        BeginShowKeyboardInput(player, title, description, defaultText, callback, state, usePasswordMode: false);

    public static IAsyncResult BeginShowKeyboardInput(
        PlayerIndex player, string title, string description, string defaultText, AsyncCallback callback,
        object state, bool usePasswordMode)
    {
        EnsurePhoneGuide();
        ThrowIfVisible();
        var result = new GamerServicesAsyncResult(callback, state, s_keyboardOwner);
        nint context = result.NativeContext();
        CnaResult status = GamerServicesInterop.WithString(title, titleView =>
            GamerServicesInterop.WithString(description, descriptionView =>
                GamerServicesInterop.WithString(defaultText, defaultView =>
                    Native.cna_guide_begin_show_keyboard_input(
                        (uint)player, titleView, descriptionView, defaultView,
                        GamerServicesInterop.Bool(usePasswordMode), GamerServicesAsyncResult.NativeCallback, context))));
        GamerServicesInterop.Check(status, nameof(BeginShowKeyboardInput));
        return result;
    }

    /// <summary>The confirmed text, or null when the user cancelled -- XNA's contract.</summary>
    public static unsafe string EndShowKeyboardInput(IAsyncResult result)
    {
        GamerServicesAsyncResult ours = GamerServicesAsyncResult.ForEnd(result, s_keyboardOwner);
        _ = ours.AsyncWaitHandle.WaitOne(0);
        string text = GamerServicesInterop.ReadString(
            Native.cna_guide_end_show_keyboard_input_size, Native.cna_guide_end_show_keyboard_input, nameof(EndShowKeyboardInput));
        GamerServicesInterop.Check(Native.cna_guide_was_keyboard_input_canceled_ext(out byte canceled), nameof(EndShowKeyboardInput));
        // The native event is independent of this result's caller-resettable managed event.
        ours.AsyncWaitHandle.WaitOne();
        return canceled != 0 ? null! : text;
    }

    public static void ShowMessages(PlayerIndex player) =>
        GamerServicesInterop.Check(Native.cna_guide_show_messages((uint)player), nameof(ShowMessages));

    public static void ShowFriends(PlayerIndex player) =>
        GamerServicesInterop.Check(Native.cna_guide_show_friends((uint)player), nameof(ShowFriends));

    public static void ShowPlayers(PlayerIndex player) =>
        GamerServicesInterop.Check(Native.cna_guide_show_players((uint)player), nameof(ShowPlayers));

    public static void ShowFriendRequest(PlayerIndex player, Gamer gamer)
    {
        ArgumentNullException.ThrowIfNull(gamer);
        GamerServicesInterop.Check(Native.cna_guide_show_friend_request((uint)player, gamer.Handle), nameof(ShowFriendRequest));
    }

    public static void ShowPlayerReview(PlayerIndex player, Gamer gamer)
    {
        ArgumentNullException.ThrowIfNull(gamer);
        GamerServicesInterop.Check(Native.cna_guide_show_player_review((uint)player, gamer.Handle), nameof(ShowPlayerReview));
    }

    public static void ShowGamerCard(PlayerIndex player, Gamer gamer)
    {
        ArgumentNullException.ThrowIfNull(gamer);
        GamerServicesInterop.Check(Native.cna_guide_show_gamer_card((uint)player, gamer.Handle), nameof(ShowGamerCard));
    }

    public static void ShowParty(PlayerIndex player) =>
        GamerServicesInterop.Check(Native.cna_guide_show_party((uint)player), nameof(ShowParty));

    public static void ShowPartySessions(PlayerIndex player) =>
        GamerServicesInterop.Check(Native.cna_guide_show_party_sessions((uint)player), nameof(ShowPartySessions));

    public static unsafe void ShowComposeMessage(PlayerIndex player, string text, IEnumerable<Gamer> recipients)
    {
        CnaHandle[] handles = recipients?.Select(gamer => gamer.Handle).ToArray() ?? [];
        fixed (CnaHandle* list = handles)
        {
            CnaHandle* pointer = list;
            GamerServicesInterop.Check(
                GamerServicesInterop.WithString(text, view =>
                    Native.cna_guide_show_compose_message((uint)player, view, pointer, (ulong)handles.Length)),
                nameof(ShowComposeMessage));
        }
    }

    public static void DelayNotifications(TimeSpan delay) =>
        GamerServicesInterop.Check(Native.cna_guide_delay_notifications(delay.Ticks), nameof(DelayNotifications));

    public static unsafe void ShowGameInvite(PlayerIndex player, IEnumerable<Gamer> recipients)
    {
        CnaHandle[] handles = recipients?.Select(gamer => gamer.Handle).ToArray() ?? [];
        fixed (CnaHandle* list = handles)
        {
            GamerServicesInterop.Check(
                Native.cna_guide_show_game_invite((uint)player, list, (ulong)handles.Length), nameof(ShowGameInvite));
        }
    }

    public static void ShowGameInvite(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        GamerServicesInterop.Check(
            GamerServicesInterop.WithString(sessionId, Native.cna_guide_show_game_invite_for_session), nameof(ShowGameInvite));
    }

    public static void ShowMarketplace(PlayerIndex player) =>
        GamerServicesInterop.Check(Native.cna_guide_show_marketplace((uint)player), nameof(ShowMarketplace));

    private delegate CnaResult FlagQuery(out byte value);

    private static bool Flag(FlagQuery query, string operation)
    {
        GamerServicesInterop.Check(query(out byte value), operation);
        return value != 0;
    }

    private static void EnsurePhoneGuide() => PhoneTitle.EnsureGuide(
        () => GamerServicesDispatcher.IsInitialized,
        () => GamerServicesDispatcher.Initialize(PhoneTitle.Title?.Services
            ?? throw new InvalidOperationException("CNA's gamer services run inside a game; create the Game first.")));

    /// <summary>XNA's IL checks this before anything else it validates for a Guide screen.</summary>
    private static void ThrowIfVisible()
    {
        if (IsVisible)
        {
            throw new GuideAlreadyVisibleException("The Guide is already visible.");
        }
    }

    private static unsafe CnaResult BeginMessageBox(
        uint player, CnaStringView title, CnaStringView text, CnaStringView[] buttons, int focusButton, uint icon, nint context)
    {
        fixed (CnaStringView* list = buttons)
        {
            return Native.cna_guide_begin_show_message_box(
                player, title, text, list, (ulong)buttons.Length, focusButton, icon, GamerServicesAsyncResult.NativeCallback, context);
        }
    }

    /// <summary>Marshals every caption at once; the views live until <paramref name="call"/> returns.</summary>
    private static unsafe CnaResult WithStrings(string[] values, Func<CnaStringView[], CnaResult> call)
    {
        var handles = new System.Runtime.InteropServices.GCHandle[values.Length];
        var views = new CnaStringView[values.Length];
        try
        {
            for (int index = 0; index < values.Length; index++)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(values[index] ?? string.Empty);
                handles[index] = System.Runtime.InteropServices.GCHandle.Alloc(bytes, System.Runtime.InteropServices.GCHandleType.Pinned);
                views[index] = new CnaStringView((byte*)handles[index].AddrOfPinnedObject(), (ulong)bytes.Length);
            }

            return call(views);
        }
        finally
        {
            foreach (System.Runtime.InteropServices.GCHandle handle in handles)
            {
                if (handle.IsAllocated)
                {
                    handle.Free();
                }
            }
        }
    }
}
