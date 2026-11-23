using JetBrains.Annotations;
using Shared.Helpers;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together_API.Misc
{
	public static class SpawnUtilsAPI
	{
		private const string SpawnUtilsTypeName = "ONI_Together.Misc.SpawnUtils, ONI_Together";

		static bool Init()
		{
			using var _ = Profiler.Scope();

			if (typesInitialized)
				return true;

			if (!ReflectionHelper.TryCreateDelegate<KNetInstantiatePrefabDelegate>(
					SpawnUtilsTypeName, "KNetInstantiate",
					[typeof(GameObject), typeof(Vector3), typeof(bool)], out _kNetInstantiatePrefab))
				return false;

			if (!ReflectionHelper.TryCreateDelegate<KNetInstantiateElementDelegate>(
					SpawnUtilsTypeName, "KNetInstantiate",
					[typeof(int), typeof(Vector3), typeof(float), typeof(float), typeof(byte), typeof(int)], out _kNetInstantiateElement))
				return false;

			if (!ReflectionHelper.TryCreateDelegate<BroadcastSpawnDelegate>(
					SpawnUtilsTypeName, "BroadcastSpawn",
					[typeof(GameObject), typeof(bool)], out _broadcastSpawn))
				return false;

			if (!ReflectionHelper.TryCreateDelegate<BroadcastResourceSpawnDelegate>(
					SpawnUtilsTypeName, "BroadcastResourceSpawn",
					[typeof(GameObject)], out _broadcastResourceSpawn))
				return false;

			typesInitialized = true;
			return true;
		}

		static bool typesInitialized = false;

		static KNetInstantiatePrefabDelegate? _kNetInstantiatePrefab = null;
		delegate GameObject? KNetInstantiatePrefabDelegate(GameObject prefab, Vector3 position, bool isActive);

		static KNetInstantiateElementDelegate? _kNetInstantiateElement = null;
		delegate GameObject? KNetInstantiateElementDelegate(int elementHash, Vector3 position, float mass, float temperature, byte diseaseIdx, int diseaseCount);

		static BroadcastSpawnDelegate? _broadcastSpawn = null;
		delegate int BroadcastSpawnDelegate(GameObject go, bool isActive);

		static BroadcastResourceSpawnDelegate? _broadcastResourceSpawn = null;
		delegate int BroadcastResourceSpawnDelegate(GameObject go);

		/// <summary>
		/// Spawns a prefab on the host, assigns it a network identity, and replicates the spawn to all clients.
		/// </summary>
		/// <param name="prefab">The prefab to instantiate.</param>
		/// <param name="position">World position for the new GameObject.</param>
		/// <param name="isActive">Whether the spawned GameObject should be active.</param>
		/// <returns>
		/// The spawned GameObject on the host, or <c>null</c> if ONI Together is not loaded,
		/// the local player is not the host, or the prefab is invalid.
		/// </returns>
		[PublicAPI]
		public static GameObject? KNetInstantiate(GameObject prefab, Vector3 position, bool isActive = true)
		{
			using var _ = Profiler.Scope();

			if (!Init() || _kNetInstantiatePrefab == null)
				return null;
			return _kNetInstantiatePrefab(prefab, position, isActive);
		}

		/// <summary>
		/// Spawns an element resource (e.g. ore from digging) on the host, preserving mass, temperature and
		/// disease data, and replicates the spawn to all clients.
		/// </summary>
		/// <param name="elementHash">The <c>SimHashes</c> value of the element cast to <c>int</c>. Use <c>(int)element.id</c>.</param>
		/// <param name="position">World position for the new resource.</param>
		/// <param name="mass">Mass of the resource in kg.</param>
		/// <param name="temperature">Temperature of the resource.</param>
		/// <param name="diseaseIdx">Disease index (0 = no disease).</param>
		/// <param name="diseaseCount">Disease germ count.</param>
		/// <returns>
		/// The spawned GameObject on the host, or <c>null</c> if ONI Together is not loaded,
		/// the local player is not the host, or the element is invalid.
		/// </returns>
		[PublicAPI]
		public static GameObject? KNetInstantiate(int elementHash, Vector3 position, float mass, float temperature, byte diseaseIdx, int diseaseCount)
		{
			using var _ = Profiler.Scope();

			if (!Init() || _kNetInstantiateElement == null)
				return null;
			return _kNetInstantiateElement(elementHash, position, mass, temperature, diseaseIdx, diseaseCount);
		}

		/// <summary>
		/// Gives a GameObject the host has already spawned a network identity and replicates the spawn to all clients.
		/// Use this for objects the you create yourself rather than via <see cref="KNetInstantiate(GameObject, Vector3, bool)"/>.
		/// </summary>
		/// <param name="go">The GameObject the host has spawned.</param>
		/// <param name="isActive">Whether the clients should spawn it active.</param>
		/// <returns>
		/// The object's NetId, or <c>0</c> if ONI Together is not loaded, <paramref name="go"/> is <c>null</c>,
		/// or the object could not be registered.
		/// </returns>
		[PublicAPI]
		public static int BroadcastSpawn(GameObject go, bool isActive = true)
		{
			using var _ = Profiler.Scope();

			if (!Init() || _broadcastSpawn == null)
				return 0;
			return _broadcastSpawn(go, isActive);
		}

		/// <summary>
		/// The resource version of <see cref="BroadcastSpawn(GameObject, bool)"/>: gives an element resource the host
		/// has already spawned (e.g. with <c>Substance.SpawnResource</c>) a network identity and replicates it to all
		/// clients, preserving the element, mass, temperature and disease of its <c>PrimaryElement</c>.
		/// Anything that is not an element resource is sent as a prefab spawn.
		/// </summary>
		/// <param name="go">The resource GameObject the host has spawned.</param>
		/// <returns>
		/// The object's NetId, or <c>0</c> if ONI Together is not loaded, <paramref name="go"/> is <c>null</c>,
		/// or the object could not be registered.
		/// </returns>
		[PublicAPI]
		public static int BroadcastResourceSpawn(GameObject go)
		{
			using var _ = Profiler.Scope();

			if (!Init() || _broadcastResourceSpawn == null)
				return 0;
			return _broadcastResourceSpawn(go);
		}
	}
}
