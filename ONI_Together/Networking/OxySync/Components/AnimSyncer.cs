using System.Linq;
using ONI_Together.DebugTools;
using Shared.OxySync;
using Shared.OxySync.Attributes;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.OxySync.Components
{
	public class AnimSyncer : NetworkBehaviour
	{
		[MyCmpGet]
		private KBatchedAnimController animController;

		[SyncVar]
		private HashedString animName;
		private int lastAnimName;
		private int VAR_AnimName_HASH;

		[SyncVar]
		private KAnim.PlayMode animPlayMode;

		[SyncVar]
		private float animSpeed;

		private float FASTFORWARD_THRESHOLD = 0.02f;
		private float epsilon = 0.01f;

		private const float HEARTBEAT_INTERVAL = 1f;
		private float _lastHeartbeatTime;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();

			if (animController == null)
				animController = GetComponent<KBatchedAnimController>();

			VAR_AnimName_HASH = nameof(animName).GetHashCode();
		}


		public override void OnCleanUp()
		{
			using var _ = Profiler.Scope();

			base.OnCleanUp();
		}

		[Client]
		private void ForceAnimUpdate(KBatchedAnimController kbac)
		{
			using var _ = Profiler.Scope();

			try
			{
				kbac.SetVisiblity(true);
				kbac.forceRebuild = true;
				kbac.SuspendUpdates(false);
				kbac.ConfigureUpdateListener();
			}
			catch (System.Exception e)
			{
				DebugConsole.LogError($"[AnimSyncer] Failed to force animation update on {kbac.gameObject?.GetProperName()}: {e}");
			}

		}

		[Server]
		private void ServerUpdate()
		{
			using var _ = Profiler.Scope();

			if (!isServer || !MultiplayerSession.SessionHasPlayers)
				return;

			if (animController == null)
			{
				DebugConsole.LogWarning($"[AnimSyncer] AnimController was null on {gameObject?.GetProperName()}.");
				return;
			}

			animName = animController.currentAnim;
			animPlayMode = animController.mode;
			animSpeed = animController.playSpeed;
		}

		private void ServerHeartbeat()
		{
			using var _ = Profiler.Scope();

			if (!isServer || !MultiplayerSession.SessionHasPlayers)
				return;

			if (animController == null)
			{
				DebugConsole.LogWarning($"[AnimSyncer] AnimController was null on {gameObject?.GetProperName()}.");
				return;
			}

			epsilon = animController.GetElapsedTime();
			MarkSyncVarAsDirty(VAR_AnimName_HASH);
		}

		[Client]
		private void ClientUpdate()
		{
			using var _ = Profiler.Scope();

			if (animController.currentAnim.hash != animName.hash)
			{
				animController.Play(animName, animPlayMode, animSpeed, 0f);
				lastAnimName = animName.hash;
				ForceAnimUpdate(animController);
			}

			if (animController.PlayMode != animPlayMode || animController.playSpeed != animSpeed)
			{
				animController.Play(animName, animPlayMode, animSpeed, animController.GetElapsedTime());
				ForceAnimUpdate(animController);
			}

			float fastForwardTime = animController.GetElapsedTime() + FASTFORWARD_THRESHOLD;
			if (animName.hash == lastAnimName && fastForwardTime <= epsilon)
			{
				// Keep it in case 
				// animController.SetElapsedTime(fastForwardTime);
				// ForceAnimUpdate(animController);
			}
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (animController != null && isServer && MultiplayerSession.SessionHasPlayers)
			{
				ServerUpdate();

				if (Time.unscaledTime - _lastHeartbeatTime >= HEARTBEAT_INTERVAL)
				{
					_lastHeartbeatTime = Time.unscaledTime;
					ServerHeartbeat();
				}
			}
			if (animController != null && isClient)
				ClientUpdate();
		}
	}
}
