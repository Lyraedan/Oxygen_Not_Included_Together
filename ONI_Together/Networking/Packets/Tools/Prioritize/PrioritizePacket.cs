using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Tools.Prioritize
{
    public class PrioritizePacket : DragToolPacket
    {
        public PrioritizePacket()
        {
            using var _ = Profiler.Scope();

            ToolInstance = PrioritizeTool.Instance;
            ToolMode     = DragToolMode.OnDragTool;
        }

		public override void Serialize(BinaryWriter writer)
		{
			base.Serialize(writer);
			if (!PriorityAuthority.IsValidToolRequest(cell, distFromOrigin, Priority))
				throw new InvalidDataException("Invalid prioritize tool request");
		}

		public override void Deserialize(BinaryReader reader)
		{
			base.Deserialize(reader);
			if (!PriorityAuthority.IsValidToolRequest(cell, distFromOrigin, Priority))
				throw new InvalidDataException("Invalid prioritize tool request");
		}

		public override void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool protocolVerified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, protocolVerified) ||
			    !PriorityAuthority.IsValidToolRequest(cell, distFromOrigin, Priority))
				return;

			base.OnDispatched();
		}

		internal static bool ShouldAccept(
			bool localIsHost,
			DispatchContext context,
			bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;
    }

	internal static class PriorityAuthority
	{
		internal static bool IsValidClientPriority(PrioritySetting priority)
		{
			switch (priority.priority_class)
			{
				case PriorityScreen.PriorityClass.basic:
				case PriorityScreen.PriorityClass.high:
					return priority.priority_value >= 1 && priority.priority_value <= 9;
				case PriorityScreen.PriorityClass.topPriority:
					return priority.priority_value == 1;
				default:
					return false;
			}
		}

		internal static bool IsValidStatePriority(int priorityClass, int priorityValue)
		{
			if (priorityClass < (int)PriorityScreen.PriorityClass.idle ||
			    priorityClass > (int)PriorityScreen.PriorityClass.compulsory)
				return false;
			return priorityValue >= -1 && priorityValue <= 9;
		}

		internal static bool IsValidToolRequest(int cell, int distance, PrioritySetting priority)
			=> Grid.IsValidCell(cell) && distance >= 0 && distance <= Grid.CellCount &&
			   IsValidClientPriority(priority);
	}

	public sealed class PrioritizeTargetRequestPacket : IPacket, IClientRelayable
	{
		public int NetId;
		public int PriorityClass;
		public int PriorityValue;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(NetId);
			writer.Write(PriorityClass);
			writer.Write(PriorityValue);
		}

		public void Deserialize(BinaryReader reader)
		{
			NetId = reader.ReadInt32();
			PriorityClass = reader.ReadInt32();
			PriorityValue = reader.ReadInt32();
			Validate();
		}

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool protocolVerified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, protocolVerified) ||
			    !NetworkIdentityRegistry.TryGetComponent(NetId, out Prioritizable prioritizable) ||
			    !prioritizable.IsPrioritizable())
				return;

			var priority = new PrioritySetting((PriorityScreen.PriorityClass)PriorityClass, PriorityValue);
			if (!prioritizable.GetMasterPriority().Equals(priority))
				prioritizable.SetMasterPriority(priority);
		}

		internal static bool ShouldAccept(
			bool localIsHost,
			DispatchContext context,
			bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;

		private void Validate()
		{
			if (NetId == 0 || !PriorityAuthority.IsValidClientPriority(
				    new PrioritySetting((PriorityScreen.PriorityClass)PriorityClass, PriorityValue)))
				throw new InvalidDataException("Invalid prioritize target request");
		}
	}
}
