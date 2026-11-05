namespace Microsoft.Xna.Framework.Net;

public class HostChangedEventArgs : EventArgs
{
    public HostChangedEventArgs(NetworkGamer? oldHost, NetworkGamer newHost)
    {
        OldHost = oldHost;
        NewHost = newHost;
    }

    public NetworkGamer? OldHost { get; }

    public NetworkGamer NewHost { get; }
}
