using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for FlatTagFilterable synchronization.
	/// Used for meteor type selection on Missile Launcher and other buildings with tag filters.
	/// </summary>

	[HarmonyPatch(typeof(FlatTagFilterable), nameof(FlatTagFilterable.ToggleTag))]
	public static class FlatTagFilterable_ToggleTag_Patch
	{
		public static void Postfix(FlatTagFilterable __instance, Tag tag)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Check if tag is now selected or not
			bool isSelected = __instance.selectedTags.Contains(tag);

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("FlatTagFilter"),
				Value = isSelected ? 1f : 0f,
				ConfigType = BuildingConfigType.String,
				StringValue = tag.Name
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[FlatTagFilterable_ToggleTag_Patch] Synced tag={tag.Name}, selected={isSelected} on {__instance.name}");
		}
	}
}
