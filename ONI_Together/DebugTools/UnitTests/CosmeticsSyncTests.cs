using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Cosmetics;
using ONI_Together.Patches.DLC.Cosmetics;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class CosmeticsSyncTests
	{
		[UnitTest(name: "Cosmetic writes enforce verified request authority", category: "Sync")]
		public static UnitTestResult AuthorityMarkers()
		{
			if (new OutfitWriteRequestPacket() is not IClientRelayable ||
			    new OutfitStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Outfit packet authority marker is missing");

			var direct = new DispatchContext(13, false);
			DispatchContext verified = direct.AsVerifiedHostBroadcast();
			if (OutfitWriteRequestPacket.ShouldAccept(true, direct, true) ||
			    OutfitWriteRequestPacket.ShouldAccept(true, verified, false) ||
			    !OutfitWriteRequestPacket.ShouldAccept(true, verified, true))
				return UnitTestResult.Fail("Outfit request provenance gate is incorrect");
			if (!OutfitStatePacket.ShouldApply(false, true) || OutfitStatePacket.ShouldApply(true, true) ||
			    OutfitStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Outfit state authority gate is incorrect");
			return UnitTestResult.Pass("Outfit requests and states enforce transport authority");
		}

		[UnitTest(name: "Cosmetic outfit targets roundtrip without ambiguity", category: "Sync")]
		public static UnitTestResult TargetRoundtrip()
		{
			var template = new OutfitWriteRequestPacket(new OutfitStateData
			{
				TargetKind = OutfitTargetKind.Template,
				OutfitId = "Cold Weather",
				OutfitType = ClothingOutfitUtility.OutfitType.Clothing,
				ItemIds = new List<string> { "hat_warm", "top_warm" }
			});
			OutfitWriteRequestPacket templateCopy = Roundtrip(template, new OutfitWriteRequestPacket());
			if (templateCopy.Data.TargetKind != OutfitTargetKind.Template ||
			    templateCopy.Data.OutfitId != "Cold Weather" || templateCopy.Data.TargetNetId != 0 ||
			    templateCopy.Data.ItemIds.Count != 2)
				return UnitTestResult.Fail("Template outfit target did not roundtrip");

			var minion = new OutfitStatePacket(new OutfitStateData
			{
				TargetKind = OutfitTargetKind.Minion,
				TargetNetId = 88,
				OutfitType = ClothingOutfitUtility.OutfitType.AtmoSuit
			});
			OutfitStatePacket minionCopy = Roundtrip(minion, new OutfitStatePacket());
			if (minionCopy.Data.TargetKind != OutfitTargetKind.Minion || minionCopy.Data.TargetNetId != 88 ||
			    minionCopy.Data.OutfitId != "")
				return UnitTestResult.Fail("Minion outfit target did not roundtrip");
			return UnitTestResult.Pass("Template IDs and minion NetIDs are unambiguous");
		}

		[UnitTest(name: "Cosmetic outfit lists are bounded and idempotent", category: "Sync")]
		public static UnitTestResult BoundsAndIdempotence()
		{
			var data = new OutfitStateData
			{
				TargetKind = OutfitTargetKind.Template,
				OutfitId = "Uniform",
				OutfitType = ClothingOutfitUtility.OutfitType.Clothing,
				ItemIds = new List<string> { "hat", "shirt" }
			};
			if (!data.IsWireValid() ||
			    !OutfitWriteSync.ItemsEqual(new[] { "shirt", "hat" }, data.ItemIds))
				return UnitTestResult.Fail("Equivalent absolute outfit state was not recognized");
			data.ItemIds.Add("hat");
			if (data.IsWireValid())
				return UnitTestResult.Fail("Duplicate outfit item was accepted");
			data.ItemIds = new List<string>();
			for (int i = 0; i <= OutfitStateData.MaxItemCount; i++)
				data.ItemIds.Add("item_" + i);
			if (data.IsWireValid())
				return UnitTestResult.Fail("Oversized outfit item list was accepted");
			return UnitTestResult.Pass("Outfit states are bounded, unique and idempotent");
		}

		[UnitTest(name: "Cosmetic template rename and delete are authoritative", category: "Sync")]
		public static UnitTestResult TemplateMutationRoundtrip()
		{
			var rename = new OutfitTemplateMutationRequestPacket(new OutfitTemplateMutationData
			{
				Kind = OutfitTemplateMutationKind.Rename,
				OutfitType = ClothingOutfitUtility.OutfitType.Clothing,
				OutfitId = "Old Uniform",
				NewOutfitId = "New Uniform"
			});
			OutfitTemplateMutationRequestPacket copy = Roundtrip(
				rename, new OutfitTemplateMutationRequestPacket());
			if (copy is not IClientRelayable || copy.Data.Kind != OutfitTemplateMutationKind.Rename ||
			    copy.Data.OutfitId != "Old Uniform" || copy.Data.NewOutfitId != "New Uniform")
				return UnitTestResult.Fail("Template rename request did not roundtrip");

			var state = new OutfitTemplateMutationStatePacket(copy.Data);
			if (state is not IHostOnlyPacket)
				return UnitTestResult.Fail("Template mutation state is not host-only");
			copy.Data.NewOutfitId = "";
			if (copy.Data.IsWireValid())
				return UnitTestResult.Fail("Rename without a destination was accepted");
			return UnitTestResult.Pass("Template rename/delete use bounded request and host state packets");
		}

		private static T Roundtrip<T>(T input, T output) where T : IPacket
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			return output;
		}
	}
}
