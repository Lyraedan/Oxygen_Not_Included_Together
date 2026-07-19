using ONI_Together.Networking.Components;
using UnityEngine;

namespace ONI_Together.Networking
{
	public static partial class NetworkIdentityRegistry
	{
		internal sealed class IdentityClaim
		{
			internal readonly NetworkIdentity Identity;
			internal readonly NetworkIdentity.BindingState PreviousState;
			internal readonly LifecycleRevisionState PreviousLifecycleState;
			internal readonly Vector3 PreviousPosition;
			internal readonly bool PreviousActiveSelf;
			internal readonly int ClaimedNetId;
			internal readonly bool WasRegistered;
			internal readonly bool WasTracked;
			internal readonly float TrackedAt;
			internal GameObject GameObject => Identity?.gameObject;

			internal IdentityClaim(NetworkIdentity identity, int claimedNetId)
			{
				Identity = identity;
				PreviousState = identity.CaptureBindingState();
				PreviousLifecycleState = CaptureLifecycleRevisionState(claimedNetId);
				PreviousPosition = identity.transform.position;
				PreviousActiveSelf = identity.gameObject.activeSelf;
				ClaimedNetId = claimedNetId;
				WasRegistered = IsRegistered(identity, identity.NetId);
				WasTracked = unassigned.TryGetValue(identity, out float trackedAt);
				TrackedAt = trackedAt;
			}
		}
	}
}
