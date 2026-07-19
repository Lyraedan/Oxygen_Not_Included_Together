using System.IO;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using UnityEngine;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World;

public partial class SpawnPrefabPacket : IPacket, IHostOnlyPacket
{
    public int NetId;
    public ulong Revision;
    public int Hash;
    public Vector3 Position;
    public int WorldId = -1;
    public bool BindExistingOnly;
    public bool IsActive = true;

    public bool HasElementData = false;
    public float Mass;
    public float Temperature;
    public byte DiseaseIndex;
    public int DiseaseCount;

    public SpawnPrefabPacket() { }

    public SpawnPrefabPacket(int netId, int hash, Vector3 position)
    {
        NetId = netId;
        Hash = hash;
        Position = position;
        HasElementData = false;
    }
    
    public SpawnPrefabPacket(int netId, int hash, Vector3 position, float mass, float temperature, byte diseaseIndex, int diseaseCount)
    {
        NetId = netId;
        Hash = hash;
        Position = position;
        HasElementData = true;
        Mass = mass;
        Temperature = temperature;
        DiseaseIndex = diseaseIndex;
        DiseaseCount = diseaseCount;
    }

	public static SpawnPrefabPacket FromIdentity(NetworkIdentity identity)
		=> FromIdentity(identity, requireExistingPersistentObject: false);

	internal static SpawnPrefabPacket FromIdentity(
		NetworkIdentity identity, bool requireExistingPersistentObject)
    {
        if (identity == null || identity.gameObject.IsNullOrDestroyed() || identity.NetId == 0
            || !NetworkIdentity.TryGetLifecyclePrefabHash(identity.gameObject, out int prefabHash))
            return null;

        GameObject go = identity.gameObject;
        Vector3 position = go.transform.position;
        SpawnPrefabPacket packet;
        if (go.TryGetComponent<PrimaryElement>(out var primary)
            && ElementLoader.GetElement(go.PrefabID()) != null)
        {
            packet = new SpawnPrefabPacket(
                identity.NetId,
                prefabHash,
                position,
                primary.Mass,
                primary.Temperature,
                primary.DiseaseIdx,
                primary.DiseaseCount)
            {
                Revision = identity.LifecycleRevision != 0
                    ? identity.LifecycleRevision
                    : NetworkIdentityRegistry.BeginLifecycle(identity.NetId),
                WorldId = go.GetMyWorld()?.id ?? -1,
				IsActive = go.activeSelf,
				BindExistingOnly = RequiresExistingSnapshotBinding(
					identity, go, requireExistingPersistentObject)
            };
        }
		else
		{
			packet = new SpawnPrefabPacket(identity.NetId, prefabHash, position)
			{
				IsActive = go.activeSelf,
				Revision = identity.LifecycleRevision != 0
					? identity.LifecycleRevision
					: NetworkIdentityRegistry.BeginLifecycle(identity.NetId),
				WorldId = go.GetMyWorld()?.id ?? -1,
				BindExistingOnly = RequiresExistingSnapshotBinding(
					identity, go, requireExistingPersistentObject)
			};
		}
		packet.CaptureAuthorityState(go);
		return packet;
    }

	private static bool RequiresExistingSnapshotBinding(
		NetworkIdentity identity, GameObject gameObject, bool requirePersistent)
		=> RequiresExistingSnapshotBinding(
			identity.RequiresExistingBinding,
			gameObject.GetComponent<SaveLoadRoot>() != null,
			requirePersistent,
			ElementLoader.GetElement(gameObject.PrefabID()) != null);

	internal static bool RequiresExistingSnapshotBinding(
		bool identityRequiresExisting, bool hasSaveLoadRoot,
		bool requirePersistent, bool hasElementData)
		=> !hasElementData
		   && (identityRequiresExisting || requirePersistent && hasSaveLoadRoot);
    
    public void Serialize(BinaryWriter writer)
    {
        if (Revision == 0)
            Revision = NetworkIdentityRegistry.BeginLifecycle(NetId);
		ValidateForWire();
        writer.Write(NetId);
        writer.Write(Revision);
        writer.Write(Hash);
        writer.Write(Position);
        writer.Write(WorldId);
        writer.Write(BindExistingOnly);
        writer.Write(IsActive);
		SerializeAuthorityState(writer);
        writer.Write(HasElementData);
        if (!HasElementData) return;
        
        writer.Write(Mass);
        writer.Write(Temperature);
        writer.Write(DiseaseIndex);
        writer.Write(DiseaseCount);
    }

