using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles MissileLauncher (Meteor Blaster) buildings.
	/// </summary>
	public class MissileLauncherHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			"MissileLauncherAmmo".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			if (packet.ConfigHash != "MissileLauncherAmmo".GetHashCode()) return false;

			var missileLauncher = go.GetSMI<MissileLauncher.Instance>();
			if (missileLauncher == null) return false;

			if (packet.ConfigType != BuildingConfigType.String || string.IsNullOrEmpty(packet.StringValue))
				return false;

			Tag ammoTag = new Tag(packet.StringValue);
			bool allowed = packet.Value > 0.5f;
			missileLauncher.ChangeAmmunition(ammoTag, allowed);

			DebugConsole.Log($"[MissileLauncherHandler] Set ammo {packet.StringValue}={allowed} on {go.name}");
			return true;
		}
	}
}
