using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.World;
using System.Collections;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Scripts.Creatures
{
	internal class CreatureMultiplayerInitializer : KMonoBehaviour
	{
		[MyCmpGet] NetworkIdentity identity;
		[MyCmpGet] KPrefabID kpref;

		private bool HasInit = false;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();
			base.OnSpawn();
			if (MultiplayerSession.InActiveSession)
			{
				FinalizeInit();
			}
			else
			{
				StartCoroutine(WaitForSessionAndInit());
			}
		}

		IEnumerator WaitForSessionAndInit()
		{
			yield return new WaitUntil((() => MultiplayerSession.InActiveSession));
			FinalizeInit();
		}

		void FinalizeInit()
		{
			using var _ = Profiler.Scope();
			if (HasInit) return;

			var go = gameObject;
			bool isCreature = kpref?.HasTag(GameTags.Creature) ?? false;
			bool isRover = go.GetComponent<RoverModifiers>() != null;
			bool hasBrain = go.GetComponent<CreatureBrain>() != null;
			if (!isCreature && !isRover && !hasBrain) return;
			if (kpref?.HasTag(GameTags.BaseMinion) ?? false) return;

			if (MultiplayerSession.IsClient)
			{
				InitializeClient(go);
			}
			else
			{
				InitializeHost(go);
			}

			HasInit = true;
		}

		void InitializeHost(GameObject go)
		{
			go.AddOrGet<StatusItemsSyncer>();

			if (MultiplayerSession.IsHostInSession && Game.Instance != null && Game.Instance.isSpawned && !GameServerHardSync.IsHardSyncInProgress && !SpawnPrefabPacket.ProcessingIncoming)
			{
				var identity = go.AddOrGet<NetworkIdentity>();
				if (identity.NetId == 0)
					identity.RegisterIdentity();

				if (identity.NetId != 0)
				{
					var packet = new ONI_Together.Networking.Packets.World.SpawnPrefabPacket(
						identity.NetId,
						go.PrefabID().GetHashCode(),
						go.transform.position,
						go.PrefabID().Name
					)
					{
						IsActive = go.activeSelf
					};
					PacketSender.SendToAllClients(packet);
				}
			}
		}

		void InitializeClient(GameObject go)
		{
			if (go.TryGetComponent<CreatureBrain>(out var brain)) brain.enabled = false;
			if (go.TryGetComponent<Sensors>(out var sensors)) sensors.enabled = false;
			if (go.TryGetComponent<ChoreConsumer>(out var consumer)) consumer.enabled = false;
			if (go.TryGetComponent<ChoreDriver>(out var driver)) driver.enabled = false;
			if (go.TryGetComponent<Navigator>(out var nav)) nav.enabled = false;

			var statusReceiver = go.AddOrGet<ClientReceiver_StatusItems>();
			statusReceiver.recieverType = ClientReceiver_StatusItems.StatusRecieverType.CREATURE;
		}
	}
}
