using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using System.Collections;
using Shared.Profiling;

namespace ONI_Together.Scripts.Duplicants
{
	internal class MinionMultiplayerInitializer : KMonoBehaviour
	{
		[MyCmpGet] NetworkIdentity identity;
		[MyCmpGet] KPrefabID kpref;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();

			if (MultiplayerSession.InSession)
				InitializeMP(null);

			Game.Instance?.Subscribe(MP_HASHES.OnMultiplayerGameSessionInitialized, InitializeMP);
			Game.Instance?.Subscribe(MP_HASHES.GameClient_OnConnectedInGame, InitializeMP);
		}

		void InitializeMP(object _ = null)
		{
			using var scope = Profiler.Scope();

			StartCoroutine(DelayedInit());
		}

		IEnumerator DelayedInit()
		{
			using var _ = Profiler.Scope();

			yield return null;
			FinalizeInit();
		}

		void FinalizeInit()
		{
			using var _ = Profiler.Scope();

			var go = gameObject;
			if (MultiplayerSession.NotInSession) return;
			if (!kpref?.HasTag(GameTags.BaseMinion) ?? false) return;

			DebugConsole.Log("OnMultiplayerGameSessionInitialized");
			// If we are a client, disable the brain/chores so the dupe is just a puppet
			if (MultiplayerSession.IsClient)
			{
				// Disable AI/decision making components
				if (go.TryGetComponent<ChoreDriver>(out var driver)) driver.enabled = false;
				if (go.TryGetComponent<ChoreConsumer>(out var consumer)) consumer.enabled = false;
				if (go.TryGetComponent<MinionBrain>(out var brain)) brain.enabled = false;

				// Disable sensors that might trigger behaviors
				if (go.TryGetComponent<Sensors>(out var sensors)) sensors.enabled = false;

                // Disable state machine controllers that could override animations
                var stateMachineControllers = go.GetComponents<StateMachineController>();
				foreach (var smc in stateMachineControllers)
				{
					if (smc != null) smc.enabled = false;
				}

				go.AddOrGet<ClientReceiver_ChoreErrands>();
				DebugConsole.Log($"[DuplicantSpawn] Client setup complete for {go.name} (NetId: {identity.NetId})");
			}
			else if (MultiplayerSession.IsHost)
			{
				// Add state sender for host to broadcast duplicant state to clients
				go.AddOrGet<DuplicantStateSender>();
				go.AddOrGet<DuplicantChoreBroadcaster>();
				DebugConsole.Log($"[DuplicantSpawn] Host setup complete for {go.name} (NetId: {identity.NetId})");
			}
		}
	}
}
