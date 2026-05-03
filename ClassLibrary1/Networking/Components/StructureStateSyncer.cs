using System;
using KSerialization;
using ONI_MP.DebugTools;
using ONI_MP.Networking.Packets.World;
using ONI_MP.Patches.World;
using Shared.Profiling;
using UnityEngine;
using static STRINGS.UI.OVERLAYS;

namespace ONI_MP.Networking.Components
{
	public class StructureStateSyncer : KMonoBehaviour
	{
		public enum StructureType
		{
			UNCATEGORIZED,
			BATTERY,
			GENERATOR
		}

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

		public StructureType structureType = StructureType.UNCATEGORIZED;

		public override void OnSpawn()
		{
			using var _ = Profiler.Scope();

			base.OnSpawn();
            cell = Grid.PosToCell(this);
            operational = GetComponent<Operational>();
        }

		public void InitalizeAsStructure(StructureType structureType)
		{
			this.structureType = structureType;
			switch(structureType)
			{
				case StructureType.BATTERY:
					battery = GetComponent<Battery>();
					break;
				case StructureType.GENERATOR:
					generator = GetComponent<Generator>();
                    break;
                case StructureType.UNCATEGORIZED:
                default:
                    break;
			}
            DebugConsole.Log($"Structure init as: " + structureType);
		}

		private void Update()
		{
			using var _ = Profiler.Scope();

            if (!MultiplayerSession.InSession)
                return;

			if (MultiplayerSession.IsHost)
			{
				// Skip if no clients connected
			    if (!MultiplayerSession.SessionHasPlayers)
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

                if (operational != null)
                    currentActive = operational.IsActive;

                switch (structureType)
                {
                    case StructureType.BATTERY:
                        if (battery != null)
                        {
                            currentValue = battery.JoulesAvailable;
                        }
                        break;

                    case StructureType.GENERATOR:
                        if (generator != null)
                        {
                            currentValue = generator.JoulesAvailable;
                        }
                        break;

                    case StructureType.UNCATEGORIZED:
                    default:
                        break;
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
                        IsActive = currentActive,
                        StructureType = structureType
                    };

                    PacketSender.SendToAllClients(packet, PacketSendMode.Unreliable);
                }
            }
            catch (System.Exception)
            {
                // Silent fail - Structure may not be ready
            }
        }

        public static void HandlePacket(StructureStatePacket packet)
		{
			using var _ = Profiler.Scope();
			if (!Grid.IsValidCell(packet.Cell)) return;

			GameObject go = Grid.Objects[packet.Cell, (int)ObjectLayer.Building];
			if (go == null) return;

            switch(packet.StructureType)
            {
                case StructureType.BATTERY:
                    ApplyBatteryState(go, packet);
                    break;
                case StructureType.GENERATOR:
                    ApplyGeneratorState(go, packet);
                    break;
                case StructureType.UNCATEGORIZED:
                default:
                    break;
            }
            ApplyOperationalState(go, packet);
		}

        #region Battery
        private static void ApplyBatteryState(GameObject go, StructureStatePacket packet)
        {
            var battery = go.GetComponent<Battery>();
            if (battery == null) return;

            battery.joulesAvailable = packet.Value;
            RefreshBatteryTracker(go);
			UpdateBatteryMeter(battery, packet.Value);
            DebugConsole.Log("BATTERY SYNC BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        }

        private static void UpdateBatteryMeter(Battery battery, float joules)
		{
			try
			{
                var meter = battery.meter;
                if (meter == null) return;

                if (battery.capacity <= 0f) return;

                float percent = Mathf.Clamp01(joules / battery.capacity);
                meter.SetPositionPercent(percent);
				DebugConsole.Log("BATTERY SYNC METER UPDATE AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

            } catch(Exception ex)
			{
                DebugConsole.LogError($"[StructureStateSyncer] Meter update failed: {ex}");
            }
		}

        private static void RefreshBatteryTracker(GameObject go)
        {
            var tracker = go.GetComponent<BatteryTracker>();
            if (tracker == null) return;

            using var allowClientRefresh = BatteryTrackerPatch.AllowClientRefresh();
            tracker.UpdateData();
        }
        #endregion

        #region Generator
        public static void ApplyGeneratorState(GameObject go, StructureStatePacket packet)
        {
            var generator = go.GetComponent<Generator>();
            if(generator == null) return;

            generator.AssignJoulesAvailable(packet.Value);
        }
        #endregion

        private static void ApplyOperationalState(GameObject go, StructureStatePacket packet)
		{
            var operational = go.GetComponent<Operational>();
            if (operational == null) return;

            operational.SetActive(packet.IsActive);
        }
	}
}
