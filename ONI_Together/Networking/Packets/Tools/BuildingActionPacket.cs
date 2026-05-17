using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.Tools
{
	// Covers all non-drag entry points for deconstruct / cancel:
	// right-click menu button, single-click context action, scripted cancel, etc.
	// The tool-drag path already syncs via DeconstructPacket / CancelPacket; this
	// packet catches everything that bypasses those tools by hooking the
	// Deconstructable / Constructable choke-point methods instead of the Tool.
	public enum BuildingActionKind : byte
	{
		QueueDeconstruct = 1,   // Deconstructable.QueueDeconstruction(userTriggered)
		CancelDeconstruct = 2,  // Deconstructable.CancelDeconstruction()
		CancelConstruct = 3,    // Constructable.OnCancel() — cancel an unfinished build
	}

	public class BuildingActionPacket : IPacket
	{
		// Guard against the handler's local call re-triggering the Harmony prefix
		// and re-broadcasting the same action in a loop.
		public static bool ProcessingIncoming;

		public int NetId;
		public BuildingActionKind Action;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(NetId);
			writer.Write((byte)Action);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			NetId = reader.ReadInt32();
			Action = (BuildingActionKind)reader.ReadByte();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			try
			{
				if (!NetworkIdentityRegistry.TryGet(NetId, out var identity) || identity == null || identity.gameObject == null)
				{
					DebugConsole.LogWarning($"[BuildingActionPacket] NetId {NetId} not found (action={Action})");
					return;
				}

				var go = identity.gameObject;
				ProcessingIncoming = true;
				try
				{
					DebugConsole.Log($"[BuildingAction] apply NetId={NetId} kind={Action} name={go.name}");
					switch (Action)
					{
						case BuildingActionKind.QueueDeconstruct:
							if (go.TryGetComponent<Deconstructable>(out var dq))
								dq.QueueDeconstruction(userTriggered: true);
							break;
						case BuildingActionKind.CancelDeconstruct:
							if (go.TryGetComponent<Deconstructable>(out var dc))
								dc.CancelDeconstruction();
							break;
						case BuildingActionKind.CancelConstruct:
							// Constructable subscribes to GameHashes.Cancel (2127324410).
							// Firing the trigger runs the same cleanup path the game uses.
							go.Trigger(2127324410);
							break;
					}
				}
				finally
				{
					ProcessingIncoming = false;
				}
			}
			catch (System.Exception ex)
			{
				ProcessingIncoming = false;
				DebugConsole.LogError($"[BuildingActionPacket] Exception handling {Action} on NetId {NetId}: {ex}");
			}
		}
	}
}
