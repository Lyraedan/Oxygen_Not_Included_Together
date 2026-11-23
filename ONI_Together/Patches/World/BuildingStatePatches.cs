using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Handlers;
using Shared.Interfaces.Networking;
using Shared.Profiling;

namespace ONI_Together.Patches.World
{
	internal class BuildingStatePatches
	{
		[HarmonyPatch(typeof(BuildingHP), nameof(BuildingHP.DoDamage))]
		public static class BuildingHP_DoDamage_Patch
		{
			public static bool Prefix()
			{
				using var _ = Profiler.Scope();
				return !MultiplayerSession.IsClient || BuildingConfigPacket.IsApplyingPacket;
			}

			public static void Postfix(BuildingHP __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsHostInSession || __instance == null)
					return;

				var identity = __instance.gameObject.AddOrGet<NetworkIdentity>();
				identity.RegisterIdentity();
				if (identity.NetId == 0)
					return;

				PacketSender.SendToAllClients(new BuildingConfigPacket
				{
					NetId = identity.NetId,
					Cell = Grid.PosToCell(__instance.gameObject),
					ConfigHash = AuthoritativeStateHandler.HitPointsKey.GetHashCode(),
					Value = __instance.HitPoints
				}, PacketSendMode.ReliableImmediate);
			}
		}

		[HarmonyPatch(typeof(EmptyConduitWorkable), nameof(EmptyConduitWorkable.MarkForEmptying))]
		public static class EmptyConduitWorkable_MarkForEmptying_Patch
		{
			public static void Postfix(EmptyConduitWorkable __instance)
			{
				using var _ = Profiler.Scope();
				Send(__instance, true);
			}

			internal static void Send(EmptyConduitWorkable workable, bool marked)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.InActiveSession || BuildingConfigPacket.IsApplyingPacket || workable == null)
					return;

				var identity = workable.gameObject.AddOrGet<NetworkIdentity>();
				identity.RegisterIdentity();
				if (identity.NetId == 0)
					return;

				PacketSender.SendToAllOtherPeers(new BuildingConfigPacket
				{
					NetId = identity.NetId,
					Cell = Grid.PosToCell(workable.gameObject),
					ConfigHash = AuthoritativeStateHandler.EmptyConduitKey.GetHashCode(),
					Value = marked ? 1f : 0f,
					ConfigType = BuildingConfigType.Boolean
				});
			}
		}

		[HarmonyPatch(typeof(EmptyConduitWorkable), nameof(EmptyConduitWorkable.CancelEmptying))]
		public static class EmptyConduitWorkable_CancelEmptying_Patch
		{
			public static void Postfix(EmptyConduitWorkable __instance)
			{
				using var _ = Profiler.Scope();
				EmptyConduitWorkable_MarkForEmptying_Patch.Send(__instance, false);
			}
		}
	}
}
