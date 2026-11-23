using JetBrains.Annotations;
using Shared.Helpers;
using Shared.Profiling;
using System;
using System.Reflection;
using UnityEngine;

namespace ONI_Together_API.Networking
{
	public static class NetworkIdentityRegistryAPI
	{
		private const string RegistryTypeName = "ONI_Together.Networking.NetworkIdentityRegistry, ONI_Together";

		static bool Init()
		{
			using var _ = Profiler.Scope();

			if (typesInitialized)
				return true;

			if (!ReflectionHelper.TryCreateDelegate<TryGetDelegate>(
					RegistryTypeName, "TryGetGameObject",
					[typeof(int), typeof(GameObject).MakeByRefType()], out _TryGet))
				return false;

			if (!ReflectionHelper.TryGetGenericMethodDefinition(
					RegistryTypeName, "TryGetComponent",
					m => m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(int),
					out _TryGetComponentGeneric))
				return false;

			typesInitialized = true;
			return true;
		}

		static bool typesInitialized = false;

		static TryGetDelegate? _TryGet = null;
		delegate bool TryGetDelegate(int netId, out GameObject gameObject);

		static MethodInfo? _TryGetComponentGeneric = null;
		delegate bool TryGetComponentDelegate<T>(int netId, out T component);

		static class ClosedCache<T>
		{
			public static TryGetComponentDelegate<T>? Closed;
			public static bool Resolved;
		}

		/// <summary>
		/// Looks up the GameObject registered for the given network id.
		/// </summary>
		/// <param name="netId">The network id assigned by <c>NetworkIdentity</c>.</param>
		/// <param name="gameObject">The registered GameObject, or <c>null</c> if not found.</param>
		/// <returns><c>true</c> if a live GameObject was found; otherwise <c>false</c>.</returns>
		[PublicAPI]
		public static bool TryGet(int netId, out GameObject gameObject)
		{
			using var _ = Profiler.Scope();

			if (!Init() || _TryGet == null)
			{
				gameObject = null!;
				return false;
			}
			return _TryGet(netId, out gameObject);
		}

		/// <summary>
		/// Looks up a component of type <typeparamref name="T"/> on the GameObject registered for the given network id.
		/// </summary>
		/// <typeparam name="T">The component type to retrieve.</typeparam>
		/// <param name="netId">The network id assigned by <c>NetworkIdentity</c>.</param>
		/// <param name="component">The component if found; otherwise <c>default</c>.</param>
		/// <returns><c>true</c> if the GameObject and component were found; otherwise <c>false</c>.</returns>
		[PublicAPI]
		public static bool TryGetComponent<T>(int netId, out T component)
		{
			using var _ = Profiler.Scope();

			component = default!;
			if (!Init() || _TryGetComponentGeneric == null)
				return false;

			if (!ClosedCache<T>.Resolved)
			{
				ClosedCache<T>.Resolved = true;
				try
				{
					var closed = _TryGetComponentGeneric.MakeGenericMethod(typeof(T));
					ClosedCache<T>.Closed = (TryGetComponentDelegate<T>)Delegate.CreateDelegate(
						typeof(TryGetComponentDelegate<T>), closed);
				}
				catch
				{
					ClosedCache<T>.Closed = null;
				}
			}

			return ClosedCache<T>.Closed != null && ClosedCache<T>.Closed(netId, out component);
		}
	}
}
