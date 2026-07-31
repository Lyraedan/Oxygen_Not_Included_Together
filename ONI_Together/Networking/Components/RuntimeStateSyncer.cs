using System.Collections.Generic;
using ONI_Together.Networking.Packets.World;
using Shared.Interfaces.Networking;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	/// <summary>
	/// Replicates the authoritative state of the state machines that were missing
	/// from the normal creature/building synchronization paths.
	/// </summary>
	public sealed class RuntimeStateSyncer : KMonoBehaviour
	{
		public const string ConfigKey = "RuntimeStateMachineState";
		private const float SendInterval = 0.5f;

		private static readonly HashSet<string> SupportedStateMachines = new HashSet<string>
		{
			"HappinessMonitor+Instance",
			"FossilHuntInitializer+Instance",
			"LonelyMinionHouse+Instance",
			"MorbRoverMakerStorytrait+Instance"
		};

		private readonly Dictionary<string, string> lastStates = new Dictionary<string, string>();
		private float timer;

		public static bool IsSupported(StateMachine.Instance smi)
		{
			return smi != null && SupportedStateMachines.Contains(smi.GetType().FullName);
		}

		private void Update()
		{
			if (!MultiplayerSession.IsHostInSession)
				return;

			timer += Time.unscaledDeltaTime;
			if (timer < SendInterval)
				return;
			timer = 0f;

			var identity = gameObject.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			if (identity.NetId == 0)
				return;

			foreach (var controller in gameObject.GetComponents<StateMachineController>())
			{
				if (controller == null)
					continue;

				foreach (var smi in controller.GetAllSMI<StateMachine.Instance>())
				{
					if (!IsSupported(smi))
						continue;

					var state = smi.GetCurrentState();
					string stateName = state?.name;
					string typeName = smi.GetType().FullName;
					if (string.IsNullOrEmpty(stateName) || string.IsNullOrEmpty(typeName))
						continue;

					if (lastStates.TryGetValue(typeName, out var previous) && previous == stateName)
						continue;
					lastStates[typeName] = stateName;

					PacketSender.SendToAllClients(new BuildingConfigPacket
					{
						NetId = identity.NetId,
						Cell = Grid.PosToCell(gameObject),
						ConfigHash = ConfigKey.GetHashCode(),
						ConfigType = BuildingConfigType.String,
						StringValue = typeName + "\n" + stateName
					}, PacketSendMode.Reliable);
				}
			}
		}
	}
}
