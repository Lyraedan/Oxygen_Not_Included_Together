using HarmonyLib;
using ONI_MP.Networking;
using ONI_MP.Networking.Packets.World.Buildings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;
using ONI_MP.Networking.Packets.Architecture;

namespace ONI_MP.Patches.World.Buildings
{
	internal class UserNameablePatch
	{
		static bool ApplyingPacket = false;
		public static void ApplyPacketName(UserNameable nameable, string name)
		{
			using var _ = Profiler.Scope();

			ApplyingPacket = true;
			nameable.SetName(name);
			ApplyingPacket = false;
		}

		[HarmonyPatch(typeof(UserNameable), nameof(UserNameable.SetName))]
		public class UserNameable_SetName_Patch
		{
			public static void Postfix(UserNameable __instance, string name)
			{
				using var _ = Profiler.Scope();

				if (MultiplayerSession.NotInSession)
					return;

				if (ApplyingPacket)
					return;
				PacketSender.SendToAllOtherPeers(new UserNameableChangePacket(__instance.GetNetId(), name));
			}
		}
	}
}
