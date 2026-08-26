using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;
using UnityEngine;

namespace ONI_Together.Networking.Packets.Tools.Clear
{
	/// <summary>
	/// Synchronizes individual ground item sweep/clear commands when a player clicks an item
	/// and clicks "Sweep" or "Cancel Sweep" on the side screen, bypassing the area drag tool.
	/// </summary>
	public class ClearableActionPacket : IPacket
	{
		public static bool ProcessingIncoming;

		public int NetId;
		public int Cell;
		public bool IsMarked;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write(Cell);
			writer.Write(IsMarked);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			IsMarked = reader.ReadBoolean();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			try
			{
				ProcessingIncoming = true;

				Clearable target = null;
				if (NetId != 0 && NetworkIdentityRegistry.TryGet(NetId, out var identity) && identity != null)
				{
					target = identity.GetComponent<Clearable>();
				}

				if (target == null && Grid.IsValidCell(Cell))
				{
					var pickupableGo = Grid.Objects[Cell, (int)ObjectLayer.Pickupables];
					if (pickupableGo != null)
					{
						target = pickupableGo.GetComponent<Clearable>();
					}
				}

				if (target != null)
				{
					if (IsMarked)
					{
						target.MarkForClear();
					}
					else
					{
						target.CancelClearing();
					}
				}
				else
				{
					DebugConsole.LogWarning($"[ClearableActionPacket] Target not found (NetId={NetId}, Cell={Cell})");
				}
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[ClearableActionPacket] Exception during dispatch: {ex}");
			}
			finally
			{
				ProcessingIncoming = false;
			}

			// Host relays client actions to other clients
			if (MultiplayerSession.IsHost)
			{
				PacketSender.SendToAllClients(this);
			}
		}
	}
}
