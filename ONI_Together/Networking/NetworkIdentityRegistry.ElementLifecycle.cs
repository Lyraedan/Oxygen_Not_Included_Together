using System.Linq;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Networking
{
	public static partial class NetworkIdentityRegistry
	{
		internal static int RetireUnstableElementLifecyclesForSnapshot()
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
				return 0;
			NetworkIdentity[] unstable = identities.Values
				.Where(IsUnstableElementLifecycle).ToArray();
			int retired = unstable.Count(identity =>
				identity.RetireAuthoritativeLifecycle());
			if (retired > 0)
				DebugConsole.Log(
					$"[LifecycleSnapshot] retired unstable element identities={retired}");
			return retired;
		}

		private static bool IsUnstableElementLifecycle(NetworkIdentity identity)
		{
			if (identity.IsNullOrDestroyed() || identity.gameObject.IsNullOrDestroyed()
			    || !identity.TryGetComponent<PrimaryElement>(out var primary))
				return false;
			return ElementLoader.GetElement(identity.gameObject.PrefabID()) != null
			       && !SpawnPrefabPacket.ShouldSynchronizeElementLifecycle(primary.Mass);
		}
	}
}
