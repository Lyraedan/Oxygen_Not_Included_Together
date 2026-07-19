using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Patches.DLC.SpacedOut
{
	internal static class RailGunPayloadSync
	{
		internal const int MaxPendingPayloads = 128;
		internal const float PendingLifetimeSeconds = 120f;
		internal static readonly PacketSendMode DomainSendMode = PacketSendMode.ReliableImmediate;
		private static readonly Dictionary<int, int> LastAppliedRevision = new();
		private static readonly Dictionary<int, PendingPayload> PendingPayloads = new();
		private static RailGun _launchingRailGun;
		private static bool _retrying;
		private static bool _resolving;
		private sealed class PendingPayload
		{
			internal RailGunPayloadStatePacket Packet;
			internal float ExpiresAt;
		}
		private sealed class ApplyTarget
		{
			internal RailGun RailGun;
			internal RailGunPayload.StatesInstance Payload;
			internal RailGunPayloadSyncMarker Marker;
			internal Storage Storage;
			internal readonly List<GameObject> Items = new();
		}
		public static void ResetSessionState()
		{
			LastAppliedRevision.Clear();
			PendingPayloads.Clear();
			_launchingRailGun = null;
			_retrying = false;
			_resolving = false;
		}
		internal static int PendingCount => PendingPayloads.Count;
		internal static void CachePending(RailGunPayloadStatePacket packet, float now)
		{
			PrunePending(now);
			if (PendingPayloads.TryGetValue(packet.PayloadNetId, out PendingPayload pending))
			{
				if (packet.Revision <= pending.Packet.Revision)
					return;
			}
			else if (PendingPayloads.Count >= MaxPendingPayloads)
				EvictOldestPending();
			PendingPayloads[packet.PayloadNetId] = new PendingPayload
			{
				Packet = packet,
				ExpiresAt = now + PendingLifetimeSeconds
			};
		}
		internal static bool TryGetPendingRevision(int payloadNetId, float now, out int revision)
		{
			PrunePending(now);
			if (PendingPayloads.TryGetValue(payloadNetId, out PendingPayload pending))
			{
				revision = pending.Packet.Revision;
				return true;
			}
			revision = -1;
			return false;
		}
		internal static bool NeedsApply(int appliedRevision, int incomingRevision)
			=> incomingRevision > appliedRevision;
		internal static void BeginLaunch(RailGun railGun) => _launchingRailGun = railGun;
		internal static void EndLaunch() => _launchingRailGun = null;
		internal static RailGun CurrentLaunchingRailGun => _launchingRailGun;
		internal static bool TryCapture(RailGunPayload.StatesInstance smi, RailGunPayloadPhase phase,
			out RailGunPayloadStatePacket packet)
		{
			packet = null;
			RailGunPayloadSyncMarker marker = smi?.gameObject.AddOrGet<RailGunPayloadSyncMarker>();
			if (marker == null || marker.SourceRailGunNetId == 0 || marker.Source == AxialI.INVALID ||
			    marker.Destination == AxialI.INVALID)
				return false;
			int payloadNetId = smi.gameObject.GetNetIdentity()?.NetId ?? 0;
			if (payloadNetId == 0 || !NetworkIdentityRegistry.TryGetComponent(marker.SourceRailGunNetId, out RailGun railGun))
				return false;
			packet = new RailGunPayloadStatePacket
			{
				SourceRailGunNetId = marker.SourceRailGunNetId,
				PayloadNetId = payloadNetId,
				Revision = marker.Revision,
				Phase = phase,
				SourceQ = marker.Source.q,
				SourceR = marker.Source.r,
				DestinationQ = marker.Destination.q,
				DestinationR = marker.Destination.r,
				DestinationWorld = ClusterUtil.GetAsteroidWorldIdAtLocation(marker.Destination),
				Position = smi.transform.GetPosition(),
				TakeoffVelocity = smi.takeoffVelocity,
				SourceParticles = railGun.hepStorage.Particles,
				SymbolSwapIndex = Traverse.Create(smi).Field("randomSymbolSwapIndex").GetValue<int>(),
				Items = CaptureItems(smi.GetComponent<Storage>())
			};
			return packet.IsWireValid();
		}

		internal static bool Publish(RailGunPayloadStatePacket packet)
		{
			var identities = new List<NetworkIdentity>(packet.Items.Count + 1);
			if (!NetworkIdentityRegistry.TryGet(packet.PayloadNetId, out NetworkIdentity payloadIdentity))
				return false;
			identities.Add(payloadIdentity);
			foreach (RailGunPayloadItemData item in packet.Items)
			{
				if (!NetworkIdentityRegistry.TryGet(item.NetId, out NetworkIdentity itemIdentity))
					return false;
				identities.Add(itemIdentity);
			}
			foreach (NetworkIdentity identity in identities)
				identity.EnsureAuthoritativeSpawnBroadcast();
			PacketSender.SendToAllClients(packet, DomainSendMode);
			return true;
		}

		internal static bool TryApply(RailGunPayloadStatePacket packet)
		{
			if (packet == null || !packet.IsWireValid())
				return false;
			PrunePending(Time.unscaledTime);
			if (PendingPayloads.TryGetValue(packet.PayloadNetId, out PendingPayload latest) &&
			    latest.Packet.Revision > packet.Revision)
				return true;
			int applied = LastAppliedRevision.TryGetValue(packet.PayloadNetId, out int revision) ? revision : -1;
			if (!NeedsApply(applied, packet.Revision))
				return true;
			_resolving = true;
			bool resolved;
			ApplyTarget target;
			try { resolved = TryResolve(packet, out target); }
			finally { _resolving = false; }
			if (!resolved)
			{
				CachePending(packet, Time.unscaledTime);
				return false;
			}
			if (!NeedsApply(target.Marker.AppliedRevision, packet.Revision))
			{
				PendingPayloads.Remove(packet.PayloadNetId);
				return true;
			}
			SpacedOutSyncGuard.Run(() =>
			{
				if (!target.Marker.Started)
				{
					target.Payload.StartSM();
					target.Marker.Started = true;
				}
				ApplySourceParticles(target.RailGun, packet.SourceParticles);
				ApplyItems(target.Storage, packet.Items, target.Items);
				target.Payload.takeoffVelocity = packet.TakeoffVelocity;
				ApplySymbol(target.Payload, packet.SymbolSwapIndex);
				ApplyPhase(target.Payload, packet);
				target.Payload.transform.SetPosition(packet.Position);
			});
			target.Marker.AppliedRevision = packet.Revision;
			LastAppliedRevision[packet.PayloadNetId] = packet.Revision;
			PendingPayloads.Remove(packet.PayloadNetId);
			return true;
		}
		private static bool TryResolve(RailGunPayloadStatePacket packet, out ApplyTarget target)
		{
			target = new ApplyTarget();
			if (!NetworkIdentityRegistry.TryGetComponent(packet.SourceRailGunNetId, out target.RailGun) ||
			    target.RailGun.hepStorage == null || !TryResolvePayload(packet, target) ||
			    !TryResolveItems(packet.Items, target))
			{
				target = null;
				return false;
			}
			return true;
		}
		private static bool TryResolvePayload(RailGunPayloadStatePacket packet, ApplyTarget target)
		{
			if (!NetworkIdentityRegistry.TryGet(packet.PayloadNetId, out NetworkIdentity identity)) return false;
			GameObject go = identity.gameObject;
			if (go.PrefabID().GetHashCode() != RailGunPayloadConfig.ID.GetHashCode()) return false;
			target.Marker = go.AddOrGet<RailGunPayloadSyncMarker>();
			go.AddOrGet<EntityPositionHandler>();
			target.Payload = go.GetSMI<RailGunPayload.StatesInstance>();
			target.Storage = go.GetComponent<Storage>();
			return target.Payload != null && target.Storage != null &&
			       CanApplySymbol(target.Payload, packet.SymbolSwapIndex);
		}
		private static bool CanApplySymbol(RailGunPayload.StatesInstance smi, int index)
		{
			if (index < 0) return true;
			RailGunPayload.Def def = smi.def;
			return def?.randomClusterSymbolSwaps != null && def.randomWorldSymbolSwaps != null &&
			       index < def.randomClusterSymbolSwaps.Count && index < def.randomWorldSymbolSwaps.Count &&
			       smi.GetComponent<BallisticClusterGridEntity>() != null && smi.animController != null &&
			       smi.animController.AnimFiles.Length > 0 &&
			       smi.animController.GetComponent<SymbolOverrideController>() != null;
		}
		private static void ApplyPhase(RailGunPayload.StatesInstance smi, RailGunPayloadStatePacket packet)
		{
			AxialI source = AxialCoordinateSync.FromQr(packet.SourceQ, packet.SourceR);
			AxialI destination = AxialCoordinateSync.FromQr(packet.DestinationQ, packet.DestinationR);
			switch (packet.Phase)
			{
				case RailGunPayloadPhase.Takeoff:
					smi.Launch(source, destination);
					break;
				case RailGunPayloadPhase.Travel:
					smi.Travel(source, destination);
					break;
				case RailGunPayloadPhase.Landing:
					smi.GoTo(smi.sm.landing.landing);
					smi.sm.destinationWorld.Set(-1, smi);
					if (GameComps.Fallers.Has(smi.gameObject)) GameComps.Fallers.Remove(smi.gameObject);
					GameComps.Fallers.Add(smi.gameObject, new Vector2(0f, -10f));
					break;
				case RailGunPayloadPhase.Grounded:
					smi.sm.destinationWorld.Set(-1, smi);
					smi.GoTo(smi.sm.grounded.crater);
					break;
			}
		}
		private static List<RailGunPayloadItemData> CaptureItems(Storage storage)
		{
			var items = new List<RailGunPayloadItemData>();
			if (storage == null) return items;
			foreach (GameObject go in storage.items)
			{
				PrimaryElement primary = go?.GetComponent<PrimaryElement>();
				int netId = go?.GetNetIdentity()?.NetId ?? 0;
				if (primary == null || netId == 0 || primary.Mass <= 0f) continue;
				items.Add(new RailGunPayloadItemData
				{
					NetId = netId,
					PrefabHash = go.PrefabID().GetHashCode(),
					Mass = primary.Mass,
					Temperature = primary.Temperature,
					DiseaseIndex = primary.DiseaseIdx,
					DiseaseCount = Math.Max(0, primary.DiseaseCount)
				});
			}
			return items;
		}
		private static void ApplySourceParticles(RailGun railGun, float particles)
		{
			railGun.hepStorage.ConsumeAll();
			railGun.hepStorage.Store(particles);
		}
		private static void ApplySymbol(RailGunPayload.StatesInstance smi, int index)
		{
			RailGunPayload.Def def = smi.def;
			if (index < 0 || def?.randomClusterSymbolSwaps == null || def.randomWorldSymbolSwaps == null ||
			    index >= def.randomClusterSymbolSwaps.Count || index >= def.randomWorldSymbolSwaps.Count)
				return;
			Traverse.Create(smi).Field("randomSymbolSwapIndex").SetValue(index);
			smi.GetComponent<BallisticClusterGridEntity>()
				.SwapSymbolFromSameAnim(def.clusterAnimSymbolSwapTarget, def.randomClusterSymbolSwaps[index]);
			KAnim.Build.Symbol symbol = smi.animController.AnimFiles[0].GetData().build
				.GetSymbol(def.randomWorldSymbolSwaps[index]);
			smi.animController.GetComponent<SymbolOverrideController>()
				.AddSymbolOverride(def.worldAnimSymbolSwapTarget, symbol);
		}
		private static void ApplyItems(Storage storage, List<RailGunPayloadItemData> desired,
			List<GameObject> resolved)
		{
			var desiredIds = new HashSet<int>();
			for (int i = 0; i < desired.Count; i++)
			{
				RailGunPayloadItemData item = desired[i];
				GameObject go = resolved[i];
				desiredIds.Add(item.NetId);
				ApplyItemState(go.GetComponent<PrimaryElement>(), item);
				if (!storage.items.Contains(go)) storage.Store(go, true, true);
			}
			for (int i = storage.items.Count - 1; i >= 0; i--)
			{
				GameObject go = storage.items[i];
				int netId = go?.GetComponent<NetworkIdentity>()?.NetId ?? 0;
				if (desiredIds.Contains(netId)) continue;
				storage.Remove(go, false);
				Util.KDestroyGameObject(go);
			}
		}
		private static bool TryResolveItems(List<RailGunPayloadItemData> desired, ApplyTarget target)
		{
			foreach (RailGunPayloadItemData item in desired)
			{
				if (!NetworkIdentityRegistry.TryGet(item.NetId, out NetworkIdentity identity)) return false;
				GameObject go = identity.gameObject;
				if (go.GetComponent<PrimaryElement>() == null || go.PrefabID().GetHashCode() != item.PrefabHash)
					return false;
				target.Items.Add(go);
			}
			return true;
		}
		private static void ApplyItemState(PrimaryElement primary, RailGunPayloadItemData item)
		{
			primary.Mass = item.Mass;
			primary.Temperature = item.Temperature;
			if (primary.DiseaseCount > 0) primary.ModifyDiseaseCount(-primary.DiseaseCount, "ONI Together railgun sync");
			if (item.DiseaseCount > 0 && item.DiseaseIndex != byte.MaxValue)
				primary.AddDisease(item.DiseaseIndex, item.DiseaseCount, "ONI Together railgun sync");
		}
		internal static void RetryPending()
		{
			if (_retrying) return;
			_retrying = true;
			try
			{
				PrunePending(Time.unscaledTime);
				foreach (PendingPayload pending in new List<PendingPayload>(PendingPayloads.Values))
					TryApply(pending.Packet);
			}
			finally
			{
				_retrying = false;
			}
		}

		internal static void IdentityAvailable()
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsClient || _resolving) return;
			RetryPending();
		}

		private static void PrunePending(float now)
		{
			foreach (int netId in new List<int>(PendingPayloads.Keys))
				if (PendingPayloads[netId].ExpiresAt < now)
					PendingPayloads.Remove(netId);
		}

		private static void EvictOldestPending()
		{
			int oldestId = 0;
			float oldestExpiry = float.MaxValue;
			foreach (KeyValuePair<int, PendingPayload> entry in PendingPayloads)
				if (entry.Value.ExpiresAt < oldestExpiry)
				{
					oldestId = entry.Key;
					oldestExpiry = entry.Value.ExpiresAt;
				}
			PendingPayloads.Remove(oldestId);
		}

	}

	internal sealed class RailGunPayloadSyncMarker : KMonoBehaviour
	{
		internal int Revision;
		internal int AppliedRevision = -1;
		internal int SourceRailGunNetId;
		internal AxialI Source = AxialI.INVALID;
		internal AxialI Destination = AxialI.INVALID;
		internal bool Started;
		internal RailGunPayloadPhase LastPhase;
	}

	[HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.OnSpawn))]
	internal static class RailGunPayloadIdentitySpawnPatch
	{
		internal static void Postfix() => RailGunPayloadSync.IdentityAvailable();
	}

	[HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.OverrideNetId))]
	internal static class RailGunPayloadIdentityUpdatePatch
	{
		internal static void Postfix(bool __result)
		{
			if (__result) RailGunPayloadSync.IdentityAvailable();
		}
	}

	[HarmonyPatch(typeof(RailGun), "LaunchProjectile")]
	internal static class RailGunLaunchProjectilePatch
	{
		internal static bool Prefix(RailGun __instance)
		{
			if (MultiplayerSession.InSession && MultiplayerSession.IsClient && !SpacedOutSyncGuard.IsApplying)
				return false;
			if (MultiplayerSession.InSession && MultiplayerSession.IsHost)
				RailGunPayloadSync.BeginLaunch(__instance);
			return true;
		}

		internal static void Postfix() => RailGunPayloadSync.EndLaunch();
		internal static Exception Finalizer(Exception __exception)
		{
			RailGunPayloadSync.EndLaunch();
			return __exception;
		}
	}

	[HarmonyPatch(typeof(RailGunPayload.StatesInstance), nameof(RailGunPayload.StatesInstance.Launch))]
	internal static class RailGunPayloadLaunchPatch
	{
		internal static void Postfix(RailGunPayload.StatesInstance __instance, AxialI source, AxialI destination)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost || SpacedOutSyncGuard.IsApplying) return;
			RailGunPayloadSyncMarker marker = __instance.gameObject.AddOrGet<RailGunPayloadSyncMarker>();
			__instance.gameObject.AddOrGet<EntityPositionHandler>();
			marker.Source = source;
			marker.Destination = destination;
			marker.Started = true;
			marker.SourceRailGunNetId = RailGunPayloadSync.CurrentLaunchingRailGun?.GetNetIdentity()?.NetId ?? 0;
			Send(__instance, marker, RailGunPayloadPhase.Takeoff);
		}

		internal static void Send(RailGunPayload.StatesInstance smi, RailGunPayloadSyncMarker marker,
			RailGunPayloadPhase phase)
		{
			marker.LastPhase = phase;
			if (RailGunPayloadSync.TryCapture(smi, phase, out RailGunPayloadStatePacket packet))
				RailGunPayloadSync.Publish(packet);
		}
	}

	[HarmonyPatch(typeof(RailGunPayload.StatesInstance), nameof(RailGunPayload.StatesInstance.MoveToSpace))]
	internal static class RailGunPayloadMoveToSpacePatch
	{
		internal static void Postfix(RailGunPayload.StatesInstance __instance)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost || SpacedOutSyncGuard.IsApplying) return;
			RailGunPayloadSyncMarker marker = __instance.gameObject.AddOrGet<RailGunPayloadSyncMarker>();
			marker.Revision++;
			RailGunPayloadLaunchPatch.Send(__instance, marker, RailGunPayloadPhase.Travel);
		}
	}

	[HarmonyPatch(typeof(RailGunPayload.StatesInstance), nameof(RailGunPayload.StatesInstance.StartLand))]
	internal static class RailGunPayloadStartLandPatch
	{
		internal static bool Prefix()
			=> SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient;

		internal static void Postfix(RailGunPayload.StatesInstance __instance)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost || SpacedOutSyncGuard.IsApplying) return;
			RailGunPayloadSyncMarker marker = __instance.gameObject.AddOrGet<RailGunPayloadSyncMarker>();
			marker.Revision++;
			RailGunPayloadLaunchPatch.Send(__instance, marker, RailGunPayloadPhase.Landing);
		}
	}

	[HarmonyPatch(typeof(RailGunPayload.StatesInstance), nameof(RailGunPayload.StatesInstance.UpdateLaunch))]
	internal static class RailGunPayloadUpdateLaunchPatch
	{
		internal static bool Prefix()
			=> SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient;
	}

	[HarmonyPatch(typeof(RailGunPayload.StatesInstance), nameof(RailGunPayload.StatesInstance.UpdateLanding))]
	internal static class RailGunPayloadUpdateLandingPatch
	{
		internal static bool Prefix(ref bool __result)
		{
			if (SpacedOutSyncGuard.IsApplying || !MultiplayerSession.InSession || !MultiplayerSession.IsClient) return true;
			__result = false;
			return false;
		}

		internal static void Postfix(RailGunPayload.StatesInstance __instance, bool __result)
		{
			if (!__result || !MultiplayerSession.InSession || !MultiplayerSession.IsHost) return;
			RailGunPayloadSyncMarker marker = __instance.gameObject.AddOrGet<RailGunPayloadSyncMarker>();
			if (marker.LastPhase == RailGunPayloadPhase.Grounded) return;
			marker.Revision++;
			RailGunPayloadLaunchPatch.Send(__instance, marker, RailGunPayloadPhase.Grounded);
		}
	}
}
