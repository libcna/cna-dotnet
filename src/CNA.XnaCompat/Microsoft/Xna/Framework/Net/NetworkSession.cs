using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using CNA;
using CNA.Interop;
using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net;

/// <summary>
/// XNA's network session, over CNA's: matchmaking, transport, host migration, readiness and the
/// session state machine are native's. What this type adds is XNA's managed shape around it.
///
/// <list type="bullet">
/// <item>One managed object per gamer. Native hands out a fresh view of a gamer for every lookup;
/// the session wraps the first and recognises the rest by the identity stamped on the native gamer,
/// and keeps its four rosters as live lists refreshed when membership changes.</item>
/// <item>Events are native's, delivered during <see cref="Update"/>. An exception a handler throws
/// cannot unwind through native, so it is held and rethrown from <see cref="Update"/>, where XNA's
/// would have surfaced.</item>
/// <item>Native is synchronous behind every Begin, so each Begin runs the operation at once and
/// completes its result synchronously; a failure is thrown by End, as XNA's is.</item>
/// </list>
/// </summary>
public sealed unsafe class NetworkSession : IDisposable
{
    public const int MaxSupportedGamers = 31;
    public const int MaxPreviousGamers = 100;

    private const uint RosterAll = 0;
    private const uint RosterLocal = 1;
    private const uint RosterRemote = 2;
    private const uint RosterPrevious = 3;

    private static readonly object s_createOwner = new();
    private static readonly object s_findOwner = new();
    private static readonly object s_joinOwner = new();
    private static readonly object s_joinInvitedOwner = new();
    private static EventHandler<InviteAcceptedEventArgs>? s_inviteAccepted;
    private static CnaHandle s_inviteRegistration;

    private readonly NativeResourceHandle _handle;
    private readonly GCHandle _self;
    private readonly List<CnaHandle> _registrations = new();
    private readonly List<NetworkGamer> _allGamers = new();
    private readonly List<LocalNetworkGamer> _localGamers = new();
    private readonly List<NetworkGamer> _remoteGamers = new();
    private readonly List<NetworkGamer> _previousGamers = new();
    private readonly HashSet<NetworkGamer> _wrapped = new();
    private readonly Dictionary<NetworkGamer, NetworkMachine> _machines = new();
    private readonly List<ExceptionDispatchInfo> _handlerFailures = new();
    private CnaHandle _scratchReader;
    private EventHandler<GamerJoinedEventArgs>? _gamerJoined;
    private bool _insideUpdate;

