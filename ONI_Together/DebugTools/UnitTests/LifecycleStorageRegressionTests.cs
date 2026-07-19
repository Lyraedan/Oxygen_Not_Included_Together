#if DEBUG
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using ONI_Together.Patches.KleiPatches;
using ONI_Together.Patches.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class LifecycleStorageRegressionTests
	{
		[UnitTest(name: "Runtime deterministic identities wait for host authority", category: "Sync")]
		public static UnitTestResult RuntimeIdentityWaitsForAuthority()
		{
			if (!NetworkIdentity.ShouldAwaitAuthorityBinding(
				    isClient: true, state: ClientState.InGame, netId: 17, hasAuthoritativeLifecycle: false)
			    || NetworkIdentity.ShouldAwaitAuthorityBinding(
				    isClient: true, state: ClientState.LoadingWorld, netId: 17, hasAuthoritativeLifecycle: false)
			    || NetworkIdentity.ShouldAwaitAuthorityBinding(
				    isClient: true, state: ClientState.InGame, netId: 17, hasAuthoritativeLifecycle: true)
			    || NetworkIdentity.ShouldAwaitAuthorityBinding(
				    isClient: false, state: ClientState.InGame, netId: 17, hasAuthoritativeLifecycle: false))
			{
				return UnitTestResult.Fail("Runtime client identity bypassed host lifecycle authority");
			}

			return UnitTestResult.Pass("Only unproven in-game client identities wait for host binding");
		}

		[UnitTest(name: "Runtime host broadcasts every authoritative spawn", category: "Sync")]
		public static UnitTestResult RuntimeHostBroadcastsEverySpawn()
		{
			if (!NetworkIdentity.ShouldBroadcastAuthoritativeSpawn(
				    inSession: true, isHost: true, alreadySent: false, registered: true,
				    managedSpawnSuppressed: false)
			    || NetworkIdentity.ShouldBroadcastAuthoritativeSpawn(
				    inSession: true, isHost: false, alreadySent: false, registered: true,
				    managedSpawnSuppressed: false)
			    || NetworkIdentity.ShouldBroadcastAuthoritativeSpawn(
				    inSession: true, isHost: true, alreadySent: true, registered: true,
				    managedSpawnSuppressed: false))
			{
				return UnitTestResult.Fail("Authoritative spawn broadcast gate is incomplete");
			}

			return UnitTestResult.Pass("Every newly registered runtime host identity is broadcast once");
		}

		[UnitTest(name: "Element spawn replay matches one authoritative create", category: "Sync")]
		public static UnitTestResult ElementSpawnReplayMatchesDescriptor()
		{
			var expected = ElementSpawn(17, 42f, 295f);
			var same = ElementSpawn(17, 42f, 295f);
			var wrongPrefab = ElementSpawn(18, 42f, 295f);
			var wrongMass = ElementSpawn(17, 42.01f, 295f);
			var wrongTemperature = ElementSpawn(17, 42f, 295.05f);
			if (!SpawnPrefabPacket.ReplayMatches(expected, same)
			    || SpawnPrefabPacket.ReplayMatches(expected, wrongPrefab)
			    || SpawnPrefabPacket.ReplayMatches(expected, wrongMass)
			    || SpawnPrefabPacket.ReplayMatches(expected, wrongTemperature))
				return UnitTestResult.Fail(
					"Client-local element spawn replay matched the wrong authoritative create");
			return UnitTestResult.Pass(
				"Element replay requires an exact authoritative descriptor");
		}

		[UnitTest(name: "Element spawn replay rejects ambiguous authority", category: "Sync")]
		public static UnitTestResult ElementSpawnReplayRejectsAmbiguity()
		{
			var actual = ElementSpawn(17, 42f, 295f);
			var candidates = new System.Collections.Generic.Dictionary<int,
				(SpawnPrefabPacket Descriptor, float CreatedAt)>
			{
				[11] = (ElementSpawn(17, 42f, 295f), 1f),
				[12] = (ElementSpawn(17, 42f, 295f), 2f),
			};
			if (SpawnPrefabPacket.FindUniqueReplayMatch(actual, candidates) != 0)
				return UnitTestResult.Fail("Ambiguous identical authority spawns selected one local object");
			candidates.Remove(12);
			return SpawnPrefabPacket.FindUniqueReplayMatch(actual, candidates) == 11
				? UnitTestResult.Pass("Only a unique exact authority descriptor can consume a replay")
				: UnitTestResult.Fail("Unique authority spawn was not selected");
		}

		[UnitTest(name: "Authoritative element materialization prevents stack merge", category: "Sync")]
		public static UnitTestResult AuthoritativeElementMaterializationPreventsMerge()
		{
			if (!SpawnPrefabPacket.ShouldPreventElementMerge(authoritativeNetId: 17)
			    || SpawnPrefabPacket.ShouldPreventElementMerge(authoritativeNetId: 0))
				return UnitTestResult.Fail(
					"An authoritative element can be absorbed before its NetId is bound");
			return UnitTestResult.Pass(
				"Every authoritative element is materialized as one exact GameObject");
		}

		[UnitTest(name: "Authoritative prefab initializes before inactive final state", category: "Sync")]
		public static UnitTestResult AuthoritativePrefabInitializesBeforeInactiveState()
		{
			if (!SpawnPrefabPacket.ShouldActivateForInitialization(desiredFinalActive: true)
			    || !SpawnPrefabPacket.ShouldActivateForInitialization(desiredFinalActive: false))
				return UnitTestResult.Fail(
					"An inactive descriptor can skip Pickupable and KMonoBehaviour initialization");
			return UnitTestResult.Pass(
				"Every prefab initializes active before the descriptor applies its final state");
		}

		[UnitTest(name: "Occupied NetId replacement keeps rollback owner alive", category: "Sync")]
		public static UnitTestResult OccupiedNetIdReplacementIsTransactional()
		{
			MethodInfo displace = typeof(SpawnPrefabPacket).GetMethod(
				"TryDisplace", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo retire = typeof(SpawnPrefabPacket).GetMethod(
				"RetireDisplaced", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo unregister = typeof(NetworkIdentityRegistry).GetMethod(
				nameof(NetworkIdentityRegistry.Unregister),
				new[] { typeof(NetworkIdentity), typeof(int) });
			MethodInfo destroy = typeof(Util).GetMethod(
				nameof(Util.KDestroyGameObject),
				BindingFlags.Public | BindingFlags.Static,
				null, new[] { typeof(UnityEngine.GameObject) }, null);
			return CallsResolved(displace, unregister) && !CallsResolved(displace, destroy)
			       && CallsResolved(retire, destroy)
				? UnitTestResult.Pass("Old owner stays alive until replacement commits")
				: UnitTestResult.Fail("Replacement cannot roll the old owner back after a failed bind");
		}

		[UnitTest(name: "Destroy prefix binds its target by position", category: "Sync")]
		public static UnitTestResult DestroyPrefixUsesPositionalBinding()
		{
			MethodInfo prefix = typeof(KDestroyGameObjectNetworkIdentityPatch).GetMethod(
				nameof(KDestroyGameObjectNetworkIdentityPatch.Prefix),
				BindingFlags.Public | BindingFlags.Static);
			ParameterInfo parameter = prefix?.GetParameters().SingleOrDefault();
			return parameter?.Name == "__0"
				? UnitTestResult.Pass("Harmony does not depend on the game's argument name")
				: UnitTestResult.Fail("Destroy prefix can fail when the game argument name changes");
		}

		[UnitTest(name: "Element replay never consumes host packet materialization", category: "Sync")]
		public static UnitTestResult ElementReplayRejectsHostDispatchReentry()
		{
			return SpawnPrefabPacket.CanConsumeReplay(
				isClient: true, inSession: true, senderIsHost: false)
			       && !SpawnPrefabPacket.CanConsumeReplay(
				       isClient: true, inSession: true, senderIsHost: true)
				? UnitTestResult.Pass("Only local simulation spawns can consume authority replay")
				: UnitTestResult.Fail("Authority packet materialization can consume an older replay");
		}

		[UnitTest(name: "Substance replay can replace SpawnResource result", category: "Sync")]
		public static UnitTestResult SubstanceReplayUsesRefResult()
		{
			MethodInfo postfix = typeof(Substance_SpawnResource_Patch).GetMethod(
				"Postfix", BindingFlags.Public | BindingFlags.Static);
			var result = postfix?.GetParameters().SingleOrDefault(parameter =>
				parameter.Name == "__result");
			return result?.ParameterType == typeof(UnityEngine.GameObject).MakeByRefType()
				? UnitTestResult.Pass("Harmony receives SpawnResource result by reference")
				: UnitTestResult.Fail("Harmony cannot replace the SpawnResource return value");
		}

		[UnitTest(name: "Identity registration completes through authoritative broadcast", category: "Sync")]
		public static UnitTestResult RegistrationCallsBroadcastCompletion()
		{
			MethodInfo register = typeof(NetworkIdentity).GetMethod(
				nameof(NetworkIdentity.RegisterIdentity), BindingFlags.Instance | BindingFlags.Public);
			MethodInfo broadcast = typeof(NetworkIdentity).GetMethod(
				nameof(NetworkIdentity.EnsureAuthoritativeSpawnBroadcast),
				BindingFlags.Instance | BindingFlags.NonPublic);
			if (!Calls(register, broadcast))
				return UnitTestResult.Fail("A successful late registration can finish without broadcasting");
			return UnitTestResult.Pass("Every registration path reaches authoritative broadcast completion");
		}

		[UnitTest(name: "Transient workables do not receive standalone identities", category: "Sync")]
		public static UnitTestResult TransientWorkablesStayOutsideRegistry()
		{
			if (NetworkIdentity.ShouldResolveBehaviourIdentity(
				    isWorkable: true, hasSaveLoadRoot: false, hasExistingIdentity: false)
			    || !NetworkIdentity.ShouldResolveBehaviourIdentity(
				    isWorkable: true, hasSaveLoadRoot: true, hasExistingIdentity: false)
			    || !NetworkIdentity.ShouldResolveBehaviourIdentity(
				    isWorkable: true, hasSaveLoadRoot: false, hasExistingIdentity: true)
			    || !NetworkIdentity.ShouldResolveBehaviourIdentity(
				    isWorkable: false, hasSaveLoadRoot: false, hasExistingIdentity: false))
			{
				return UnitTestResult.Fail("Transient helper workable can enter the lifecycle registry");
			}

			return UnitTestResult.Pass("Only persistent or already-owned workables use network identity");
		}

		[UnitTest(name: "Duplicant storage replicates membership and carry visual", category: "Sync")]
		public static UnitTestResult DuplicantStorageUsesBothReplicationPaths()
		{
			StorageReplicationKind regular = StoragePatches.RequiredReplication(isMinionStorage: false);
			StorageReplicationKind minion = StoragePatches.RequiredReplication(isMinionStorage: true);
			if (regular != StorageReplicationKind.Membership
			    || minion != (StorageReplicationKind.Membership | StorageReplicationKind.CarryVisual))
			{
				return UnitTestResult.Fail("Duplicant storage still treats visual and membership as alternatives");
			}

			return UnitTestResult.Pass("Duplicant storage sends authoritative membership plus carry visual");
		}

		[UnitTest(name: "Terminal lifecycle cannot emit storage removal", category: "Sync")]
		public static UnitTestResult TerminalLifecycleSkipsStorageRemoval()
		{
			return !StoragePatches.ShouldReplicateRemoval(itemUnavailableForBinding: true)
			       && StoragePatches.ShouldReplicateRemoval(itemUnavailableForBinding: false)
				? UnitTestResult.Pass("Retired items cannot emit delayed storage deltas")
				: UnitTestResult.Fail("A retired item can still emit a storage removal delta");
		}

		[UnitTest(name: "Storage disease transfer requires both primary elements", category: "Sync")]
		public static UnitTestResult DiseaseTransferRejectsMissingPrimaryElement()
		{
			if (!StorageItemPacket.ShouldTransferDisease(
				    enabled: true, hasItemPrimary: true, hasStoragePrimary: true)
			    || StorageItemPacket.ShouldTransferDisease(
				    enabled: true, hasItemPrimary: true, hasStoragePrimary: false)
			    || StorageItemPacket.ShouldTransferDisease(
				    enabled: true, hasItemPrimary: false, hasStoragePrimary: true)
			    || StorageItemPacket.ShouldTransferDisease(
				    enabled: false, hasItemPrimary: true, hasStoragePrimary: true))
			{
				return UnitTestResult.Fail("Storage disease transfer can dereference a missing primary element");
			}

			return UnitTestResult.Pass("Disease transfer runs only with both primary elements");
		}

		[UnitTest(name: "Storage stack absorption uses returned authoritative item", category: "Sync")]
		public static UnitTestResult StorageStorePatchConsumesResult()
		{
			MethodInfo postfix = typeof(StoragePatches.StorageStorePatch).GetMethod(
				"Postfix", BindingFlags.Public | BindingFlags.Static);
			bool hasResult = postfix?.GetParameters().Any(parameter =>
				parameter.Name == "__result" && parameter.ParameterType == typeof(UnityEngine.GameObject)) == true;
			if (!hasResult)
				return UnitTestResult.Fail("Storage.Store postfix ignores the object returned by stack absorption");
			return UnitTestResult.Pass("Storage.Store consumes its authoritative returned item");
		}

		[UnitTest(name: "Storage membership carries exact element state", category: "Sync")]
		public static UnitTestResult StorageElementStateRoundTrips()
		{
			var packet = new StorageItemPacket
			{
				NetId = 11,
				StorageNetId = 22,
				Revision = 33,
				FxPrefix = Storage.FXPrefix.Delivered,
				ConsumedPrefabHash = 44,
				ConsumedAmount = 2f,
				HasElementState = true,
				ElementMass = 9.5f,
				ElementTemperature = 301.25f,
				ElementDiseaseIdx = 7,
				ElementDiseaseCount = 123,
			};
			StorageItemPacket copy = RoundTrip(packet);
			if (!copy.HasElementState || copy.ElementMass != 9.5f
			    || copy.ElementTemperature != 301.25f || copy.ElementDiseaseIdx != 7
			    || copy.ElementDiseaseCount != 123)
				return UnitTestResult.Fail("Authoritative merged element state changed on the wire");
			return UnitTestResult.Pass("Merged storage membership preserves exact element state");
		}

		private static bool Calls(MethodInfo caller, MethodInfo callee)
		{
			byte[] il = caller?.GetMethodBody()?.GetILAsByteArray();
			if (il == null || callee == null)
				return false;
			byte[] token = BitConverter.GetBytes(callee.MetadataToken);
			for (int index = 0; index <= il.Length - token.Length; index++)
				if (il.Skip(index).Take(token.Length).SequenceEqual(token))
					return true;
			return false;
		}

		private static bool CallsResolved(MethodInfo caller, MethodInfo callee)
		{
			byte[] il = caller?.GetMethodBody()?.GetILAsByteArray();
			if (il == null || callee == null)
				return false;
			for (int index = 0; index <= il.Length - 5; index++)
			{
				if (il[index] != 0x28 && il[index] != 0x6f)
					continue;
				try
				{
					MethodBase resolved = caller.Module.ResolveMethod(
						BitConverter.ToInt32(il, index + 1));
					if (resolved?.Module == callee.Module
					    && resolved.MetadataToken == callee.MetadataToken)
						return true;
				}
				catch (ArgumentException) { }
			}
			return false;
		}

		private static int FindToken(byte[] il, int metadataToken)
		{
			byte[] token = BitConverter.GetBytes(metadataToken);
			for (int index = 0; index <= il.Length - token.Length; index++)
				if (il.Skip(index).Take(token.Length).SequenceEqual(token))
					return index;
			return -1;
		}

		private static StorageItemPacket RoundTrip(StorageItemPacket packet)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				packet.Serialize(writer);
			stream.Position = 0;
			var copy = new StorageItemPacket();
			using var reader = new BinaryReader(stream);
			copy.Deserialize(reader);
			return copy;
		}

		private static SpawnPrefabPacket ElementSpawn(
			int prefabHash, float mass, float temperature)
		{
			return new SpawnPrefabPacket
			{
				Hash = prefabHash,
				Position = new UnityEngine.Vector3(10f, 20f, 0f),
				WorldId = 3,
				HasElementData = true,
				Mass = mass,
				Temperature = temperature,
				DiseaseIndex = 2,
				DiseaseCount = 10,
			};
		}
	}
}
#endif
