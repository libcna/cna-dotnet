using CNA.Interop;
using Microsoft.Xna.Framework.GamerServices;

namespace Microsoft.Xna.Framework.Net;

/// <summary>
/// A gamer playing on this machine. Sends and receives go through native's session; the
/// <see cref="PacketWriter"/> and <see cref="PacketReader"/> overloads move bytes between those
/// managed streams and native, which is all XNA's own do.
/// </summary>
public sealed unsafe class LocalNetworkGamer : NetworkGamer
{
    internal LocalNetworkGamer(NetworkSession session, CnaHandle handle)
        : base(session, handle)
    {
    }

    public SignedInGamer SignedInGamer
    {
        get
        {
            GamerServicesInterop.Check(
                Native.cna_local_network_gamer_get_signed_in_gamer(Handle, out CnaHandle gamer), nameof(SignedInGamer));
            if (gamer.IsNull)
            {
                return null!;
            }

            return Gamer.Wrap(
                gamer, ownsHandle: true, static (h, owned) => new SignedInGamer(h, owned), Native.cna_signed_in_gamer_destroy);
        }
    }

    public bool IsDataAvailable => Flag(Native.cna_local_network_gamer_get_is_data_available, nameof(IsDataAvailable));

    public void EnableSendVoice(NetworkGamer remoteGamer, bool enable)
    {
        ArgumentNullException.ThrowIfNull(remoteGamer);
        GamerServicesInterop.Check(
            Native.cna_local_network_gamer_enable_send_voice(Handle, remoteGamer.Handle, GamerServicesInterop.Bool(enable)),
            nameof(EnableSendVoice));
    }

    public void SendPartyInvites() =>
        GamerServicesInterop.Check(Native.cna_local_network_gamer_send_party_invites(Handle), nameof(SendPartyInvites));

    public void SendData(byte[] data, SendDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        fixed (byte* bytes = data)
        {
            Send(Native.cna_local_network_gamer_send_data(Handle, bytes, (ulong)data.Length, (uint)options));
        }
    }

    public void SendData(byte[] data, SendDataOptions options, NetworkGamer recipient)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recipient);
        fixed (byte* bytes = data)
        {
            Send(Native.cna_local_network_gamer_send_data_to(Handle, bytes, (ulong)data.Length, (uint)options, recipient.Handle));
        }
    }

    public void SendData(byte[] data, int offset, int count, SendDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        fixed (byte* bytes = data)
        {
            Send(Native.cna_local_network_gamer_send_data_range(Handle, bytes, (ulong)data.Length, offset, count, (uint)options));
        }
    }

    public void SendData(byte[] data, int offset, int count, SendDataOptions options, NetworkGamer recipient)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recipient);
        fixed (byte* bytes = data)
        {
            Send(Native.cna_local_network_gamer_send_data_range_to(
                Handle, bytes, (ulong)data.Length, offset, count, (uint)options, recipient.Handle));
        }
    }

    /// <summary>Sends what the writer holds, then empties it, as XNA does.</summary>
    public void SendData(PacketWriter data, SendDataOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        fixed (byte* bytes = data.ByteArray)
        {
            Send(Native.cna_local_network_gamer_send_data_range(
                Handle, bytes, (ulong)data.ByteArray.Length, 0, data.Length, (uint)options));
        }

        data.Clear();
    }

    public void SendData(PacketWriter data, SendDataOptions options, NetworkGamer recipient)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recipient);
        fixed (byte* bytes = data.ByteArray)
        {
            Send(Native.cna_local_network_gamer_send_data_range_to(
                Handle, bytes, (ulong)data.ByteArray.Length, 0, data.Length, (uint)options, recipient.Handle));
        }

        data.Clear();
    }

    public int ReceiveData(byte[] data, out NetworkGamer sender)
    {
        ArgumentNullException.ThrowIfNull(data);
        CnaHandle from;
        ulong received;
        fixed (byte* bytes = data)
        {
            GamerServicesInterop.Check(
                Native.cna_local_network_gamer_receive_data(Handle, bytes, (ulong)data.Length, out from, out received),
                nameof(ReceiveData));
        }

        sender = Session.TakeSender(from);
        return (int)received;
    }

    public int ReceiveData(byte[] data, int offset, out NetworkGamer sender)
    {
        ArgumentNullException.ThrowIfNull(data);
        CnaHandle from;
        ulong received;
        fixed (byte* bytes = data)
        {
            GamerServicesInterop.Check(
                Native.cna_local_network_gamer_receive_data_at(Handle, bytes, (ulong)data.Length, offset, out from, out received),
                nameof(ReceiveData));
        }

        sender = Session.TakeSender(from);
        return (int)received;
    }

    /// <summary>
    /// Resizes the reader to the next packet (empties it when none is queued) and rewinds it. The
    /// packet goes through a native reader, which is the only route that reports a packet's size
    /// before it is taken off the queue; its bytes are then copied into the managed reader.
    /// </summary>
    public int ReceiveData(PacketReader data, out NetworkGamer sender)
    {
        ArgumentNullException.ThrowIfNull(data);
        CnaHandle reader = Session.ScratchReader;
        GamerServicesInterop.Check(
            Native.cna_local_network_gamer_receive_data_into_packet_reader(Handle, reader, out CnaHandle from, out ulong received),
            nameof(ReceiveData));
        byte[] packet = received == 0 ? [] : new byte[received];
        fixed (byte* bytes = packet)
        {
            GamerServicesInterop.Check(
                Native.cna_packet_reader_copy_data_ext(reader, bytes, (ulong)packet.Length, out ulong copied),
                nameof(ReceiveData));
            data.SetPacket(packet, (int)copied);
        }

        // XNA resizes first, then reads through the managed reader's allocated buffer. A
        // never-allocated reader refuses an empty queue even though the shared scratch has space.
        if (received == 0 && ((MemoryStream)data.BaseStream).Capacity == 0)
        {
            throw new ArgumentOutOfRangeException("offset");
        }

        sender = Session.TakeSender(from);
        return (int)received;
    }

    private static void Send(CnaResult result) => GamerServicesInterop.Check(result, nameof(SendData));
}
