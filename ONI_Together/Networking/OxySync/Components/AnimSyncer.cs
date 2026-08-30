using System.Linq;
using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;

namespace ONI_Together.Networking.OxySync.Components
{
	public class AnimSyncer : NetworkBehaviour
	{
		[MyCmpGet]
		private KBatchedAnimController animController;

		private ulong sequenceNumber;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();
			sequenceNumber = 0;
		}


		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();

			base.OnCleanUp();
		}

		public void RequestToSyncAnim(float timestamp, bool queueing, HashedString[] animNames, KAnim.PlayMode mode, float speed = 1f, float timeOffset = 0f)
		{
			using var _ = Profiler.Scope();

			if (!isServer || !MultiplayerSession.IsHost)
			{
				return;
			}

			sequenceNumber++;
			try
			{
				CallClientRpc(nameof(RpcPlayAnim), timestamp, sequenceNumber, queueing, animNames, (byte)mode, speed, timeOffset);
			}
			catch (System.Exception e)
			{
				DebugConsole.LogError($"[OxySync] Failed to send animation packet: {e}");
			}
		}

		[ClientRpc]
		private void RpcPlayAnim(float timestamp, ulong sequenceNumber, bool queueing, HashedString[] animNames, byte mode, float speed, float timeOffset)
		{
			using var _ = Profiler.Scope();

			if (!isClient || !MultiplayerSession.IsClient)
            {
                return;
            }

			if (sequenceNumber <= this.sequenceNumber)
			{
				DebugConsole.LogWarning($"[OxySync] Ignoring out-of-order animation packet.");
				return;
			}
			this.sequenceNumber = sequenceNumber;

			if (animController == null)
				animController = GetComponent<KBatchedAnimController>();
			if (animController == null || animNames == null || animNames.Length == 0)
				return;

			float currentTime = GameClock.Instance?.GetTime() ?? 0f;
			if (currentTime < timestamp)
			{
				// If the packet is from the future, adjust the time offset to account for the difference.
				// This ensures that the animation plays at the correct time relative to the game clock.
				timeOffset += timestamp - currentTime;
			}
			else if (currentTime > timestamp)
			{
				// If the packet is from the past, adjust the time offset to account for the difference.
				// This ensures that the animation plays at the correct time relative to the game clock.
				timeOffset -= currentTime - timestamp;
			}

			// Prevent negative time offset, just play it anyway.
			if (timeOffset < 0f)
				timeOffset = 0f;

			try
			{
				if (animNames.Length > 1)
					animController.Play(animNames, (KAnim.PlayMode)mode);
				else if (queueing)
					animController.Queue(animNames.FirstOrDefault(), (KAnim.PlayMode)mode, speed, timeOffset);
				else
					animController.Play(animNames.FirstOrDefault(), (KAnim.PlayMode)mode, speed, timeOffset);
			}
			catch (System.Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer] Failed to play animation on {animController?.gameObject?.GetProperName()}: {e}");
			}
		}
	}
}
