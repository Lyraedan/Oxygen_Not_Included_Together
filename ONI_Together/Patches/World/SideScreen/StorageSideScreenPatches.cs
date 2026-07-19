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
	/// Patches for storage-related sync (CounterSideScreen, Storage, ComplexFabricator, FoodStorage)
	/// </summary>

	/// <summary>
	/// Sync signal counter max count
	/// </summary>
	[HarmonyPatch(typeof(CounterSideScreen), nameof(CounterSideScreen.SetMaxCount))]
	public static class CounterSideScreen_SetMaxCount_Patch
	{
		public static void Postfix(CounterSideScreen __instance, int newValue)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetLogicCounter == null) return;

			var identity = __instance.targetLogicCounter.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetLogicCounter.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("CounterMaxCount"),
				Value = newValue,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync signal counter advanced mode toggle
	/// </summary>
	[HarmonyPatch(typeof(CounterSideScreen), nameof(CounterSideScreen.ToggleAdvanced))]
	public static class CounterSideScreen_ToggleAdvanced_Patch
	{
		public static void Postfix(CounterSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetLogicCounter == null) return;

			var identity = __instance.targetLogicCounter.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetLogicCounter.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("CounterAdvancedMode"),
				Value = __instance.targetLogicCounter.advancedMode ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync signal counter reset
	/// </summary>
	[HarmonyPatch(typeof(CounterSideScreen), nameof(CounterSideScreen.ResetCounter))]
	public static class CounterSideScreen_ResetCounter_Patch
	{
		public static void Postfix(CounterSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.targetLogicCounter == null) return;

			var identity = __instance.targetLogicCounter.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.targetLogicCounter.gameObject),
				ConfigHash = NetworkingHash.ForConfigKey("CounterReset"),
				Value = 1f,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync storage sweep-only toggle
	/// </summary>
	[HarmonyPatch(typeof(Storage), nameof(Storage.SetOnlyFetchMarkedItems))]
	public static class Storage_SetOnlyFetchMarkedItems_Patch
	{
		public static void Postfix(Storage __instance, bool is_set)
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
				ConfigHash = NetworkingHash.ForConfigKey("StorageSweepOnly"),
				Value = is_set ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}

	/// <summary>
	/// Sync fabricator recipe queue changes
	/// </summary>
	[HarmonyPatch(typeof(ComplexFabricator), nameof(ComplexFabricator.SetRecipeQueueCount))]
	public static class ComplexFabricator_SetRecipeQueueCount_Patch
	{
	public static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe, int count)
		{
			using var _ = Profiler.Scope();

			try
			{
				DebugConsole.Log($"[ComplexFabricator] SetRecipeQueueCount Postfix called: recipe={recipe?.id ?? "null"}, count={count}");

				if (BuildingConfigPacket.IsApplyingPacket)
				{
					DebugConsole.Log($"[ComplexFabricator] Ignoring sync - IsApplyingPacket=true");
					return;
				}
				if (!MultiplayerSession.InSession)
				{
					DebugConsole.Log($"[ComplexFabricator] Not in session, skipping sync");
					return;
				}

				var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
				identity.RegisterIdentity();

				// Use recipe ID hash and pack count into Value
				var packet = new BuildingConfigPacket
				{
					NetId = identity.NetId,
					Cell = Grid.PosToCell(__instance.gameObject),
					ConfigHash = NetworkingHash.ForConfigKey(recipe.id),
					Value = count,
					ConfigType = BuildingConfigType.RecipeQueue
				};

				DebugConsole.Log($"[ComplexFabricator] Sending recipe={recipe.id}, count={count}, NetId={identity.NetId}, Hash={packet.ConfigHash}");

				if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
				else PacketSender.SendToHost(packet);
			}
			catch (System.Exception ex)
			{
				DebugConsole.Log($"[ComplexFabricator] ERROR in Postfix: {ex.Message}");
			}
		}
	}

	/// <summary>
	/// Sync FoodStorage SpicedFoodOnly property changes (when toggled via sidescreen)
	/// </summary>
	[HarmonyPatch(typeof(FoodStorage), "SpicedFoodOnly", MethodType.Setter)]
	public static class FoodStorage_SpicedFoodOnly_Patch
	{
		public static void Postfix(FoodStorage __instance, bool value)
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
				ConfigHash = NetworkingHash.ForConfigKey("FoodStorageSpicedFoodOnly"),
				Value = value ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}
	}
}
