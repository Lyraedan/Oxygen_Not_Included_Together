using ONI_Together.Networking.Components;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;


namespace ONI_Together.Networking
{
	public static class Extensions
	{
		public static NetworkIdentity GetNetIdentity(this MonoBehaviour behaviour)
		{
			using var _ = Profiler.Scope();

			if (behaviour.IsNullOrDestroyed() || behaviour.gameObject.IsNullOrDestroyed())
			{
				return null;
			}
			return behaviour.gameObject.GetNetIdentity();
		}
		public static NetworkIdentity GetNetIdentity(this GameObject go)
		{
			using var _ = Profiler.Scope();

			if (go.IsNullOrDestroyed())
			{
				return null;
			}

			if (go.TryGetComponent<NetworkIdentity>(out var identity))
				return identity;

			return go.AddComponent<NetworkIdentity>();
		}

		public static bool TryGetNetIdentity(this GameObject go, out NetworkIdentity identity)
		{
			using var _ = Profiler.Scope();
			identity = GetNetIdentity(go);
			return identity != null;
		}

		/// <summary>
		/// The identity this object already has, or null. Never attaches one.
		///
		/// GetNetIdentity above ends in AddComponent, which is right when the caller is
		/// about to address the object and wrong when it is only asking. Five places in
		/// the mod already need the asking version and spell it out by hand as
		/// GetComponent&lt;NetworkIdentity&gt;() - both overlay draw paths, both overlay
		/// hover paths, and the cell fallback in BuildingConfigPacket - so they lose the
		/// null check and the profiler scope the helpers have.
		///
		/// The overlay is the clearest case: drawing a network overlay should not attach
		/// a NetworkIdentity to whatever the cursor passes over.
		/// </summary>
		public static NetworkIdentity GetExistingNetIdentity(this GameObject go)
		{
			using var _ = Profiler.Scope();

			if (go.IsNullOrDestroyed())
				return null;

			return go.TryGetComponent<NetworkIdentity>(out var identity) ? identity : null;
		}

		/// <summary>Same, from a component - the overload GetNetIdentity already has.</summary>
		public static NetworkIdentity GetExistingNetIdentity(this MonoBehaviour behaviour)
		{
			using var _ = Profiler.Scope();

			if (behaviour.IsNullOrDestroyed())
				return null;

			return behaviour.gameObject.GetExistingNetIdentity();
		}

		/// <summary>The asking form of TryGetNetIdentity: reports what is there, adds nothing.</summary>
		public static bool TryGetExistingNetIdentity(this GameObject go, out NetworkIdentity identity)
		{
			using var _ = Profiler.Scope();
			identity = GetExistingNetIdentity(go);
			return identity != null;
		}

		public static int GetNetId(this MonoBehaviour behaviour)
		{
			using var _ = Profiler.Scope();

			if (!behaviour.IsNullOrDestroyed() && behaviour.gameObject.TryGetNetIdentity(out var identity))
			{
				return identity.NetId;
			}

			return 0;
		}

		// Used to replace CSteamID
        public static bool IsValid(this ulong value)
        {
	        using var _ = Profiler.Scope();

            return value != ulong.MaxValue && !value.Equals(value.Nil());
        }

		public static CSteamID AsCSteamID(this ulong value)
		{
			using var _ = Profiler.Scope();

			return new CSteamID(value);
		}

		public static ulong Nil(this ulong value)
		{
			using var _ = Profiler.Scope();

			return 0uL; // Stole this badboy from the steamworks api
        }
    }
}
