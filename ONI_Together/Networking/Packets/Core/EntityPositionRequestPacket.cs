using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;

namespace ONI_Together.Networking.Packets.Core
{
    public class EntityPositionRequestPacket : IPacket
    {
        private static int _rejectedPackets;
        public ulong RequesterId = MultiplayerSession.LocalUserID;
        public int NetId;
        internal static bool ShouldAccept(ulong requesterId, DispatchContext context) =>
            requesterId != 0 && !context.SenderIsHost && SyncBarrier.SenderMatches(requesterId, context.SenderId);

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(RequesterId);
            writer.Write(NetId);
        }

        public void Deserialize(BinaryReader reader)
        {
            RequesterId = reader.ReadUInt64();
            NetId = reader.ReadInt32();
        }

        public void OnDispatched()
        {
            if (!MultiplayerSession.IsHost) return;
#if DEBUG
			if (EntityPositionHandler.CheckpointFrozen)
				return;
#endif
            if (!ShouldAccept(RequesterId, PacketHandler.CurrentContext))
            {
                int rejected = ++_rejectedPackets;
                if (rejected <= 5 || rejected % 100 == 0)
                    DebugConsole.LogWarning($"[EntityPositionRequestPacket] Rejected requester {RequesterId} from {PacketHandler.CurrentContext.SenderId}, host={PacketHandler.CurrentContext.SenderIsHost} (#{rejected})");
                return;
            }

            if(!NetworkIdentityRegistry.TryGet(NetId, out var identity))
                return;

            if (!identity.TryGetComponent<EntityPositionHandler>(out var handler))
                return;

            var packet = new EntityPositionPacket
            {
                NetId = NetId,
                Position = handler.transform.position,
                FlipX = handler.kbac?.FlipX ?? false,
                FlipY = handler.kbac?.FlipY ?? false,
				NavType = handler.navigator != null
				          && handler.navigator.CurrentNavType != NavType.NumNavTypes
					? handler.navigator.CurrentNavType
					: NavType.Floor,
				Timestamp = EntityPositionHandler.NextHostSequence()
            };

            PacketSender.SendToPlayer(RequesterId, packet, PacketSendMode.ReliableImmediate);
        }
    }
}
