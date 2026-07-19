using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for filter side screens (FilterSideScreen, TreeFilterable)
	/// </summary>

	/// <summary>
	/// Sync filter element selection
	/// </summary>
	[HarmonyPatch(typeof(FilterSideScreen), nameof(FilterSideScreen.SetFilterTag))]
	public static class FilterSideScreen_SetFilterTag_Patch
	{
		public static void Postfix(FilterSideScreen __instance, Tag tag)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetFilterable == null) return;

			var targetGO = (__instance.targetFilterable as Component)?.gameObject;
			if (targetGO == null) return;

			var identity = targetGO.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Use string-based tag name instead of hash for proper reconstruction
			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(targetGO),
				ConfigHash = NetworkingHash.ForConfigKey("FilterTagString"),
				Value = 0,
				ConfigType = BuildingConfigType.String,
				StringValue = tag.Name
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync storage filter add tag
	/// </summary>
	[HarmonyPatch(typeof(TreeFilterable), nameof(TreeFilterable.AddTagToFilter))]
	public static class TreeFilterable_AddTagToFilter_Patch
	{
		public static void Postfix(TreeFilterable __instance, Tag t)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("StorageFilterAdd"),
				Value = 0,
				ConfigType = BuildingConfigType.String,
				StringValue = t.Name
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync storage filter remove tag
	/// </summary>
	[HarmonyPatch(typeof(TreeFilterable), nameof(TreeFilterable.RemoveTagFromFilter))]
	public static class TreeFilterable_RemoveTagFromFilter_Patch
	{
		public static void Postfix(TreeFilterable __instance, Tag t)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("StorageFilterRemove"),
				Value = 0,
				ConfigType = BuildingConfigType.String,
				StringValue = t.Name
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}
}
