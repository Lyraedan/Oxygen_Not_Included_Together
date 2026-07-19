#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using ONI_Together.Misc;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Patches.DLC.SpacedOut;
using UnityEngine;

namespace ONI_Together.DebugTools
{
	internal struct SoakCellState
	{
		public int Cell;
		public ushort ElementIdx;
		public int Mass;
		public int Temperature;
		public byte DiseaseIdx;
		public int DiseaseCount;
	}

	internal struct SoakEntityState
	{
		public int NetId;
		public int PrefabHash;
		public bool Active;
		public ulong Revision;
		public bool Tombstoned;
		public bool IsDuplicant;
		public bool IsDead;
		public bool HasDeadTag;
		public bool MonitorIsDead;
		public bool IsCorpse;
		public bool IsInLiveRoster;
		public bool IsInLiveRosterByModel;
		public string DeathId;
	}

	internal struct SoakWorldMembershipState
	{
		public int NetId;
		public int WorldId;
		public int Cell;
		public int PositionX;
		public int PositionY;
		public int PositionZ;
		public bool HasPositionHandler;
		public bool FlipX;
		public bool FlipY;
		public NavType NavType;
	}

	internal struct SoakStorageMembershipState
	{
		public int StorageNetId;
		public int StorageIndex;
		public int Capacity;
		public bool HasItem;
		public int ItemIndex;
		public int ItemNetId;
		public int LinkedStorageNetId;
		public int LinkedStorageIndex;
		public int PrefabHash;
		public int Mass;
		public int Temperature;
		public byte DiseaseIdx;
		public int DiseaseCount;
	}

	internal struct SoakClusterRocketState
	{
		public int NetId;
		public bool HasClusterLocation;
		public int ClusterQ;
		public int ClusterR;
		public bool HasDestinationSelector;
		public bool HasDestination;
		public int DestinationQ;
		public int DestinationR;
		public int PadNetId;
		public bool Repeat;
		public bool HasCraftState;
		public int CraftLocationQ;
		public int CraftLocationR;
		public RocketCraftPhase CraftPhase;
		public bool HasCurrentPad;
		public int CurrentPadNetId;
		public bool HasControlStation;
		public bool RestrictWhenGrounded;
	}

	internal sealed class SoakStateHashes
	{
		public byte[] Grid { get; set; }
		public byte[] EntityLifecycle { get; set; }
		public byte[] WorldMembership { get; set; }
		public byte[] StorageMembership { get; set; }
		public byte[] ClusterRocket { get; set; }
		public int GridRecords { get; set; }
		public int EntityLifecycleRecords { get; set; }
		public int WorldMembershipRecords { get; set; }
		public int StorageMembershipRecords { get; set; }
		public int ClusterRocketRecords { get; set; }
		public SoakLifecycleDiagnostics Lifecycle { get; set; } = new();
		internal IReadOnlyList<SoakEntityState> EntityStates { get; set; }
		internal IReadOnlyList<SoakWorldMembershipState> WorldStates { get; set; }

		public byte[] Registry => EntityLifecycle;
		public int RegistryRecords => EntityLifecycleRecords;
	}

	internal static partial class SoakStateHash
	{
		internal static int NormalizeFloatBits(float value)
		{
			if (value == 0f)
				return 0;
			if (float.IsNaN(value))
				return unchecked((int)0x7fc00000);
			return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
		}

		public static SoakStateHashes Compute(
			IEnumerable<SoakCellState> cells,
			IEnumerable<SoakEntityState> entities)
			=> Compute(
				cells,
				entities,
				Enumerable.Empty<SoakWorldMembershipState>(),
				Enumerable.Empty<SoakStorageMembershipState>(),
				Enumerable.Empty<SoakClusterRocketState>());

		public static SoakStateHashes Compute(
			IEnumerable<SoakCellState> cells,
			IEnumerable<SoakEntityState> entities,
			IEnumerable<SoakWorldMembershipState> worlds,
			IEnumerable<SoakStorageMembershipState> storage,
			IEnumerable<SoakClusterRocketState> rockets)
		{
			List<SoakCellState> cellList = cells.ToList();
			List<SoakEntityState> entityList = entities.ToList();
			List<SoakWorldMembershipState> worldList = worlds.ToList();
			List<SoakStorageMembershipState> storageList = storage.ToList();
			List<SoakClusterRocketState> rocketList = rockets.ToList();
			return BuildHashes(cellList, entityList, worldList, storageList, rocketList, false);
		}

		internal static SoakStateHashes CaptureCurrent()
		{
			if (!TryCaptureCurrent(out SoakStateHashes hashes, out string failure))
				throw new InvalidOperationException(failure);
			return hashes;
		}

