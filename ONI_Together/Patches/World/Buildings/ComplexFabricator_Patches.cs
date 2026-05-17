using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;
using ONI_Together.Networking.Packets.World.Buildings;
using Shared.Profiling;
using System.Collections.Generic;
using UnityEngine;

namespace ONI_Together.Patches.World.Buildings
{
	internal class ComplexFabricator_Patches
	{
		private const float SEND_INTERVAL = 0.5f;
		private static readonly Dictionary<int, float> _nextSendTime = new();

		[HarmonyPatch(typeof(ComplexFabricatorWorkable), nameof(ComplexFabricatorWorkable.UpdateOrderProgress))]
		public class ComplexFabricatorWorkable_UpdateOrderProgress_Patch
		{
			public static void Postfix(ComplexFabricatorWorkable __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession || __instance.IsNullOrDestroyed())
					return;

				if (!__instance.TryGetComponent<ComplexFabricator>(out var fabricator) || fabricator == null || fabricator.IsNullOrDestroyed())
					return;

				int netId = fabricator.GetNetId();
				if (netId == 0)
					return;

				float now = Time.time;
				if (_nextSendTime.TryGetValue(netId, out float next) && now < next)
					return;

				_nextSendTime[netId] = now + SEND_INTERVAL;
				PacketSender.SendToAllClients(WorkableProgressPacket.CreateComplexFabricator(fabricator, showProgressBar: true), PacketSendMode.Unreliable);
			}
		}

		[HarmonyPatch(typeof(ComplexFabricator), nameof(ComplexFabricator.CancelWorkingOrder))]
		public class ComplexFabricator_CancelWorkingOrder_Patch
		{
			public static void Postfix(ComplexFabricator __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsHost || !MultiplayerSession.InSession || __instance.IsNullOrDestroyed())
					return;

				PacketSender.SendToAllClients(WorkableProgressPacket.CreateComplexFabricator(__instance, showProgressBar: false), PacketSendMode.ReliableImmediate);
			}
		}

		[HarmonyPatch(typeof(ComplexFabricator), nameof(ComplexFabricator.SpawnOrderProduct))]
		public class ComplexFabricator_SpawnOrderProduct_Patch
		{
			public static void Postfix(ComplexFabricator __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.InSession || !MultiplayerSession.IsHost)
					return;

				PacketSender.SendToAllClients(WorkableProgressPacket.CreateComplexFabricator(__instance, showProgressBar: false), PacketSendMode.ReliableImmediate);
				PacketSender.SendToAllClients(new ComplexFabricatorSpawnProductPacket(__instance));
			}
		}
	}
}
