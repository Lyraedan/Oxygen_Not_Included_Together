using KSerialization;
using ONI_Together.DebugTools;
using ONI_Together.Misc;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.States;
using System.IO;
using System.Collections;
using Shared.Profiling;

namespace ONI_Together.Networking.Components
{
	[SerializationConfig(MemberSerialization.OptIn)]
	public class NetworkIdentity : KMonoBehaviour
	{
		private static int managedSpawnSuppressionDepth;

		[Serialize]
		public int NetId = 0;

		[SkipSaveFileSerialization]
		private bool IsRegistered = false;
		[SkipSaveFileSerialization]
		private bool registrationRetryScheduled;
		[SkipSaveFileSerialization]
		private bool spawnBroadcastSent;
		[SkipSaveFileSerialization]
		private bool lifecycleTerminal;
		[SkipSaveFileSerialization]
		private bool destructionPending;
		[SkipSaveFileSerialization]
		internal ulong LifecycleRevision;
		[SkipSaveFileSerialization]
		internal int ExpectedAuthorityNetId;
		[SkipSaveFileSerialization]
		internal bool RequiresExistingBinding;

		internal static bool IsManagedSpawnSuppressed => managedSpawnSuppressionDepth > 0;
		internal bool IsLifecycleTerminal => lifecycleTerminal;
		internal bool IsUnavailableForBinding => lifecycleTerminal || destructionPending;

		internal readonly struct BindingState
		{
			internal readonly int NetId;
			internal readonly bool IsRegistered;
			internal readonly bool SpawnBroadcastSent;
			internal readonly ulong LifecycleRevision;
			internal readonly int ExpectedAuthorityNetId;
			internal readonly bool RequiresExistingBinding;

			internal BindingState(NetworkIdentity identity)
			{
				NetId = identity.NetId;
				IsRegistered = identity.IsRegistered;
				SpawnBroadcastSent = identity.spawnBroadcastSent;
				LifecycleRevision = identity.LifecycleRevision;
				ExpectedAuthorityNetId = identity.ExpectedAuthorityNetId;
				RequiresExistingBinding = identity.RequiresExistingBinding;
			}
		}

		internal static bool ShouldBeginLifecycleForOverride(bool inSession, bool isHost)
			=> !inSession || isHost;

		internal static bool ShouldAwaitAuthorityBinding(
			bool isClient, ClientState state, int netId, bool hasAuthoritativeLifecycle)
			=> isClient && state == ClientState.InGame && netId != 0
			   && !hasAuthoritativeLifecycle;

		internal static bool ShouldBroadcastAuthoritativeSpawn(
			bool inSession, bool isHost, bool alreadySent, bool registered,
			bool managedSpawnSuppressed)
			=> inSession && isHost && !alreadySent && registered
			   && !managedSpawnSuppressed;

		internal static bool ShouldResolveBehaviourIdentity(
			bool isWorkable, bool hasSaveLoadRoot, bool hasExistingIdentity)
			=> !isWorkable || hasSaveLoadRoot || hasExistingIdentity;

		internal static void BeginManagedSpawn()
		{
			managedSpawnSuppressionDepth++;
		}

		internal static void EndManagedSpawn()
		{
			if (managedSpawnSuppressionDepth > 0)
				managedSpawnSuppressionDepth--;
		}

		internal static void ResetSessionState()
		{
			managedSpawnSuppressionDepth = 0;
		}

		internal static NetworkIdentity EnsurePersistentPrefabIdentity(
			UnityEngine.GameObject gameObject)
		{
			if (gameObject == null)
				return null;
			gameObject.GetComponent<SaveLoadRoot>()?
				.TryDeclareOptionalComponent<NetworkIdentity>();
			return gameObject.AddOrGet<NetworkIdentity>();
		}

		internal static bool ShouldEndLifecycleLocally(
			bool inSession, bool isHost, bool isRegistered)
		{
			return isRegistered && !inSession;
		}

		public override void OnSpawn()
		{
			using var scope = Profiler.Scope();

			base.OnSpawn();
			RegisterIdentity();
			BroadcastAuthoritativeSpawn();
		}

		public void RegisterIdentity()
		{
			using var scope = Profiler.Scope();
			if (IsUnavailableForBinding || IsRegistered)
				return;
			if (Grid.WidthInCells == 0)
			{
				ScheduleRegistrationWhenGridReady();
				return;
			}
			AssignDeterministicNetId();
			if (!RegisterResolvedIdentity())
				return;
			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost)
				LifecycleRevision = NetworkIdentityRegistry.BeginLifecycle(NetId);
			ApplyPendingStorageTransfers();
			EnsureAuthoritativeSpawnBroadcast();
		}

