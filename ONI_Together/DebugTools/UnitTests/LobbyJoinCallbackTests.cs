using System;
using ONI_Together.Networking.Transport.Steamworks;
using Steamworks;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class LobbyJoinCallbackTests
	{
		[UnitTest(name: "Lobby join completion is consumed before invocation and runs once", category: "Transport")]
		public static UnitTestResult CompletionRunsOnce()
		{
			var pending = new PendingLobbyJoinCallback();
			int calls = 0;
			ulong joinedId = 0;
			bool alreadyConsumed = false;
			pending.Register(lobby =>
			{
				calls++;
				joinedId = lobby.m_SteamID;
				alreadyConsumed = pending.Consume() == null;
			});

			pending.Consume()?.Invoke(new CSteamID(123));
			pending.Consume()?.Invoke(new CSteamID(456));
			if (calls != 1 || joinedId != 123 || !alreadyConsumed)
				return UnitTestResult.Fail("Completion was retained, repeated, or received the wrong lobby ID");

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "New lobby join replaces the previous completion", category: "Transport")]
		public static UnitTestResult RegistrationReplacesPrevious()
		{
			var pending = new PendingLobbyJoinCallback();
			int oldCalls = 0, newCalls = 0;
			pending.Register(_ => oldCalls++);
			pending.Register(_ => newCalls++);
			pending.Consume()?.Invoke(new CSteamID(123));
			if (oldCalls != 0 || newCalls != 1)
				return UnitTestResult.Fail("A new join must replace the old completion");

			pending.Register(_ => oldCalls++);
			pending.Register(null);
			if (pending.Consume() != null)
				return UnitTestResult.Fail("Joining without a completion must release the old callback");

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Cleared lobby join never replays during later hosting", category: "Transport")]
		public static UnitTestResult ClearDiscardsCompletion()
		{
			var pending = new PendingLobbyJoinCallback();
			int calls = 0;
			pending.Register(_ => calls++);
			pending.Clear();
			pending.Clear();
			pending.Consume()?.Invoke(new CSteamID(456));
			if (calls != 0)
				return UnitTestResult.Fail("Cleared completion was replayed for a later lobby entry");

			pending.Register(_ => calls++);
			pending.Consume()?.Invoke(new CSteamID(789));
			if (calls != 1)
				return UnitTestResult.Fail("Clearing must allow a subsequent join to complete");

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Reentrant lobby join registration survives completion", category: "Transport")]
		public static UnitTestResult ReentrantRegistration()
		{
			var pending = new PendingLobbyJoinCallback();
			string order = "";
			pending.Register(_ =>
			{
				order += "first ";
				pending.Register(lobby => order += "second " + lobby.m_SteamID);
			});
			pending.Consume()?.Invoke(new CSteamID(123));
			pending.Consume()?.Invoke(new CSteamID(456));
			if (order != "first second 456" || pending.Consume() != null)
				return UnitTestResult.Fail("Completion discarded the callback registered during its invocation");

			return UnitTestResult.Pass();
		}

		[UnitTest(name: "Throwing lobby join completion cannot replay and allows recovery", category: "Transport")]
		public static UnitTestResult ExceptionReleasesCompletion()
		{
			var pending = new PendingLobbyJoinCallback();
			pending.Register(_ => throw new InvalidOperationException("Simulated stale UI"));
			bool threw = false;
			try
			{
				pending.Consume()?.Invoke(new CSteamID(123));
			}
			catch (InvalidOperationException)
			{
				threw = true;
			}
			if (!threw || pending.Consume() != null)
				return UnitTestResult.Fail("A throwing completion must already have been released");

			bool recovered = false;
			pending.Register(_ => recovered = true);
			pending.Consume()?.Invoke(new CSteamID(456));
			if (!recovered)
				return UnitTestResult.Fail("A callback exception prevented a subsequent join");

			return UnitTestResult.Pass();
		}
	}
}
