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

		public static int GetNetId(this MonoBehaviour behaviour)
		{
			using var _ = Profiler.Scope();

			if (behaviour.gameObject.TryGetNetIdentity(out var identity))
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
