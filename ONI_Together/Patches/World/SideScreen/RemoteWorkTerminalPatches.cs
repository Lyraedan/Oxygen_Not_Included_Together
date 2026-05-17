using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for Remote Work Terminal (DLC3 - Frosty Planet Pack) synchronization.
	/// Syncs the FutureDock selection (which dock the terminal will use).
	/// </summary>

	[HarmonyPatch(typeof(RemoteWorkTerminal), nameof(RemoteWorkTerminal.FutureDock), MethodType.Setter)]
	public static class RemoteWorkTerminal_FutureDock_Patch
	{
		public static void Postfix(RemoteWorkTerminal __instance, RemoteWorkerDock value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			// Get the dock's NetId if available
			int dockNetId = -1;
			if (value != null)
			{
				var dockIdentity = value.gameObject.GetComponent<NetworkIdentity>();
				if (dockIdentity != null)
				{
					dockNetId = dockIdentity.NetId;
				}
			}

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = "RemoteWorkTerminalDock".GetHashCode(),
				Value = 0f,
				SliderIndex = dockNetId, // Store dock NetId
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);

			DebugConsole.Log($"[RemoteWorkTerminal_FutureDock_Patch] Synced FutureDock={value?.GetProperName() ?? "null"} (NetId={dockNetId}) on {__instance.name}");
		}
	}
}
