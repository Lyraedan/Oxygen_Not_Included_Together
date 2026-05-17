using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Buildings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	internal class MinionIdentity_Patches
	{
		static bool ApplyingPacket = false;
		public static void ApplyPacketName(MinionIdentity nameable, string name)
		{
			using var _ = Profiler.Scope();

			ApplyingPacket = true;
			nameable.SetName(name);
			ApplyingPacket = false;
		}

		[HarmonyPatch(typeof(MinionIdentity), nameof(MinionIdentity.SetName))]
		public class MinionIdentity_SetName_Patch
		{
			public static void Postfix(MinionIdentity __instance, string name)
			{
				using var _ = Profiler.Scope();

				if (MultiplayerSession.NotInSession)
					return;

				if (ApplyingPacket)
					return;
				PacketSender.SendToAllOtherPeers(new MinionIdentitySetNamePacket(__instance.GetNetId(), name));
			}
		}
	}
}
