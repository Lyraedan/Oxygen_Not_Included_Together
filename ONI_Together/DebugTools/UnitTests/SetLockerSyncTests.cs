using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SetLockerSyncTests
	{
		[UnitTest(name: "Set locker Harmony targets match game signatures", category: "Sync")]
		public static UnitTestResult HarmonyTargets()
		{
			if (!Matches(typeof(SetLocker), nameof(SetLocker.ChooseContents), typeof(void)) ||
			    !Matches(typeof(SetLocker), nameof(SetLocker.DropContents), typeof(void)) ||
			    !Matches(typeof(SetLocker), "CompleteChore", typeof(void)) ||
			    !Matches(typeof(SetLocker), nameof(SetLocker.OnSidescreenButtonPressed), typeof(void)))
				return UnitTestResult.Fail("A SetLocker Harmony target signature changed");
			return UnitTestResult.Pass("SetLocker Harmony targets match the game assembly");
		}

		[UnitTest(name: "Set locker request and state enforce authority", category: "Sync")]
		public static UnitTestResult Authority()
		{
			if (new SetLockerRequestPacket() is not IClientRelayable ||
			    new SetLockerStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Set locker packet authority markers are missing");
			var directClient = new DispatchContext(7, false);
			DispatchContext verified = directClient.AsVerifiedHostBroadcast();
			if (SetLockerRequestPacket.ShouldAccept(true, directClient) ||
			    !SetLockerRequestPacket.ShouldAccept(true, verified) ||
			    SetLockerRequestPacket.ShouldAccept(false, verified) ||
			    !SetLockerStatePacket.ShouldApply(false, true) ||
			    SetLockerStatePacket.ShouldApply(true, true) ||
			    SetLockerStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Set locker authority gate is incorrect");
			return UnitTestResult.Pass("Set locker requests require verified clients and states require host");
		}

		[UnitTest(name: "Set locker state roundtrips bounded absolute contents", category: "Sync")]
		public static UnitTestResult StateRoundtrip()
		{
			var input = new SetLockerStatePacket
			{
				TargetNetId = -919,
				PendingRummage = false,
				Used = true,
				Phase = SetLockerPhase.Off,
				Contents = new List<string> { "AtmoSuit", "JetSuit" }
			};
			var output = Roundtrip(input);
			if (output.TargetNetId != -919 || !output.Used || output.PendingRummage ||
			    output.Phase != SetLockerPhase.Off || output.Contents.Count != 2 ||
			    output.Contents[1] != "JetSuit")
				return UnitTestResult.Fail("Set locker state did not roundtrip");

			input.PendingRummage = true;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Used locker accepted a pending rummage chore");
			return UnitTestResult.Pass("Set locker state is bounded and absolute");
		}

		[UnitTest(name: "Set locker random rewards run only on host", category: "Sync")]
		public static UnitTestResult HostGameplay()
		{
			if (!SetLockerSync.ShouldRunGameplay(false, false, false) ||
			    !SetLockerSync.ShouldRunGameplay(true, true, false) ||
			    SetLockerSync.ShouldRunGameplay(true, false, false) ||
			    !SetLockerSync.ShouldRunGameplay(true, false, true))
				return UnitTestResult.Fail("Set locker gameplay authority gate is incorrect");
			return UnitTestResult.Pass("Set locker RNG and reward spawning run only on host");
		}

		private static SetLockerStatePacket Roundtrip(SetLockerStatePacket input)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			var output = new SetLockerStatePacket();
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			if (stream.Position != stream.Length)
				throw new InvalidDataException("Set locker packet left unread bytes");
			return output;
		}

		private static bool Matches(Type type, string name, Type returnType, params Type[] parameters)
		{
			MethodInfo method = AccessTools.Method(type, name, parameters);
			return method != null && method.ReturnType == returnType;
		}
	}
}
