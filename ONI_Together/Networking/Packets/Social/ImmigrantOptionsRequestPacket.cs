using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.GamePatches;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public class ImmigrantOptionsRequestPacket : IPacket
	{
		public int PrintingPodWorldIndex;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			writer.Write(PrintingPodWorldIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			PrintingPodWorldIndex = reader.ReadInt32();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsHost)
				return;

			DispatchContext context = PacketHandler.CurrentContext;
			MultiplayerPlayer player = MultiplayerSession.GetPlayer(context.SenderId);
			if (context.SenderIsHost || player?.ProtocolVerified != true || PrintingPodWorldIndex < 0)
				return;

			if (ImmigrantScreenPatch.OptionsLocked)
			{
				SendCachedOptions(context.SenderId);
				return;
			}

			if (ImmigrantScreenPatch.OptionsCaptureInProgress || Immigration.Instance?.ImmigrantsAvailable != true)
				return;

			Telepad telepad = FindTelepad(PrintingPodWorldIndex);
			if (telepad == null || ImmigrantScreen.instance == null)
			{
				DebugConsole.LogWarning("[ImmigrantOptionsRequest] Host cannot initialize requested Printing Pod");
				return;
			}

			ImmigrantScreenPatch.OptionsCaptureInProgress = true;
			ImmigrantScreen.instance.Initialize(telepad);
		}

		private static void SendCachedOptions(ulong recipient)
		{
			if (ImmigrantScreenPatch.AvailableOptions == null)
				return;
			PacketSender.SendToPlayer(recipient, new ImmigrantOptionsPacket
			{
				Options = ImmigrantScreenPatch.AvailableOptions
			});
		}

		private static Telepad FindTelepad(int worldIndex)
		{
			foreach (Telepad telepad in global::Components.Telepads)
			{
				if (telepad.GetMyWorldId() == worldIndex)
					return telepad;
			}
			return null;
		}
	}
}
