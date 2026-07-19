using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DuplicantActions;
using ONI_Together.Patches.Duplicant;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests;

public static class SkillResumeAuthorityTests
{
	[UnitTest(name: "Skill resume: mastery request requires verified relay", category: "Networking")]
	public static UnitTestResult MasteryRequestRequiresVerifiedRelay()
	{
		var packet = new SkillMasteryRequestPacket();
		var verifiedRelay = new DispatchContext(42, senderIsHost: false).AsVerifiedHostBroadcast();
		var directClient = new DispatchContext(42, senderIsHost: false);
		var host = new DispatchContext(1, senderIsHost: true);

		if (packet is not IClientRelayable)
			return UnitTestResult.Fail("Mastery request is not client-relayable");
		if (!SkillMasteryRequestPacket.ShouldAccept(localIsHost: true, verifiedRelay, protocolVerified: true))
			return UnitTestResult.Fail("Verified client relay was rejected");
		if (SkillMasteryRequestPacket.ShouldAccept(true, directClient, true) ||
		    SkillMasteryRequestPacket.ShouldAccept(true, verifiedRelay, false) ||
		    SkillMasteryRequestPacket.ShouldAccept(true, host, true) ||
		    SkillMasteryRequestPacket.ShouldAccept(false, verifiedRelay, true))
			return UnitTestResult.Fail("Untrusted mastery request was accepted");

		return UnitTestResult.Pass("Only verified client relays reach host mastery validation");
	}

	[UnitTest(name: "Skill resume: hat request requires verified relay", category: "Networking")]
	public static UnitTestResult HatRequestRequiresVerifiedRelay()
	{
		var packet = new SkillHatRequestPacket();
		var verified = new DispatchContext(42, false).AsVerifiedHostBroadcast();
		var direct = new DispatchContext(42, false);
		if (packet is not IClientRelayable
		    || !SkillHatRequestPacket.ShouldAccept(true, verified, true)
		    || SkillHatRequestPacket.ShouldAccept(true, direct, true)
		    || SkillHatRequestPacket.ShouldAccept(true, verified, false)
		    || SkillHatRequestPacket.ShouldAccept(false, verified, true))
			return UnitTestResult.Fail("Hat request authority gate is incorrect");

		return UnitTestResult.Pass("Only verified client relays can request host hat changes");
	}

	[UnitTest(name: "Skill resume: mastery request wire is bounded", category: "Networking")]
	public static UnitTestResult MasteryRequestWireIsBounded()
	{
		var source = new SkillMasteryRequestPacket { NetId = -17, SkillId = "Mining1" };
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			source.Serialize(writer);
		stream.Position = 0;
		var copy = new SkillMasteryRequestPacket();
		using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			copy.Deserialize(reader);
		if (copy.NetId != -17 || copy.SkillId != "Mining1")
			return UnitTestResult.Fail("Signed non-zero NetId or skill ID did not roundtrip");

		if (!SerializeRequestFails(new SkillMasteryRequestPacket { NetId = 0, SkillId = "Mining1" }) ||
		    !SerializeRequestFails(new SkillMasteryRequestPacket { NetId = 4, SkillId = "" }) ||
		    !SerializeRequestFails(new SkillMasteryRequestPacket
		    {
			    NetId = 4,
			    SkillId = new string('x', SkillMasteryRequestPacket.MaxSkillIdLength + 1)
		    }))
			return UnitTestResult.Fail("Invalid mastery request was serialized");

		return UnitTestResult.Pass("Mastery requests preserve signed IDs and reject invalid payloads");
	}

	[UnitTest(name: "Skill resume: host snapshot is bounded absolute state", category: "Networking")]
	public static UnitTestResult HostSnapshotIsBoundedAbsoluteState()
	{
		SkillResumeStatePacket source = CreateSnapshotTestPacket();

		if (source is not IHostOnlyPacket)
			return UnitTestResult.Fail("Skill resume snapshot is not host-only");
		if (!SkillResumeStatePacket.ShouldApply(localIsHost: false, senderIsHost: true) ||
		    SkillResumeStatePacket.ShouldApply(false, false) ||
		    SkillResumeStatePacket.ShouldApply(true, true))
			return UnitTestResult.Fail("Skill resume snapshot authority gate is incorrect");
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			source.Serialize(writer);
		stream.Position = 0;
		var copy = new SkillResumeStatePacket();
		using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			copy.Deserialize(reader);

		SkillResumeStateData data = copy.Data;
		if (data.NetId != -29 || data.Revision != 7 || data.TotalExperience != 0f ||
		    data.AvailableSkillPoints != 0 || data.MasteredSkillIds.Count != 1 ||
		    data.GrantedSkillIds.Count != 1 || data.Aptitudes.Count != 1 ||
		    data.OwnedHats.Count != 1 || data.CurrentHat != "hat_mining" || data.TargetHat != "")
			return UnitTestResult.Fail("Absolute resume snapshot did not roundtrip");
		if (!SkillResumeSync.IsNewerRevision(6, 7) || SkillResumeSync.IsNewerRevision(7, 7) ||
		    SkillResumeSync.IsNewerRevision(8, 7) || SkillResumeSync.IsNewerRevision(0, 0))
			return UnitTestResult.Fail("Snapshot revision ordering is not idempotent");

		data.MasteredSkillIds.Add("Swimming");
		if (!SerializeStateFails(copy))
			return UnitTestResult.Fail("Duplicate mastery was serialized");
		if (!DeserializeOversizedStateCountFails())
			return UnitTestResult.Fail("Oversized mastery count was deserialized");

		return UnitTestResult.Pass("Host-only snapshot roundtrips bounded progression and rejects replay");
	}

