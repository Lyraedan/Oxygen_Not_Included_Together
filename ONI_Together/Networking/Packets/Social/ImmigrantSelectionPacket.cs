using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.GamePatches;
using Shared.Profiling;
using System.IO;

namespace ONI_Together.Networking.Packets.Social
{
	public class ImmigrantSelectionPacket : IPacket
	{
		public int PrintingPodWorldIndex;
		public int SelectedOptionIndex = -1;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			writer.Write(PrintingPodWorldIndex);
			writer.Write(SelectedOptionIndex);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			PrintingPodWorldIndex = reader.ReadInt32();
			SelectedOptionIndex = reader.ReadInt32();
		}

		public static bool IsSelectionRequestValid(
			DispatchContext context,
			bool protocolVerified,
			bool optionsLocked,
			int optionCount,
			int worldIndex,
			int optionIndex)
		{
			if (context.SenderIsHost || !protocolVerified || !optionsLocked)
				return false;
			if (worldIndex == -1)
				return optionIndex == -1;
			return worldIndex >= 0 && optionIndex >= 0 && optionIndex < optionCount;
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.InSession)
				return;

			DispatchContext context = PacketHandler.CurrentContext;
			if (MultiplayerSession.IsClient)
			{
				HandleHostNotification(context);
				return;
			}

			HandleClientRequest(context);
		}

		private void HandleHostNotification(DispatchContext context)
		{
			if (!context.SenderIsHost || SelectedOptionIndex != -1
			    || (PrintingPodWorldIndex != -1 && PrintingPodWorldIndex != -2))
			{
				DebugConsole.LogWarning("[ImmigrantSelectionPacket] Rejected invalid host notification");
				return;
			}

			ImmigrantScreenPatch.ClearOptionsLock();
			if (ImmigrantScreen.instance != null && ImmigrantScreen.instance.gameObject.activeInHierarchy)
				ImmigrantScreen.instance.Deactivate();

			try
			{
				Immigration.Instance?.EndImmigration();
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[ImmigrantSelectionPacket] Error ending immigration: {ex}");
			}
		}

		private void HandleClientRequest(DispatchContext context)
		{
			MultiplayerPlayer player = MultiplayerSession.GetPlayer(context.SenderId);
			int optionCount = ImmigrantScreenPatch.AvailableOptions?.Count ?? 0;
			if (!IsSelectionRequestValid(context, player?.ProtocolVerified == true,
				ImmigrantScreenPatch.OptionsLocked, optionCount,
				PrintingPodWorldIndex, SelectedOptionIndex))
			{
				DebugConsole.LogWarning($"[ImmigrantSelectionPacket] Rejected selection from {context.SenderId}");
				return;
			}

			if (PrintingPodWorldIndex == -1)
			{
				RejectAll();
				return;
			}

			DeliverSelectedOption();
		}

		private void RejectAll()
		{
			Immigration.Instance?.EndImmigration();
			ImmigrantScreenPatch.ClearOptionsLock();
			CloseHostScreen();
			BroadcastClose(-1);
		}

		private void DeliverSelectedOption()
		{
			Telepad telepad = FindTelepad(PrintingPodWorldIndex);
			if (telepad == null)
			{
				DebugConsole.LogWarning($"[ImmigrantSelectionPacket] No Telepad in world {PrintingPodWorldIndex}");
				return;
			}

			ImmigrantOptionEntry option = ImmigrantScreenPatch.AvailableOptions[SelectedOptionIndex];
			ITelepadDeliverable deliverable = option.ToGameDeliverable();
			if (deliverable == null)
			{
				DebugConsole.LogWarning("[ImmigrantSelectionPacket] Host option could not be materialized");
				return;
			}

			CloseHostScreen();
			telepad.OnAcceptDelivery(deliverable);
			ImmigrantScreenPatch.ClearOptionsLock();
			BroadcastClose(-2);
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

		private static void CloseHostScreen()
		{
			if (ImmigrantScreen.instance != null && ImmigrantScreen.instance.gameObject.activeInHierarchy)
				ImmigrantScreen.instance.Deactivate();
		}

		private static void BroadcastClose(int state)
		{
			PacketSender.SendToAllClients(new ImmigrantSelectionPacket
			{
				PrintingPodWorldIndex = state,
				SelectedOptionIndex = -1
			});
		}
	}
}
