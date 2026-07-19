using System;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class SpacedOutClusterSystemsTests
	{
		[UnitTest(name: "Temporal tear uses verified request and absolute host state", category: "Sync")]
		public static UnitTestResult TemporalTearAuthority()
		{
			var request = Roundtrip(
				new TemporalTearRequestPacket { OpenerNetId = 17 },
				new TemporalTearRequestPacket());
			var state = Roundtrip(new TemporalTearStatePacket
			{
				LocationQ = 8, LocationR = -3, Revealed = true, Open = true
			}, new TemporalTearStatePacket());
			var direct = new DispatchContext(4, false);
			if (request is not IClientRelayable || state is not IHostOnlyPacket ||
			    request.OpenerNetId != 17 || state.LocationQ != 8 || state.LocationR != -3 ||
			    !state.Revealed || !state.Open ||
			    TemporalTearRequestPacket.ShouldAccept(true, direct) ||
			    !TemporalTearRequestPacket.ShouldAccept(true, direct.AsVerifiedHostBroadcast()))
				return UnitTestResult.Fail("Temporal tear authority or roundtrip is incorrect");
			if (TemporalTearStatePacket.NeedsApply(true, true, state) ||
			    !TemporalTearStatePacket.NeedsApply(false, false, state))
				return UnitTestResult.Fail("Repeated temporal tear state is not idempotent");
			if (!Matches(typeof(TemporalTearOpener.Instance), "FireTemporalTearOpener",
				typeof(void), typeof(TemporalTearOpener.Instance)) ||
			    !Matches(typeof(TemporalTearOpener.Instance), nameof(TemporalTearOpener.Instance.OpenTemporalTear),
				    typeof(void)))
				return UnitTestResult.Fail("Temporal tear Harmony target signature changed");
			return UnitTestResult.Pass("Temporal tear request and absolute state are bounded and idempotent");
		}

		[UnitTest(name: "Cluster telescope publishes absolute fog and meteor discovery", category: "Sync")]
		public static UnitTestResult TelescopeDiscoveryState()
		{
			var fog = Roundtrip(new ClusterDiscoveryStatePacket
			{
				Kind = ClusterDiscoveryKind.Fog,
				LocationQ = -4,
				LocationR = 6,
				Progress = 0.75f,
				Complete = false
			}, new ClusterDiscoveryStatePacket());
			var meteor = Roundtrip(new ClusterDiscoveryStatePacket
			{
				Kind = ClusterDiscoveryKind.Meteor,
				LocationQ = 2,
				LocationR = -1,
				DestinationWorldId = 3,
				MeteorArrivalTime = 1200f,
				Progress = 1f,
				Complete = true
			}, new ClusterDiscoveryStatePacket());
			if (fog is not IHostOnlyPacket || meteor is not IHostOnlyPacket ||
			    fog.Kind != ClusterDiscoveryKind.Fog || fog.Progress != 0.75f || fog.Complete ||
			    meteor.Kind != ClusterDiscoveryKind.Meteor || meteor.DestinationWorldId != 3 ||
			    meteor.MeteorArrivalTime != 1200f ||
			    !meteor.Complete)
				return UnitTestResult.Fail("Cluster discovery state did not roundtrip");
			if (ClusterDiscoveryStatePacket.NeedsApply(0.75f, false, fog) ||
			    !ClusterDiscoveryStatePacket.NeedsApply(0.25f, false, fog))
				return UnitTestResult.Fail("Repeated discovery state is not idempotent");
			meteor.Progress = float.NaN;
			if (meteor.IsWireValid())
				return UnitTestResult.Fail("Non-finite discovery progress was accepted");
			if (!Matches(typeof(ClusterFogOfWarManager.Instance),
				nameof(ClusterFogOfWarManager.Instance.EarnRevealPointsForLocation),
				typeof(bool), typeof(AxialI), typeof(float)) ||
			    !Matches(typeof(ClusterMapMeteorShower.Instance),
				    nameof(ClusterMapMeteorShower.Instance.ProgressIdentifiction),
				    typeof(void), typeof(float)))
				return UnitTestResult.Fail("Cluster telescope Harmony target signature changed");
			return UnitTestResult.Pass("Fog and meteor discovery are bounded absolute host state");
		}

		[UnitTest(name: "Mission control selects craft and syncs absolute boost", category: "Sync")]
		public static UnitTestResult MissionControlState()
		{
			var state = Roundtrip(new MissionControlStatePacket
			{
				WorkableNetId = 71,
				CraftNetId = -92,
				BuffTimeRemaining = 600f
			}, new MissionControlStatePacket());
			if (state is not IHostOnlyPacket || state.WorkableNetId != 71 ||
			    state.CraftNetId != -92 || state.BuffTimeRemaining != 600f)
				return UnitTestResult.Fail("Mission control target or boost did not roundtrip");
			if (MissionControlStatePacket.NeedsApply(-92, 600f, state) ||
			    !MissionControlStatePacket.NeedsApply(0, 0f, state))
				return UnitTestResult.Fail("Repeated mission control state is not idempotent");
			state.BuffTimeRemaining = -1f;
			if (state.IsWireValid())
				return UnitTestResult.Fail("Negative mission-control boost was accepted");
			if (!Matches(typeof(MissionControlCluster.Instance),
				nameof(MissionControlCluster.Instance.GetRandomBoostableClustercraft),
				typeof(Clustercraft)) ||
			    !Matches(typeof(MissionControlCluster.Instance),
				    nameof(MissionControlCluster.Instance.ApplyEffect),
				    typeof(void), typeof(Clustercraft)))
				return UnitTestResult.Fail("Mission control Harmony target signature changed");
			return UnitTestResult.Pass("Mission target and boost remaining time are bounded host state");
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

		private static bool Matches(Type type, string name, Type returnType, params Type[] parameters)
		{
			MethodInfo method = AccessTools.Method(type, name, parameters);
			return method != null && method.ReturnType == returnType;
		}
	}
}
