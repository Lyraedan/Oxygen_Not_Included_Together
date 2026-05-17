using UnityEngine;
using ONI_Together.DebugTools;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.World.Handlers
{
	/// <summary>
	/// Handles LogicTimerSensor and LogicTimeOfDaySensor buildings.
	/// </summary>
	public class TimerSensorHandler : IBuildingConfigHandler
	{
		private static readonly int[] _hashes = new int[]
		{
			"TimerOnDuration".GetHashCode(),
			"TimerOffDuration".GetHashCode(),
			"StartTime".GetHashCode(),
			"Duration".GetHashCode(),
		};

		public int[] SupportedConfigHashes => _hashes;

		public bool TryApplyConfig(GameObject go, BuildingConfigPacket packet)
		{
			using var _ = Profiler.Scope();

			int hash = packet.ConfigHash;

			// Handle LogicTimerSensor
			var timerSensor = go.GetComponent<LogicTimerSensor>();
			if (timerSensor != null)
			{
				if (hash == "TimerOnDuration".GetHashCode())
				{
					timerSensor.onDuration = packet.Value;
					DebugConsole.Log($"[TimerSensorHandler] Set onDuration={packet.Value} on {go.name}");
					return true;
				}
				if (hash == "TimerOffDuration".GetHashCode())
				{
					timerSensor.offDuration = packet.Value;
					DebugConsole.Log($"[TimerSensorHandler] Set offDuration={packet.Value} on {go.name}");
					return true;
				}
			}

			// Handle LogicTimeOfDaySensor (Cycle Sensor)
			var cycleSensor = go.GetComponent<LogicTimeOfDaySensor>();
			if (cycleSensor != null)
			{
				if (hash == "StartTime".GetHashCode())
				{
					cycleSensor.startTime = packet.Value;
					DebugConsole.Log($"[TimerSensorHandler] Set startTime={packet.Value} on {go.name}");
					return true;
				}
				if (hash == "Duration".GetHashCode())
				{
					cycleSensor.duration = packet.Value;
					DebugConsole.Log($"[TimerSensorHandler] Set duration={packet.Value} on {go.name}");
					return true;
				}
			}

			return false;
		}
	}
}
