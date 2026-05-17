using HarmonyLib;
using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using System;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(WorldDamage), nameof(WorldDamage.OnDigComplete))]
	public static class WorldDamagePatch
	{
		[HarmonyPrefix]
		public static bool Prefix(int cell, float mass, float temperature, ushort element_idx, byte disease_idx, int disease_count)
		{
			using var _ = Profiler.Scope();

			try
			{
				OnDigCompletedUpdated(cell, mass, temperature, element_idx, disease_idx, disease_count);
				return false;
			}
			catch (Exception ex)
			{
				DebugConsole.LogError($"[WorldDamagePatch.Prefix] {ex}");
				return true;
			}
		}

		private static void OnDigCompletedUpdated(int cell, float mass, float temperature, ushort element_idx, byte disease_idx, int disease_count)
		{
			using var _ = Profiler.Scope();

			Vector3 vector = Grid.CellToPos(cell, CellAlignment.RandomInternal, Grid.SceneLayer.Ore);
			Element element = ElementLoader.elements[element_idx];
			Grid.Damage[cell] = 0f;
			InvokePlaySoundForSubstance(element, vector);
			//Instance.PlaySoundForSubstance(element, vector);
			float num = mass * 0.5f;
			if (!(num <= 0f))
			{
				GameObject gameObject = element.substance.SpawnResource(vector, num, temperature, disease_idx, disease_count);
				NetworkIdentity networkIdentity = gameObject.GetComponent<NetworkIdentity>();

				Pickupable component = gameObject.GetComponent<Pickupable>();
				if (component != null && component.GetMyWorld() != null && component.GetMyWorld().worldInventory.IsReachable(component))
				{
					PopFXManager.Instance.SpawnFX(PopFXManager.Instance.sprite_Resource, Mathf.RoundToInt(num) + " " + element.name, gameObject.transform);
				}

				if (MultiplayerSession.IsHost)
				{
					// Bug-D: host-side NetId=0 race. SpawnResource should have triggered
					// OnSpawn→RegisterIdentity synchronously, but some prefabs lack a
					// NetworkIdentity, or Grid.WidthInCells==0 during world-load skips
					// registration. Force a registration retry, then skip the send if
					// we still don't have a valid NetId — otherwise clients receive a
					// packet keyed to 0 and every subsequent pickup/update never matches.
					if (networkIdentity == null)
					{
						DebugConsole.LogWarning($"[WorldDamagePatch] spawned ore '{gameObject.name}' has no NetworkIdentity; skipping sync");
					}
					else
					{
						if (networkIdentity.NetId == 0)
							networkIdentity.RegisterIdentity();

						if (networkIdentity.NetId == 0)
						{
							DebugConsole.LogWarning($"[WorldDamagePatch] NetId still 0 after RegisterIdentity for '{gameObject.name}'; skipping spawn sync (client will be short one item)");
						}
						else
						{
							Vector3 pos = Grid.CellToPos(cell, CellAlignment.RandomInternal, Grid.SceneLayer.Ore);

							var packet = new WorldDamageSpawnResourcePacket
							{
								NetId = networkIdentity.NetId,
								Position = pos,
								Mass = mass * 0.5f,
								Temperature = temperature,
								ElementIndex = element_idx,
								DiseaseIndex = disease_idx,
								DiseaseCount = disease_count
							};

							PacketSender.SendToAllClients(packet);
							DebugConsole.Log("Sent spawn resource packet with netid " + networkIdentity.NetId);
						}
					}
				}
			}
		}

		private static void InvokePlaySoundForSubstance(Element element, Vector3 position)
		{
			using var _ = Profiler.Scope();

			var method = typeof(WorldDamage).GetMethod("PlaySoundForSubstance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

			if (method == null)
			{
				DebugConsole.LogWarning("[Multiplayer] Could not find PlaySoundForSubstance via reflection.");
				return;
			}

			var worldDamage = WorldDamage.Instance;

			if (worldDamage == null)
			{
				DebugConsole.LogWarning("[Multiplayer] WorldDamage.Instance is null.");
				return;
			}

			method.Invoke(worldDamage, new object[] { element, position });
		}
	}
}
