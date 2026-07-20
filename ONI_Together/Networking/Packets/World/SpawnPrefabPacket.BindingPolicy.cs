using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public partial class SpawnPrefabPacket
	{
		private static bool RequiresExistingSnapshotBinding(
			NetworkIdentity identity, GameObject gameObject, bool requirePersistent)
			=> RequiresBuildStateMaterialization(
				gameObject.GetComponent<Constructable>() != null)
			   || RequiresExistingSnapshotBinding(
				identity.RequiresExistingBinding,
				gameObject.GetComponent<SaveLoadRoot>() != null,
				requirePersistent,
				ElementLoader.GetElement(gameObject.PrefabID()) != null);

		internal static bool RequiresBuildStateMaterialization(bool hasConstructable)
			=> hasConstructable;

		internal static bool RequiresExistingSnapshotBinding(
			bool identityRequiresExisting, bool hasSaveLoadRoot,
			bool requirePersistent, bool hasElementData)
			=> !hasElementData
			   && (identityRequiresExisting || requirePersistent && hasSaveLoadRoot);
	}
}
