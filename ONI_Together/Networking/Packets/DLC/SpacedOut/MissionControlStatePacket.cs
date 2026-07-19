using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public sealed class MissionControlStatePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxBuffSeconds = 600f;
		public int WorkableNetId;
		public int CraftNetId;
		public float BuffTimeRemaining;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid mission control state");
			writer.Write(WorkableNetId);
			writer.Write(CraftNetId);
			writer.Write(BuffTimeRemaining);
		}

		public void Deserialize(BinaryReader reader)
		{
			WorkableNetId = reader.ReadInt32();
			CraftNetId = reader.ReadInt32();
			BuffTimeRemaining = reader.ReadSingle();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid mission control state");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost && PacketHandler.CurrentContext.SenderIsHost)
				MissionControlSync.TryApply(this);
		}

		internal bool IsWireValid()
			=> WorkableNetId != 0 && !float.IsNaN(BuffTimeRemaining) &&
			   !float.IsInfinity(BuffTimeRemaining) && BuffTimeRemaining >= 0f &&
			   BuffTimeRemaining <= MaxBuffSeconds && (CraftNetId != 0 || BuffTimeRemaining == 0f);

		internal static bool NeedsApply(int currentCraftNetId, float currentBuff, MissionControlStatePacket state)
			=> currentCraftNetId != state.CraftNetId || Math.Abs(currentBuff - state.BuffTimeRemaining) > 0.0001f;
	}
}
