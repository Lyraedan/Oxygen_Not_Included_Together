using System.IO;
using System.Linq;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Animation;
using ONI_Together.Networking.Packets.Core;
using Shared.Interfaces.Networking;

namespace ONI_Together.DebugTools.UnitTests
{
	public static class AnimSyncTests
	{
		[UnitTest(name: "Anim reconciliation: detects wrong animation", category: "Animation")]
		public static UnitTestResult DetectsWrongAnimation()
		{
			var identities = NetworkIdentityRegistry.AllIdentities;
			foreach (var id in identities)
			{
				if (!id.gameObject.TryGetComponent<KBatchedAnimController>(out var kbac))
					continue;
				if (!id.gameObject.GetComponent<KPrefabID>()?.HasTag(GameTags.BaseMinion) ?? true)
					continue;

				if (kbac.CurrentAnim == null)
					continue;

				string currentAnim = kbac.CurrentAnim.name;
				if (string.IsNullOrEmpty(currentAnim))
					continue;

				var wrongHash = new HashedString("fake_anim_that_doesnt_exist");
				if (kbac.currentAnim == wrongHash)
					return UnitTestResult.Fail("Hash collision with fake anim");

				return UnitTestResult.Pass($"Minion '{id.gameObject.name}' anim='{currentAnim}', would detect mismatch");
			}
			return UnitTestResult.Fail("No minions with anim controller found");
		}

		[UnitTest(name: "Anim reconciliation: elapsed time readable", category: "Animation")]
		public static UnitTestResult ElapsedTimeReadable()
		{
			var identities = NetworkIdentityRegistry.AllIdentities;
			foreach (var id in identities)
			{
				if (!id.gameObject.TryGetComponent<KBatchedAnimController>(out var kbac))
					continue;
				if (!id.gameObject.GetComponent<KPrefabID>()?.HasTag(GameTags.BaseMinion) ?? true)
					continue;

				float elapsed = kbac.GetElapsedTime();
				return UnitTestResult.Pass($"ElapsedTime={elapsed:F3}s on '{id.gameObject.name}'");
			}
			return UnitTestResult.Fail("No minions found");
		}
	}
}