using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Events;
using ONI_Together.Networking.Packets.World.Buildings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.Buildings
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