		private void ScheduleRegistrationWhenGridReady()
		{
			if (registrationRetryScheduled)
				return;
			registrationRetryScheduled = true;
			StartCoroutine(RegisterWhenGridReady());
		}

		private void AssignDeterministicNetId()
		{
			if (NetId != 0)
				return;
			if (TryGetComponent<Building>(out _))
			{
				NetId = NetIdHelper.GetDeterministicBuildingId(gameObject);
				return;
			}
			if (!TryGetComponent<Pickupable>(out _)
			    && !TryGetComponent<CreatureBrain>(out _)
			    && TryGetComponent<Workable>(out _))
				NetId = NetIdHelper.GetDeterministicWorkableId(gameObject);
		}

		private bool RegisterResolvedIdentity()
		{
			bool hasAuthoritativeLifecycle = NetId != 0
			                                 && NetworkIdentityRegistry.GetLastLifecycleRevision(NetId) != 0
			                                 && !NetworkIdentityRegistry.IsLifecycleTombstoned(NetId);
			if (ShouldAwaitAuthorityBinding(
				    MultiplayerSession.IsClient, GameClient.State, NetId,
				    hasAuthoritativeLifecycle))
			{
				ExpectedAuthorityNetId = NetId;
				NetId = 0;
				NetworkIdentityRegistry.TrackUnassigned(this);
				DebugConsole.LogWarning(
					$"[NetworkIdentity] Waiting for host lifecycle binding for {gameObject.name}");
				return false;
			}
			if (NetId == 0)
			{
				if (MultiplayerSession.InSession && MultiplayerSession.IsClient)
				{
					NetworkIdentityRegistry.TrackUnassigned(this);
					DebugConsole.LogWarning($"[NetworkIdentity] Waiting for host-assigned NetId for {gameObject.name}");
					return false;
				}
				NetId = NetworkIdentityRegistry.Register(this);
				IsRegistered = true;
				return true;
			}
			if (NetworkIdentityRegistry.RegisterExisting(this, NetId))
			{
				IsRegistered = true;
				return true;
			}
			if (!MultiplayerSession.InSession || MultiplayerSession.IsHost)
			{
				int collidedId = NetId;
				NetId = NetworkIdentityRegistry.Register(this);
				IsRegistered = true;
				RequiresExistingBinding = true;
				DebugConsole.LogWarning($"[NetworkIdentity] Reassigned colliding NetId {collidedId} to {NetId} for {gameObject.name}");
				return true;
			}
			ExpectedAuthorityNetId = NetId;
			NetId = 0;
			NetworkIdentityRegistry.TrackUnassigned(this);
			DebugConsole.LogWarning($"[NetworkIdentity] Waiting for host collision binding for {gameObject.name}");
			return false;
		}

		private IEnumerator RegisterWhenGridReady()
		{
			while (Grid.WidthInCells == 0 && !gameObject.IsNullOrDestroyed())
				yield return null;

			registrationRetryScheduled = false;
			if (gameObject.IsNullOrDestroyed())
				yield break;
			RegisterIdentity();
			BroadcastAuthoritativeSpawn();
		}

		/// <summary>
		/// This will be primarily used when the host spawns in an object and the client and host need to sync the netid
		/// </summary>
		/// <param name="netIdOverride"></param>
		public bool OverrideNetId(int netIdOverride)
		{
			using var _ = Profiler.Scope();

			if (IsUnavailableForBinding || netIdOverride == 0)
				return false;
			bool ownsLifecycle = ShouldBeginLifecycleForOverride(
				MultiplayerSession.InSession, MultiplayerSession.IsHost);
			if (NetId == netIdOverride && NetworkIdentityRegistry.IsRegistered(this, NetId))
			{
				LifecycleRevision = ownsLifecycle
					? NetworkIdentityRegistry.BeginLifecycle(netIdOverride)
					: NetworkIdentityRegistry.GetLastLifecycleRevision(netIdOverride);
				return true;
			}
			int previousId = NetId;
			bool previousRegistered = NetworkIdentityRegistry.IsRegistered(this, previousId);
			if (!NetworkIdentityRegistry.RegisterOverride(this, netIdOverride))
				return false;

			if (ownsLifecycle && previousId != 0 && previousId != netIdOverride
			    && previousRegistered)
				NetworkIdentityRegistry.EndLifecycle(previousId);
			NetId = netIdOverride;
			IsRegistered = true;
			LifecycleRevision = ownsLifecycle
				? NetworkIdentityRegistry.BeginLifecycle(netIdOverride)
				: NetworkIdentityRegistry.GetLastLifecycleRevision(netIdOverride);
			ExpectedAuthorityNetId = 0;
			NetworkIdentityRegistry.UntrackUnassigned(this);
			if (previousId != 0 && previousId != netIdOverride)
				NetworkIdentityRegistry.Unregister(this, previousId);
			ApplyPendingStorageTransfers();
			return true;
		}

