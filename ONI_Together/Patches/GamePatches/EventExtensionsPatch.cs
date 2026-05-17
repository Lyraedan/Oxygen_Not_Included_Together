using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Events;
using System;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.GamePatches
{
	[HarmonyPatch(typeof(EventExtensions))]
	public static class EventExtensionsPatch
	{
		// Flag to prevent recursive syncing
		private static bool isSyncing = false;

		// Use Postfix to intercept after the original method runs - avoids needing to call internal APIs
		[HarmonyPostfix]
		[HarmonyPatch(nameof(EventExtensions.Trigger), new Type[] { typeof(GameObject), typeof(int), typeof(object) })]
		public static void Postfix(GameObject go, int hash, object data)
		{
			return; // Disabled for now

			using var _ = Profiler.Scope();

			// Prevent recursive sync that could cause infinite loops
			if (isSyncing) return;

			if (go == null) return;

			try
			{
				// Only sync events from host to clients
				if (!MultiplayerSession.IsHost) return;

				isSyncing = true;

				NetworkIdentity identity = go.GetComponent<NetworkIdentity>();
				if (identity != null)
				{
					// Skip syncing events with Unity object data - these cannot be serialized
					// and attempting to serialize them causes freezes
					object safeData = data;
					if (data != null)
					{
						var dataType = data.GetType();
						// Skip UnityEngine.Object types (MonoBehaviour, Component, GameObject, ScriptableObject, etc.)
						if (typeof(UnityEngine.Object).IsAssignableFrom(dataType))
						{
							safeData = null; // Don't try to serialize Unity objects
						}
						// Skip any type from Assembly-CSharp that inherits from UnityEngine.Object
						else if (dataType.Assembly.GetName().Name == "Assembly-CSharp" &&
								 typeof(UnityEngine.Object).IsAssignableFrom(dataType.BaseType))
						{
							safeData = null;
						}
					}

					try
					{
						var packet = new EventTriggeredPacket(identity.NetId, hash, safeData);
						PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
					}
					catch (Exception)
					{
						// Silently ignore packet sending errors
					}
				}
			}
			catch (Exception e)
			{
				// Catch-all to prevent any crash
				DebugConsole.LogWarning($"[EventExtensionsPatch] Postfix error: {e.Message}");
			}
			finally
			{
				isSyncing = false;
			}
		}
	}
}
