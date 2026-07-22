using ONI_Together.DebugTools;
using ONI_Together.Networking;
using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.World;
using System.Security.Principal;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Patches.World.SideScreen
{
	/// <summary>
	/// Generic sync helpers for Side Screen UI components.
	/// Used by all side screen patches to send building config packets.
	/// </summary>
	public static class SideScreenSyncHelper
	{
		public static void SyncSliderChange(Component target, float value, int sliderIndex = 0)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (!MultiplayerSession.InActiveSession) return;
			if (target == null) return;

			var identity = target.gameObject.AddOrGet<NetworkIdentity>();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(target.gameObject),
				ConfigHash = "Slider".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.SliderIndex,
				SliderIndex = sliderIndex
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}

		public static void SyncThresholdChange(GameObject target, float value)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log($"[SideScreenSyncHelper.SyncThresholdChange] Called for {target?.name ?? "null"}, value={value}");

			if (BuildingConfigPacket.IsApplyingPacket)
			{
				DebugConsole.Log("[SideScreenSyncHelper.SyncThresholdChange] IsApplyingPacket=true, skipping");
				return;
			}
			if (target == null) return;

			var identity = target.AddOrGet<NetworkIdentity>();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(target),
				ConfigHash = "Threshold".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			DebugConsole.Log($"[SideScreenSyncHelper.SyncThresholdChange] Sending packet: ConfigHash={packet.ConfigHash}, Value={value}");

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}

		public static void SyncThresholdDirection(GameObject target, bool activateAbove)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (target == null) return;

			var identity = target.AddOrGet<NetworkIdentity>();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(target),
				ConfigHash = "ThresholdDir".GetHashCode(),
				Value = activateAbove ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}

		public static void SyncCheckboxChange(GameObject target, bool value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (target == null) return;

			var identity = target.AddOrGet<NetworkIdentity>();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(target),
				ConfigHash = "Checkbox".GetHashCode(),
				Value = value ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}

		public static void SyncCapacityChange(GameObject target, float value)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (target == null) return;

			var identity = target.AddOrGet<NetworkIdentity>();

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(target),
				ConfigHash = "Capacity".GetHashCode(),
				Value = value,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}

		public static void SyncDoorState(GameObject target, Door.ControlState state)
		{
			using var _ = Profiler.Scope();

			if (BuildingConfigPacket.IsApplyingPacket) return;
			if (target == null) return;

			var identity = target.GetNetIdentity();
			if (identity == null || identity.NetId == 0)
				return;

			var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(target),
				ConfigHash = "DoorState".GetHashCode(),
				Value = (float)(int)state,
				ConfigType = BuildingConfigType.Float
			};

			if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
			else PacketSender.SendToHost(packet);
		}

        public static void SyncQueueToggleable(GameObject target, bool expectedQueue)
        {
	        using var _ = Profiler.Scope();

            if (BuildingConfigPacket.IsApplyingPacket) return;
            if (target == null) return;

            var identity = target.AddOrGet<NetworkIdentity>();
            identity.RegisterIdentity();

            var packet = new BuildingConfigPacket
            {
                NetId = identity.NetId,
                Cell = Grid.PosToCell(target),
                ConfigHash = "QueueToggleable".GetHashCode(),
                Value = expectedQueue ? 1f : 0f,
                ConfigType = BuildingConfigType.Boolean
            };

            DebugConsole.Log($"[SideScreenSyncHelper.SyncQueueToggleable] Sending packet: ConfigHash={packet.ConfigHash}, Value={packet.Value}");

            if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
            else PacketSender.SendToHost(packet);
        }

        public static void SyncToggleableState(GameObject target, bool buildingEnabled)
        {
	        using var _ = Profiler.Scope();

            if (BuildingConfigPacket.IsApplyingPacket) return;
            if (target == null) return;

            var identity = target.AddOrGet<NetworkIdentity>();
            identity.RegisterIdentity();

            var packet = new BuildingConfigPacket
			{
				NetId = identity.NetId,
				Cell = Grid.PosToCell(target),
				ConfigHash = "ToggleableChange".GetHashCode(),
				Value = buildingEnabled ? 1f : 0f,
				ConfigType = BuildingConfigType.Boolean
			};

            DebugConsole.Log($"[SideScreenSyncHelper.SyncToggleableState] Sending packet: ConfigHash={packet.ConfigHash}, Value={packet.Value}");

            // Only hosts sync building enabled state to clients
            if (MultiplayerSession.IsHost) PacketSender.SendToAllClients(packet);
		}
    }
}
