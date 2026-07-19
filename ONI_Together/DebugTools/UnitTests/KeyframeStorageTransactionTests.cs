#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using ONI_Together.Networking;
using ONI_Together.Networking.Packets.World;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class KeyframeStorageTransactionTests
	{
		private static int _nextStorageNetId = -2_000_000;

		[UnitTest(name: "Soak keyframe failed apply leaves storage revision retryable", category: "Networking")]
		public static UnitTestResult FailedApplyLeavesRevisionRetryable()
		{
			int storageNetId = Interlocked.Decrement(ref _nextStorageNetId);
			const ulong revision = 500;
			var pending = new Dictionary<int, ulong> { [storageNetId] = revision };
			var commit = new KeyframeStorageRevisionCommit(pending);

			if (commit.TryComplete(applySucceeded: false)
			    || NetworkIdentityRegistry.GetLastStorageSnapshotRevision(storageNetId) != 0)
			{
				return UnitTestResult.Fail(
					"A failed keyframe consumed its storage revision");
			}
			if (!commit.TryComplete(applySucceeded: true)
			    || NetworkIdentityRegistry.GetLastStorageSnapshotRevision(storageNetId) != revision)
			{
				return UnitTestResult.Fail(
					"The same storage revision could not commit after a successful retry");
			}
			return UnitTestResult.Pass(
				"Failed apply keeps the revision unconsumed and retryable");
		}

		[UnitTest(name: "Soak keyframe revalidates lifecycle after storage apply", category: "Networking")]
		public static UnitTestResult RevalidatesLifecycleAfterStorageApply()
		{
			MethodInfo apply = typeof(SoakHashDomainKeyframePacket).GetMethod(
				"TryApplyAll", BindingFlags.Static | BindingFlags.NonPublic);
			MethodInfo validate = typeof(NetworkIdentityRegistry).GetMethod(
				"ValidateCurrentLifecycleMembership",
				BindingFlags.Static | BindingFlags.NonPublic);
			byte[] il = apply?.GetMethodBody()?.GetILAsByteArray();
			byte[] token = validate == null ? null : BitConverter.GetBytes(validate.MetadataToken);
			bool callsValidation = il != null && token != null
			                       && Enumerable.Range(0, il.Length - token.Length + 1)
				                       .Any(index => il.Skip(index).Take(token.Length).SequenceEqual(token));
			return callsValidation
				? UnitTestResult.Pass("Storage apply cannot silently delete lifecycle identities")
				: UnitTestResult.Fail("Keyframe commits without a final lifecycle membership check");
		}
	}
}
#endif