		internal static bool TryCaptureCurrent(
			out SoakStateHashes hashes, out string failure)
		{
			hashes = null;
			failure = string.Empty;
			IReadOnlyList<NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry> baseline =
				NetworkIdentityRegistry.GetLifecycleRevisionSnapshot();
			NetworkIdentityRegistry.LifecycleMembershipValidationResult membership =
				NetworkIdentityRegistry.ValidateCurrentLifecycleMembership(baseline);
			List<SoakCellState> cells = CaptureCells();
			NetworkIdentity[] identities = CaptureIdentities();
			List<SoakEntityState> entities = CaptureEntityLifecycle(identities);
			List<SoakWorldMembershipState> worlds = CaptureWorldMembership(identities);
			List<SoakStorageMembershipState> storage = CaptureStorageMembership(identities);
			List<SoakClusterRocketState> rockets = CaptureClusterRocket(identities);
			hashes = BuildHashes(cells, entities, worlds, storage, rockets, true);
			hashes.Lifecycle = new SoakLifecycleDiagnostics
			{
				MissingLiveCount = membership.MissingLiveCount,
				UnexpectedLiveCount = membership.UnexpectedLiveCount,
				TombstonedLiveCount = membership.TombstonedLiveCount,
				UnassignedLiveCount = membership.UnassignedLiveCount,
			};
			return true;
		}

		private static SoakStateHashes BuildHashes(
			List<SoakCellState> cells,
			List<SoakEntityState> entities,
			List<SoakWorldMembershipState> worlds,
			List<SoakStorageMembershipState> storage,
			List<SoakClusterRocketState> rockets,
			bool cellsAlreadyOrdered)
		{
			return new SoakStateHashes
			{
				Grid = cellsAlreadyOrdered ? HashCellsInOrder(cells) : HashCells(cells),
				EntityLifecycle = HashEntityLifecycle(entities),
				WorldMembership = HashWorldMembership(worlds),
				StorageMembership = HashStorageMembership(storage),
				ClusterRocket = HashClusterRocket(rockets),
				GridRecords = cells.Count,
				EntityLifecycleRecords = entities.Count,
				WorldMembershipRecords = worlds.Count,
				StorageMembershipRecords = storage.Count,
					ClusterRocketRecords = rockets.Count,
					EntityStates = entities,
					WorldStates = worlds,
				};
		}

		internal static string ToHex(byte[] hash)
		{
			return BitConverter.ToString(hash).Replace("-", string.Empty);
		}

		private static List<SoakCellState> CaptureCells()
		{
			var cells = new List<SoakCellState>(Grid.CellCount);
			for (int cell = 0; cell < Grid.CellCount; cell++)
			{
				if (!Grid.IsValidCell(cell))
					continue;
				float mass = Grid.Mass[cell];
				cells.Add(new SoakCellState
				{
					Cell = cell,
					ElementIdx = Grid.ElementIdx[cell],
					Mass = NormalizeFloatBits(mass),
					Temperature = mass == 0f ? 0 : NormalizeFloatBits(Grid.Temperature[cell]),
					DiseaseIdx = Grid.DiseaseIdx[cell],
					DiseaseCount = Grid.DiseaseCount[cell],
				});
			}
			return cells;
		}

		private static NetworkIdentity[] CaptureIdentities()
			=> NetworkIdentityRegistry.AllIdentities
				.Where(IsHashableIdentity)
				.ToArray();

		private static bool IsHashableIdentity(NetworkIdentity identity)
			=> !identity.IsNullOrDestroyed() && !identity.gameObject.IsNullOrDestroyed()
			   && identity.NetId != 0;

		private static List<SoakEntityState> CaptureEntityLifecycle(NetworkIdentity[] identities)
		{
			Dictionary<int, NetworkIdentity> live = identities.ToDictionary(identity => identity.NetId);
			var entities = new List<SoakEntityState>();
			var captured = new HashSet<int>();
			foreach (NetworkIdentityRegistry.LifecycleRevisionSnapshotEntry entry
			         in NetworkIdentityRegistry.GetLifecycleRevisionSnapshot())
			{
				live.TryGetValue(entry.NetId, out NetworkIdentity identity);
				entities.Add(CreateEntityState(
					entry.NetId, identity, entry.Revision, entry.Tombstoned));
				captured.Add(entry.NetId);
			}
			foreach (NetworkIdentity identity in identities)
			{
				if (captured.Contains(identity.NetId))
					continue;
				entities.Add(CreateEntityState(identity.NetId, identity, 0, false));
			}
			return entities;
		}

