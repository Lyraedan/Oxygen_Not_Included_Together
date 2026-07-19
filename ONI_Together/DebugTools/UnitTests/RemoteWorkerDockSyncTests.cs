using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.DLC.Bionic;
using ONI_Together.Patches.DLC.Bionic;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class RemoteWorkerDockSyncTests
	{
		[UnitTest(name: "Remote worker dock Harmony targets match build 740622", category: "Sync")]
		public static UnitTestResult HarmonyTargets()
		{
			if (!Matches(typeof(RemoteWorkerDock), "RequestNewWorker", typeof(void), typeof(object)) ||
			    !Matches(typeof(RemoteWorkerDock), "MakeNewWorker", typeof(void), typeof(object)) ||
			    !Matches(typeof(RemoteWorkerDock), nameof(RemoteWorkerDock.StartWorking), typeof(bool),
				    typeof(RemoteWorkTerminal)) ||
			    !Matches(typeof(RemoteWorkerDock), nameof(RemoteWorkerDock.OnRemoteWorkTick), typeof(bool),
				    typeof(float)) ||
			    !Matches(typeof(RemoteWorkerSM), nameof(RemoteWorkerSM.TickResources), typeof(void), typeof(float)) ||
			    !Matches(typeof(RemoteWorkTerminal), "OnWorkTick", typeof(bool), typeof(WorkerBase), typeof(float)))
				return UnitTestResult.Fail("A RemoteWorkerDock authority target changed");
			return UnitTestResult.Pass("RemoteWorkerDock authority targets match build 740622");
		}

		[UnitTest(name: "Remote chore Harmony targets exclude inherited Chore methods", category: "Sync")]
		public static UnitTestResult RemoteChoreHarmonyTargets()
		{
			Type contexts = typeof(List<Chore.Precondition.Context>);
			int collectChores = 0;
			int prepareChore = 0;
			int end = 0;
			try
			{
				foreach (MethodBase method in RemoteChoreAuthorityPatch.TargetMethods())
				{
					if (method == null || method.DeclaringType != typeof(RemoteChore))
						return UnitTestResult.Fail("Remote chore authority returned an invalid target");
						switch (method.Name)
						{
						case nameof(RemoteChore.CollectChores):
							ParameterInfo[] parameters = method.GetParameters();
							if (parameters.Length != 5 || parameters[0].ParameterType != typeof(ChoreConsumerState) ||
							    parameters[1].ParameterType != contexts || parameters[2].ParameterType != contexts ||
							    parameters[3].ParameterType != contexts || parameters[4].ParameterType != typeof(bool))
								return UnitTestResult.Fail("RemoteChore.CollectChores signature changed");
								collectChores++;
							break;
						case nameof(RemoteChore.PrepareChore):
							if (!HasParameters(method, typeof(Chore.Precondition.Context).MakeByRefType()))
								return UnitTestResult.Fail("RemoteChore.PrepareChore signature changed");
							prepareChore++;
							break;
						case "End":
							if (!HasParameters(method, typeof(string)))
								return UnitTestResult.Fail("RemoteChore.End signature changed");
							end++;
							break;
						default:
							return UnitTestResult.Fail("Remote chore authority returned an unrelated target");
					}
				}
			}
			catch (AmbiguousMatchException exception)
			{
				return UnitTestResult.Fail($"Remote chore overload resolution is ambiguous: {exception.Message}");
			}

			if (collectChores != 1 || prepareChore != 1 || end != 1)
				return UnitTestResult.Fail(
					$"Remote chore targets incomplete: CollectChores={collectChores}, PrepareChore={prepareChore}, End={end}");
			return UnitTestResult.Pass(
				"Remote chore authority patches only RemoteChore declarations");
		}

		[UnitTest(name: "Remote worker dock requests require verified relay", category: "Sync")]
		public static UnitTestResult RequestAuthority()
		{
			if (new RemoteWorkerDockSelectionRequestPacket() is not IClientRelayable ||
			    new RemoteWorkerDockSelectionStatePacket() is not IHostOnlyPacket ||
			    new RemoteWorkerDockStatePacket() is not IHostOnlyPacket)
				return UnitTestResult.Fail("Remote worker dock authority marker is missing");

			var direct = new DispatchContext(7, false);
			DispatchContext verified = direct.AsVerifiedHostBroadcast();
			if (RemoteWorkerDockSelectionRequestPacket.ShouldAccept(true, direct, true) ||
			    RemoteWorkerDockSelectionRequestPacket.ShouldAccept(true, verified, false) ||
			    !RemoteWorkerDockSelectionRequestPacket.ShouldAccept(true, verified, true) ||
			    RemoteWorkerDockSelectionRequestPacket.ShouldAccept(false, verified, true) ||
			    !RemoteWorkerDockStatePacket.ShouldApply(false, true) ||
			    RemoteWorkerDockStatePacket.ShouldApply(true, true) ||
			    RemoteWorkerDockStatePacket.ShouldApply(false, false))
				return UnitTestResult.Fail("Remote worker dock authority gate is incorrect");
			return UnitTestResult.Pass("Client commands require verified host relay; outcomes require host");
		}

		[UnitTest(name: "Remote worker dock state is bounded absolute state", category: "Sync")]
		public static UnitTestResult StateRoundtrip()
		{
			var input = new RemoteWorkerDockStatePacket
			{
				DockNetId = 101,
				Revision = 5,
				WorkerNetId = 202,
				TerminalNetId = 303,
				Docked = true,
				ActivelyControlled = true,
				ActivelyWorking = false,
				Available = false,
				PlayNewWorker = true,
				Charge = 42f,
				WorkerPrimary = Element(SimHashes.Steel, 200f, 310f),
				WorkerOil = Element(SimHashes.CrudeOil, 7f, 300f),
				WorkerGunk = Element(SimHashes.LiquidGunk, 2f, 301f),
				DockMaterial = Element(SimHashes.Steel, 250f, 305f),
				DockOil = Element(SimHashes.CrudeOil, 20f, 300f),
				DockGunk = Element(SimHashes.LiquidGunk, 4f, 302f)
			};
			RemoteWorkerDockStatePacket output = Roundtrip(input, new RemoteWorkerDockStatePacket());
			if (output.DockNetId != 101 || output.Revision != 5 || output.WorkerNetId != 202 ||
			    output.TerminalNetId != 303 || !output.Docked || !output.ActivelyControlled ||
			    output.Charge != 42f || output.WorkerPrimary.Mass != 200f ||
			    output.DockMaterial.Mass != 250f || output.WorkerOil.Mass != 7f)
				return UnitTestResult.Fail("Remote worker dock absolute state did not roundtrip");

			input.Charge = RemoteWorkerDockStatePacket.MaxCharge + 0.1f;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Out-of-bounds remote worker charge was accepted");
			input.Charge = 42f;
			input.WorkerOil.Mass = RemoteWorkerDockStatePacket.MaxWorkerResourceMass + 0.1f;
			if (input.IsWireValid())
				return UnitTestResult.Fail("Out-of-bounds worker resource mass was accepted");
			return UnitTestResult.Pass("Relationships, element data, and resource masses are bounded absolute state");
		}

		[UnitTest(name: "Remote worker dock revisions and gameplay are host authoritative", category: "Sync")]
		public static UnitTestResult GameplayAuthority()
		{
			if (!RemoteWorkerDockSync.ShouldRunGameplay(false, false) ||
			    !RemoteWorkerDockSync.ShouldRunGameplay(true, true) ||
			    RemoteWorkerDockSync.ShouldRunGameplay(true, false) ||
			    !RemoteWorkerDockSync.IsNewerRevision(4, 5) ||
				    RemoteWorkerDockSync.IsNewerRevision(5, 5) ||
			    RemoteWorkerDockSync.IsNewerRevision(6, 5))
				return UnitTestResult.Fail("Remote worker dock host authority or idempotence gate is incorrect");
			if (!RemoteWorkerDockAuthority.AllowsHostOutcomeMutation(false, false, false) ||
			    !RemoteWorkerDockAuthority.AllowsHostOutcomeMutation(true, true, false) ||
			    !RemoteWorkerDockAuthority.AllowsHostOutcomeMutation(true, false, true) ||
			    RemoteWorkerDockAuthority.AllowsHostOutcomeMutation(true, false, false))
				return UnitTestResult.Fail("Client host-outcome setters were not blocked");
			return UnitTestResult.Pass("Clients cannot consume mass or run chores and stale outcomes are ignored");
		}

		[UnitTest(name: "Deferred remote worker spawn starts exactly once after relationship", category: "Sync")]
		public static UnitTestResult DeferredSpawnActivation()
		{
			var activation = new DeferredRemoteWorkerActivation();
			int starts = 0;
			activation.Defer();
			if (activation.TryActivate(relationshipReady: false, () => starts++) || starts != 0)
				return UnitTestResult.Fail("Deferred worker started before its dock relationship was ready");
			if (!activation.TryActivate(relationshipReady: true, () => starts++) || starts != 1)
				return UnitTestResult.Fail("Deferred worker did not start when its relationship became ready");
			activation.Defer();
			if (activation.TryActivate(relationshipReady: true, () => starts++) || starts != 1)
				return UnitTestResult.Fail("Deferred worker lifecycle started more than once");

			MethodInfo original = AccessTools.Method(typeof(RemoteWorkerStateMachineSpawnPatch),
				nameof(RemoteWorkerStateMachineSpawnPatch.RunOriginalOnSpawn));
			if (original?.GetCustomAttribute<HarmonyReversePatch>() == null)
				return UnitTestResult.Fail("Deferred activation re-enters the patched OnSpawn path");
			return UnitTestResult.Pass("Relationship readiness releases the unpatched worker lifecycle exactly once");
		}

		private static RemoteWorkerElementState Element(SimHashes element, float mass, float temperature)
			=> new RemoteWorkerElementState
			{
				Present = true,
				ElementId = (int)element,
				Mass = mass,
				Temperature = temperature,
				DiseaseIndex = byte.MaxValue
			};

		private static T Roundtrip<T>(T input, T output) where T : IPacket
		{
			using var stream = new MemoryStream();
			using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
				input.Serialize(writer);
			stream.Position = 0;
			using var reader = new BinaryReader(stream);
			output.Deserialize(reader);
			if (stream.Position != stream.Length)
				throw new InvalidDataException("Remote worker dock packet left unread bytes");
			return output;
		}

		private static bool Matches(Type type, string name, Type returnType, params Type[] parameters)
		{
			MethodInfo method = AccessTools.Method(type, name, parameters);
			return method != null && method.ReturnType == returnType;
		}

		private static bool HasParameters(MethodBase method, params Type[] expected)
		{
			ParameterInfo[] actual = method.GetParameters();
			if (actual.Length != expected.Length)
				return false;
			for (int i = 0; i < actual.Length; i++)
			{
				if (actual[i].ParameterType != expected[i])
					return false;
			}
			return true;
		}
	}
}