		private void ApplyPendingStorageTransfers()
		{
			if (TryGetComponent<Storage>(out var storage))
				StorageItemPacket.TryApplyPendingForStorage(NetId, storage);
		}

		internal void EnsureAuthoritativeSpawnBroadcast()
		{
			if (IsUnavailableForBinding)
				return;
			bool registered = NetworkIdentityRegistry.IsRegistered(this, NetId);
			if (!ShouldBroadcastAuthoritativeSpawn(
				    MultiplayerSession.InSession, MultiplayerSession.IsHost,
				    spawnBroadcastSent, registered, IsManagedSpawnSuppressed))
			{
				if (MultiplayerSession.InSession && MultiplayerSession.IsHost
				    && registered && IsManagedSpawnSuppressed)
					spawnBroadcastSent = true;
				return;
			}

			SpawnPrefabPacket packet = SpawnPrefabPacket.FromIdentity(this);
			if (packet != null)
			{
				PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
				spawnBroadcastSent = true;
			}
		}

		internal void RearmAuthoritativeSpawnBroadcast()
		{
			if (!IsUnavailableForBinding)
				spawnBroadcastSent = false;
		}

		private void BroadcastAuthoritativeSpawn() => EnsureAuthoritativeSpawnBroadcast();

		internal bool RetireAuthoritativeLifecycle()
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost
			    || lifecycleTerminal || !NetworkIdentityRegistry.IsRegistered(this, NetId))
				return false;
			lifecycleTerminal = true;
			int retiredNetId = NetId;
			var despawn = new DespawnEntityPacket(retiredNetId);
			PacketSender.SendToAllClients(despawn, PacketSendMode.ReliableImmediate);
			RemoteProgressRegistry.Clear(retiredNetId);
			NetworkIdentityRegistry.Unregister(this, retiredNetId);
			NetworkIdentityRegistry.UntrackUnassigned(this);
			IsRegistered = false;
			spawnBroadcastSent = false;
			LifecycleRevision = 0;
			gameObject.SetActive(false);
			Util.KDestroyGameObject(gameObject);
			return true;
		}

		internal void MarkLifecycleTerminalForTests() => lifecycleTerminal = true;
		internal void MarkDestructionPending() => destructionPending = true;

		internal BindingState CaptureBindingState() => new(this);

		internal void RestoreBindingState(BindingState state)
		{
			NetId = state.NetId;
			IsRegistered = state.IsRegistered;
			spawnBroadcastSent = state.SpawnBroadcastSent;
			LifecycleRevision = state.LifecycleRevision;
			ExpectedAuthorityNetId = state.ExpectedAuthorityNetId;
			RequiresExistingBinding = state.RequiresExistingBinding;
		}


		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();

#if DEBUG
			SpawnPrefabPacket.RecordCleanupDiagnostic(this, System.Environment.StackTrace);
#endif
			bool isRegistered = NetworkIdentityRegistry.IsRegistered(this, NetId);
			if (!lifecycleTerminal && MultiplayerSession.IsHost
			    && MultiplayerSession.InSession && isRegistered)
				PacketSender.SendToAllClients(new DespawnEntityPacket(NetId));
			else if (!lifecycleTerminal && ShouldEndLifecycleLocally(
				         MultiplayerSession.InSession, MultiplayerSession.IsHost, isRegistered))
				NetworkIdentityRegistry.EndLifecycle(NetId);

			RemoteProgressRegistry.Clear(NetId);
			NetworkIdentityRegistry.Unregister(this, NetId);
			NetworkIdentityRegistry.UntrackUnassigned(this);
			IsRegistered = false;
			registrationRetryScheduled = false;
			spawnBroadcastSent = false;
			RequiresExistingBinding = false;
			LifecycleRevision = 0;
			ExpectedAuthorityNetId = 0;
			//DebugConsole.Log($"[NetworkIdentity] Unregistered NetId {NetId} for {gameObject.name}");
			base.OnCleanUp();
		}
	}
}
