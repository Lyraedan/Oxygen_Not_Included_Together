using System;
using System.Collections.Generic;
using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using Shared;
using Shared.Profiling;

namespace ONI_Together.Patches.World.SideScreen
{
	[HarmonyPatch(typeof(SpiceGrinderWorkable), nameof(SpiceGrinderWorkable.SetSelectedOption))]
	public static class SpiceGrinderWorkable_SetSelectedOption_Patch
	{
		internal const string ConfigKey = "SpiceGrinderOption";
		internal static readonly int ConfigHash = NetworkingHash.ForConfigKey(ConfigKey);

		public static void Postfix(SpiceGrinderWorkable __instance, IConfigurableConsumerOption option)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket || !MultiplayerSession.InSession || option == null)
				return;

			string optionId = option.GetID().Name;
			if (string.IsNullOrEmpty(optionId))
				return;

			var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(__instance.gameObject),
				ConfigHash = ConfigHash,
				ConfigType = BuildingConfigType.String,
				StringValue = optionId
			};

			if (MultiplayerSession.IsHost)
				PacketSender.SendToAllClients(packet);
			else
				PacketSender.SendToHost(packet);
		}

		internal static bool ShouldApplyOption(string currentId, string requestedId)
		{
			return !string.IsNullOrEmpty(requestedId) &&
			       !string.Equals(currentId, requestedId, StringComparison.Ordinal);
		}

		internal static int FindOptionIndex(IReadOnlyList<string> optionIds, string requestedId)
		{
			if (optionIds == null || string.IsNullOrEmpty(requestedId))
				return -1;

			for (int i = 0; i < optionIds.Count; i++)
			{
				if (string.Equals(optionIds[i], requestedId, StringComparison.Ordinal))
					return i;
			}

			return -1;
		}
	}
}
