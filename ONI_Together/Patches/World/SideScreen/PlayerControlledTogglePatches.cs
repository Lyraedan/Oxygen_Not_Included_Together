using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Patches for player-controlled toggle switches using the side screen.
	/// Uses ToggleRequested property to detect if toggle came from a paused request.
	/// </summary>
	public static class PlayerControlledTogglePatchState
	{
		// Track which buildings had a pending toggle (ToggleRequested was true before Toggle executed)
		// This is set in Prefix and checked in Postfix
		public static HashSet<int> HadPendingToggle = new HashSet<int>();
	}

	[HarmonyPatch(typeof(PlayerControlledToggleSideScreen), "RequestToggle")]
	public static class PlayerControlledToggleSideScreen_RequestToggle_Patch
	{
		public static void Postfix(PlayerControlledToggleSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.target == null) return;

			try
			{
				var selectable = __instance.target.GetSelectable();
				if (selectable == null) return;

				var go = selectable.gameObject;
				var identity = go.AddOrGet<NetworkIdentity>();
				identity.RegisterIdentity();

				// When game is paused and player clicks toggle, sync the TARGET state (opposite of current)
				bool currentState = __instance.target.ToggledOn();
				bool targetState = !currentState;

				var packet = new BuildingConfigPacket
				{
					NetId = identity.NetId,
					Cell = Grid.PosToCell(go),
					ConfigHash = "LogicSwitchState".GetHashCode(),
					Value = targetState ? 1f : 0f,
					ConfigType = BuildingConfigType.Boolean
				};

				if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
				else PacketSender.SendToHost(packet);

				DebugConsole.Log($"[RequestToggle_Patch] Synced target={targetState} on {go.name}");
			}
			catch (System.Exception ex)
			{
				DebugConsole.Log($"[RequestToggle_Patch] ERROR: {ex.Message}");
			}
		}
	}

	[HarmonyPatch(typeof(PlayerControlledToggleSideScreen), "Toggle")]
	public static class PlayerControlledToggleSideScreen_Toggle_Patch
	{
		// Prefix: Check if there's a pending toggle BEFORE Toggle executes
		public static void Prefix(PlayerControlledToggleSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (__instance.target == null) return;

			try
			{
				var selectable = __instance.target.GetSelectable();
				if (selectable == null) return;

				var go = selectable.gameObject;
				var identity = go.GetComponent<NetworkIdentity>();
				if (identity == null) return;

				// Check if there's a pending toggle request (from paused state)
				if (__instance.target.ToggleRequested)
				{
					PlayerControlledTogglePatchState.HadPendingToggle.Add(identity.NetId);
					DebugConsole.Log($"[Toggle_Prefix] Detected pending toggle on {go.name}");
				}
			}
			catch (System.Exception ex)
			{
				DebugConsole.Log($"[Toggle_Prefix] ERROR: {ex.Message}");
			}
		}

		// Postfix: Only sync if this wasn't from a pending toggle (already synced in RequestToggle)
		public static void Postfix(PlayerControlledToggleSideScreen __instance)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InSession) return;
			if (__instance.target == null) return;

			try
			{
				var selectable = __instance.target.GetSelectable();
				if (selectable == null) return;

				var go = selectable.gameObject;
				var identity = go.AddOrGet<NetworkIdentity>();
				identity.RegisterIdentity();

				// Check if this toggle was from a pending request
				if (PlayerControlledTogglePatchState.HadPendingToggle.Contains(identity.NetId))
				{
					// Clear the flag and skip - we already synced in RequestToggle
					PlayerControlledTogglePatchState.HadPendingToggle.Remove(identity.NetId);
					DebugConsole.Log($"[Toggle_Postfix] Skipping - was from pending toggle on {go.name}");
					return;
				}

				// Fresh toggle (not from pending), sync the current state
				bool currentState = __instance.target.ToggledOn();

				var packet = new BuildingConfigPacket
				{
					NetId = identity.NetId,
					Cell = Grid.PosToCell(go),
					ConfigHash = "LogicSwitchState".GetHashCode(),
					Value = currentState ? 1f : 0f,
					ConfigType = BuildingConfigType.Boolean
				};

				if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
				else PacketSender.SendToHost(packet);

				DebugConsole.Log($"[Toggle_Postfix] Synced state={currentState} on {go.name}");
			}
			catch (System.Exception ex)
			{
				DebugConsole.Log($"[Toggle_Postfix] ERROR: {ex.Message}");
			}
		}
	}
}
