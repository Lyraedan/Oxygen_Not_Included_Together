using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Steamworks;
using System.IO;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Tools.Move
{
	public class MoveToLocationPacket : IPacket
	{
		public int Cell;
		public int TargetNetId;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Cell);
			writer.Write(TargetNetId);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Cell = reader.ReadInt32();
			TargetNetId = reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHost)
				return;

			if (!Grid.IsValidCell(Cell))
			{
				DebugConsole.LogWarning($"[MoveToLocationPacket] Invalid cell: {Cell}");
				return;
			}

			if (NetworkIdentityRegistry.TryGet(TargetNetId, out var go))
			{
				if(go == null)
				{
                    // This should never happen
                    return;
				}
                if (go.TryGetComponent(out Navigator nav))
                {
					if (nav == null)
					{
						// This should never happen
						return;
					}
                    nav.GetSMI<MoveToLocationMonitor.Instance>()?.MoveToLocation(Cell);
                    DebugConsole.Log($"[Host] Navigator moved to {Cell} for NetId {TargetNetId}");
                }
                else if (go.TryGetComponent(out Movable movable))
                {
					if (movable == null)
					{
						// This should never happen
						return;
					}
                    movable.MoveToLocation(Cell);
                    DebugConsole.Log($"[Host] Movable moved to {Cell} for NetId {TargetNetId}");
                }
                else
                {
                    DebugConsole.LogWarning($"[MoveToLocationPacket] No Navigator/Movable found on entity {TargetNetId}");
                }
            }
		}
	}
}