    private NetworkSession(CnaHandle handle)
    {
        _handle = new NativeResourceHandle(
            handle.Value, value => Native.cna_network_session_destroy(new CnaHandle(value)).IsSuccess());
        _self = GCHandle.Alloc(this, GCHandleType.Weak);
        AllGamers = new GamerCollection<NetworkGamer>(_allGamers);
        LocalGamers = new GamerCollection<LocalNetworkGamer>(_localGamers);
        RemoteGamers = new GamerCollection<NetworkGamer>(_remoteGamers);
        PreviousGamers = new GamerCollection<NetworkGamer>(_previousGamers);
        SessionProperties = NetworkSessionProperties.CreateLive(ReadProperties, WriteProperty);
        try
        {
            RefreshRosters();
            Subscribe();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    ~NetworkSession()
    {
        Dispose(false);
    }

    internal CnaHandle Handle
    {
        get
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(NetworkSession));
            }

            return new CnaHandle(_handle.DangerousGetHandle());
        }
    }

    private bool _isDisposed;

    public bool IsDisposed => _isDisposed;

    public GamerCollection<NetworkGamer> AllGamers { get; }

    public GamerCollection<LocalNetworkGamer> LocalGamers { get; }

    public GamerCollection<NetworkGamer> RemoteGamers { get; }

    public GamerCollection<NetworkGamer> PreviousGamers { get; }

    public NetworkSessionProperties SessionProperties { get; }

    public NetworkSessionType SessionType => (NetworkSessionType)ReadUInt32(Native.cna_network_session_get_session_type, nameof(SessionType));

    public NetworkSessionState SessionState => (NetworkSessionState)ReadUInt32(Native.cna_network_session_get_session_state, nameof(SessionState));

    public bool IsHost => ReadFlag(Native.cna_network_session_get_is_host, nameof(IsHost));

    public bool IsEveryoneReady => ReadFlag(Native.cna_network_session_get_is_everyone_ready, nameof(IsEveryoneReady));

    public NetworkGamer? Host
    {
        get
        {
            GamerServicesInterop.Check(Native.cna_network_session_get_host(Handle, out CnaHandle host), nameof(Host));
            return TakeView(host);
        }
    }

    public int MaxGamers
    {
        get => ReadInt32(Native.cna_network_session_get_max_gamers, nameof(MaxGamers));
        set
        {
            if (value < MinSupportedGamers(SessionType) || value > MaxSupportedGamers || value < AllGamers.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            GamerServicesInterop.Check(Native.cna_network_session_set_max_gamers(Handle, value), nameof(MaxGamers));
        }
    }

    public int PrivateGamerSlots
    {
        get => ReadInt32(Native.cna_network_session_get_private_gamer_slots, nameof(PrivateGamerSlots));
        set
        {
            if (value < 0 || value > MaxGamers)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            GamerServicesInterop.Check(Native.cna_network_session_set_private_gamer_slots(Handle, value), nameof(PrivateGamerSlots));
        }
    }

    public float SimulatedPacketLoss
    {
        get
        {
            GamerServicesInterop.Check(Native.cna_network_session_get_simulated_packet_loss(Handle, out float value), nameof(SimulatedPacketLoss));
            return value;
        }
        set => GamerServicesInterop.Check(Native.cna_network_session_set_simulated_packet_loss(Handle, value), nameof(SimulatedPacketLoss));
    }

    public TimeSpan SimulatedLatency
    {
        get
        {
            GamerServicesInterop.Check(Native.cna_network_session_get_simulated_latency_ticks(Handle, out long ticks), nameof(SimulatedLatency));
            return new TimeSpan(ticks);
        }
        set => GamerServicesInterop.Check(
            Native.cna_network_session_set_simulated_latency_ticks(Handle, value.Ticks), nameof(SimulatedLatency));
    }

    public int BytesPerSecondSent => ReadInt32(Native.cna_network_session_get_bytes_per_second_sent, nameof(BytesPerSecondSent));

    public int BytesPerSecondReceived => ReadInt32(Native.cna_network_session_get_bytes_per_second_received, nameof(BytesPerSecondReceived));

    public bool AllowJoinInProgress
    {
        get => ReadFlag(Native.cna_network_session_get_allow_join_in_progress, nameof(AllowJoinInProgress));
        set => GamerServicesInterop.Check(
            Native.cna_network_session_set_allow_join_in_progress(Handle, GamerServicesInterop.Bool(value)), nameof(AllowJoinInProgress));
    }

    public bool AllowHostMigration
    {
        get => ReadFlag(Native.cna_network_session_get_allow_host_migration, nameof(AllowHostMigration));
        set => GamerServicesInterop.Check(
            Native.cna_network_session_set_allow_host_migration(Handle, GamerServicesInterop.Bool(value)), nameof(AllowHostMigration));
    }

    public event EventHandler<NetworkSessionEndedEventArgs> SessionEnded = null!;

    /// <summary>XNA replays this event to a new handler for every gamer already in the session.</summary>
    public event EventHandler<GamerJoinedEventArgs> GamerJoined
    {
        add
        {
            _gamerJoined += value;
            foreach (NetworkGamer gamer in AllGamers)
            {
                value?.Invoke(this, new GamerJoinedEventArgs(gamer));
            }
        }
        remove => _gamerJoined -= value;
    }

    public event EventHandler<GamerLeftEventArgs> GamerLeft = null!;

    public event EventHandler<GameStartedEventArgs> GameStarted = null!;

    public event EventHandler<GameEndedEventArgs> GameEnded = null!;

    public event EventHandler<HostChangedEventArgs> HostChanged = null!;

    public event EventHandler<WriteLeaderboardsEventArgs> WriteArbitratedLeaderboard = null!;

    public event EventHandler<WriteLeaderboardsEventArgs> WriteTrueSkill = null!;

    public event EventHandler<WriteLeaderboardsEventArgs> WriteUnarbitratedLeaderboard = null!;

    public static event EventHandler<InviteAcceptedEventArgs> InviteAccepted
    {
        add
        {
            if (s_inviteAccepted is null && value is not null)
            {
                GamerServicesInterop.Check(
                    Native.cna_network_session_subscribe_invite_accepted(
                        (nint)(delegate* unmanaged[Cdecl]<CnaInviteAcceptedEventInfo*, nint, void>)&OnInviteAccepted, 0,
                        out s_inviteRegistration),
                    nameof(InviteAccepted));
            }

            s_inviteAccepted += value;
        }
        remove
        {
            s_inviteAccepted -= value;
            if (s_inviteAccepted is null && !s_inviteRegistration.IsNull)
            {
                _ = Native.cna_network_session_unsubscribe(s_inviteRegistration);
                s_inviteRegistration = CnaHandle.Zero;
            }
        }
    }

    // ---- Creation, search and joining --------------------------------------------------------

    public static NetworkSession Create(NetworkSessionType sessionType, int maxLocalGamers, int maxGamers) =>
        EndCreate(BeginCreate(sessionType, maxLocalGamers, maxGamers, null!, null!));

    public static NetworkSession Create(
        NetworkSessionType sessionType, int maxLocalGamers, int maxGamers, int privateGamerSlots,
        NetworkSessionProperties sessionProperties) =>
        EndCreate(BeginCreate(sessionType, maxLocalGamers, maxGamers, privateGamerSlots, sessionProperties, null!, null!));

    public static NetworkSession Create(
        NetworkSessionType sessionType, IEnumerable<SignedInGamer> localGamers, int maxGamers, int privateGamerSlots,
        NetworkSessionProperties sessionProperties) =>
        EndCreate(BeginCreate(sessionType, localGamers, maxGamers, privateGamerSlots, sessionProperties, null!, null!));

    public static IAsyncResult BeginCreate(
        NetworkSessionType sessionType, int maxLocalGamers, int maxGamers, AsyncCallback callback, object asyncState) =>
        BeginCreate(sessionType, maxLocalGamers, maxGamers, 0, null!, callback, asyncState);

    public static IAsyncResult BeginCreate(
        NetworkSessionType sessionType, int maxLocalGamers, int maxGamers, int privateGamerSlots,
        NetworkSessionProperties sessionProperties, AsyncCallback callback, object asyncState)
    {
        ValidateCreate(sessionType, maxLocalGamers, maxGamers, privateGamerSlots);
        return Run(s_createOwner, callback, asyncState, () => WithProperties(sessionProperties, properties =>
        {
            GamerServicesInterop.Check(
                Native.cna_network_session_create_with_properties_async(
                    (uint)sessionType, maxLocalGamers, maxGamers, privateGamerSlots, properties, 0, 0, out CnaHandle session),
                nameof(Create));
            return new NetworkSession(session);
        }));
    }

    public static IAsyncResult BeginCreate(
        NetworkSessionType sessionType, IEnumerable<SignedInGamer> localGamers, int maxGamers, int privateGamerSlots,
        NetworkSessionProperties sessionProperties, AsyncCallback callback, object asyncState)
    {
        CnaHandle[] gamers = LocalGamerHandles(localGamers);
        ValidateCreate(sessionType, gamers.Length, maxGamers, privateGamerSlots);
        return Run(s_createOwner, callback, asyncState, () => WithProperties(sessionProperties, properties =>
        {
            CnaHandle session;
            fixed (CnaHandle* list = gamers)
            {
                GamerServicesInterop.Check(
                    Native.cna_network_session_create_with_local_gamers_async(
                        (uint)sessionType, list, (ulong)gamers.Length, maxGamers, privateGamerSlots, properties, 0, 0, out session),
                    nameof(Create));
            }

            return new NetworkSession(session);
        }));
    }

    public static NetworkSession EndCreate(IAsyncResult result) => (NetworkSession)End(result, s_createOwner);

    public static AvailableNetworkSessionCollection Find(
        NetworkSessionType sessionType, int maxLocalGamers, NetworkSessionProperties searchProperties) =>
        EndFind(BeginFind(sessionType, maxLocalGamers, searchProperties, null!, null!));

    public static AvailableNetworkSessionCollection Find(
        NetworkSessionType sessionType, IEnumerable<SignedInGamer> localGamers, NetworkSessionProperties searchProperties) =>
        EndFind(BeginFind(sessionType, localGamers, searchProperties, null!, null!));

    public static IAsyncResult BeginFind(
        NetworkSessionType sessionType, int maxLocalGamers, NetworkSessionProperties searchProperties,
        AsyncCallback callback, object asyncState)
    {
        ValidateFind(sessionType, maxLocalGamers);
        return Run(s_findOwner, callback, asyncState, () => WithProperties(searchProperties, properties =>
        {
            GamerServicesInterop.Check(
                Native.cna_network_session_find_async((uint)sessionType, maxLocalGamers, properties, 0, 0, out CnaHandle found),
                nameof(Find));
            return AvailableNetworkSessionCollection.FromNative(found);
        }));
    }

    public static IAsyncResult BeginFind(
        NetworkSessionType sessionType, IEnumerable<SignedInGamer> localGamers, NetworkSessionProperties searchProperties,
        AsyncCallback callback, object asyncState)
    {
        CnaHandle[] gamers = LocalGamerHandles(localGamers);
        ValidateFind(sessionType, gamers.Length);
        return Run(s_findOwner, callback, asyncState, () => WithProperties(searchProperties, properties =>
        {
            CnaHandle found;
            fixed (CnaHandle* list = gamers)
            {
                GamerServicesInterop.Check(
                    Native.cna_network_session_find_with_local_gamers_async(
                        (uint)sessionType, list, (ulong)gamers.Length, properties, 0, 0, out found),
                    nameof(Find));
            }

            return AvailableNetworkSessionCollection.FromNative(found);
        }));
    }

    public static AvailableNetworkSessionCollection EndFind(IAsyncResult result) =>
        (AvailableNetworkSessionCollection)End(result, s_findOwner);

    public static NetworkSession Join(AvailableNetworkSession availableSession) =>
        EndJoin(BeginJoin(availableSession, null!, null!));

    public static IAsyncResult BeginJoin(AvailableNetworkSession availableSession, AsyncCallback callback, object asyncState)
    {
        ArgumentNullException.ThrowIfNull(availableSession);
        if (availableSession.Parent.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(availableSession));
        }

        return Run(s_joinOwner, callback, asyncState, () =>
        {
            CheckJoin(Native.cna_network_session_join_async(availableSession.Handle, 0, 0, out CnaHandle session), nameof(Join));
            return new NetworkSession(session);
        });
    }

    public static NetworkSession EndJoin(IAsyncResult result) => (NetworkSession)End(result, s_joinOwner);

    public static NetworkSession JoinInvited(int maxLocalGamers) =>
        EndJoinInvited(BeginJoinInvited(maxLocalGamers, null!, null!));

    public static NetworkSession JoinInvited(IEnumerable<SignedInGamer> localGamers) =>
        EndJoinInvited(BeginJoinInvited(localGamers, null!, null!));

    public static IAsyncResult BeginJoinInvited(int maxLocalGamers, AsyncCallback callback, object asyncState)
    {
        if (maxLocalGamers is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLocalGamers));
        }

        return Run(s_joinInvitedOwner, callback, asyncState, () =>
        {
            CheckJoin(Native.cna_network_session_join_invited_async(maxLocalGamers, 0, 0, out CnaHandle session), nameof(JoinInvited));
            return new NetworkSession(session);
        });
    }

    public static IAsyncResult BeginJoinInvited(IEnumerable<SignedInGamer> localGamers, AsyncCallback callback, object asyncState)
    {
        CnaHandle[] gamers = LocalGamerHandles(localGamers);
        return Run(s_joinInvitedOwner, callback, asyncState, () =>
        {
            CnaHandle session;
            fixed (CnaHandle* list = gamers)
            {
                CheckJoin(
                    Native.cna_network_session_join_invited_with_local_gamers_async(list, (ulong)gamers.Length, 0, 0, out session),
                    nameof(JoinInvited));
            }

            return new NetworkSession(session);
        });
    }

    public static NetworkSession EndJoinInvited(IAsyncResult result) => (NetworkSession)End(result, s_joinInvitedOwner);

    // ---- The session --------------------------------------------------------------------------

    /// <summary>
    /// Pumps native: packets, membership, host migration and state changes, raising their events.
    /// A handler's exception is rethrown here once native has finished the update.
    /// </summary>
    public void Update()
    {
        CnaHandle handle = Handle;
        _insideUpdate = true;
        try
        {
            GamerServicesInterop.Check(Native.cna_network_session_update(handle), nameof(Update));
        }
        finally
        {
            _insideUpdate = false;
        }

        if (_handlerFailures.Count > 0)
        {
            ExceptionDispatchInfo failure = _handlerFailures[0];
            _handlerFailures.Clear();
            failure.Throw();
        }
    }

    public void StartGame() => GamerServicesInterop.Check(Native.cna_network_session_start_game(Handle), nameof(StartGame));

    public void EndGame() => GamerServicesInterop.Check(Native.cna_network_session_end_game(Handle), nameof(EndGame));

    public void ResetReady() => GamerServicesInterop.Check(Native.cna_network_session_reset_ready(Handle), nameof(ResetReady));

    public NetworkGamer FindGamerById(byte gamerId)
    {
        GamerServicesInterop.Check(Native.cna_network_session_find_gamer_by_id(Handle, gamerId, out CnaHandle gamer), nameof(FindGamerById));
        return TakeView(gamer)!;
    }

    public void AddLocalGamer(SignedInGamer gamer)
    {
        ArgumentNullException.ThrowIfNull(gamer);
        GamerServicesInterop.Check(Native.cna_network_session_add_local_gamer(Handle, gamer.Handle), nameof(AddLocalGamer));
        // Native adds the gamer and raises GamerJoined on the next Update.
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Native refuses to destroy a session while a view of its gamers is open, so the registrations
    /// and every gamer view this session handed out are released before it. From the finalizer the
    /// handles are queued to the owning thread instead (NativeResourceHandle), whose drain retries
    /// a parent after its children.
    /// </summary>
    private void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (!disposing)
        {
            return;
        }

        CnaHandle handle = new(_handle.DangerousGetHandle());
        _ = Native.cna_network_session_dispose(handle);
        foreach (CnaHandle registration in _registrations)
        {
            _ = Native.cna_network_session_unsubscribe(registration);
        }

        _registrations.Clear();
        foreach (NetworkGamer gamer in _wrapped)
        {
            gamer.Release();
        }

        _wrapped.Clear();
        _machines.Clear();
        if (!_scratchReader.IsNull)
        {
            _ = Native.cna_packet_reader_destroy(_scratchReader);
            _scratchReader = CnaHandle.Zero;
        }

        _handle.Dispose();
        if (_self.IsAllocated)
        {
            _self.Free();
        }
    }

    // ---- Gamers --------------------------------------------------------------------------------

    /// <summary>A native packet reader the receive route fills before its bytes are copied out.</summary>
    internal CnaHandle ScratchReader
    {
        get
        {
            if (_scratchReader.IsNull)
            {
                GamerServicesInterop.Check(Native.cna_packet_reader_create(1, out _scratchReader), nameof(LocalNetworkGamer.ReceiveData));
            }

            return _scratchReader;
        }
    }

    /// <summary>Rebuilds the four rosters from native, keeping every gamer's managed object.</summary>
    internal void RefreshRosters()
    {
        Fill(RosterAll, _allGamers);
        Fill(RosterLocal, _localGamers);
        Fill(RosterRemote, _remoteGamers);
        Fill(RosterPrevious, _previousGamers);
    }

    /// <summary>
    /// The managed gamer a borrowed handle names, without keeping the handle. A gamer the session
    /// has not wrapped yet is reached through a roster refresh first.
    /// </summary>
    internal NetworkGamer? Resolve(CnaHandle handle)
    {
        if (handle.IsNull)
        {
            return null;
        }

        if (Gamer.FromHandle(handle) is NetworkGamer known)
        {
            return known;
        }

        RefreshRosters();
        return Gamer.FromHandle(handle) as NetworkGamer;
    }

    /// <summary>Resolves an owned view (a host, a lookup, a packet's sender) and releases it.</summary>
    internal NetworkGamer? TakeView(CnaHandle view)
    {
        if (view.IsNull)
        {
            return null;
        }

        try
        {
            return Resolve(view);
        }
        finally
        {
            GamerServicesInterop.Check(Native.cna_network_gamer_destroy(view), nameof(NetworkGamer));
        }
    }

    internal NetworkGamer TakeSender(CnaHandle sender) => TakeView(sender)!;

    /// <summary>One machine object per machine, whichever of its gamers it is reached through.</summary>
    internal NetworkMachine MachineOf(NetworkGamer gamer)
    {
        if (_machines.TryGetValue(gamer, out NetworkMachine? known))
        {
            return known;
        }

        var machine = new NetworkMachine(this, gamer);
        machine.Refresh();
        foreach (NetworkGamer member in machine.Gamers)
        {
            if (_machines.TryGetValue(member, out NetworkMachine? existing))
            {
                _machines[gamer] = existing;
                return existing;
            }
        }

        _machines[gamer] = machine;
        foreach (NetworkGamer member in machine.Gamers)
        {
            _machines[member] = machine;
        }

        return machine;
    }

    private void Fill<T>(uint roster, List<T> list)
        where T : NetworkGamer
    {
        GamerServicesInterop.Check(Native.cna_network_session_get_gamer_count(Handle, roster, out int count), nameof(AllGamers));
        list.Clear();
        for (int index = 0; index < count; index++)
        {
            GamerServicesInterop.Check(Native.cna_network_session_get_gamer(Handle, roster, index, out CnaHandle view), nameof(AllGamers));
            NetworkGamer gamer = Gamer.Wrap(view, ownsHandle: true, CreateGamer, Native.cna_network_gamer_destroy);
            list.Add((T)gamer);
        }
    }

    private NetworkGamer CreateGamer(CnaHandle view, bool owned)
    {
        GamerServicesInterop.Check(Native.cna_network_gamer_get_is_local(view, out byte local), nameof(NetworkGamer));
        NetworkGamer gamer = local != 0 ? new LocalNetworkGamer(this, view) : new NetworkGamer(this, view);
        _wrapped.Add(gamer);
        return gamer;
    }

    // ---- Properties ----------------------------------------------------------------------------

    private int?[] ReadProperties()
    {
        GamerServicesInterop.Check(Native.cna_network_session_copy_session_properties(Handle, out CnaHandle properties), nameof(SessionProperties));
        try
        {
            return NetworkSessionProperties.ReadNative(properties);
        }
        finally
        {
            _ = Native.cna_network_session_properties_destroy(properties);
        }
    }

    /// <summary>XNA validates a property change before storing it: here native refuses one from a
    /// machine that is not the host, and sends an accepted one to the others.</summary>
    private void WriteProperty(int index, int? value)
    {
        GamerServicesInterop.Check(Native.cna_network_session_copy_session_properties(Handle, out CnaHandle properties), nameof(SessionProperties));
        try
        {
            GamerServicesInterop.Check(
                Native.cna_network_session_properties_set_item(properties, index, NetworkSessionProperties.ToNative(value)),
                nameof(SessionProperties));
            GamerServicesInterop.Check(
                Native.cna_network_session_replace_session_properties(Handle, properties), nameof(SessionProperties));
        }
        finally
        {
            _ = Native.cna_network_session_properties_destroy(properties);
        }
    }

    // ---- Events --------------------------------------------------------------------------------

    private void Subscribe()
    {
        nint context = GCHandle.ToIntPtr(_self);
        Register(Native.cna_network_session_subscribe_game_started(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaGameStartedEventInfo*, nint, void>)&OnGameStarted, context, out CnaHandle r1), r1);
        Register(Native.cna_network_session_subscribe_game_ended(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaGameEndedEventInfo*, nint, void>)&OnGameEnded, context, out CnaHandle r2), r2);
        Register(Native.cna_network_session_subscribe_gamer_joined(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaGamerJoinedEventInfo*, nint, void>)&OnGamerJoined, context, out CnaHandle r3), r3);
        Register(Native.cna_network_session_subscribe_gamer_left(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaGamerLeftEventInfo*, nint, void>)&OnGamerLeft, context, out CnaHandle r4), r4);
        Register(Native.cna_network_session_subscribe_host_changed(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaHostChangedEventInfo*, nint, void>)&OnHostChanged, context, out CnaHandle r5), r5);
        Register(Native.cna_network_session_subscribe_session_ended(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaNetworkSessionEndedEventInfo*, nint, void>)&OnSessionEnded, context, out CnaHandle r6), r6);
        Register(Native.cna_network_session_subscribe_write_arbitrated_leaderboard(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaWriteLeaderboardsEventInfo*, nint, void>)&OnWriteArbitrated, context, out CnaHandle r7), r7);
        Register(Native.cna_network_session_subscribe_write_unarbitrated_leaderboard(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaWriteLeaderboardsEventInfo*, nint, void>)&OnWriteUnarbitrated, context, out CnaHandle r8), r8);
        Register(Native.cna_network_session_subscribe_write_true_skill(
            Handle, (nint)(delegate* unmanaged[Cdecl]<CnaHandle, CnaWriteLeaderboardsEventInfo*, nint, void>)&OnWriteTrueSkill, context, out CnaHandle r9), r9);
    }

    private void Register(CnaResult result, CnaHandle registration)
    {
        GamerServicesInterop.Check(result, nameof(NetworkSession));
        _registrations.Add(registration);
    }

    /// <summary>Runs a handler; its exception is held for <see cref="Update"/> when native raised the
    /// event from there, and otherwise goes to the running game.</summary>
    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception exception)
        {
            if (_insideUpdate)
            {
                _handlerFailures.Add(ExceptionDispatchInfo.Capture(exception));
            }
            else
            {
                GamerServicesInterop.ReportCallbackException(exception);
            }
        }
    }

    private static NetworkSession? From(nint context) =>
        context == 0 ? null : GCHandle.FromIntPtr(context).Target as NetworkSession;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGameStarted(CnaHandle session, CnaGameStartedEventInfo* info, nint context)
    {
        if (From(context) is { } target)
        {
            target.Raise(() => target.GameStarted?.Invoke(target, new GameStartedEventArgs()));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGameEnded(CnaHandle session, CnaGameEndedEventInfo* info, nint context)
    {
        if (From(context) is { } target)
        {
            target.Raise(() => target.GameEnded?.Invoke(target, new GameEndedEventArgs()));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGamerJoined(CnaHandle session, CnaGamerJoinedEventInfo* info, nint context)
    {
        if (From(context) is { } target)
        {
            CnaHandle handle = info->Gamer;
            target.Raise(() =>
            {
                target.RefreshRosters();
                if (target.Resolve(handle) is { } gamer)
                {
                    target._gamerJoined?.Invoke(target, new GamerJoinedEventArgs(gamer));
                }
            });
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnGamerLeft(CnaHandle session, CnaGamerLeftEventInfo* info, nint context)
    {
        if (From(context) is { } target)
        {
            CnaHandle handle = info->Gamer;
            target.Raise(() =>
            {
                target.RefreshRosters();
                if (target.Resolve(handle) is { } gamer)
                {
                    target.GamerLeft?.Invoke(target, new GamerLeftEventArgs(gamer));
                }
            });
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnHostChanged(CnaHandle session, CnaHostChangedEventInfo* info, nint context)
    {
        if (From(context) is { } target)
        {
            CnaHandle oldHost = info->OldHost;
            CnaHandle newHost = info->NewHost;
            target.Raise(() => target.HostChanged?.Invoke(
                target, new HostChangedEventArgs(target.Resolve(oldHost), target.Resolve(newHost)!)));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSessionEnded(CnaHandle session, CnaNetworkSessionEndedEventInfo* info, nint context)
    {
        if (From(context) is { } target)
        {
            var reason = (NetworkSessionEndReason)info->EndReason;
            target.Raise(() => target.SessionEnded?.Invoke(target, new NetworkSessionEndedEventArgs(reason)));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWriteArbitrated(CnaHandle session, CnaWriteLeaderboardsEventInfo* info, nint context) =>
        RaiseWrite(info, context, static owner => owner.WriteArbitratedLeaderboard);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWriteUnarbitrated(CnaHandle session, CnaWriteLeaderboardsEventInfo* info, nint context) =>
        RaiseWrite(info, context, static owner => owner.WriteUnarbitratedLeaderboard);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnWriteTrueSkill(CnaHandle session, CnaWriteLeaderboardsEventInfo* info, nint context) =>
        RaiseWrite(info, context, static owner => owner.WriteTrueSkill);

    private static void RaiseWrite(
        CnaWriteLeaderboardsEventInfo* info, nint context, Func<NetworkSession, EventHandler<WriteLeaderboardsEventArgs>?> handler)
    {
        if (From(context) is { } target)
        {
            CnaHandle handle = info->Gamer;
            bool leaving = info->IsLeaving != 0;
            target.Raise(() => handler(target)?.Invoke(
                target, new WriteLeaderboardsEventArgs(target.Resolve(handle)!, leaving)));
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnInviteAccepted(CnaInviteAcceptedEventInfo* info, nint context)
    {
        try
        {
            SignedInGamer? gamer = Gamer.FromHandle(info->Gamer) as SignedInGamer;
            if (gamer is null)
            {
                SignedInGamerCollection.Refresh();
                gamer = Gamer.FromHandle(info->Gamer) as SignedInGamer;
            }

            s_inviteAccepted?.Invoke(null, new InviteAcceptedEventArgs(gamer!, info->IsCurrentSession != 0));
        }
        catch (Exception exception)
        {
            GamerServicesInterop.ReportCallbackException(exception);
        }
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static int MinSupportedGamers(NetworkSessionType sessionType) =>
        sessionType == NetworkSessionType.Local ? 1 : 2;

    /// <summary>XNA's own argument checks, in its order, before anything reaches native.</summary>
    private static void ValidateCreate(NetworkSessionType sessionType, int maxLocalGamers, int maxGamers, int privateGamerSlots)
    {
        if (maxLocalGamers is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLocalGamers));
        }

        if (maxGamers < MinSupportedGamers(sessionType) || maxGamers > MaxSupportedGamers)
        {
            throw new ArgumentOutOfRangeException(nameof(maxGamers));
        }

        if (privateGamerSlots < 0 || privateGamerSlots >= maxGamers)
        {
            throw new ArgumentOutOfRangeException(nameof(privateGamerSlots));
        }
    }

    private static void ValidateFind(NetworkSessionType sessionType, int maxLocalGamers)
    {
        if (sessionType == NetworkSessionType.Local)
        {
            throw new ArgumentException("A local session cannot be searched for.", nameof(sessionType));
        }

        if (maxLocalGamers is < 1 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maxLocalGamers));
        }
    }

    /// <summary>XNA's checks on an explicit local-gamer list: present, no nulls or disposed gamers,
    /// one to four of them.</summary>
    private static CnaHandle[] LocalGamerHandles(IEnumerable<SignedInGamer> localGamers)
    {
        ArgumentNullException.ThrowIfNull(localGamers);
        var handles = new List<CnaHandle>();
        foreach (SignedInGamer gamer in localGamers)
        {
            if (gamer is null)
            {
                throw new ArgumentException("The local gamer list contains a null gamer.", nameof(localGamers));
            }

            if (gamer.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(localGamers), "The local gamer list contains a gamer who is no longer valid.");
            }

            handles.Add(gamer.Handle);
        }

        if (handles.Count is < 1 or > 4)
        {
            throw new ArgumentException("A session needs between one and four local gamers.", nameof(localGamers));
        }

        return [.. handles];
    }

    private static object WithProperties(NetworkSessionProperties? properties, Func<CnaHandle, object> body)
    {
        if (properties is null)
        {
            return body(CnaHandle.Zero);
        }

        CnaHandle native = properties.CreateNative();
        try
        {
            return body(native);
        }
        finally
        {
            _ = Native.cna_network_session_properties_destroy(native);
        }
    }

    /// <summary>A join failure carries its <see cref="NetworkSessionJoinError"/>, which native
    /// records beside the failure for exactly this.</summary>
    private static void CheckJoin(CnaResult result, string operation)
    {
        if (result.IsSuccess())
        {
            return;
        }

        if (Native.cna_net_get_last_join_error(out uint joinError, out byte hasJoinError).IsSuccess() && hasJoinError != 0)
        {
            string message = CnaError.GetLastErrorMessage();
            throw new NetworkSessionJoinException(
                string.IsNullOrEmpty(message) ? $"{operation} failed." : message, (NetworkSessionJoinError)joinError);
        }

        GamerServicesInterop.Check(result, operation);
    }

    private static IAsyncResult Run(object owner, AsyncCallback callback, object asyncState, Func<object> operation)
    {
        var result = new GamerServicesAsyncResult(callback, asyncState, owner);
        try
        {
            result.Payload = operation();
        }
        catch (Exception exception)
        {
            result.Payload = ExceptionDispatchInfo.Capture(exception);
        }

        result.Complete(synchronously: true);
        return result;
    }

    private static object End(IAsyncResult result, object owner)
    {
        GamerServicesAsyncResult ours = GamerServicesAsyncResult.ForEnd(result, owner);
        if (ours.Payload is ExceptionDispatchInfo failure)
        {
            failure.Throw();
        }

        return ours.Payload!;
    }

    private delegate CnaResult UInt32Query(CnaHandle session, out uint value);

    private delegate CnaResult Int32Query(CnaHandle session, out int value);

    private delegate CnaResult FlagQuery(CnaHandle session, out byte value);

    private uint ReadUInt32(UInt32Query query, string operation)
    {
        GamerServicesInterop.Check(query(Handle, out uint value), operation);
        return value;
    }

    private int ReadInt32(Int32Query query, string operation)
    {
        GamerServicesInterop.Check(query(Handle, out int value), operation);
        return value;
    }

    private bool ReadFlag(FlagQuery query, string operation)
    {
        GamerServicesInterop.Check(query(Handle, out byte value), operation);
        return value != 0;
    }
}
