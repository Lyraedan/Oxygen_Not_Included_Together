#if DEBUG
using System.Collections.Generic;
using System.Linq;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Core;

namespace ONI_Together.DebugTools.UnitTests;

public static class OrderedReliableReconnectTeardownTests
{
	[UnitTest(name: "Ordered reliable drain abandons pending pages after reconnect teardown", category: "Networking")]
	public static UnitTestResult TeardownAbandonsPendingPages()
	{
		var context = new DispatchContext(42, true);
		var applied = new List<int>();
		OrderedReliableChannel.ResetSessionState();
		try
		{
			OrderedReliableReceiveBuffer buffer =
				OrderedReliableChannel.GetOrCreateIncoming(context);
			if (buffer.Accept(2, System.BitConverter.GetBytes(2), _ => { })
			    != OrderedReliableAcceptResult.Buffered)
				return UnitTestResult.Fail("Could not stage the pending old-session page");

			OrderedReliableAcceptResult result = buffer.Accept(
				1,
				System.BitConverter.GetBytes(1),
				payload =>
				{
					applied.Add(System.BitConverter.ToInt32(payload, 0));
					OrderedReliableChannel.DropIncoming(context.SenderId);
				},
				() => OrderedReliableChannel.IsCurrentIncomingForTests(context, buffer));
			return result == OrderedReliableAcceptResult.Abandoned
			       && applied.SequenceEqual(new[] { 1 })
				? UnitTestResult.Pass("Reconnect teardown stops the detached ordered drain without overflow")
				: UnitTestResult.Fail("A removed old-session buffer drained or terminated its pending page");
		}
		finally
		{
			OrderedReliableChannel.ResetSessionState();
		}
	}
}
#endif
