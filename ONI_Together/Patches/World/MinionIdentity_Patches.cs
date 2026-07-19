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
		private static int _applyDepth;

		internal static bool IsApplyingPacket => _applyDepth > 0;

		internal static void RunWithPacketGuard(System.Action action)
		{
			_applyDepth++;
			try
			{
				action();
			}
			finally
			{
				_applyDepth--;
			}
		}

		internal static void ResetPacketGuardForTests() => _applyDepth = 0;

		public static void ApplyPacketName(MinionIdentity nameable, string name)
		{
			using var _ = Profiler.Scope();

			RunWithPacketGuard(() => nameable.SetName(name));
		}

		[HarmonyPatch(typeof(MinionIdentity), nameof(MinionIdentity.SetName))]
		public class MinionIdentity_SetName_Patch
		{
			public static void Postfix(MinionIdentity __instance, string name)
			{
				using var _ = Profiler.Scope();

				if (MultiplayerSession.NotInSession)
					return;

				if (IsApplyingPacket)
					return;
				PacketSender.SendToAllOtherPeers(new MinionIdentitySetNamePacket(__instance.GetNetId(), name));
			}
		}
	}
}
