using HarmonyLib;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Social;
using Shared.Profiling;

namespace ONI_Together.Patches.Duplicant
{
	internal class ThoughtGraph_Patches
	{
		[HarmonyPatch(typeof(ThoughtGraph.Instance), nameof(ThoughtGraph.Instance.AddThought))]
		public static class ThoughtGraph_AddThought_Patch
		{
			public static bool Prefix()
			{
				using var _ = Profiler.Scope();

				return !MultiplayerSession.IsClient;
			}
		}

		[HarmonyPatch(typeof(ThoughtGraph.Instance), nameof(ThoughtGraph.Instance.RemoveThought))]
		public static class ThoughtGraph_RemoveThought_Patch
		{
			public static bool Prefix()
			{
				using var _ = Profiler.Scope();

				return !MultiplayerSession.IsClient;
			}
		}

		[HarmonyPatch(typeof(ThoughtGraph.Instance), nameof(ThoughtGraph.Instance.CreateBubble))]
		public static class ThoughtGraph_CreateBubble_Patch
		{
			public static bool Prefix()
			{
				using var _ = Profiler.Scope();

				return !MultiplayerSession.IsClient;
			}

			public static void Postfix(ThoughtGraph.Instance __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsHost)
					return;

				var thought = __instance.currentThought;
				if (thought == null)
					return;

				var go = __instance.master.gameObject;
				if (go.IsNullOrDestroyed())
					return;

				if (!go.TryGetComponent<NetworkIdentity>(out var identity))
					return;
				
				var packet = new ThoughtBubblePacket
				{
					NetId = identity.NetId,
					IsVisible = true,
					IsConvo = thought.modeSprite != null,
					HoverText = (string)thought.hoverText ?? string.Empty,
					BubbleSpriteName = thought.bubbleSprite?.name ?? string.Empty,
					TopicSpriteName = thought.sprite?.name ?? string.Empty,
					ModeSpriteName = thought.modeSprite?.name ?? string.Empty
				};

				PacketSender.SendToAllClients(packet, PacketSendMode.Reliable);
			}
		}

		[HarmonyPatch(typeof(ThoughtGraph.Instance), nameof(ThoughtGraph.Instance.DestroyBubble))]
		public static class ThoughtGraph_DestroyBubble_Patch
		{
			public static bool Prefix()
			{
				using var _ = Profiler.Scope();

				return !MultiplayerSession.IsClient;
			}

			public static void Postfix(ThoughtGraph.Instance __instance)
			{
				using var _ = Profiler.Scope();

				if (!MultiplayerSession.IsHost)
					return;

				var go = __instance.master.gameObject;
				if (go.IsNullOrDestroyed())
					return;

				if (!go.TryGetComponent<NetworkIdentity>(out var identity))
					return;

				var packet = new ThoughtBubblePacket
				{
					NetId = identity.NetId,
					IsVisible = false
				};

				PacketSender.SendToAllClients(packet, PacketSendMode.Reliable);
			}
		}
	}
}
