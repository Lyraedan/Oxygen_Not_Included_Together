using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class ReactorMeltdownSync
	{
		private const float EmitInterval = 0.5f;
		private const float MaxMassPerEmission = 5f;
		internal static readonly PacketSendMode DomainSendMode = PacketSendMode.ReliableImmediate;

		internal static bool NeedsApply(int appliedRevision, int incomingRevision)
			=> incomingRevision > appliedRevision;

		internal static void UpdateHost(Reactor.StatesInstance smi, float dt)
		{
			Reactor reactor = smi.master;
			reactor.timeSinceMeltdownEmit += dt;
			float remaining = smi.sm.meltdownMassRemaining.Get(smi);
			if (reactor.timeSinceMeltdownEmit <= EmitInterval || remaining <= 0f)
				return;

			reactor.timeSinceMeltdownEmit -= EmitInterval;
			float emissionMass = Mathf.Min(remaining, MaxMassPerEmission);
			smi.sm.meltdownMassRemaining.Delta(-emissionMass, smi);
			NetworkIdentity reactorIdentity = reactor.GetNetIdentity();
			if (reactorIdentity == null || reactorIdentity.NetId == 0)
				return;

			ReactorMeltdownSyncMarker marker = reactor.gameObject.AddOrGet<ReactorMeltdownSyncMarker>();
			var packet = new ReactorMeltdownOutcomePacket
			{
				ReactorNetId = reactorIdentity.NetId,
				Revision = ++marker.Revision,
				MeltdownMassRemaining = smi.sm.meltdownMassRemaining.Get(smi),
				TimeSinceMeltdownEmit = reactor.timeSinceMeltdownEmit
			};

			for (int i = 0; i < ReactorMeltdownOutcomePacket.MaxComets && emissionMass >= NuclearWasteCometConfig.MASS; i++)
			{
				ReactorMeltdownCometData comet = SpawnComet(reactor);
				if (comet != null) packet.Comets.Add(comet);
				emissionMass -= NuclearWasteCometConfig.MASS;
			}
			if (emissionMass >= 0.001f)
				EmitCells(reactor, emissionMass, packet);

			if (packet.IsWireValid())
				Publish(packet);
		}

		internal static bool Publish(ReactorMeltdownOutcomePacket packet)
		{
			var identities = new List<NetworkIdentity>(packet.Comets.Count);
			foreach (ReactorMeltdownCometData comet in packet.Comets)
			{
				if (!NetworkIdentityRegistry.TryGet(comet.NetId, out NetworkIdentity identity))
					return false;
				identities.Add(identity);
			}
			foreach (NetworkIdentity identity in identities)
				identity.EnsureAuthoritativeSpawnBroadcast();
			PacketSender.SendToAllClients(packet, DomainSendMode);
			return true;
		}

		internal static bool TryApply(ReactorMeltdownOutcomePacket packet)
		{
			if (packet == null || !packet.IsWireValid() ||
			    !NetworkIdentityRegistry.TryGetComponent(packet.ReactorNetId, out Reactor reactor) ||
			    !AllCometsBound(packet))
				return false;

			ReactorMeltdownSyncMarker marker = reactor.gameObject.AddOrGet<ReactorMeltdownSyncMarker>();
			if (!NeedsApply(marker.AppliedRevision, packet.Revision))
				return true;

			SpacedOutSyncGuard.Run(() =>
			{
				Reactor.StatesInstance smi = reactor.smi;
				smi.sm.meltdownMassRemaining.Set(packet.MeltdownMassRemaining, smi);
				reactor.timeSinceMeltdownEmit = packet.TimeSinceMeltdownEmit;
				foreach (ReactorMeltdownCometData comet in packet.Comets)
					ApplyComet(reactor, comet);
				byte disease = Db.Get().Diseases.GetIndex(Db.Get().Diseases.RadiationPoisoning.Id);
				foreach (ReactorMeltdownCellData cell in packet.Cells)
					SimMessages.AddRemoveSubstance(cell.Cell, SimHashes.NuclearWaste,
						CellEventLogger.Instance.ElementEmitted, cell.Mass, cell.Temperature,
						disease, cell.DiseaseCount);
			});
			marker.AppliedRevision = packet.Revision;
			return true;
		}

		private static ReactorMeltdownCometData SpawnComet(Reactor reactor)
		{
			GameObject prefab = Assets.GetPrefab(NuclearWasteCometConfig.ID);
			if (prefab == null) return null;
			GameObject go = Util.KInstantiate(prefab, reactor.transform.position + Vector3.up * 2f, Quaternion.identity);
			go.SetActive(true);
			Comet comet = go.GetComponent<Comet>();
			PrimaryElement primary = go.GetComponent<PrimaryElement>();
			KBatchedAnimController anim = go.GetComponent<KBatchedAnimController>();
			NetworkIdentity identity = go.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			go.AddOrGet<EntityPositionHandler>();
			go.AddOrGet<ReactorMeltdownCometMarker>();
			if (comet == null || primary == null || anim == null || identity.NetId == 0) return null;

			comet.ignoreObstacleForDamage.Set(reactor.gameObject.GetComponent<KPrefabID>());
			comet.addTiles = 1;
			int angle = 270;
			while (angle > 225 && angle < 335) angle = UnityEngine.Random.Range(0, 360);
			float radians = angle * MathF.PI / 180f;
			comet.Velocity = new Vector2(-Mathf.Cos(radians) * 20f, Mathf.Sin(radians) * 20f);
			anim.Rotation = -angle - 90f;

			return new ReactorMeltdownCometData
			{
				NetId = identity.NetId,
				Position = go.transform.GetPosition(),
				Velocity = comet.Velocity,
				Rotation = anim.Rotation,
				Mass = primary.Mass,
				Temperature = primary.Temperature,
				DiseaseIndex = primary.DiseaseIdx,
				DiseaseCount = Math.Max(0, primary.DiseaseCount)
			};
		}

		private static void EmitCells(Reactor reactor, float mass, ReactorMeltdownOutcomePacket packet)
		{
			byte disease = Db.Get().Diseases.GetIndex(Db.Get().Diseases.RadiationPoisoning.Id);
			for (int i = 0; i < ReactorMeltdownOutcomePacket.MaxCells; i++)
			{
				int cell = Grid.PosToCell(reactor.transform.position + Vector3.up * 3f + Vector3.right * i * 2f);
				float cellMass = mass / ReactorMeltdownOutcomePacket.MaxCells;
				int diseaseCount = Mathf.RoundToInt(50f * cellMass);
				SimMessages.AddRemoveSubstance(cell, SimHashes.NuclearWaste,
					CellEventLogger.Instance.ElementEmitted, cellMass, 3000f, disease, diseaseCount);
				packet.Cells.Add(new ReactorMeltdownCellData
				{
					Cell = cell, Mass = cellMass, Temperature = 3000f, DiseaseCount = diseaseCount
				});
			}
		}

		private static void ApplyComet(Reactor reactor, ReactorMeltdownCometData data)
		{
			if (!NetworkIdentityRegistry.TryGet(data.NetId, out NetworkIdentity identity)) return;
			GameObject go = identity.gameObject;
			AttachClientCometComponents(go);

			Comet comet = go.GetComponent<Comet>();
			PrimaryElement primary = go.GetComponent<PrimaryElement>();
			KBatchedAnimController anim = go.GetComponent<KBatchedAnimController>();
			if (comet == null || primary == null || anim == null) return;
			go.transform.SetPosition(data.Position);
			comet.Velocity = data.Velocity;
			comet.addTiles = 1;
			comet.ignoreObstacleForDamage.Set(reactor.gameObject.GetComponent<KPrefabID>());
			anim.Rotation = data.Rotation;
			primary.Mass = data.Mass;
			primary.Temperature = data.Temperature;
			if (primary.DiseaseCount > 0) primary.ModifyDiseaseCount(-primary.DiseaseCount, "ONI Together meltdown sync");
			if (data.DiseaseCount > 0 && data.DiseaseIndex != byte.MaxValue)
				primary.AddDisease(data.DiseaseIndex, data.DiseaseCount, "ONI Together meltdown sync");
		}

		internal static void AttachClientCometComponents(GameObject go)
		{
			go.AddOrGet<EntityPositionHandler>();
			go.AddOrGet<ReactorMeltdownCometMarker>();
		}

		private static bool AllCometsBound(ReactorMeltdownOutcomePacket packet)
		{
			int prefabHash = NuclearWasteCometConfig.ID.GetHashCode();
			foreach (ReactorMeltdownCometData data in packet.Comets)
			{
				if (!NetworkIdentityRegistry.TryGet(data.NetId, out NetworkIdentity identity)) return false;
				GameObject go = identity.gameObject;
				if (go.PrefabID().GetHashCode() != prefabHash || go.GetComponent<Comet>() == null ||
				    go.GetComponent<PrimaryElement>() == null || go.GetComponent<KBatchedAnimController>() == null)
					return false;
			}
			return true;
		}
	}

	internal sealed class ReactorMeltdownSyncMarker : KMonoBehaviour
	{
		internal int Revision;
		internal int AppliedRevision = -1;
	}

	internal sealed class ReactorMeltdownCometMarker : KMonoBehaviour { }

	[HarmonyPatch]
	internal static class ReactorMeltdownUpdatePatch
	{
		internal static MethodBase TargetMethod()
		{
			Type closure = typeof(Reactor.States).GetNestedType("<>c", BindingFlags.NonPublic);
			FieldInfo field = AccessTools.Field(typeof(Reactor), "timeSinceMeltdownEmit");
			if (closure == null || field == null) return null;
			return closure.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
				.FirstOrDefault(method => IsMeltdownUpdate(method, field.MetadataToken));
		}

		internal static bool Prefix(Reactor.StatesInstance smi, float dt)
		{
			if (ShouldRunOriginal(MultiplayerSession.InSession)) return true;
			if (MultiplayerSession.IsHost) ReactorMeltdownSync.UpdateHost(smi, dt);
			return false;
		}

		internal static bool ShouldRunOriginal(bool inSession) => !inSession;

		private static bool IsMeltdownUpdate(MethodInfo method, int fieldToken)
		{
			ParameterInfo[] parameters = method.GetParameters();
			if (method.ReturnType != typeof(void) || parameters.Length != 2 ||
			    parameters[0].ParameterType != typeof(Reactor.StatesInstance) ||
			    parameters[1].ParameterType != typeof(float)) return false;
			byte[] il = method.GetMethodBody()?.GetILAsByteArray();
			byte[] token = BitConverter.GetBytes(fieldToken);
			if (il == null) return false;
			for (int i = 0; i <= il.Length - token.Length; i++)
				if (il[i] == token[0] && il[i + 1] == token[1] && il[i + 2] == token[2] && il[i + 3] == token[3])
					return true;
			return false;
		}
	}

	[HarmonyPatch(typeof(Comet), nameof(Comet.Sim33ms))]
	internal static class ReactorMeltdownCometSimulationPatch
	{
		internal static bool Prefix(Comet __instance)
			=> !MultiplayerSession.InSession || !MultiplayerSession.IsClient ||
			   __instance.GetComponent<ReactorMeltdownCometMarker>() == null;
	}
}
