using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World.Buildings;
using ONI_Together.Scripts.Buildings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Patches.World.Buildings
{
	internal class Operational_Patch
	{
		///Server sends state updates to clients
		///

		[HarmonyPatch(typeof(Operational), nameof(Operational.IsOperational), MethodType.Setter)]
		public class Operational_IsOperational_Setter_Patch
		{
			public static void Postfix(Operational __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
					return;
				PacketSender.SendToAllClients(new OperationalStatePacket(__instance));
			}
		}
		[HarmonyPatch(typeof(Operational), nameof(Operational.IsActive), MethodType.Setter)]
		public class Operational_IsOperational_IsActive_Patch
		{
			public static void Postfix(Operational __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
					return;
				PacketSender.SendToAllClients(new OperationalStatePacket(__instance));
			}
		}
		[HarmonyPatch(typeof(Operational), nameof(Operational.IsFunctional), MethodType.Setter)]
		public class Operational_IsOperational_IsFunctional_Patch
		{
			public static void Postfix(Operational __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
					return;
				PacketSender.SendToAllClients(new OperationalStatePacket(__instance));
			}
		}



		/// <summary>
		/// Clients receive their states from the server
		/// </summary>
		///

		[HarmonyPatch(typeof(Operational), nameof(Operational.IsOperational), MethodType.Getter)]
        public class Operational_IsOperational_Patch
        {
            public static bool Prefix(Operational __instance, ref bool __result)
            {
	            using var _ = Profiler.Scope();

                if (!MultiplayerSession.IsClient)
                    return true;

                if(__instance.TryGetComponent<ClientReceiver_Operational>(out var wrap))
                {
                    __result = wrap.IsOperational;
					return false;
                }
                return true;
            }
        }


        [HarmonyPatch(typeof(Operational), nameof(Operational.IsActive), MethodType.Getter)]
		public class Operational_IsActive_Patch
        {
			public static bool Prefix(Operational __instance, ref bool __result)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsClient)
					return true;

				if (__instance.TryGetComponent<ClientReceiver_Operational>(out var wrap))
				{
					__result = wrap.IsActive;
					return false;
				}
				return true;
			}
		}
		[HarmonyPatch(typeof(Operational), nameof(Operational.IsFunctional), MethodType.Getter)]
		public class Operational_IsFunctional_Patch
		{
			public static bool Prefix(Operational __instance, ref bool __result)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsClient)
					return true;

				if (__instance.TryGetComponent<ClientReceiver_Operational>(out var wrap))
				{
					__result = wrap.IsActive;
					return false;
				}
				return true;
			}
		}
	}
}
