using System.Collections.Generic;
using ONI_Together.Networking.Packets.World;
using Shared.Interfaces.Networking;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Components
{
	/// <summary>
	/// Replicates host-authoritative state machines that are not covered by the
	/// normal creature and building synchronization paths.
	/// </summary>
	public sealed class StateMachineStateSyncer : KMonoBehaviour
	{
		public const string ConfigKey = "StateMachineState";
		private const float SEND_INTERVAL = 0.5f;

		private static readonly HashSet<string> _supportedStateMachines = new HashSet<string>
		{
			"HappinessMonitor+Instance",
			"FossilHuntInitializer+Instance",
			"LonelyMinionHouse+Instance",
			"MorbRoverMakerStorytrait+Instance"
		};

		private readonly Dictionary<string, string> _lastStates = new Dictionary<string, string>();
		private float _timer;

		public static bool IsSupported(StateMachine.Instance smi)
		{
			return smi != null && _supportedStateMachines.Contains(smi.GetType().FullName);
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (!MultiplayerSession.IsHostInSession)
				return;

			_timer += Time.unscaledDeltaTime;
			if (_timer < SEND_INTERVAL)
				return;
			_timer = 0f;

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

					if (_lastStates.TryGetValue(typeName, out var previous) && previous == stateName)
						continue;
					_lastStates[typeName] = stateName;

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
