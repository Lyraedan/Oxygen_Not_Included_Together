using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.Patches.World
{
	[HarmonyPatch(typeof(DeathMonitor.Instance), nameof(DeathMonitor.Instance.ApplyDeath))]
	internal static class DuplicantDeathPatch
	{
		private static void Postfix(DeathMonitor.Instance __instance)
		{
			if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost
			    || !DuplicantDeathStatePacket.TryCreate(__instance, out var packet))
				return;
			PacketSender.SendToAllClients(packet, PacketSendMode.ReliableImmediate);
		}
	}
}
