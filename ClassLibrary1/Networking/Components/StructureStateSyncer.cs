using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.World;
using ONI_MP.Patches.World;
using Shared.Profiling;
using UnityEngine;

namespace ONI_MP.Networking.Components
{
	public class StructureStateSyncer : KMonoBehaviour
	{
		private float sendInterval = 0.5f; // Sync every 500ms
		private float timer;

		private Battery battery;
		private Generator generator;
		private Operational operational;
		private int cell;

		private float lastSentValue;
		private bool lastSentActive;

		// Grace period
		private bool _initialized = false;
		private float _initializationTime;
		private const float INITIAL_DELAY = 5f;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();

			if (!MultiplayerSession.InSession)
			{
				enabled = false;
				return;
			}

			cell = Grid.PosToCell(this);
			battery = GetComponent<Battery>();
			generator = GetComponent<Generator>();
			operational = GetComponent<Operational>();

			if (battery == null && generator == null)
			{
				// Not a relevant structure
				enabled = false;
			}
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
			{
				// Skip if no clients connected
				if (MultiplayerSession.ConnectedPlayers.Count == 0)
					return;

				// Grace period after world load
				if (!_initialized)
				{
					_initializationTime = Time.unscaledTime;
					_initialized = true;
					return;
				}

				if (Time.unscaledTime - _initializationTime < INITIAL_DELAY)
					return;

				HostUpdate();
			}
		}

		private void HostUpdate()
		{
			using var _ = Profiler.Scope();

			try
			{
				timer += Time.unscaledDeltaTime;
				if (timer < sendInterval) return;
				timer = 0f;

				float currentValue = 0f;
				bool currentActive = false;

				if (battery != null)
				{
					currentValue = battery.JoulesAvailable;
				}

				if (operational != null)
				{
					currentActive = operational.IsActive;
				}

				// Sync if changed significantly
				if (Mathf.Abs(currentValue - lastSentValue) > 0.1f || currentActive != lastSentActive)
				{
					lastSentValue = currentValue;
					lastSentActive = currentActive;

					var packet = new StructureStatePacket
					{
						Cell = cell,
						Value = currentValue,
						IsActive = currentActive
					};
					PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
				}
			}
			catch (System.Exception)
			{
				// Silently ignore - structure state may not be ready yet
			}
		}

		// Cached reflection field
		private static System.Reflection.FieldInfo _batteryJoulesField;
		private static bool _batteryFieldLookupAttempted = false;
		private static System.Reflection.FieldInfo _batteryMeterField;
		private static bool _batteryMeterFieldLookupAttempted = false;

		// Static handler for client-side reception
		public static void HandlePacket(StructureStatePacket packet)
		{
			using var _ = Profiler.Scope();

			if (!Grid.IsValidCell(packet.Cell)) return;

			GameObject go = Grid.Objects[packet.Cell, (int)ObjectLayer.Building];
			if (go == null) return;

			// Apply state
			var battery = go.GetComponent<Battery>();
			if (battery != null)
			{
				// JoulesAvailable is read-only, set backing field via reflection
				try
				{
					if (_batteryJoulesField == null && !_batteryFieldLookupAttempted)
					{
						_batteryFieldLookupAttempted = true;
						_batteryJoulesField = typeof(Battery).GetField("joulesAvailable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
					}
					if (_batteryJoulesField != null)
					{
						_batteryJoulesField.SetValue(battery, packet.Value);
					}
				}
				catch (System.Exception ex)
				{
					DebugConsole.LogError($"[StructureStateSyncer] Failed to set battery joules: {ex}");
				}

				// Preserve the historical client-side crash guard and only allow
				// this explicit refresh path to execute UpdateData on clients.
				var tracker = go.GetComponent<BatteryTracker>();
				if (tracker != null)
				{
					using var allowClientRefresh = BatteryTrackerPatch.AllowClientRefresh();
					tracker.UpdateData();
				}

				// Drive the visual fill meter: normally updated inside Battery.EnergySim200ms,
				// which is skipped on clients by BatteryClientSimSkipPatch.
				try
				{
					if (_batteryMeterField == null && !_batteryMeterFieldLookupAttempted)
					{
						_batteryMeterFieldLookupAttempted = true;
						_batteryMeterField = typeof(Battery).GetField("meter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
					}
					if (_batteryMeterField != null)
					{
						var meter = _batteryMeterField.GetValue(battery) as MeterController;
						if (meter != null && battery.capacity > 0f)
						{
							meter.SetPositionPercent(Mathf.Clamp01(packet.Value / battery.capacity));
						}
					}
				}
				catch (System.Exception ex)
				{
					DebugConsole.LogError($"[StructureStateSyncer] Failed to update meter: {ex}");
				}
			}

			var operational = go.GetComponent<Operational>();
			if (operational != null)
			{
				operational.SetActive(packet.IsActive);
			}
		}
	}
}
