using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System.Collections.Generic;
using System.IO;
using Shared.Profiling;
using static LogicGateBase;

namespace ONI_Together.Networking.Packets.Social
{
	public class ImmigrantOptionsPacket : IPacket
	{

		public List<ImmigrantOptionEntry> Options = new List<ImmigrantOptionEntry>();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Options.Count);
			foreach (var opt in Options)
			{
				opt.Serialize(writer);
			}
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			int count = reader.ReadInt32();
			Options = new List<ImmigrantOptionEntry>();
			for (int i = 0; i < count; i++)
			{
				var opt = ImmigrantOptionEntry.Deserialize(reader);
				Options.Add(opt);
			}
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"[ImmigrantOptionsPacket] Received {Options.Count} options");

			// Log each option for debugging
			for (int i = 0; i < Options.Count; i++)
			{
				var opt = Options[i];
				if (opt.IsDuplicant)
				{
					DebugConsole.Log($"[ImmigrantOptionsPacket]   Option {i}: Duplicant '{opt.Name}' (Personality: {opt.PersonalityId})");
				}
				else
				{
					DebugConsole.Log($"[ImmigrantOptionsPacket]   Option {i}: CarePackage '{opt.CarePackageId}' x{opt.Quantity}");
				}
			}

			// Check if options are already locked (first-opener-wins)
			if (ONI_Together.Patches.GamePatches.ImmigrantScreenPatch.OptionsLocked)
			{
				DebugConsole.Log("[ImmigrantOptionsPacket] Options already locked, ignoring packet");
				return;
			}

			// Store and lock options
			ONI_Together.Patches.GamePatches.ImmigrantScreenPatch.AvailableOptions = Options;
			ONI_Together.Patches.GamePatches.ImmigrantScreenPatch.OptionsLocked = true;

			if (MultiplayerSession.IsHost)
			{
				// Host received from client - rebroadcast to all clients
				DebugConsole.Log("[ImmigrantOptionsPacket] Host received options from client, rebroadcasting to all clients");
				PacketSender.SendToAllClients(this);
			}

			// If the screen is already open, refresh it immediately
			if (ImmigrantScreen.instance != null && ImmigrantScreen.instance.gameObject.activeInHierarchy)
			{
				DebugConsole.Log("[ImmigrantOptionsPacket] ImmigrantScreen is open, applying options immediately");
				ONI_Together.Patches.GamePatches.ImmigrantScreenPatch.ApplyOptionsToScreen(ImmigrantScreen.instance);
			}
			else
			{
				DebugConsole.Log("[ImmigrantOptionsPacket] ImmigrantScreen is not open, options stored for later");
			}
		}
	}
}
