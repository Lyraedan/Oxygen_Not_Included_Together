using ONI_MP.DebugTools;
using ONI_MP.Networking;
using ONI_MP.Networking.Components;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_MP.Scripts.Duplicants
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

                /*
                // I think the state machine crashes are caused by all the RationalAi smis being disabled
				//disable all RationalAi smis
				var ai_smi = this.GetSMI<RationalAi.Instance>();
				if (ai_smi != null)
				{
					var stateMachinesToStopGetter = RationalAi.GetStateMachinesToRunWhenAlive(ai_smi);
					foreach(var getter in stateMachinesToStopGetter)
					{
						var smi = getter.Invoke(ai_smi);
						//smi.StopSM("Stopped by multiplayer mod");
						smi.PauseSMI();
                    }

					ai_smi.PauseSMI();
                    //ai_smi.StopSM("Client dupe do not get to have ai");
                }*/

                // Disable state machine controllers that could override animations
                var stateMachineControllers = go.GetComponents<StateMachineController>();
				foreach (var smc in stateMachineControllers)
				{
					if (smc != null) smc.enabled = false;
				}

                // go.AddOrGet<DuplicantClientController>();
				DebugConsole.Log($"[DuplicantSpawn] Client setup complete for {go.name} (NetId: {identity.NetId})");
			}
			else if (MultiplayerSession.IsHost)
			{
				// Add state sender for host to broadcast duplicant state to clients
				go.AddOrGet<DuplicantStateSender>();
				DebugConsole.Log($"[DuplicantSpawn] Host setup complete for {go.name} (NetId: {identity.NetId})");
			}
		}
	}
}