    public void Deserialize(BinaryReader reader)
    {
        NetId = reader.ReadInt32();
        Revision = reader.ReadUInt64();
        Hash = reader.ReadInt32();
        Position = reader.ReadVector3();
        WorldId = reader.ReadInt32();
        BindExistingOnly = reader.ReadBoolean();
		if (NetId == 0 || Revision == 0)
			throw new InvalidDataException("Invalid spawn lifecycle metadata");
        IsActive = reader.ReadBoolean();
		DeserializeAuthorityState(reader);
        HasElementData = reader.ReadBoolean();
        if (!HasElementData)
		{
			ValidateForWire();
			return;
		}
        
        Mass = reader.ReadSingle();
        Temperature = reader.ReadSingle();
        DiseaseIndex =  reader.ReadByte();
        DiseaseCount = reader.ReadInt32();
		if (!IsValidElementState(Mass, Temperature, DiseaseCount))
			throw new InvalidDataException("Invalid spawned element state");
		ValidateForWire();
    }

    public void OnDispatched()
    {
        ulong lastRevision = NetworkIdentityRegistry.GetLastLifecycleRevision(NetId);
		bool entityExists = NetworkIdentityRegistry.Exists(NetId);
        if (!ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost,
		        entityExists, lastRevision, Revision,
		        NetworkIdentityRegistry.IsLifecycleTombstoned(NetId)))
            return;
		GroundItemPickedUpPacket.CancelPending(NetId);
		if (TryReconcileOccupied(out NetworkIdentityRegistry.IdentityClaim displaced))
			return;
		if (TryFinishClaimedRuntimeObject(displaced))
			return;
	        if (BindExistingOnly)
		{
			NetworkIdentityRegistry.RollbackClaim(displaced);
			StorePendingBinding(this);
			return;
		}
		FinishCreatedRuntimeObject(displaced);
	}

	private bool TryFinishClaimedRuntimeObject(
		NetworkIdentityRegistry.IdentityClaim displaced)
	{
		NetworkIdentityRegistry.IdentityClaim claim;
		bool claimed = BindExistingOnly
			? NetworkIdentityRegistry.TryBeginAuthorityBindingClaim(
				Hash, Position, WorldId, NetId, out claim)
			: NetworkIdentityRegistry.TryBeginUnassignedClaim(
				Hash, Position, WorldId, NetId, out claim);
		if (!claimed)
			return false;
		if (FinishRuntimeMaterialization(claim.GameObject))
		{
			RetireDisplaced(displaced);
			return true;
		}
		NetworkIdentityRegistry.RollbackClaim(claim);
		NetworkIdentityRegistry.RollbackClaim(displaced);
		StorePendingBinding(this);
		return true;
	}

	private void FinishCreatedRuntimeObject(
		NetworkIdentityRegistry.IdentityClaim displaced)
	{
		NetworkIdentityRegistry.LifecycleRevisionState previousLifecycle =
			NetworkIdentityRegistry.CaptureLifecycleRevisionState(NetId);
		GameObject go = CreateAuthoritativeObject();
		if (go == null)
		{
			NetworkIdentityRegistry.RollbackClaim(displaced);
			return;
		}
		if (!FinishRuntimeMaterialization(go))
	        {
	            RollbackCreatedMaterialization(go, previousLifecycle);
	            NetworkIdentityRegistry.RollbackClaim(displaced);
	            return;
	        }
		RetireDisplaced(displaced);
		RecordClientReplayExpectation();
	}

	private bool TryReconcileOccupied(
		out NetworkIdentityRegistry.IdentityClaim displaced)
	{
		displaced = null;
		NetworkIdentityRegistry.ReleaseUnavailableRegistration(NetId);
		if (!NetworkIdentityRegistry.Exists(NetId)
		    || !NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity identity))
			return false;
		GameObject existing = identity.gameObject;
		if (existing.PrefabID().GetHashCode() == Hash)
		{
			if (!NetworkIdentityRegistry.TryBeginRegisteredMutation(
				    identity, NetId, out NetworkIdentityRegistry.IdentityClaim mutation))
			{
				StorePendingBinding(this);
				return true;
			}
			if (FinishRuntimeMaterialization(existing))
				return true;
			NetworkIdentityRegistry.RollbackClaim(mutation);
			if (ShouldQueueFailedOccupiedMaterialization(BindExistingOnly))
			{
				StorePendingBinding(this);
				return true;
			}
		}
		else if (BindExistingOnly)
		{
			StorePendingBinding(this);
			return true;
		}
		if (!TryDisplace(identity, out displaced))
		{
			StorePendingBinding(this);
			return true;
		}
		return false;
	}

	private static bool TryDisplace(
		NetworkIdentity identity, out NetworkIdentityRegistry.IdentityClaim displaced)
	{
		displaced = null;
		if (!NetworkIdentityRegistry.TryBeginRegisteredMutation(
			    identity, identity.NetId, out NetworkIdentityRegistry.IdentityClaim mutation))
			return false;
		if (!NetworkIdentityRegistry.Unregister(identity, identity.NetId))
			return false;
		NetworkIdentityRegistry.UntrackUnassigned(identity);
		displaced = mutation;
		return true;
	}

	private static void RetireDisplaced(NetworkIdentityRegistry.IdentityClaim displaced)
	{
		GameObject gameObject = displaced?.GameObject;
		if (gameObject == null || gameObject.IsNullOrDestroyed())
			return;
		gameObject.SetActive(false);
		Util.KDestroyGameObject(gameObject);
	}

	private void RollbackCreatedMaterialization(
		GameObject gameObject,
		NetworkIdentityRegistry.LifecycleRevisionState previousLifecycle)
	{
		if (gameObject != null && !gameObject.IsNullOrDestroyed()
		    && gameObject.TryGetComponent(out NetworkIdentity identity))
		{
			NetworkIdentityRegistry.Unregister(identity, identity.NetId);
			NetworkIdentityRegistry.UntrackUnassigned(identity);
		}
		NetworkIdentityRegistry.RestoreLifecycleRevisionState(NetId, previousLifecycle);
		if (gameObject == null || gameObject.IsNullOrDestroyed())
			return;
		gameObject.SetActive(false);
		Util.KDestroyGameObject(gameObject);
	}

    internal static bool ShouldApply(bool localIsHost, bool senderIsHost, bool entityExists)
		=> !localIsHost && senderIsHost;

	internal static bool ShouldApply(
		bool localIsHost,
		bool senderIsHost,
		bool entityExists,
		ulong lastRevision,
		ulong incomingRevision,
		bool tombstoned)
		=> ShouldApply(localIsHost, senderIsHost, entityExists)
		   && incomingRevision != 0 && incomingRevision >= lastRevision
		   && (incomingRevision > lastRevision || !entityExists && !tombstoned);

	internal bool CanApplySnapshot()
		=> GetSnapshotApplicabilityFailure() == null;

	internal string GetSnapshotApplicabilityFailure()
	{
		if (NetId == 0 || Revision == 0 || Hash == 0
		    || float.IsNaN(Position.x) || float.IsInfinity(Position.x)
		    || float.IsNaN(Position.y) || float.IsInfinity(Position.y)
		    || float.IsNaN(Position.z) || float.IsInfinity(Position.z))
			return "invalid lifecycle metadata";
		if (!HasValidAuthorityState())
			return "invalid duplicant lifecycle state";
		NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity identity);
		if (CanReconcileOccupiedIdentity(identity))
		{
			if (CanApplyMaterialization(identity.gameObject))
				return null;
			if (BindExistingOnly)
				return "occupied lifecycle target cannot apply authoritative state";
		}
		if (BindExistingOnly)
				return NetworkIdentityRegistry.CanClaimAuthorityBinding(
					Hash, Position, WorldId, NetId)
				? null
				: DescribeExistingBindingFailure(identity);
		if (HasElementData)
			return ElementLoader.GetElement(new Tag(Hash))?.substance != null
				? null
				: "element substance is unavailable";
		return Assets.GetPrefab(new Tag(Hash)) != null
			? null
			: "prefab is unavailable";
	}

	private bool CanReconcileOccupiedIdentity(NetworkIdentity identity)
	{
		if (identity.IsNullOrDestroyed() || identity.gameObject.IsNullOrDestroyed())
			return false;
		GameObject gameObject = identity.gameObject;
		return CanReconcileOccupiedIdentity(
			identity.IsUnavailableForBinding,
			gameObject.PrefabID().GetHashCode() == Hash,
			gameObject.GetMyWorldId(), WorldId);
	}

	internal static bool CanReconcileOccupiedIdentity(
		bool samePrefab, int occupiedWorldId, int snapshotWorldId)
		=> CanReconcileOccupiedIdentity(
			unavailable: false, samePrefab, occupiedWorldId, snapshotWorldId);

	internal static bool CanReconcileOccupiedIdentity(
		bool unavailable, bool samePrefab, int occupiedWorldId, int snapshotWorldId)
		=> !unavailable && samePrefab && occupiedWorldId == snapshotWorldId;

	private string DescribeExistingBindingFailure(NetworkIdentity identity)
	{
		if (identity.IsNullOrDestroyed() || identity.gameObject.IsNullOrDestroyed())
			return "bind-existing candidate is unavailable or ambiguous; NetId is unoccupied";
		GameObject gameObject = identity.gameObject;
		bool samePrefab = gameObject.PrefabID().GetHashCode() == Hash;
		return $"bind-existing candidate is unavailable or ambiguous; occupied=" +
		       $"samePrefab:{samePrefab}, world:{gameObject.GetMyWorldId()}, " +
		       $"positionMatch:{gameObject.transform.position.Equals(Position)}, " +
		       $"elementMatch:{ElementSnapshotMatches(gameObject)}";
	}

	internal bool TryApplySnapshot()
	{
		if (!CanApplySnapshot())
		{
#if DEBUG
			RecordSnapshotDiagnostic("preflight-failed", null);
#endif
			return false;
		}
		NetworkIdentityRegistry.ReleaseUnavailableRegistration(NetId);
		if (!TryPrepareSnapshotOccupied(
			    out NetworkIdentityRegistry.IdentityClaim displaced,
			    out bool alreadyApplied))
			return false;
		if (alreadyApplied)
			return true;
		return TryApplySnapshotReplacement(displaced);
	}

	private bool TryPrepareSnapshotOccupied(
		out NetworkIdentityRegistry.IdentityClaim displaced,
		out bool alreadyApplied)
	{
		displaced = null;
		alreadyApplied = false;
		if (!NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity existing))
			return true;
		bool samePrefab = existing.gameObject.PrefabID().GetHashCode() == Hash;
		if (!NetworkIdentityRegistry.TryBeginRegisteredMutation(
			    existing, NetId, out NetworkIdentityRegistry.IdentityClaim mutation))
			return false;
		if (samePrefab && CompleteAndVerifySnapshot(existing.gameObject))
		{
#if DEBUG
			RecordSnapshotDiagnostic("occupied-reconciled", existing.gameObject);
#endif
			alreadyApplied = true;
			return true;
		}
		NetworkIdentityRegistry.RollbackClaim(mutation);
		if (samePrefab && BindExistingOnly)
		{
#if DEBUG
			RecordSnapshotDiagnostic("occupied-bind-existing-failed", existing.gameObject);
#endif
			return false;
		}
