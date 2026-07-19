using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.GamePatches;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public class ImmigrantOptionsPacket : IPacket, IHostOnlyPacket
	{
		public const int MaxOptions = 32;
		public List<ImmigrantOptionEntry> Options = new();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			if (Options.Count > MaxOptions)
				throw new InvalidDataException($"Too many immigrant options: {Options.Count}");

			writer.Write(Options.Count);
			foreach (ImmigrantOptionEntry option in Options)
				option.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxOptions)
				throw new InvalidDataException($"Invalid immigrant option count: {count}");

			Options = new List<ImmigrantOptionEntry>(count);
			for (int i = 0; i < count; i++)
				Options.Add(ImmigrantOptionEntry.Deserialize(reader));
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsClient || !PacketHandler.CurrentContext.SenderIsHost
			    || Options.Count == 0)
				return;

			ImmigrantScreenPatch.AvailableOptions = Options;
			ImmigrantScreenPatch.OptionsLocked = true;
			ImmigrantScreenPatch.OptionsCaptureInProgress = false;

			if (ImmigrantScreen.instance != null && ImmigrantScreen.instance.gameObject.activeInHierarchy)
				ImmigrantScreenPatch.ApplyOptionsToScreen(ImmigrantScreen.instance);
		}
	}
}