	private static SkillResumeStatePacket CreateSnapshotTestPacket()
		=> new(new SkillResumeStateData
		{
			NetId = -29,
			Revision = 7,
			TotalExperience = 0f,
			AvailableSkillPoints = 0,
			MasteredSkillIds = new List<string> { "Swimming" },
			GrantedSkillIds = new List<string> { "Swimming" },
			Aptitudes = new List<SkillResumeAptitudeData>
			{
				new() { SkillGroupHash = 101, Amount = 1.5f }
			},
			OwnedHats = new List<SkillResumeHatData>
			{
				new() { HatId = "hat_mining", IsUnlocked = true }
			},
			CurrentHat = "hat_mining",
			TargetHat = ""
		});

	[UnitTest(name: "Skill resume: all progression mutations share client authority gate", category: "Networking")]
	public static UnitTestResult AllProgressionMutationsShareClientAuthorityGate()
	{
		string[] methods =
		{
			nameof(MinionResume.MasterSkill), nameof(MinionResume.UnmasterSkill),
			nameof(MinionResume.GrantSkill), nameof(MinionResume.UngrantSkill)
		};
		foreach (string method in methods)
		{
			if (AccessTools.DeclaredMethod(typeof(MinionResume), method, new[] { typeof(string) }) == null)
				return UnitTestResult.Fail($"Build 740622 method is missing: MinionResume.{method}(string)");
		}
		System.Type[] patches =
		{
			typeof(MinionResumeMasterSkillPatch), typeof(MinionResumeUnmasterSkillPatch),
			typeof(MinionResumeGrantSkillPatch), typeof(MinionResumeUngrantSkillPatch)
		};
		foreach (System.Type patch in patches)
		{
			if (patch.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0)
				return UnitTestResult.Fail($"Progression mutation is not patched: {patch.Name}");
		}
		System.Type[] hatPatches =
		{
			typeof(MinionResumeSetHatsPatch), typeof(MinionResumeApplyTargetHatPatch),
			typeof(MinionResumeCreateHatChangeChorePatch)
		};
		foreach (System.Type patch in hatPatches)
		{
			if (patch.GetCustomAttributes(typeof(HarmonyPatch), inherit: false).Length == 0)
				return UnitTestResult.Fail($"Hat mutation is not patched: {patch.Name}");
		}

		if (SkillResumeSync.ShouldRunLocally(inSession: true, isHost: false, applyingSnapshot: false) ||
		    !SkillResumeSync.ShouldRunLocally(true, true, false) ||
		    !SkillResumeSync.ShouldRunLocally(true, false, true) ||
		    !SkillResumeSync.ShouldRunLocally(false, false, false))
			return UnitTestResult.Fail("Progression mutation authority gate is incorrect");
		if (!SkillResumeSync.ShouldSendMasteryRequest(true, false, false) ||
		    SkillResumeSync.ShouldSendMasteryRequest(true, true, false) ||
		    SkillResumeSync.ShouldSendMasteryRequest(true, false, true))
			return UnitTestResult.Fail("Client mastery request gate is incorrect");

		return UnitTestResult.Pass("Master, unmaster, grant, and ungrant use one host-authoritative gate");
	}

	[UnitTest(name: "Skill resume: hat request wire is bounded", category: "Networking")]
	public static UnitTestResult HatRequestWireIsBounded()
	{
		var source = new SkillHatRequestPacket { NetId = -19, TargetHat = "hat_mining" };
		using var stream = new MemoryStream();
		using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
			source.Serialize(writer);
		stream.Position = 0;
		var copy = new SkillHatRequestPacket();
		using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
			copy.Deserialize(reader);
		if (copy.NetId != -19 || copy.TargetHat != "hat_mining")
			return UnitTestResult.Fail("Hat request did not roundtrip");
		if (!SerializeHatRequestFails(new SkillHatRequestPacket { NetId = 0, TargetHat = "hat_mining" })
		    || !SerializeHatRequestFails(new SkillHatRequestPacket
		    {
			    NetId = 1,
			    TargetHat = new string('x', SkillResumeStateData.MaxIdLength + 1)
		    }))
			return UnitTestResult.Fail("Invalid hat request serialized successfully");

		return UnitTestResult.Pass("Hat requests preserve target intent and reject unbounded payloads");
	}

	private static bool SerializeRequestFails(SkillMasteryRequestPacket packet)
	{
		try
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			packet.Serialize(writer);
			return false;
		}
		catch (InvalidDataException)
		{
			return true;
		}
	}

	private static bool SerializeHatRequestFails(SkillHatRequestPacket packet)
	{
		try
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			packet.Serialize(writer);
			return false;
		}
		catch (InvalidDataException)
		{
			return true;
		}
	}

	private static bool SerializeStateFails(SkillResumeStatePacket packet)
	{
		try
		{
			using var stream = new MemoryStream();
			using var writer = new BinaryWriter(stream);
			packet.Serialize(writer);
			return false;
		}
		catch (InvalidDataException)
		{
			return true;
		}
	}

	private static bool DeserializeOversizedStateCountFails()
	{
		try
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
			{
				writer.Write(-1);
				writer.Write((ulong)1);
				writer.Write(0f);
				writer.Write(0);
				writer.Write(SkillResumeStateData.MaxSkillCount + 1);
			}
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			new SkillResumeStatePacket().Deserialize(reader);
			return false;
		}
		catch (InvalidDataException)
		{
			return true;
		}
	}
}
