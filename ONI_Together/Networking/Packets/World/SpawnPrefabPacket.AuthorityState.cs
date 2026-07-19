using System.IO;
using ONI_Together.Networking.Synchronization;
using ONI_Together.Scripts.Buildings;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public partial class SpawnPrefabPacket
	{
		public bool HasOperationalState;
		public bool OperationalIsActive;
		public bool OperationalIsFunctional;
		public bool OperationalIsOperational;

		internal static bool ShouldQueueFailedOccupiedMaterialization(bool bindExistingOnly)
			=> bindExistingOnly;

		internal static bool ShouldPreventElementMerge(int authoritativeNetId)
			=> authoritativeNetId != 0;

		internal static bool ShouldActivateForInitialization(bool desiredFinalActive)
			=> true;

		private void CaptureAuthorityState(GameObject gameObject)
		{
			CaptureDuplicantState(gameObject);
			HasOperationalState = gameObject.TryGetComponent(out Operational operational);
			if (!HasOperationalState)
				return;
			OperationalIsActive = operational.IsActive;
			OperationalIsFunctional = operational.IsFunctional;
			OperationalIsOperational = operational.IsOperational;
		}

		private void SerializeAuthorityState(BinaryWriter writer)
		{
			writer.Write(HasDuplicantState);
			if (HasDuplicantState)
			{
				writer.Write(IsDuplicantDead);
				if (IsDuplicantDead)
					DuplicantDeathWire.WriteDeathId(writer, DuplicantDeathId);
			}
			writer.Write(HasOperationalState);
			if (!HasOperationalState)
				return;
			writer.Write(OperationalIsActive);
			writer.Write(OperationalIsFunctional);
			writer.Write(OperationalIsOperational);
		}

		private void DeserializeAuthorityState(BinaryReader reader)
		{
			HasDuplicantState = reader.ReadBoolean();
			if (HasDuplicantState)
			{
				IsDuplicantDead = reader.ReadBoolean();
				DuplicantDeathId = IsDuplicantDead
					? DuplicantDeathWire.ReadDeathId(reader)
					: string.Empty;
			}
			HasOperationalState = reader.ReadBoolean();
			if (!HasOperationalState)
				return;
			OperationalIsActive = reader.ReadBoolean();
			OperationalIsFunctional = reader.ReadBoolean();
			OperationalIsOperational = reader.ReadBoolean();
		}

		private bool ApplyAuthorityState(GameObject gameObject)
		{
			if (HasDuplicantState && !DuplicantDeathSync.Apply(
				    gameObject, IsDuplicantDead, DuplicantDeathId))
				return false;
			if (!HasOperationalState)
				return true;
			ClientReceiver_Operational receiver =
				gameObject.AddOrGet<ClientReceiver_Operational>();
			receiver.ApplySnapshot(
				OperationalIsActive, OperationalIsOperational, OperationalIsFunctional);
			return true;
		}

		private bool CanApplyAuthorityState(GameObject gameObject)
			=> !HasDuplicantState
			   || DuplicantDeathSync.CanApply(
				   gameObject, IsDuplicantDead, DuplicantDeathId);

		private bool AuthorityStateSnapshotMatches(GameObject gameObject)
		{
			if (!DuplicantSnapshotMatches(gameObject))
				return false;
			if (!HasOperationalState)
				return true;
			return gameObject.TryGetComponent(out ClientReceiver_Operational receiver)
			       && receiver.SnapshotMatches(
				       OperationalIsActive, OperationalIsOperational, OperationalIsFunctional);
		}

		private bool HasValidAuthorityState() => HasValidDuplicantState();
	}
}
