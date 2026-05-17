using System.Collections.Generic;
using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Registry for building configuration handlers.
	/// Maps ConfigHash values to their respective handlers for fast lookup.
	/// </summary>
	public static class BuildingConfigHandlerRegistry
	{
		private static readonly Dictionary<int, IBuildingConfigHandler> _handlersByHash = new Dictionary<int, IBuildingConfigHandler>();
		private static readonly List<IBuildingConfigHandler> _allHandlers = new List<IBuildingConfigHandler>();
		private static bool _initialized = false;

		/// <summary>
		/// Initializes the registry by discovering and registering all handlers.
		/// Called automatically on first use.
		/// </summary>
		public static void Initialize()
		{
			using var _ = Profiler.Scope();

			if (_initialized) return;
			_initialized = true;

			// Register all handlers here
			// Each handler will be registered for each of its supported ConfigHashes
			RegisterHandler(new ActivationRangeHandler());
			RegisterHandler(new ThresholdSwitchHandler());
			RegisterHandler(new SliderControlHandler());
			RegisterHandler(new CapacityHandler());
			RegisterHandler(new DoorHandler());
			RegisterHandler(new TimerSensorHandler());
			RegisterHandler(new AlarmHandler());
			RegisterHandler(new GeoTunerHandler());
			RegisterHandler(new MissileLauncherHandler());
			RegisterHandler(new FilterableHandler());
			RegisterHandler(new StorageFilterHandler());
			RegisterHandler(new ReceptacleHandler());
			RegisterHandler(new MiscBuildingHandler());
			RegisterHandler(new AccessControlHandler());
			RegisterHandler(new CraftingHandler());
			RegisterHandler(new CometDetectorHandler());
			RegisterHandler(new ToggleableHandler());
			RegisterHandler(new UprootHandler());

			DebugConsole.Log($"[BuildingConfigHandlerRegistry] Initialized with {_allHandlers.Count} handlers, {_handlersByHash.Count} hash mappings");
		}

		/// <summary>
		/// Registers a handler for all of its supported ConfigHashes.
		/// </summary>
		public static void RegisterHandler(IBuildingConfigHandler handler)
		{
			using var _ = Profiler.Scope();

			_allHandlers.Add(handler);

			foreach (var hash in handler.SupportedConfigHashes)
			{
				if (_handlersByHash.ContainsKey(hash))
				{
					DebugConsole.Log($"[BuildingConfigHandlerRegistry] Warning: ConfigHash {hash} already registered, overwriting");
				}
				_handlersByHash[hash] = handler;
			}
		}

		/// <summary>
		/// Attempts to handle a building configuration packet.
		/// </summary>
		/// <param name="go">Target GameObject</param>
		/// <param name="packet">The configuration packet</param>
		/// <returns>True if the configuration was handled by a registered handler</returns>
		public static bool TryHandle(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			if (!_initialized) Initialize();

			// Fast path: lookup by hash
			if (_handlersByHash.TryGetValue(packet.ConfigHash, out var handler))
			{
				if (handler.TryApplyConfig(go, packet))
				{
					return true;
				}
			}

			// Fallback: iterate all handlers (for handlers that check component existence)
			foreach (var h in _allHandlers)
			{
				if (h.TryApplyConfig(go, packet))
				{
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Clears all registered handlers. Primarily for testing.
		/// </summary>
		public static void Clear()
		{
			using var _ = Profiler.Scope();

			_handlersByHash.Clear();
			_allHandlers.Clear();
			_initialized = false;
		}
	}
}