#if DEBUG
		RecordSnapshotDiagnostic("occupied-replaced", existing.gameObject);
#endif
		return TryDisplace(existing, out displaced);
	}

	private bool TryApplySnapshotReplacement(
		NetworkIdentityRegistry.IdentityClaim displaced)
	{
		NetworkIdentityRegistry.LifecycleRevisionState previousLifecycle =
			NetworkIdentityRegistry.CaptureLifecycleRevisionState(NetId);
		NetworkIdentityRegistry.IdentityClaim claim;
		bool claimed = BindExistingOnly
			? NetworkIdentityRegistry.TryBeginAuthorityBindingClaim(
				Hash, Position, WorldId, NetId, out claim)
			: NetworkIdentityRegistry.TryBeginUnassignedClaim(
				Hash, Position, WorldId, NetId, out claim);
		GameObject gameObject = claim?.GameObject;
		if (!claimed)
			gameObject = BindExistingOnly ? null : CreateAuthoritativeObject();
		bool created = !claimed && gameObject != null;
		bool applied = gameObject != null && CompleteAndVerifySnapshot(gameObject);
		if (claimed && !applied)
			NetworkIdentityRegistry.RollbackClaim(claim);
		if (created && !applied)
			RollbackCreatedMaterialization(gameObject, previousLifecycle);
		if (!applied)
			NetworkIdentityRegistry.RollbackClaim(displaced);
		else
			RetireDisplaced(displaced);
#if DEBUG
		RecordSnapshotDiagnostic(
			claimed ? "candidate-claimed" : "object-created", gameObject);
#endif
		return applied;
	}

	private bool CompleteAndVerifySnapshot(GameObject gameObject)
	{
		return CompleteMaterialization(gameObject) && SnapshotMatches(gameObject)
		       && gameObject.activeSelf == IsActive;
	}

	private bool SnapshotMatches(GameObject gameObject)
		=> gameObject != null && !gameObject.IsNullOrDestroyed()
		   && gameObject.PrefabID().GetHashCode() == Hash
		   && gameObject.GetMyWorldId() == WorldId
		   && gameObject.transform.position.Equals(Position)
		   && ElementSnapshotMatches(gameObject)
		   && AuthorityStateSnapshotMatches(gameObject);

	private void ValidateForWire()
	{
		if (NetId == 0 || Revision == 0 || Hash == 0 || !HasValidAuthorityState())
			throw new InvalidDataException("Invalid spawn lifecycle state");
	}

	private bool ElementSnapshotMatches(GameObject gameObject)
	{
		if (!HasElementData)
			return true;
		return gameObject.TryGetComponent<PrimaryElement>(out var primary)
		       && primary.Mass.Equals(Mass)
		       && primary.Temperature.Equals(Temperature)
		       && primary.DiseaseIdx == DiseaseIndex
		       && primary.DiseaseCount == DiseaseCount;
	}

	    internal static void ClearState()
	    {
	        ClearPendingBindings();
	        ClearClientReplayExpectations();
			DuplicantDeathStatePacket.ClearState();
	    }

}
