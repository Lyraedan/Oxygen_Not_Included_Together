using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Common;
using ONI_Together.Patches.DLC.Common;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class PoiTechSyncTests
	{
		[UnitTest(name: "POI tech Harmony targets match game signatures", category: "Sync")]
		public static UnitTestResult HarmonyTargets()
		{
			if (!Matches(typeof(POITechItemUnlocks.Instance),
				    nameof(POITechItemUnlocks.Instance.OnSidescreenButtonPressed), typeof(void)) ||
			    !Matches(typeof(POITechItemUnlockWorkable), "OnCompleteWork", typeof(void),
				    typeof(WorkerBase)) ||
			    !Matches(typeof(POITechItemUnlocks), "OnNotificationAknowledged", typeof(void),
				    typeof(object)))
				return UnitTestResult.Fail("A POI tech Harmony target signature changed");
			return UnitTestResult.Pass("POI tech Harmony targets match the game assembly");
		}

		[UnitTest(name: "POI tech request and state enforce authority", category: "Sync")]
		public static UnitTestResult Authority()
		{
			if (new PoiTechRequestPacket() is not IClientRelayable ||
			    new PoiTechStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("POI tech packet authority markers are missing");
			var directClient = new DispatchContext(42, false);
			DispatchContext verified = directClient.AsVerifiedHostBroadcast();
			if (PoiTechRequestPacket.ShouldAccept(true, directClient) ||
			    !PoiTechRequestPacket.ShouldAccept(true, verified) ||
			    PoiTechRequestPacket.ShouldAccept(false, verified) ||
			    !PoiTechStatePacket.ShouldApply(false, true) ||
			    PoiTechStatePacket.ShouldApply(true, true) ||
			    PoiTechStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("POI tech authority gate is incorrect");
			return UnitTestResult.Pass("POI tech requests require verified clients and states require host");
		}

		[UnitTest(name: "POI tech state is bounded absolute state", category: "Sync")]
		public static UnitTestResult StateRoundtrip()
		{
			var input = new PoiTechStatePacket
			{
				TargetNetId = -411,
				IsUnlocked = true,
				PendingChore = false,
				SeenNotification = true
			};
			var output = Roundtrip(input);
			if (output.TargetNetId != -411 || !output.IsUnlocked || output.PendingChore ||
			    !output.SeenNotification)
				return UnitTestResult.Fail("POI tech state did not roundtrip");

			input.PendingChore = true;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Unlocked POI accepted a pending chore");
			return UnitTestResult.Pass("POI tech state is bounded, absolute and monotonic");
		}

		[UnitTest(name: "POI tech gameplay advances only on host", category: "Sync")]
		public static UnitTestResult HostGameplay()
		{
			if (!PoiTechSync.ShouldRunGameplay(false, false, false) ||
			    !PoiTechSync.ShouldRunGameplay(true, true, false) ||
			    PoiTechSync.ShouldRunGameplay(true, false, false) ||
			    !PoiTechSync.ShouldRunGameplay(true, false, true))
				return UnitTestResult.Fail("POI tech gameplay authority gate is incorrect");
			return UnitTestResult.Pass("POI tech work and unlock side effects advance only on host state");
		}

		private static PoiTechStatePacket Roundtrip(PoiTechStatePacket input)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			var output = new PoiTechStatePacket();
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			if (stream.Position != stream.Length)
				throw new InvalidDataException("POI tech packet left unread bytes");
			return output;
		}

		private static bool Matches(Type type, string name, Type returnType, params Type[] parameters)
		{
			MethodInfo method = AccessTools.Method(type, name, parameters);
			return method != null && method.ReturnType == returnType;
		}
	}
}
