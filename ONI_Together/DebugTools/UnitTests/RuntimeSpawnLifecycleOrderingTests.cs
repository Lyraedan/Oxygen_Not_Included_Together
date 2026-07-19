using System;
using System.Reflection;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.SpacedOut;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class RuntimeSpawnLifecycleOrderingTests
	{
		[UnitTest(name: "Runtime spawn create precedes reliable domain state", category: "Sync")]
		public static UnitTestResult CreateBeforeDomainState()
		{
			MethodInfo ensure = Method(typeof(NetworkIdentity), "EnsureAuthoritativeSpawnBroadcast");
			MethodInfo send = typeof(PacketSender).GetMethod(nameof(PacketSender.SendToAllClients),
				new[] { typeof(IPacket), typeof(PacketSendMode) });
			if (ReactorMeltdownSync.DomainSendMode != PacketSendMode.ReliableImmediate ||
			    HighEnergyParticleSync.DomainSendMode != PacketSendMode.ReliableImmediate ||
			    RailGunPayloadSync.DomainSendMode != PacketSendMode.ReliableImmediate)
				return UnitTestResult.Fail("A runtime domain state is not ReliableImmediate");
			if (!CallsInOrder(Method(typeof(ReactorMeltdownSync), "Publish"), ensure, send) ||
			    !CallsInOrder(Method(typeof(HighEnergyParticleSync), "Publish"), ensure, send) ||
			    !CallsInOrder(Method(typeof(RailGunPayloadSync), "Publish"), ensure, send))
				return UnitTestResult.Fail("A domain state can publish before its lifecycle create");
			return UnitTestResult.Pass("Runtime creates are journaled before reliable domain state");
		}

		[UnitTest(name: "Runtime domain state never allocates authoritative NetIds", category: "Sync")]
		public static UnitTestResult DomainStateWaitsForBinding()
		{
			MethodInfo overrideNetId = Method(typeof(NetworkIdentity), nameof(NetworkIdentity.OverrideNetId));
			if (Method(typeof(HighEnergyParticleSync), "TrySpawnParticle") != null ||
			    Method(typeof(RailGunPayloadSync), "ResolveItem") != null ||
			    Calls(Method(typeof(ReactorMeltdownSync), "ApplyComet"), overrideNetId) ||
			    Calls(Method(typeof(RailGunPayloadSync), "TryResolvePayload"), overrideNetId) ||
			    Calls(Method(typeof(RailGunPayloadSync), "TryResolveItems"), overrideNetId))
				return UnitTestResult.Fail("A client domain state still creates or claims an authoritative NetId");
			MethodInfo hepSpawn = Method(typeof(HighEnergyParticleSpawnPatch), "Postfix");
			MethodInfo delayedCleanup = Method(typeof(HighEnergyParticleSync), "ScheduleUnassignedCleanup");
			MethodInfo applyComet = Method(typeof(ReactorMeltdownSync), "ApplyComet");
			MethodInfo attachComet = Method(typeof(ReactorMeltdownSync), "AttachClientCometComponents");
			if (!Calls(hepSpawn, delayedCleanup) || !Calls(applyComet, attachComet))
				return UnitTestResult.Fail("A generic spawn can miss its client runtime markers");
			return UnitTestResult.Pass("Domain state waits for generic lifecycle binding");
		}

		[UnitTest(name: "Runtime pending state is latest bounded and resettable", category: "Sync")]
		public static UnitTestResult PendingLifecycle()
		{
			HighEnergyParticleSync.ResetSessionState();
			HighEnergyParticleSync.CachePending(new HighEnergyParticleStatePacket { NetId = 51, Revision = 2 });
			HighEnergyParticleSync.CachePending(new HighEnergyParticleStatePacket { NetId = 51, Revision = 4 });
			HighEnergyParticleSync.CachePending(new HighEnergyParticleStatePacket { NetId = 51, Revision = 3 });
			if (!HighEnergyParticleSync.TryGetPendingRevision(51, out int revision) || revision != 4)
				return UnitTestResult.Fail("HEP pending state did not retain its latest revision");
			for (int i = 0; i <= HighEnergyParticleSync.MaxPendingStates; i++)
				HighEnergyParticleSync.CachePending(
					new HighEnergyParticleStatePacket { NetId = 1000 + i, Revision = i });
			if (HighEnergyParticleSync.PendingCount > HighEnergyParticleSync.MaxPendingStates)
				return UnitTestResult.Fail("HEP pending states exceeded their bound");
			HighEnergyParticleSync.ResetSessionState();
			RailGunPayloadSync.ResetSessionState();
			if (HighEnergyParticleSync.PendingCount != 0 || RailGunPayloadSync.PendingCount != 0)
				return UnitTestResult.Fail("Runtime pending state survived session reset");
			if (HighEnergyParticleSync.NeedsApply(4, 4) || RailGunPayloadSync.NeedsApply(4, 3) ||
			    ReactorMeltdownSync.NeedsApply(4, 4))
				return UnitTestResult.Fail("A runtime domain accepted a stale revision");
			return UnitTestResult.Pass("Runtime pending state is latest bounded and session-scoped");
		}

		private static MethodInfo Method(Type type, string name)
			=> type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic |
			                        BindingFlags.Static | BindingFlags.Instance);

		private static bool Calls(MethodInfo method, MethodInfo target)
			=> IndexOfToken(method, target) >= 0;

		private static bool CallsInOrder(MethodInfo method, MethodInfo first, MethodInfo second)
		{
			int firstIndex = IndexOfToken(method, first);
			int secondIndex = IndexOfToken(method, second);
			return firstIndex >= 0 && secondIndex > firstIndex;
		}

		private static int IndexOfToken(MethodInfo method, MethodInfo target)
		{
			byte[] il = method?.GetMethodBody()?.GetILAsByteArray();
			if (il == null || target == null) return -1;
			byte[] token = BitConverter.GetBytes(target.MetadataToken);
			for (int i = 0; i <= il.Length - token.Length; i++)
				if (il[i] == token[0] && il[i + 1] == token[1] &&
				    il[i + 2] == token[2] && il[i + 3] == token[3])
					return i;
			return -1;
		}
	}
}
