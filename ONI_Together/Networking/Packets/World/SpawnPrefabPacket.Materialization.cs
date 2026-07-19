using ONI_Together.Networking.Synchronization;
using ONI_Together.Scripts.Buildings;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public partial class SpawnPrefabPacket
	{
		private GameObject CreateAuthoritativeObject()
		{
			if (HasElementData)
			{
				Element element = ElementLoader.GetElement(new Tag(Hash));
				return element?.substance.SpawnResource(
					Position, Mass, Temperature, DiseaseIndex, DiseaseCount,
					prevent_merge: ShouldPreventElementMerge(NetId));
			}
			GameObject prefab = Assets.GetPrefab(new Tag(Hash));
			if (prefab == null)
				return null;
			GameObject created = Util.KInstantiate(prefab, Position);
			created.SetActive(ShouldActivateForInitialization(IsActive));
			return created;
		}

		private bool CompleteMaterialization(GameObject gameObject)
		{
			if (!CanApplyMaterialization(gameObject))
				return false;
			MaterializationState previous =
				MaterializationState.Capture(gameObject, HasDuplicantState);
			gameObject.transform.SetPosition(Position);
			if (!NetworkIdentityRegistry.TryBindAuthoritativeLifecycle(
				    gameObject, NetId, Revision))
				return RestoreFailedMaterialization(gameObject, previous);
			ApplyElementState(gameObject);
			gameObject.SetActive(IsActive);
			if (!ApplyAuthorityState(gameObject)
			    || !SnapshotMatches(gameObject) || gameObject.activeSelf != IsActive)
				return RestoreFailedMaterialization(gameObject, previous);
			return true;
		}

		private static bool RestoreFailedMaterialization(
			GameObject gameObject, MaterializationState previous)
		{
			previous.Restore(gameObject);
			return false;
		}

		private bool CanApplyMaterialization(GameObject gameObject)
		{
			if (gameObject == null || gameObject.IsNullOrDestroyed()
			    || gameObject.PrefabID().GetHashCode() != Hash
			    || gameObject.GetMyWorldId() != WorldId)
				return false;
			if (HasElementData && !gameObject.TryGetComponent<PrimaryElement>(out _))
				return false;
			return CanApplyAuthorityState(gameObject);
		}

		private bool FinishRuntimeMaterialization(GameObject gameObject)
		{
			if (!CompleteMaterialization(gameObject))
				return false;
			ConsumePendingPickup(gameObject);
			DuplicantDeathStatePacket.TryApplyPending(NetId, gameObject);
			return true;
		}

		private void ConsumePendingPickup(GameObject gameObject)
		{
			if (GroundItemPickedUpPacket.TryConsumePending(NetId))
			{
				Util.KDestroyGameObject(gameObject);
				return;
			}
			StorageItemPacket.TryApplyPending(NetId, gameObject);
			if (gameObject.TryGetComponent<Storage>(out var storage))
				StorageItemPacket.TryApplyPendingForStorage(NetId, storage);
		}

		private void ApplyElementState(GameObject gameObject)
		{
			if (!HasElementData || !gameObject.TryGetComponent<PrimaryElement>(out var primary))
				return;
			primary.Mass = Mass;
			primary.Temperature = Temperature;
			if (primary.DiseaseIdx == DiseaseIndex && primary.DiseaseCount == DiseaseCount)
				return;
			if (primary.DiseaseCount > 0)
				primary.ModifyDiseaseCount(
					-primary.DiseaseCount, "ONI Together claimed spawn sync");
			if (DiseaseCount > 0)
				primary.AddDisease(
					DiseaseIndex, DiseaseCount, "ONI Together claimed spawn sync");
		}

		internal static bool IsValidElementState(
			float mass, float temperature, int diseaseCount)
		{
			return ShouldSynchronizeElementLifecycle(mass)
			       && !float.IsNaN(temperature) && !float.IsInfinity(temperature)
			       && temperature >= 0f && diseaseCount >= 0;
		}

		internal static bool ShouldSynchronizeElementLifecycle(float mass)
			=> !float.IsNaN(mass) && !float.IsInfinity(mass) && mass > 0f;

		private sealed class MaterializationState
		{
			private Vector3 Position { get; set; }
			private bool ActiveSelf { get; set; }
			private bool HasElement { get; set; }
			private float Mass { get; set; }
			private float Temperature { get; set; }
			private byte DiseaseIndex { get; set; }
			private int DiseaseCount { get; set; }
			private bool HasOperational { get; set; }
			private bool OperationalIsActive { get; set; }
			private bool OperationalIsOperational { get; set; }
			private bool OperationalIsFunctional { get; set; }
			private bool HasDuplicant { get; set; }
			private DuplicantDeathSync.RollbackState Duplicant { get; set; }

			internal static MaterializationState Capture(
				GameObject gameObject, bool captureDuplicant)
			{
				bool hasElement = gameObject.TryGetComponent(out PrimaryElement element);
				bool hasOperational = gameObject.TryGetComponent(
					out ClientReceiver_Operational operational);
				DuplicantDeathSync.RollbackState duplicant = default;
				bool hasDuplicant = captureDuplicant
				                    && DuplicantDeathSync.TryCaptureRollbackState(
					                    gameObject, out duplicant);
				return new MaterializationState
				{
					Position = gameObject.transform.position,
					ActiveSelf = gameObject.activeSelf,
					HasElement = hasElement,
					Mass = hasElement ? element.Mass : 0f,
					Temperature = hasElement ? element.Temperature : 0f,
					DiseaseIndex = hasElement ? element.DiseaseIdx : (byte)0,
					DiseaseCount = hasElement ? element.DiseaseCount : 0,
					HasOperational = hasOperational,
					OperationalIsActive = hasOperational && operational.IsActive,
					OperationalIsOperational = hasOperational && operational.IsOperational,
					OperationalIsFunctional = hasOperational && operational.IsFunctional,
					HasDuplicant = hasDuplicant,
					Duplicant = duplicant
				};
			}

			internal void Restore(GameObject gameObject)
			{
				if (gameObject == null || gameObject.IsNullOrDestroyed())
					return;
				gameObject.transform.position = Position;
				RestoreElement(gameObject);
				if (HasDuplicant)
					DuplicantDeathSync.RestoreRollbackState(gameObject, Duplicant);
				RestoreOperational(gameObject);
				gameObject.SetActive(ActiveSelf);
			}

			private void RestoreElement(GameObject gameObject)
			{
				if (!HasElement || !gameObject.TryGetComponent(out PrimaryElement element))
					return;
				element.Mass = Mass;
				element.Temperature = Temperature;
				if (element.DiseaseCount > 0)
					element.ModifyDiseaseCount(
						-element.DiseaseCount, "ONI Together lifecycle rollback");
				if (DiseaseCount > 0)
					element.AddDisease(
						DiseaseIndex, DiseaseCount, "ONI Together lifecycle rollback");
			}

			private void RestoreOperational(GameObject gameObject)
			{
				if (HasOperational && gameObject.TryGetComponent(
					    out ClientReceiver_Operational operational))
				{
					operational.ApplySnapshot(
						OperationalIsActive, OperationalIsOperational,
						OperationalIsFunctional);
					return;
				}
				if (!HasOperational && gameObject.TryGetComponent(
					    out ClientReceiver_Operational addedOperational))
					UnityEngine.Object.Destroy(addedOperational);
			}
		}
	}
}
