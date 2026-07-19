using System.IO;
using System.Collections.Generic;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.Social;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests;

public static class ImmigrationAuthorityTests
{
	[UnitTest(name: "Immigration: selection wire carries only authoritative index", category: "Networking")]
	public static UnitTestResult SelectionWireCarriesOnlyIndex()
	{
		var packet = new ImmigrantSelectionPacket
		{
			PrintingPodWorldIndex = 3,
			SelectedOptionIndex = 2
		};

		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			packet.Serialize(writer);
		if (stream.Length != sizeof(int) * 2)
			return UnitTestResult.Fail($"Selection request serialized {stream.Length} bytes instead of two indexes");

		stream.Position = 0;
		var copy = new ImmigrantSelectionPacket();
		using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			copy.Deserialize(reader);

		return copy.PrintingPodWorldIndex == 3 && copy.SelectedOptionIndex == 2
			? UnitTestResult.Pass("Selection request contains only world and host-option indexes")
			: UnitTestResult.Fail("Selection indexes did not roundtrip");
	}

	[UnitTest(name: "Immigration: host validates selection request", category: "Networking")]
	public static UnitTestResult HostValidatesSelectionRequest()
	{
		var client = new DispatchContext(42, senderIsHost: false);
		var host = new DispatchContext(1, senderIsHost: true);

		if (!ImmigrantSelectionPacket.IsSelectionRequestValid(client, protocolVerified: true,
			optionsLocked: true, optionCount: 3, worldIndex: 0, optionIndex: 2))
			return UnitTestResult.Fail("Valid client selection was rejected");
		if (ImmigrantSelectionPacket.IsSelectionRequestValid(host, true, true, 3, 0, 2))
			return UnitTestResult.Fail("Host-origin traffic was accepted as a client selection request");
		if (ImmigrantSelectionPacket.IsSelectionRequestValid(client, false, true, 3, 0, 2))
			return UnitTestResult.Fail("Unverified client selection was accepted");
		if (ImmigrantSelectionPacket.IsSelectionRequestValid(client, true, false, 3, 0, 2))
			return UnitTestResult.Fail("Selection without host options was accepted");
		if (ImmigrantSelectionPacket.IsSelectionRequestValid(client, true, true, 3, 0, 3))
			return UnitTestResult.Fail("Out-of-range option index was accepted");
		if (ImmigrantSelectionPacket.IsSelectionRequestValid(client, true, true, 3, -2, 0))
			return UnitTestResult.Fail("Host-only close notification was accepted as a client request");

		return UnitTestResult.Pass("Host accepts only verified selections in its current option set");
	}

	[UnitTest(name: "Immigration: options originate only from host", category: "Networking")]
	public static UnitTestResult OptionsOriginateOnlyFromHost()
	{
		var packet = new ImmigrantOptionsPacket();
		if (packet is not IHostOnlyPacket)
			return UnitTestResult.Fail("Immigrant options are not host-only");
		if (PacketHandler.CanDispatchPacket(packet, new DispatchContext(42, false), localIsHost: true))
			return UnitTestResult.Fail("Host accepted client-generated immigrant options");

		return UnitTestResult.Pass("Client-generated immigrant options are rejected before deserialization");
	}

	[UnitTest(name: "Immigration: option count is bounded", category: "Networking")]
	public static UnitTestResult OptionCountIsBounded()
	{
		var packet = new ImmigrantOptionsPacket
		{
			Options = new List<ImmigrantOptionEntry>(new ImmigrantOptionEntry[ImmigrantOptionsPacket.MaxOptions + 1])
		};
		try
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			packet.Serialize(writer);
			return UnitTestResult.Fail("Oversized immigrant option list was serialized");
		}
		catch (InvalidDataException)
		{
			return UnitTestResult.Pass("Immigrant option count is bounded before allocation");
		}
	}
}
