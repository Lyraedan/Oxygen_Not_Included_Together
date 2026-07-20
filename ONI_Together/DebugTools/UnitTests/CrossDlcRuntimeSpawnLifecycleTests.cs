using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Aquatic;
using ONI_Together.Networking.Packets.DLC.Frosty;
using ONI_Together.Networking.Packets.DLC.Prehistoric;
using ONI_Together.Networking.Packets.Social;
using ONI_Together.Patches.DLC.Aquatic;
using ONI_Together.Patches.DLC.Frosty;
using ONI_Together.Patches.DLC.Prehistoric;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class CrossDlcRuntimeSpawnLifecycleTests
	{
		[UnitTest(name: "Cross-DLC runtime spawns publish generic identity before domain state", category: "Sync")]
		public static UnitTestResult GenericSpawnOrdering()
		{
			MethodInfo send = Method(typeof(PacketSender), nameof(PacketSender.SendToAllClients),
				typeof(IPacket), typeof(PacketSendMode));
			if (!Ordered(typeof(SeaTreeBranchSync), "SendState", "TryBuildPacket", "EnsureNetId", send) ||
			    !Ordered(typeof(VineBranchSync), "SendChain", "TryCapture", "EnsureId", send) ||
			    !Ordered(typeof(SpaceTreeBranchSync), "BroadcastBranches", "Capture", "EnsureNetId", send) ||
			    !Ordered(typeof(MiniCometSpawnPatch), "Postfix", typeof(MiniCometSync), "TryCapture",
				    "EnsureIdentity", send))
				return UnitTestResult.Fail("A runtime domain packet can precede its generic spawn binding");
			return UnitTestResult.Pass("Every generic-capable runtime entity publishes identity before domain state");
		}

		[UnitTest(name: "Cross-DLC runtime prefabs persist network identity", category: "Sync")]
		public static UnitTestResult PersistentPrefabIdentityContracts()
		{
			MethodInfo helper = Method(typeof(NetworkIdentity),
				nameof(NetworkIdentity.EnsurePersistentPrefabIdentity));
			Type[] patches =
			{
				typeof(SeaTreeRootPrefabIdentityPatch), typeof(SeaTreeBranchPrefabIdentityPatch),
				typeof(VineMotherPrefabIdentityPatch), typeof(VineBranchPrefabIdentityPatch),
				typeof(MiniCometPrefabIdentityPatch), typeof(SpaceTreeSeedCometPrefabIdentityPatch),
				typeof(SpaceTreePrefabIdentityPatch), typeof(SpaceTreeBranchPrefabIdentityPatch),
				typeof(MinnowPoiPrefabIdentityPatch)
			};
			foreach (Type patch in patches)
				if (!Calls(Method(patch, "Postfix"), helper))
					return UnitTestResult.Fail($"{patch.Name} does not declare persistent identity");
			var targets = new HashSet<Type>();
			foreach (MethodBase target in MinnowPoiPrefabIdentityPatch.TargetMethods())
				targets.Add(target?.DeclaringType);
			if (!targets.SetEquals(new[]
			    {
				    typeof(MinnowImperativePOIAConfig), typeof(MinnowImperativePOIBConfig),
				    typeof(MinnowImperativePOICConfig)
			    }))
				return UnitTestResult.Fail("A Minnow POI prefab is missing persistent identity injection");
			return UnitTestResult.Pass("Runtime prefab identities are declared for save/load and late join");
		}

		[UnitTest(name: "Cross-DLC runtime pending queues are bounded and resettable", category: "Sync")]
		public static UnitTestResult PendingQueues()
		{
			bool generationsAdvance = RetryGenerationsAdvance();
			FillSeaTreePending();
			FillVinePending();
			FillMiniCometPending();
			FillSpaceTreePending();
			bool bounded = SeaTreeBranchSync.PendingCount == SeaTreeBranchSync.MaxPendingStates &&
			               VineBranchSync.PendingCount == VineBranchSync.MaxPendingStates &&
			               MiniCometSync.PendingCount == MiniCometSync.MaxPendingStates &&
			               SpaceTreeBranchSync.PendingCount == SpaceTreeBranchSync.MaxPendingStates;
			ResetPending();
			if (!generationsAdvance || !bounded ||
			    SeaTreeBranchSync.PendingCount != 0 || VineBranchSync.PendingCount != 0 ||
			    MiniCometSync.PendingCount != 0 || SpaceTreeBranchSync.PendingCount != 0)
				return UnitTestResult.Fail("A runtime pending queue is unbounded or survived session reset");
			return UnitTestResult.Pass("Runtime state waits are capacity- and session-bounded");
		}

		[UnitTest(name: "Minnow custom spawn is lifecycle-bound and scope-balanced", category: "Sync")]
		public static UnitTestResult MinnowLifecycle()
		{
			MinnowSpawnStatePacket copy = Roundtrip(new MinnowSpawnStatePacket
			{
				SourceNetId = 71,
				MinionNetId = 72,
				LifecycleRevision = 73,
				Position = new Vector3(4f, 5f, 0f),
				ArrivalTime = -10f,
				SkillPoints = 3,
				EntityData = Minnow()
			});
			if (copy.LifecycleRevision != 73 || MinnowSpawnStatePacket.CanApplyLifecycle(8, false, 7) ||
			    MinnowSpawnStatePacket.CanApplyLifecycle(8, true, 8) ||
			    !MinnowSpawnStatePacket.CanApplyLifecycle(8, false, 8) ||
			    !MinnowSpawnStatePacket.CanApplyLifecycle(8, true, 9) || !MinnowScopeIsBalanced())
				return UnitTestResult.Fail("Minnow accepted stale lifecycle state or leaked managed-spawn scope");
			return UnitTestResult.Pass("Minnow carries lifecycle revision and balances generic-spawn suppression");
		}

		[UnitTest(name: "Completed building lifecycle follows build materialization", category: "Sync")]
		public static UnitTestResult CompletedBuildingLifecycle()
		{
			MethodInfo prefix = Method(typeof(ConstructablePatch), "Prefix");
			MethodInfo postfix = Method(typeof(ConstructablePatch), "Postfix");
			MethodInfo finalizer = Method(typeof(ConstructablePatch), "Finalizer");
			MethodInfo begin = Method(typeof(NetworkIdentity), nameof(NetworkIdentity.BeginManagedSpawn));
			MethodInfo end = Method(typeof(NetworkIdentity), nameof(NetworkIdentity.EndManagedSpawn));
			MethodInfo send = Method(typeof(PacketSender), nameof(PacketSender.SendToAllClients),
				typeof(IPacket), typeof(PacketSendMode));
			if (!Calls(prefix, begin) || Calls(prefix, send) ||
			    !CallsBefore(postfix, end, send) || !Calls(finalizer, end))
				return UnitTestResult.Fail(
					"A completed building can publish before host success or materialize through generic lifecycle");
			return UnitTestResult.Pass(
				"Every completed building materializes before its bind-existing lifecycle is published");
		}

		private static bool Ordered(Type owner, string senderName, string captureName,
			string ensureName, MethodInfo send)
		{
			MethodInfo sender = Method(owner, senderName);
			MethodInfo capture = Method(owner, captureName);
			MethodInfo ensure = Method(owner, ensureName);
			return CallsBefore(sender, capture, send) && Calls(capture, ensure) &&
			       Calls(ensure, Method(typeof(NetworkIdentity),
				       nameof(NetworkIdentity.EnsureAuthoritativeSpawnBroadcast)));
		}

		private static bool Ordered(Type senderOwner, string senderName, Type syncOwner,
			string captureName, string ensureName, MethodInfo send)
		{
			MethodInfo sender = Method(senderOwner, senderName);
			MethodInfo capture = Method(syncOwner, captureName);
			MethodInfo ensure = Method(syncOwner, ensureName);
			return CallsBefore(sender, capture, send) && Calls(capture, ensure) &&
			       Calls(ensure, Method(typeof(NetworkIdentity),
				       nameof(NetworkIdentity.EnsureAuthoritativeSpawnBroadcast)));
		}

		private static bool MinnowScopeIsBalanced()
		{
			MethodInfo prefix = Method(typeof(MinnowSpawnPatch), "Prefix");
			MethodInfo postfix = Method(typeof(MinnowSpawnPatch), "Postfix");
			MethodInfo finalizer = Method(typeof(MinnowSpawnPatch), "Finalizer");
			MethodInfo finish = Method(typeof(MinnowPoiSync), "FinishMinnowCapture");
			MethodInfo rearm = Method(typeof(MinnowPoiSync), "RearmMinnowSpawnBroadcast");
			MethodInfo begin = Method(typeof(NetworkIdentity), nameof(NetworkIdentity.BeginManagedSpawn));
			MethodInfo end = Method(typeof(NetworkIdentity), nameof(NetworkIdentity.EndManagedSpawn));
			MethodInfo ensure = Method(typeof(NetworkIdentity),
				nameof(NetworkIdentity.EnsureAuthoritativeSpawnBroadcast));
			MethodInfo send = Method(typeof(PacketSender), nameof(PacketSender.SendToAllClients),
				typeof(IPacket), typeof(PacketSendMode));
			return Calls(prefix, begin) && CallsBefore(postfix, end, rearm) &&
			       CallsBefore(postfix, rearm, finish) &&
			       Calls(finalizer, end) && CallsBefore(finish, ensure, send);
		}

		private static bool CallsBefore(MethodInfo caller, MethodInfo first, MethodInfo second)
		{
			int firstOffset = CallOffset(caller, first);
			int secondOffset = CallOffset(caller, second);
			return firstOffset >= 0 && secondOffset > firstOffset;
		}

		private static bool Calls(MethodInfo caller, MethodInfo callee) => CallOffset(caller, callee) >= 0;

		private static bool RetryGenerationsAdvance()
		{
			return GenerationAdvances(typeof(SeaTreeBranchSync)) &&
			       GenerationAdvances(typeof(VineBranchSync)) &&
			       GenerationAdvances(typeof(MiniCometSync)) &&
			       GenerationAdvances(typeof(SpaceTreeBranchSync));
		}

		private static bool GenerationAdvances(Type type)
		{
			const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
			FieldInfo field = type.GetField("retryGeneration", flags);
			MethodInfo reset = Method(type, "ResetSessionState");
			if (field == null || reset == null)
				return false;
			int before = (int)field.GetValue(null);
			reset.Invoke(null, null);
			return (int)field.GetValue(null) == before + 1;
		}

		private static int CallOffset(MethodInfo caller, MethodInfo callee)
		{
			byte[] il = caller?.GetMethodBody()?.GetILAsByteArray();
			if (il == null || callee == null)
				return -1;
			byte[] token = BitConverter.GetBytes(callee.MetadataToken);
			for (int i = 0; i <= il.Length - token.Length; i++)
				if (il[i] == token[0] && il[i + 1] == token[1] &&
				    il[i + 2] == token[2] && il[i + 3] == token[3])
					return i;
			return -1;
		}

		private static MethodInfo Method(Type type, string name, params Type[] parameters)
		{
			const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance |
			                           BindingFlags.Public | BindingFlags.NonPublic;
			return parameters.Length == 0 ? type.GetMethod(name, flags) : type.GetMethod(name, flags, null, parameters, null);
		}

		private static void FillSeaTreePending()
		{
			SeaTreeBranchSync.ResetSessionState();
			for (int i = 0; i <= SeaTreeBranchSync.MaxPendingStates; i++)
				SeaTreeBranchSync.QueuePending(new SeaTreeBranchStatePacket
				{
					RootNetId = -1, BranchNetId = i + 1, PrefabHash = 1
				});
		}

		private static void FillVinePending()
		{
			VineBranchSync.ResetSessionState();
			for (int i = 0; i <= VineBranchSync.MaxPendingStates; i++)
				VineBranchSync.QueuePending(new VineBranchStatePacket
				{
					MotherNetId = -1, BranchNetId = i + 1, PrefabHash = 1,
					MotherSide = VineMotherSide.Left, Shape = VineBranch.Shape.InCornerTopLeft,
					RootShape = VineBranch.Shape.Left, RootDirection = Direction.Right, BranchNumber = 1
				});
		}

		private static void FillMiniCometPending()
		{
			MiniCometSync.ResetSessionState();
			for (int i = 0; i <= MiniCometSync.MaxPendingStates; i++)
				MiniCometSync.QueuePending(new MiniCometStatePacket
				{
					TargetNetId = i + 1, Element = SimHashes.Iron, Mass = 1f, Temperature = 300f
				});
		}

		private static void FillSpaceTreePending()
		{
			SpaceTreeBranchSync.ResetSessionState();
			for (int i = 0; i <= SpaceTreeBranchSync.MaxPendingStates; i++)
				SpaceTreeBranchSync.QueuePending(new SpaceTreeBranchStatePacket
				{
					TrunkNetId = -1, BranchNetId = i + 1, PrefabHash = 1, Slot = 0
				});
		}

		private static void ResetPending()
		{
			SeaTreeBranchSync.ResetSessionState();
			VineBranchSync.ResetSessionState();
			MiniCometSync.ResetSessionState();
			SpaceTreeBranchSync.ResetSessionState();
		}

		private static MinnowSpawnStatePacket Roundtrip(MinnowSpawnStatePacket input)
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			var output = new MinnowSpawnStatePacket();
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			return output;
		}

		private static ImmigrantOptionEntry Minnow()
			=> new()
			{
				EntryType = 0,
				Name = "Minnow",
				PersonalityId = "MINNOW",
				TraitIds = new List<string>(),
				StressTraitId = "StressVomiter",
				JoyTraitId = "BalloonArtist",
				StickerType = string.Empty,
				SkillAptitudes = new Dictionary<string, float>(),
				StartingLevels = new Dictionary<string, int>()
			};
	}
}
