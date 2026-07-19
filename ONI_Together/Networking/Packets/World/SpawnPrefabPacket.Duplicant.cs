using ONI_Together.Networking.Synchronization;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public partial class SpawnPrefabPacket
	{
		public bool HasDuplicantState;
		public bool IsDuplicantDead;
		public string DuplicantDeathId = string.Empty;

		private bool DuplicantSnapshotMatches(GameObject gameObject)
			=> !HasDuplicantState || DuplicantDeathSync.SnapshotMatches(
				gameObject, IsDuplicantDead, DuplicantDeathId);

		private void CaptureDuplicantState(GameObject gameObject)
		{
			HasDuplicantState = DuplicantDeathSync.TryCapture(
				gameObject, out bool isDead, out string deathId);
			IsDuplicantDead = HasDuplicantState && isDead;
			DuplicantDeathId = IsDuplicantDead ? deathId : string.Empty;
		}

		private bool HasValidDuplicantState()
			=> !HasDuplicantState
			   || (IsDuplicantDead
				   ? DuplicantDeathSync.IsValidDeathId(DuplicantDeathId)
				   : string.IsNullOrEmpty(DuplicantDeathId));
	}
}