		private static SoakEntityState CreateEntityState(
			int netId, NetworkIdentity identity, ulong revision, bool tombstoned)
		{
			GameObject gameObject = identity?.gameObject;
			MinionIdentity minion = gameObject?.GetComponent<MinionIdentity>();
			bool isDuplicant = minion != null;
			DeathMonitor.Instance deathMonitor = isDuplicant
				? gameObject.GetSMI<DeathMonitor.Instance>()
				: null;
			bool hasDeadTag = isDuplicant && gameObject.HasTag(GameTags.Dead);
			bool monitorIsDead = deathMonitor?.IsDead() ?? false;
			bool isDead = isDuplicant
			              && Networking.Synchronization.DuplicantDeathSync.IsDeadState(
				              hasDeadTag, monitorIsDead);
			return new SoakEntityState
			{
				NetId = netId,
				PrefabHash = gameObject == null ? 0 : gameObject.PrefabID().GetHashCode(),
				Active = gameObject != null && gameObject.activeSelf,
				Revision = revision,
				Tombstoned = tombstoned,
				IsDuplicant = isDuplicant,
				IsDead = isDead,
				HasDeadTag = hasDeadTag,
				MonitorIsDead = monitorIsDead,
				IsCorpse = isDuplicant && gameObject.HasTag(GameTags.Corpse),
				IsInLiveRoster = isDuplicant
				                 && Networking.Synchronization.DuplicantDeathSync.IsInLiveRoster(minion),
				IsInLiveRosterByModel = isDuplicant
				                        && Networking.Synchronization.DuplicantDeathSync
					                        .IsInLiveRosterByModel(minion),
				DeathId = Networking.Synchronization.DuplicantDeathSync.CanonicalDeathId(
					isDead, deathMonitor == null
						? string.Empty
						: Networking.Synchronization.DuplicantDeathSync.GetDeathId(deathMonitor)),
			};
		}

		private static List<SoakWorldMembershipState> CaptureWorldMembership(NetworkIdentity[] identities)
		{
			var states = new List<SoakWorldMembershipState>(identities.Length);
			foreach (NetworkIdentity identity in identities)
			{
				UnityEngine.Vector3 position = identity.transform.position;
				EntityPositionHandler positionHandler =
					identity.GetComponent<EntityPositionHandler>();
				bool authoritative = MultiplayerSession.IsClient
				                     && positionHandler?.serverTimestamp > 0;
				if (positionHandler != null)
				{
					position = EntityPositionHandler.SelectHashPosition(
						MultiplayerSession.IsClient, position,
						positionHandler.serverTimestamp, positionHandler.serverPosition);
				}
				states.Add(new SoakWorldMembershipState
				{
					NetId = identity.NetId,
					WorldId = identity.gameObject.GetMyWorldId(),
					Cell = Grid.PosToCell(position),
					PositionX = NormalizeFloatBits(position.x),
					PositionY = NormalizeFloatBits(position.y),
					PositionZ = NormalizeFloatBits(position.z),
					HasPositionHandler = positionHandler != null,
					FlipX = authoritative
						? positionHandler.serverFlipX
						: positionHandler?.kbac != null && positionHandler.kbac.FlipX,
					FlipY = authoritative
						? positionHandler.serverFlipY
						: positionHandler?.kbac != null && positionHandler.kbac.FlipY,
					NavType = authoritative
						? positionHandler.serverNavType
						: CurrentNavType(positionHandler),
				});
			}
			return states;
		}

		private static NavType CurrentNavType(EntityPositionHandler handler)
		{
			return handler?.navigator != null
			       && handler.navigator.CurrentNavType != NavType.NumNavTypes
				? handler.navigator.CurrentNavType
				: NavType.Floor;
		}

		private static List<SoakStorageMembershipState> CaptureStorageMembership(NetworkIdentity[] identities)
		{
			var states = new List<SoakStorageMembershipState>();
			foreach (NetworkIdentity identity in identities)
			{
				Storage[] containers = identity.gameObject.GetComponents<Storage>();
				for (int storageIndex = 0; storageIndex < containers.Length; storageIndex++)
				{
					Storage container = containers[storageIndex];
					int capacity = NormalizeFloatBits(container.capacityKg);
					List<UnityEngine.GameObject> items = StorageSnapshotSync.CollectSnapshotItems(
						container, prepareIdentityMembership: false);
					if (items.Count == 0)
					{
						states.Add(new SoakStorageMembershipState
						{
							StorageNetId = identity.NetId,
							StorageIndex = storageIndex,
							Capacity = capacity,
						});
					}
					for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
					{
						UnityEngine.GameObject item = items[itemIndex];
						PrimaryElement primary = item.GetComponent<PrimaryElement>();
						GetLinkedStorageAddress(
							item, out int linkedStorageNetId, out int linkedStorageIndex);
						states.Add(new SoakStorageMembershipState
						{
							StorageNetId = identity.NetId,
							StorageIndex = storageIndex,
							Capacity = capacity,
							HasItem = true,
							ItemIndex = itemIndex,
							ItemNetId = item.GetComponent<NetworkIdentity>()?.NetId ?? 0,
							LinkedStorageNetId = linkedStorageNetId,
							LinkedStorageIndex = linkedStorageIndex,
							PrefabHash = item.PrefabID().GetHashCode(),
							Mass = NormalizeFloatBits(primary?.Mass ?? 0f),
							Temperature = NormalizeFloatBits(primary?.Temperature ?? 0f),
							DiseaseIdx = primary?.DiseaseIdx ?? byte.MaxValue,
							DiseaseCount = primary?.DiseaseCount ?? 0,
						});
					}
				}
			}
			return states;
		}

