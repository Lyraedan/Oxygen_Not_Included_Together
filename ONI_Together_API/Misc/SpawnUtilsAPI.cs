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

			typesInitialized = true;
			return true;
		}

		static bool typesInitialized = false;

		static KNetInstantiatePrefabDelegate? _kNetInstantiatePrefab = null;
		delegate GameObject? KNetInstantiatePrefabDelegate(GameObject prefab, Vector3 position, bool isActive);

		static KNetInstantiateElementDelegate? _kNetInstantiateElement = null;
		delegate GameObject? KNetInstantiateElementDelegate(int elementHash, Vector3 position, float mass, float temperature, byte diseaseIdx, int diseaseCount);

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
	}
}
