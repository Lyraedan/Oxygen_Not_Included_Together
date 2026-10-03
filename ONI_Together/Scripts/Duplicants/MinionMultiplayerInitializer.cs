using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.OxySync.Components;
using System.Collections;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Scripts.Duplicants
{
	internal class MinionMultiplayerInitializer : KMonoBehaviour
	{
		[MyCmpGet] NetworkIdentity identity;
		[MyCmpGet] KPrefabID kpref;

		private bool HasInit = false;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();
			base.OnSpawn();
			if (MultiplayerSession.InActiveSession)
			{
				FinalizeInit();
			}
			else
			{
				StartCoroutine(WaitForSessionAndInit());
			}
		}

		IEnumerator WaitForSessionAndInit()
		{
			yield return new WaitUntil((() => MultiplayerSession.InActiveSession));
			FinalizeInit();
		}

		void FinalizeInit()
		{
			using var _ = Profiler.Scope();
			if (HasInit) return;

			var go = gameObject;
			if (!kpref?.HasTag(GameTags.BaseMinion) ?? false) return;

			if (MultiplayerSession.IsClient)
				InitializeClient(go);
			else
				InitializeHost(go);

			HasInit = true;
		}
		
		void InitializeClient(GameObject go)
		{
			if (go.TryGetComponent<ChoreDriver>(out var driver)) driver.enabled = false;
			if (go.TryGetComponent<ChoreConsumer>(out var consumer)) consumer.enabled = false;
			if (go.TryGetComponent<MinionBrain>(out var brain)) brain.enabled = false;
			if (go.TryGetComponent<Sensors>(out var sensors)) sensors.enabled = false;

			// Do NOT disable StateMachineController!
			// In Klei's engine, StateMachineController drives visual animation and rendering of the Duplicant.

			go.AddOrGet<ClientReceiver_ChoreErrands>();
			var statusSync = go.AddOrGet<StatusItemsSyncer>();
			statusSync.recieverType = StatusItemsSyncer.StatusRecieverType.DUPLICANT;
		}

		void InitializeHost(GameObject go)
		{
			go.AddOrGet<DuplicantStateSender>();
			go.AddOrGet<DuplicantChoreBroadcaster>();
			go.AddOrGet<StatusItemsSyncer>();
		}
	}
}
