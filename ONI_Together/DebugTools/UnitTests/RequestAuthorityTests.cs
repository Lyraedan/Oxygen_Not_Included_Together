using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;
using ONI_Together.Networking.Packets.World;
using System;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class RequestAuthorityTests
	{
		[UnitTest(name: "Host requests require matching non-host sender", category: "Networking")]
		public static UnitTestResult RequestSenderMatrix()
		{
			var checks = new (string Name, Func<ulong, DispatchContext, bool> Accepts)[]
			{
				(nameof(TcpFallbackRequestPacket), TcpFallbackRequestPacket.ShouldAccept),
				(nameof(ChunkAckPacket), ChunkAckPacket.ShouldAccept),
				(nameof(AnimResyncRequestPacket), AnimResyncRequestPacket.ShouldAccept),
				(nameof(EntityPositionRequestPacket), EntityPositionRequestPacket.ShouldAccept),
				(nameof(StructureStateRequestPacket), StructureStateRequestPacket.ShouldAccept),
				(nameof(SyncProgressPacket), SyncProgressPacket.ShouldAccept),
				(nameof(WorldDataRequestPacket), WorldDataRequestPacket.ShouldAccept),
			};
			var validClient = new DispatchContext(42, false);
			var otherClient = new DispatchContext(7, false);
			var host = new DispatchContext(42, true);

			foreach (var check in checks)
			{
				if (!check.Accepts(42, validClient))
					return UnitTestResult.Fail($"{check.Name} rejected its real client sender");
				if (check.Accepts(42, otherClient))
					return UnitTestResult.Fail($"{check.Name} accepted a spoofed payload sender");
				if (check.Accepts(42, host))
					return UnitTestResult.Fail($"{check.Name} accepted a host-role sender");
				if (check.Accepts(0, new DispatchContext(0, false)))
					return UnitTestResult.Fail($"{check.Name} accepted an invalid zero sender");
			}

			return UnitTestResult.Pass("All host-ingress request packets require their matching non-host sender");
		}
	}
}
