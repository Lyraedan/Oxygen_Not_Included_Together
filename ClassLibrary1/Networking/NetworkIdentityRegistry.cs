using ONI_MP.DebugTools;
using ONI_MP.Networking.Components;
using ONI_MP.Networking.Packets.World;
using System;
using System.Collections.Generic;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking
{
	public static class NetworkIdentityRegistry
	{
		private static readonly Dictionary<int, NetworkIdentity> identities = new Dictionary<int, NetworkIdentity>();

		private static int _lookupFailCount = 0;
		private static float _lastFailLogTime = 0f;

		public static int Count => identities?.Count ?? 0;

		public static int Register(NetworkIdentity entity)
		{
			using var _ = Profiler.Scope();

			int id, attempt = 0;
			do
			{
				id = Guid.NewGuid().GetHashCode() + attempt++;
			} while (identities.ContainsKey(id));

			identities[id] = entity;
			return id;
		}

		public static void Unregister(int netId)
		{
			using var _ = Profiler.Scope();

			identities.Remove(netId);
		}


		public static void RegisterExisting(NetworkIdentity entity, int netId)
		{
			using var _ = Profiler.Scope();

			if (!identities.ContainsKey(netId))
			{
				identities[netId] = entity;
				//DebugConsole.Log($"[NetEntityRegistry] Registered existing entity with net id: {netId}");
			}
			//else
			//{
			//    DebugConsole.LogWarning($"[NetEntityRegistry] NetId {netId} already registered. Skipping duplicate registration.");
			//}
		}

		public static void RegisterOverride(NetworkIdentity entity, int netId)
		{
			using var _ = Profiler.Scope();

			if (identities.ContainsKey(netId))
			{
				DebugConsole.LogWarning($"[NetEntityRegistry] Overwriting existing entity for NetId {netId}");
				identities[netId] = entity;
			}
			else
			{
				identities.Add(netId, entity);
				DebugConsole.Log($"[NetEntityRegistry] Registered overridden NetId {netId} for {entity.name}");
			}
		}
		public static bool Exists(int netId) => identities.ContainsKey(netId);



		public static bool TryGet(int netId, out NetworkIdentity entity)
		{
			using var _ = Profiler.Scope();

			bool found = identities.TryGetValue(netId, out entity);
			if (!found)
			{
				_lookupFailCount++;
				if (_lookupFailCount <= 3 || _lookupFailCount % 500 == 0 || Time.unscaledTime - _lastFailLogTime > 1f)
				{
					_lastFailLogTime = Time.unscaledTime;
					DebugConsole.LogWarning($"[Registry] Lookup failed (#{_lookupFailCount}): NetId {netId} not found. Count: {identities.Count}");
				}
			}
			return found;
		}

		public static bool TryGetComponent<T>(int netId, out T component)
		{
			using var _ = Profiler.Scope();

			component = default(T);
			if (!TryGet(netId, out var ni))
				return false;
			if(ni.gameObject.IsNullOrDestroyed())
				return false;
			return ni.gameObject.TryGetComponent<T>(out component);
		}
		public static bool TryGetComponent<T>(NetworkIdentity ni, out T component)
		{
			using var _ = Profiler.Scope();

			component = default(T);
			if (ni.IsNullOrDestroyed() || ni.gameObject.IsNullOrDestroyed())
				return false;
			return ni.gameObject.TryGetComponent<T>(out component);
		}

		public static void Clear()
		{
			using var _ = Profiler.Scope();

			identities.Clear();
			_lookupFailCount = 0;
			GroundItemPickedUpPacket.ClearPending();
			PlayAnimPacket.ClearState();
		}

		public static IEnumerable<NetworkIdentity> AllIdentities => identities.Values;
	}
}
