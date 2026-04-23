using HarmonyLib;
using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for ComplexFabricator recipe queue changes.
	/// These are called by the SelectedRecipeQueueScreen UI.
	/// </summary>

	[HarmonyPatch(typeof(ComplexFabricator), nameof(ComplexFabricator.IncrementRecipeQueueCount))]
	public static class ComplexFabricator_IncrementRecipeQueueCount_Patch
	{
		public static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe)
		{
			using var _ = Profiler.Scope();

			ComplexFabricatorSyncHelper.SyncRecipe(__instance, recipe, "IncrementRecipeQueueCount");
		}
	}

	[HarmonyPatch(typeof(ComplexFabricator), nameof(ComplexFabricator.DecrementRecipeQueueCount))]
	public static class ComplexFabricator_DecrementRecipeQueueCount_Patch
	{
		public static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe)
		{
			using var _ = Profiler.Scope();

			ComplexFabricatorSyncHelper.SyncRecipe(__instance, recipe, "DecrementRecipeQueueCount");
		}
	}

	// SetRecipeQueueCount is already patched in StoragePatches.cs but may not be working
	// Adding a backup patch here
	[HarmonyPatch(typeof(ComplexFabricator), nameof(ComplexFabricator.SetRecipeQueueCount))]
	public static class ComplexFabricator_SetRecipeQueueCount_Patch2
	{
		public static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe, int count)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			try
			{
				var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
				identity.RegisterIdentity();

				var packet = new BuildingConfigPacket
				{
					NetId = identity.NetId,
					Cell = Grid.PosToCell(__instance.gameObject),
					ConfigHash = recipe.id.GetHashCode(),
					Value = count,
					ConfigType = BuildingConfigType.RecipeQueue
				};

				if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
				else PacketSender.SendToHost(packet);

				DebugConsole.Log($"[SetRecipeQueueCount_Patch2] Synced recipe={recipe.id}, count={count} on {__instance.name}");
			}
			catch (System.Exception ex)
			{
				DebugConsole.Log($"[SetRecipeQueueCount_Patch2] ERROR: {ex.Message}");
			}
		}
	}

	public static class ComplexFabricatorSyncHelper
	{
		public static void SyncRecipe(ComplexFabricator fabricator, ComplexRecipe recipe, string methodName)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			try
			{
				var identity = fabricator.gameObject.AddOrGet<NetworkIdentity>();
				identity.RegisterIdentity();

				// Get the current queue count after the change
				int count = fabricator.GetRecipeQueueCount(recipe);

				var packet = new BuildingConfigPacket
				{
					NetId = identity.NetId,
					Cell = Grid.PosToCell(fabricator.gameObject),
					ConfigHash = recipe.id.GetHashCode(),
					Value = count,
					ConfigType = BuildingConfigType.RecipeQueue
				};

				if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
				else PacketSender.SendToHost(packet);

				DebugConsole.Log($"[{methodName}] Synced recipe={recipe.id}, count={count} on {fabricator.name}");
			}
			catch (System.Exception ex)
			{
				DebugConsole.Log($"[{methodName}] ERROR: {ex.Message}");
			}
		}
	}
}