		private static void GetLinkedStorageAddress(
			UnityEngine.GameObject item, out int storageNetId, out int storageIndex)
		{
			Storage linked = item.GetComponent<Pickupable>()?.storage;
			NetworkIdentity owner = linked?.GetComponent<NetworkIdentity>();
			storageNetId = owner?.NetId ?? 0;
			storageIndex = -1;
			if (linked == null || owner == null)
				return;
			Storage[] storages = owner.gameObject.GetComponents<Storage>();
			storageIndex = System.Array.IndexOf(storages, linked);
		}

		private static List<SoakClusterRocketState> CaptureClusterRocket(NetworkIdentity[] identities)
		{
			var states = new List<SoakClusterRocketState>();
			foreach (NetworkIdentity identity in identities)
			{
				SoakClusterRocketState state = new SoakClusterRocketState { NetId = identity.NetId };
				bool clusterCaptured = TryCaptureCluster(identity, ref state);
				bool rocketCaptured = TryCaptureRocket(identity, ref state);
				if (clusterCaptured || rocketCaptured)
					states.Add(state);
			}
			return states;
		}

		internal static void LogClusterRocketRecords(string side, string phase)
		{
			foreach (SoakClusterRocketState state in
			         CaptureClusterRocket(CaptureIdentities()).OrderBy(value => value.NetId))
			{
				DebugConsole.Log(
					$"[SoakHash][CLUSTER_ROCKET_RECORD] side={side} phase={phase} " +
					$"netId={state.NetId} cluster={state.HasClusterLocation}:{state.ClusterQ},{state.ClusterR} " +
					$"selector={state.HasDestinationSelector} destination={state.HasDestination}:" +
					$"{state.DestinationQ},{state.DestinationR} pad={state.PadNetId} repeat={state.Repeat} " +
					$"craft={state.HasCraftState}:{state.CraftLocationQ},{state.CraftLocationR}:" +
					$"{state.CraftPhase} currentPad={state.HasCurrentPad}:{state.CurrentPadNetId} " +
					$"station={state.HasControlStation} restrict={state.RestrictWhenGrounded}");
			}
		}

		private static bool TryCaptureCluster(NetworkIdentity identity, ref SoakClusterRocketState state)
		{
			ClusterGridEntity cluster = identity.GetComponent<ClusterGridEntity>();
			if (cluster == null)
				return false;
			AxialI location = cluster.Location;
			state.HasClusterLocation = true;
			state.ClusterQ = location.q;
			state.ClusterR = location.r;
			return true;
		}

		private static bool TryCaptureRocket(NetworkIdentity identity, ref SoakClusterRocketState state)
		{
			RocketModuleCluster module = identity.GetComponent<RocketModuleCluster>();
			RocketClusterDestinationSelector selector = module?.CraftInterface?.GetClusterDestinationSelector();
			if (selector != null && RocketSettingsSync.TryCapture(selector, out RocketSettingsPacketData data))
			{
				state.HasDestinationSelector = true;
				state.HasDestination = data.HasDestination;
				state.DestinationQ = data.DestinationQ;
				state.DestinationR = data.DestinationR;
				state.PadNetId = data.HasPad ? data.PadNetId : 0;
				state.Repeat = data.Repeat;
				state.HasCraftState = data.HasCraftState;
				state.CraftLocationQ = data.CraftLocationQ;
				state.CraftLocationR = data.CraftLocationR;
				state.CraftPhase = data.CraftPhase;
				state.HasCurrentPad = data.HasCurrentPad;
				state.CurrentPadNetId = data.CurrentPadNetId;
				return true;
			}
			RocketControlStation station = identity.GetComponent<RocketControlStation>();
			if (station == null)
				return false;
			state.HasControlStation = true;
			state.RestrictWhenGrounded = station.RestrictWhenGrounded;
			return true;
		}

	}
}
#endif
