using System;
using System.Runtime.InteropServices;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using Shared.Profiling;
using Steamworks;

namespace ONI_Together.Networking.Transport.Steam
{
    public class SteamworksPacketSender : TransportPacketSender
    {
        public override bool SendPacket(object conn, IPacket packet, PacketSendMode sendType = PacketSendMode.ReliableImmediate)
        {
            using var _ = Profiler.Scope();

            if (conn is not HSteamNetConnection connection)
                return false;

            byte[] bytes = PacketSender.SerializePacketForSending(packet);
            int steamSendType = ConvertSendType(sendType);
            if (bytes.Length >= Utils.MaxSteamNetworkingSocketsMessageSizeSend)
            {
                if (packet is ChunkedPacket)
                    return false;
                return ChunkedPacket.TrySendSerializedChunks(
                    bytes,
                    Utils.MaxSteamNetworkingSocketsMessageSizeSend,
                    chunk => SendRaw(
                        connection,
                        PacketSender.SerializePacketForSending(chunk),
                        chunk,
                        steamSendType));
            }
            return SendRaw(connection, bytes, packet, steamSendType);
        }

        private static bool SendRaw(
            HSteamNetConnection connection,
            byte[] bytes,
            IPacket packet,
            int steamSendType)
        {
            IntPtr unmanagedPointer = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, unmanagedPointer, bytes.Length);

                EResult result = SteamNetworkingSockets.SendMessageToConnection(
                    connection,
                    unmanagedPointer,
                    (uint)bytes.Length,
                    steamSendType,
                    out _);

                bool sent = result == EResult.k_EResultOK;

                if (!sent)
                {
                    // DebugConsole.LogError($"[Sockets] Failed to send {packet.Type} to conn {conn} ({Utils.FormatBytes(bytes.Length)} | result: {result})", false);
                }
                else
                {
                    PacketTracker.TrackSent(new PacketTracker.PacketTrackData
                    {
                        packet = packet,
                        size = bytes.Length
                    });
                    //DebugConsole.Log($"[Sockets] Sent {packet.Type} to conn {conn} ({Utils.FormatBytes(bytes.Length)})");
                }
                return sent;
            }
            finally
            {
                Marshal.FreeHGlobal(unmanagedPointer);
            }
        }

        public int ConvertSendType(PacketSendMode mode)
        {
            int result = 0;

            // Reliable / Unreliable
            if ((mode & PacketSendMode.Reliable) == PacketSendMode.Reliable)
                result |= 8;  // k_nSteamNetworkingSend_Reliable
            else
                result |= 0;  // k_nSteamNetworkingSend_Unreliable (implicitly 0)

            // Immediate (flush) corresponds to NoNagle behavior
            if ((mode & PacketSendMode.Immediate) == PacketSendMode.Immediate)
                result |= 1;  // k_nSteamNetworkingSend_NoNagle

            // NoDelay (drop if can't send soon)
            if ((mode & PacketSendMode.NoDelay) == PacketSendMode.NoDelay)
                result |= 4;  // k_nSteamNetworkingSend_NoDelay

            return result;
        }

    }
}
