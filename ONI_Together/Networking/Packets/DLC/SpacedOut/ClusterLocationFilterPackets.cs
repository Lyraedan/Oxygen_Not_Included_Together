using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class ClusterLocationFilterPacketData
	{
		internal const int MaxLocationCount = 256;

		public int TargetNetId;
		public bool ActiveInSpace;
		public List<AxialI> ActiveLocations = new();

		internal void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid cluster location filter payload");

			writer.Write(TargetNetId);
			writer.Write(ActiveInSpace);
			writer.Write(ActiveLocations.Count);
			foreach (AxialI location in ActiveLocations)
			{
				writer.Write(location.q);
				writer.Write(location.r);
			}
		}

		internal static ClusterLocationFilterPacketData Deserialize(BinaryReader reader)
		{
			var data = new ClusterLocationFilterPacketData
			{
				TargetNetId = reader.ReadInt32(),
				ActiveInSpace = reader.ReadBoolean()
			};
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxLocationCount)
				throw new InvalidDataException($"Invalid cluster location count: {count}");

			var seen = new HashSet<AxialI>();
			for (int i = 0; i < count; i++)
			{
				int q = reader.ReadInt32();
				int r = reader.ReadInt32();
				if (!RocketSettingsPacketData.CoordinateWithinBounds(q, r))
					throw new InvalidDataException("Cluster location is out of bounds");
				AxialI location = AxialCoordinateSync.FromQr(q, r);
				if (!seen.Add(location))
					throw new InvalidDataException("Duplicate cluster location");
				data.ActiveLocations.Add(location);
			}
			if (!data.IsWireValid())
				throw new InvalidDataException("Invalid cluster location filter payload");
			return data;
		}

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || ActiveLocations == null || ActiveLocations.Count > MaxLocationCount)
				return false;
			var seen = new HashSet<AxialI>();
			foreach (AxialI location in ActiveLocations)
			{
				if (!RocketSettingsPacketData.CoordinateWithinBounds(location.q, location.r) ||
				    !seen.Add(location))
					return false;
			}
			return true;
		}
	}

	public sealed class ClusterLocationFilterRequestPacket : IPacket, IClientRelayable
	{
		public ClusterLocationFilterPacketData Data = new();

		public ClusterLocationFilterRequestPacket()
		{
		}

		internal ClusterLocationFilterRequestPacket(ClusterLocationFilterPacketData data)
		{
			Data = data;
		}

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);

		public void Deserialize(BinaryReader reader) => Data = ClusterLocationFilterPacketData.Deserialize(reader);

		public void OnDispatched()
		{
			if (!ShouldAccept(MultiplayerSession.IsHost, PacketHandler.CurrentContext) ||
			    !ClusterLocationFilterSync.TryApply(Data) ||
			    !NetworkIdentityRegistry.TryGetComponent(
				    Data.TargetNetId, out LogicClusterLocationSensor sensor) ||
			    !ClusterLocationFilterSync.TryCapture(sensor, out ClusterLocationFilterPacketData state))
				return;

			PacketSender.SendToAllClients(new ClusterLocationFilterStatePacket(state));
		}

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast;
	}

	public sealed class ClusterLocationFilterStatePacket : IPacket, IHostOnlyPacket
	{
		public ClusterLocationFilterPacketData Data = new();

		public ClusterLocationFilterStatePacket()
		{
		}

		internal ClusterLocationFilterStatePacket(ClusterLocationFilterPacketData data)
		{
			Data = data;
		}

		public void Serialize(BinaryWriter writer) => Data.Serialize(writer);

		public void Deserialize(BinaryReader reader) => Data = ClusterLocationFilterPacketData.Deserialize(reader);

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				ClusterLocationFilterSync.TryApply(Data);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
