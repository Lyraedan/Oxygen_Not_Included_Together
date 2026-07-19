using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Packets.World.Handlers;
using ONI_Together.DebugTools;
using System.IO;
using UnityEngine;
using HarmonyLib;
using Shared.Profiling;
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
		private const int MaxCell = 16 * 1024 * 1024;
		private const int MaxSliderIndex = 1024;
		private const int MaxStringLength = 1024;

		private ulong Sender; // Who triggered this
		public int NetId;
		public int Cell; // Deterministic location-based identification
		public int DeterministicBuildingId;
		public int ConfigHash; // Hash of the property name (e.g. "Threshold", "Logic")
		public float Value;
		public BuildingConfigType ConfigType = BuildingConfigType.Float;
		public int SliderIndex = 0; // For ISliderControl multi-sliders
		public int ReferenceNetId; // Optional signed host-assigned entity reference; zero means none
		public string StringValue = ""; // For tag names and text fields
		public string SecondaryStringValue = ""; // Paired string payloads that must apply atomically

		private static int _applyDepth;
		public static bool IsApplyingPacket => _applyDepth > 0;
        
		// Delay refreshing because things like storage lockers cause lag
		private static float _lastRefreshTime = -999f;
        private const float REFRESH_COOLDOWN = 0.1f; // ~30 frames at 60fps, consistent regardless of FPS

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			BindAuthoritativeIdentity();
			if (!IsValidMetadata(NetId, Cell, DeterministicBuildingId, ConfigType,
				SliderIndex, ReferenceNetId, Value, StringValue)
			    || (SecondaryStringValue?.Length ?? 0) > MaxStringLength)
				throw new InvalidDataException("Invalid building config metadata");

			Sender = NetworkConfig.GetLocalID();
			writer.Write(Sender);
			writer.Write(NetId);
			writer.Write(Cell);
			writer.Write(DeterministicBuildingId);
			writer.Write(ConfigHash);
			writer.Write(Value);
			writer.Write((byte)ConfigType);
			writer.Write(SliderIndex);
			writer.Write(ReferenceNetId);
			writer.Write(StringValue ?? "");
			writer.Write(SecondaryStringValue ?? "");
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Sender = reader.ReadUInt64();
			NetId = reader.ReadInt32();
			Cell = reader.ReadInt32();
			DeterministicBuildingId = reader.ReadInt32();
			ConfigHash = reader.ReadInt32();
			Value = reader.ReadSingle();
			ConfigType = (BuildingConfigType)reader.ReadByte();
			SliderIndex = reader.ReadInt32();
			ReferenceNetId = reader.ReadInt32();
			StringValue = reader.ReadString();
			SecondaryStringValue = reader.ReadString();
			if (!IsValidMetadata(NetId, Cell, DeterministicBuildingId, ConfigType,
				SliderIndex, ReferenceNetId, Value, StringValue)
			    || SecondaryStringValue.Length > MaxStringLength)
				throw new InvalidDataException("Invalid building config metadata");
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			DispatchContext context = PacketHandler.CurrentContext;
			bool clientRequest = MultiplayerSession.IsHost && !context.SenderIsHost;
			bool hostOutcome = MultiplayerSession.IsClient && context.SenderIsHost;
			if (Sender != context.SenderId || (!clientRequest && !hostOutcome))
			{
				DebugConsole.LogWarning($"[BuildingConfigPacket] Rejected sender {Sender} from transport {context.SenderId}");
				return;
			}
			if (clientRequest)
			{
				var player = MultiplayerSession.GetPlayer(context.SenderId);
				if (player == null || !player.ProtocolVerified || !SyncBarrier.IsExactReady(player.readyState))
					return;
			}

			NetworkIdentity identity = ResolveIdentity(clientRequest, hostOutcome);

			if (identity != null)
			{
				BeginApplyingPacket();
				try
				{
					bool applied = ApplyConfig(identity.gameObject);
					if (!applied)
						return;
					RefreshSideScreenIfOpen(identity.gameObject);
				}
				finally
				{
					EndApplyingPacket();
				}

				if (clientRequest)
				{
					PacketSender.SendToAllClients(this);
				}
			}
			else
			{
				DebugConsole.LogWarning($"[BuildingConfigPacket] FAILED to resolve entity for NetId {NetId} at Cell {Cell}");
			}
		}

		private void BindAuthoritativeIdentity()
		{
			if (NetId == 0 || !NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity identity)
			    || identity == null)
				return;

			Cell = Grid.PosToCell(identity.gameObject);
			DeterministicBuildingId = NetIdHelper.GetDeterministicBuildingId(identity.gameObject);
		}

		private NetworkIdentity ResolveIdentity(bool localIsHost, bool senderIsHost)
		{
			if (NetworkIdentityRegistry.TryGet(NetId, out NetworkIdentity identity)
			    && IdentityMatches(NetId, Cell, DeterministicBuildingId, identity.NetId,
				    Grid.PosToCell(identity.gameObject), NetIdHelper.GetDeterministicBuildingId(identity.gameObject)))
				return identity;

			if (!AllowsCellResolution(localIsHost, senderIsHost) || !Grid.IsValidCell(Cell))
				return null;

			GameObject building = Grid.Objects[Cell, (int)ObjectLayer.Building];
			if (building == null || NetIdHelper.GetDeterministicBuildingId(building) != DeterministicBuildingId)
				return null;

			identity = building.AddOrGet<NetworkIdentity>();
			identity.RegisterIdentity();
			return IdentityMatches(NetId, Cell, DeterministicBuildingId, identity.NetId,
				Grid.PosToCell(building), NetIdHelper.GetDeterministicBuildingId(building))
				? identity
				: null;
		}

		internal static bool AllowsCellResolution(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		internal static bool IdentityMatches(
			int expectedNetId, int expectedCell, int expectedDeterministicId,
			int actualNetId, int actualCell, int actualDeterministicId)
			=> expectedNetId != 0 && expectedDeterministicId != 0
			   && expectedNetId == actualNetId && expectedCell == actualCell
			   && expectedDeterministicId == actualDeterministicId;

		internal static bool IsValidMetadata(
			int netId, int cell, int deterministicId, BuildingConfigType configType,
			int sliderIndex, int referenceNetId, float value, string stringValue)
			=> netId != 0 && deterministicId != 0 && cell >= 0 && cell < MaxCell
			   && configType >= BuildingConfigType.Float && configType <= BuildingConfigType.String
			   && sliderIndex >= 0 && sliderIndex <= MaxSliderIndex
			   && !float.IsNaN(value) && !float.IsInfinity(value)
			   && (stringValue?.Length ?? 0) <= MaxStringLength;

		internal static bool IsBooleanValue(float value) => value == 0f || value == 1f;

		internal static bool IsIntegralValue(float value)
			=> value >= int.MinValue && value <= int.MaxValue && value == (int)value;

		internal static bool IsInRange(float value, float minimum, float maximum)
			=> value >= minimum && value <= maximum;

		internal static void BeginApplyingPacket()
		{
			_applyDepth++;
		}

		internal static void EndApplyingPacket()
		{
			if (_applyDepth > 0)
				_applyDepth--;
		}

		internal static void ResetSessionState()
		{
			_applyDepth = 0;
			_lastRefreshTime = -999f;
		}

		internal static void ResetApplyingPacketForTests() => ResetSessionState();

        /// <summary>
        /// Applies the configuration to the target building.
        /// All handlers are now in the BuildingConfigHandlerRegistry.
        /// </summary>
        private bool ApplyConfig(GameObject go)
		{
			using var _ = Profiler.Scope();

			if (go == null) return false;

            // All handlers are now in the registry
            if (BuildingConfigHandlerRegistry.TryHandle(go, this))
			{
				DebugConsole.Log($"[BuildingConfigPacket] Handled by registry for {go.name}");
				return true;
			}

			// Log unhandled configs for debugging
			DebugConsole.LogWarning($"[BuildingConfigPacket] Unhandled config: Hash={ConfigHash}, Type={ConfigType}, Value={Value}, String={StringValue} on {go.name}");
			return false;
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
