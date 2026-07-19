#if DEBUG
using System.IO;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SaveRecoveryTests
	{
		[UnitTest(name: "Save transfer restart requires the active transfer token", category: "Networking")]
		public static UnitTestResult SaveRestartRequiresActiveTransferToken()
		{
			var player = new MultiplayerPlayer(8123);
			if (!player.TryBeginSaveTransfer(out long initialGeneration)
			    || !player.TrySetSaveTransferToken(initialGeneration, "active-transfer"))
			{
				return UnitTestResult.Fail("Could not arrange an authenticated save transfer");
			}

			if (player.TryRestartSaveTransfer("wrong-transfer", out _)
			    || !player.TryRestartSaveTransfer("active-transfer", out long restartGeneration)
			    || restartGeneration <= initialGeneration
			    || player.TryRestartSaveTransfer("active-transfer", out _))
			{
				return UnitTestResult.Fail(
					"A stale, mismatched, or replayed save restart was accepted");
			}

			return UnitTestResult.Pass(
				"Only the active transfer token can advance the save generation once");
		}

		[UnitTest(name: "Save restart request preserves its sender-bound transfer token", category: "Networking")]
		public static UnitTestResult SaveRestartRequestPreservesTransferToken()
		{
			SaveFileRequestPacket request = SaveFileRequestPacket.CreateRestart(
				requester: 8123, transferId: "active-transfer");
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				request.Serialize(writer);
			stream.Position = 0;
			var received = new SaveFileRequestPacket();
			using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
				received.Deserialize(reader);

			return received.Requester == 8123
			       && received.RestartTransferId == "active-transfer"
				? UnitTestResult.Pass("Save restart token survives the authenticated request wire")
				: UnitTestResult.Fail("Save restart request lost its requester or transfer token");
		}

		[UnitTest(name: "Downloaded save path is generation-isolated and temporary", category: "Networking")]
		public static UnitTestResult DownloadedSaveUsesIsolatedTemporaryPath()
		{
			string tempRoot = Path.Combine(Path.GetTempPath(), "oni-together-path-test");
			string first = SaveHelper.GetMultiplayerSnapshotPath(
				tempRoot, hostId: 44, snapshotGeneration: 7, name: "Colony.sav");
			string second = SaveHelper.GetMultiplayerSnapshotPath(
				tempRoot, hostId: 44, snapshotGeneration: 8, name: "Colony.sav");
			string normalSave = Path.Combine(tempRoot, "Colony", "Colony.sav");

			if (first == second || first == normalSave
			    || !first.StartsWith(
				    Path.Combine(tempRoot, "ONI_Together", "MultiplayerSnapshots", "44", "7"),
				    System.StringComparison.Ordinal))
			{
				return UnitTestResult.Fail(
					"Downloaded host save can collide with a normal save slot or another generation");
			}

			return UnitTestResult.Pass(
				"Downloaded host saves are isolated by host and snapshot generation");
		}
	}
}
#endif
