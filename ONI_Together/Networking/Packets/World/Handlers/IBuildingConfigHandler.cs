using UnityEngine;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Interface for building configuration handlers.
	/// Each handler is responsible for applying specific types of building configurations.
	/// </summary>
	public interface IBuildingConfigHandler
	{
		/// <summary>
		/// Gets all ConfigHash values this handler can process.
		/// Used by the registry for fast lookup.
		/// </summary>
		int[] SupportedConfigHashes { get; }

		/// <summary>
		/// Attempts to apply the configuration to the target GameObject.
		/// </summary>
		/// <param name="go">The target GameObject</param>
		/// <param name="packet">The configuration packet</param>
		/// <returns>True if the configuration was handled, false otherwise</returns>
		bool TryApplyConfig(GameObject go, BuildingConfigPacket packet);
	}
}
