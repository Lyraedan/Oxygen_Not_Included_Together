using Shared.Profiling;

namespace ONI_Together.Scripts.Buildings
{
	internal class ClientReceiver_Operational : KMonoBehaviour
	{
		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();
		}

		public bool IsFunctional { get; set; }

		public bool IsOperational { get; set; } = true;

		public bool IsActive { get; set; }

		internal void ApplySnapshot(bool isActive, bool isOperational, bool isFunctional)
		{
			IsActive = isActive;
			IsOperational = isOperational;
			IsFunctional = isFunctional;
		}

		internal bool SnapshotMatches(
			bool isActive, bool isOperational, bool isFunctional)
			=> IsActive == isActive
			   && IsOperational == isOperational
			   && IsFunctional == isFunctional;
	}
}
