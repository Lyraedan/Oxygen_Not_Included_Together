using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Networking.Packets.World;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.GamePatches
{
	internal static class ManagedDeliverScope
	{
		internal static void Begin(out bool active)
		{
			active = MultiplayerSession.IsHostInSession;
			if (active)
				NetworkIdentity.BeginManagedSpawn();
		}

		internal static System.Exception End(System.Exception exception, bool active)
		{
			if (active)
				NetworkIdentity.EndManagedSpawn();
			return exception;
		}

		internal static ulong EnsureLifecycle(NetworkIdentity identity)
		{
			if (identity.NetId == 0)
				identity.RegisterIdentity();
			if (identity.NetId == 0)
				return 0;
			ulong revision = identity.LifecycleRevision;
			if (revision == 0)
			{
				revision = NetworkIdentityRegistry.BeginLifecycle(identity.NetId);
				identity.LifecycleRevision = revision;
			}
			return revision;
		}
	}

	/// <summary>
	/// Patch MinionStartingStats.Deliver to send EntitySpawnPacket when duplicants are spawned.
	/// This ensures newly printed duplicants are synced to clients with correct NetIds.
	/// </summary>
	[HarmonyPatch(typeof(MinionStartingStats), nameof(MinionStartingStats.Deliver))]
	public static class MinionDeliverPatch
	{
		public static void Prefix(out bool __state) => ManagedDeliverScope.Begin(out __state);

		public static void Postfix(MinionStartingStats __instance, Vector3 location, ref GameObject __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;
			if (!MultiplayerSession.IsHost) return;
			if (__result == null) return;

			try
			{
				// Get or add NetworkIdentity
				var identity = __result.AddOrGet<NetworkIdentity>();

				ulong revision = ManagedDeliverScope.EnsureLifecycle(identity);
				if (identity.NetId == 0 || revision == 0)
					return;
				// Send EntitySpawnPacket to clients
				var packet = new TelepadEntitySpawnPacket
				{
					NetId = identity.NetId,
					Revision = revision,
					Pos = location,
					EntityData = ImmigrantOptionEntry.FromGameDeliverable(__instance)
				};

				PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
				DebugConsole.Log($"[MinionDeliverPatch] Sent EntitySpawnPacket for {__instance.Name} (NetId: {identity.NetId})");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[MinionDeliverPatch] Error: {ex.Message}");
			}
		}

		public static System.Exception Finalizer(System.Exception __exception, bool __state)
			=> ManagedDeliverScope.End(__exception, __state);
	}

	/// <summary>
	/// Patch CarePackageInfo.Deliver to send EntitySpawnPacket when care packages are spawned.
	/// </summary>
	[HarmonyPatch(typeof(CarePackageInfo), nameof(CarePackageInfo.Deliver))]
	public static class CarePackageDeliverPatch
	{
		public static void Prefix(out bool __state) => ManagedDeliverScope.Begin(out __state);

		public static void Postfix(CarePackageInfo __instance, Vector3 location, ref GameObject __result)
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.InSession) return;
			if (!MultiplayerSession.IsHost) return;
			if (__result == null) return;

			try
			{
				// Get or add NetworkIdentity
				var identity = __result.AddOrGet<NetworkIdentity>();

				ulong revision = ManagedDeliverScope.EnsureLifecycle(identity);
				if (identity.NetId == 0 || revision == 0)
					return;

				// Send EntitySpawnPacket to clients
				var packet = new TelepadEntitySpawnPacket
				{
					NetId = identity.NetId,
					Revision = revision,
					Pos = location,
					EntityData = ImmigrantOptionEntry.FromGameDeliverable(__instance)
				};

				PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
				DebugConsole.Log($"[CarePackageDeliverPatch] Sent EntitySpawnPacket for {__instance.id} x{__instance.quantity} (NetId: {identity.NetId})");
			}
			catch (System.Exception ex)
			{
				DebugConsole.LogError($"[CarePackageDeliverPatch] Error: {ex.Message}");
			}
		}

		public static System.Exception Finalizer(System.Exception __exception, bool __state)
			=> ManagedDeliverScope.End(__exception, __state);
	}
}
