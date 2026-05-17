using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World.Handlers;
using ONI_Together.DebugTools;
using System.IO;
using UnityEngine;
using HarmonyLib;
using Shared.Profiling;
using System.Security.Principal;
using ONI_Together.Misc;

namespace ONI_Together.Networking.Packets.World
{
	public enum BuildingConfigType : byte
	{
		Float = 0,      // Standard float value (valve flow, thresholds)
		Boolean = 1,    // Checkbox values
		SliderIndex = 2, // Slider with index (for multi-slider controls)
		RecipeQueue = 3, // Fabricator recipe queue (ConfigHash = recipe ID hash, Value = count)
		String = 4       // String value (tag names, text fields)
	}

	public class BuildingConfigPacket : IPacket
	{
		private ulong Sender; // Who triggered this
		public int NetId;
		public int Cell; // Deterministic location-based identification
		public int ConfigHash; // Hash of the property name (e.g. "Threshold", "Logic")
		public float Value;
		public BuildingConfigType ConfigType = BuildingConfigType.Float;
		public int SliderIndex = 0; // For ISliderControl multi-sliders
		public string StringValue = ""; // For tag names and text fields

		public static bool IsApplyingPacket = false;
        
		// Delay refreshing because things like storage lockers cause lag
		private static float _lastRefreshTime = -999f;
        private const float REFRESH_COOLDOWN = 0.1f; // ~30 frames at 60fps, consistent regardless of FPS

        public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			Sender = NetworkConfig.GetLocalID();
			writer.Write(Sender);
			writer.Write(NetId);
			writer.Write(Cell);
			writer.Write(ConfigHash);
			writer.Write(Value);
			writer.Write((byte)ConfigType);
			writer.Write(SliderIndex);
			writer.Write(StringValue ?? "");
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Sender = reader.ReadUInt64();
			NetId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			ConfigHash = reader.ReadInt32();
			Value = reader.ReadSingle();
			ConfigType = (BuildingConfigType)reader.ReadByte();
			SliderIndex = reader.ReadInt32();
			StringValue = reader.ReadString();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			// We sent this ignore
			if (Sender == NetworkConfig.GetLocalID())
				return;

			//DebugConsole.Log($"[BuildingConfigPacket] Received a config update packet. NetId={NetId}, Cell={Cell}");

			if (!NetworkIdentityRegistry.TryGet(NetId, out var identity) || identity == null)
			{
				// Attempt to find building by cell
				if (Grid.IsValidCell(Cell))
				{
					// For multi-layered buildings, we might need a more specific search, but usually
					// we just look for BuildingComplete components.
					GameObject buildingGO = Grid.Objects[Cell, (int)ObjectLayer.Building];
					if (buildingGO != null)
					{
						identity = buildingGO.GetComponent<NetworkIdentity>();
						if (identity)
						{
							identity.OverrideNetId(NetId); // Override properly from the host
						}
						else
						{
							identity = buildingGO.AddOrGet<NetworkIdentity>();
							identity.NetId = NetId; // Client forces the NetId from Host
							identity.RegisterIdentity();
						}

                        //DebugConsole.Log($"[BuildingConfigPacket] Resolved missing identity for {buildingGO.name} at cell {Cell}. Assigned NetId: {NetId}");
					}
				}
			}

			if (identity != null)
			{
				try
				{
					IsApplyingPacket = true;
					ApplyConfig(identity.gameObject);
				}
				finally
				{
                    RefreshSideScreenIfOpen(identity.gameObject);
                    IsApplyingPacket = false;
                }

                // HOST RELAY: If host received this from a client, re-broadcast to all other clients
                if (MultiplayerSession.IsHost)
				{
					PacketSender.SendToAllClients(this);
					//DebugConsole.Log($"[BuildingConfigPacket] Host relayed config to all clients: NetId={NetId}, ConfigHash={ConfigHash}");
				}
			}
			else
			{
				DebugConsole.LogWarning($"[BuildingConfigPacket] FAILED to resolve entity for NetId {NetId} at Cell {Cell}");
			}
		}

        /// <summary>
        /// Applies the configuration to the target building.
        /// All handlers are now in the BuildingConfigHandlerRegistry.
        /// </summary>
        private void ApplyConfig(GameObject go)
		{
			using var _ = Profiler.Scope();

			if (go == null) return;

            // All handlers are now in the registry
            if (BuildingConfigHandlerRegistry.TryHandle(go, this))
			{
				DebugConsole.Log($"[BuildingConfigPacket] Handled by registry for {go.name}");
                return;
			}

			// Log unhandled configs for debugging
			DebugConsole.LogWarning($"[BuildingConfigPacket] Unhandled config: Hash={ConfigHash}, Type={ConfigType}, Value={Value}, String={StringValue} on {go.name}");
		}

        private void RefreshSideScreenIfOpen(GameObject go)
        {
            using var _ = Profiler.Scope();
            if (go == null) return;

            if (Time.unscaledTime - _lastRefreshTime < REFRESH_COOLDOWN) return;
            _lastRefreshTime = Time.unscaledTime;

            try
            {
                if (go.TryGetComponent<KSelectable>(out var selectable) && SelectTool.Instance.selected == selectable)
                {
                    SelectTool.Instance.Select(null, true);
                    SelectTool.Instance.Select(selectable, true);
                }
            }
            catch (System.Exception e)
            {
                DebugConsole.Log($"[BuildingConfigPacket] UI refresh failed: {e.Message}");
            }
        }
    }
}
