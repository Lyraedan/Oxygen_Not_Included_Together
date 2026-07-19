using UnityEngine;
using ONI_Together.DebugTools;
using Shared;
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
			NetworkingHash.ForConfigKey("MissileLauncherAmmo"),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			if (packet.ConfigHash != NetworkingHash.ForConfigKey("MissileLauncherAmmo")) return false;

			var missileLauncher = go.GetSMI<MissileLauncher.Instance>();
			if (missileLauncher == null) return false;

			if (packet.ConfigType != BuildingConfigType.String || string.IsNullOrEmpty(packet.StringValue))
				return false;
			if (!BuildingConfigPacket.IsBooleanValue(packet.Value))
				return false;

			Tag ammoTag = new Tag(packet.StringValue);
			if (!missileLauncher.GetValidAmmunitionTags().Contains(ammoTag))
				return false;
			bool allowed = packet.Value > 0.5f;
			missileLauncher.ChangeAmmunition(ammoTag, allowed);
			packet.Value = missileLauncher.AmmunitionIsAllowed(ammoTag) ? 1f : 0f;

			//DebugConsole.Log($"[MissileLauncherHandler] Set ammo {packet.StringValue}={allowed} on {go.name}");
			return true;
		}
	}
}
