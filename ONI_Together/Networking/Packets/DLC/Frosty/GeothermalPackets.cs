using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Frosty;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Frosty
{
	public enum GeothermalControllerPhase : byte
	{
		OfflineInitial,
		OfflineFetchSteel,
		OfflineCheckSupplies,
		OfflineReconnectPipes,
		OfflineNotifyRepaired,
		OfflineRepaired,
		OfflineFilling,
		OfflineFilledReady,
		OfflineFilledObstructed,
		OnlineActive,
		OnlineVentingPre,
		OnlineVentingLoop,
		OnlineVentingPost,
		OnlineObstructed
	}

	public sealed class GeothermalControllerRequestPacket : IPacket, IClientRelayable
	{
		public int TargetNetId;
		public GeothermalController.ProgressState ExpectedProgress;
		public GeothermalController.ProgressState DesiredProgress;

		public GeothermalControllerRequestPacket()
		{
		}

		internal GeothermalControllerRequestPacket(
			int targetNetId,
			GeothermalController.ProgressState expectedProgress,
			GeothermalController.ProgressState desiredProgress)
		{
			TargetNetId = targetNetId;
			ExpectedProgress = expectedProgress;
			DesiredProgress = desiredProgress;
		}

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid geothermal controller target");
			writer.Write(TargetNetId);
			writer.Write((byte)ExpectedProgress);
			writer.Write((byte)DesiredProgress);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			ExpectedProgress = (GeothermalController.ProgressState)reader.ReadByte();
			DesiredProgress = (GeothermalController.ProgressState)reader.ReadByte();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid geothermal controller target");
		}

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool protocolVerified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (!ShouldAccept(MultiplayerSession.IsHost, context, protocolVerified) ||
			    !NetworkIdentityRegistry.TryGetComponent(TargetNetId, out GeothermalController controller) ||
			    controller?.smi == null || controller.State != ExpectedProgress)
				return;

			ISidescreenButtonControl control = controller.smi;
			if (!control.SidescreenEnabled() || !control.SidescreenButtonInteractable())
				return;

			FrostySyncGuard.Run(() => control.OnSidescreenButtonPressed());
			if (controller.State != DesiredProgress)
				return;
			if (GeothermalControllerSync.TryCapture(controller, out GeothermalControllerStatePacket state))
				PacketSender.SendToAllClients(state);
			foreach (GeothermalVent vent in controller.FindVents(requireEnabled: false))
				GeothermalVentSync.SendState(vent);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 && IsValidTransition(ExpectedProgress, DesiredProgress);

		internal static bool IsValidTransition(
			GeothermalController.ProgressState expected,
			GeothermalController.ProgressState desired)
			=> (expected == GeothermalController.ProgressState.NOT_STARTED &&
			    desired == GeothermalController.ProgressState.FETCHING_STEEL) ||
			   ((expected == GeothermalController.ProgressState.FETCHING_STEEL ||
			     expected == GeothermalController.ProgressState.RECONNECTING_PIPES) &&
			    desired == GeothermalController.ProgressState.NOT_STARTED) ||
			   (expected == GeothermalController.ProgressState.AT_CAPACITY &&
			    desired == GeothermalController.ProgressState.COMPLETE);

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;
	}

	public sealed class GeothermalControllerStatePacket : IPacket, IHostOnlyPacket
	{
		public int TargetNetId;
		public GeothermalController.ProgressState Progress;
		public GeothermalControllerPhase Phase;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid geothermal controller state");
			writer.Write(TargetNetId);
			writer.Write((byte)Progress);
			writer.Write((byte)Phase);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			Progress = (GeothermalController.ProgressState)reader.ReadByte();
			Phase = (GeothermalControllerPhase)reader.ReadByte();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid geothermal controller state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				GeothermalControllerSync.TryApply(this);
		}

		internal bool IsWireValid()
			=> TargetNetId != 0 &&
			   (byte)Progress <= (byte)GeothermalController.ProgressState.COMPLETE &&
			   (byte)Phase <= (byte)GeothermalControllerPhase.OnlineObstructed;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}

	public sealed class GeothermalElementState
	{
		public bool IsSolid;
		public SimHashes Element;
		public float Mass;
		public float Temperature;
		public byte DiseaseIndex;
		public int DiseaseCount;
	}

	public sealed class GeothermalVentStatePacket : IPacket, IHostOnlyPacket
	{
		internal const int MaxMaterialCount = 64;
		public int TargetNetId;
		public float RecentMass;
		public bool HasEmitterElement;
		public GeothermalElementState EmitterElement;
		public List<GeothermalElementState> AvailableMaterial = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid geothermal vent state");

			writer.Write(TargetNetId);
			writer.Write(RecentMass);
			writer.Write(HasEmitterElement);
			if (HasEmitterElement)
				WriteElement(writer, EmitterElement);
			writer.Write(AvailableMaterial.Count);
			foreach (GeothermalElementState element in AvailableMaterial)
				WriteElement(writer, element);
		}

		public void Deserialize(BinaryReader reader)
		{
			TargetNetId = reader.ReadInt32();
			RecentMass = reader.ReadSingle();
			HasEmitterElement = reader.ReadBoolean();
			EmitterElement = HasEmitterElement ? ReadElement(reader) : null;
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxMaterialCount)
				throw new InvalidDataException("Invalid geothermal material count");
			AvailableMaterial = new List<GeothermalElementState>(count);
			for (int i = 0; i < count; i++)
				AvailableMaterial.Add(ReadElement(reader));
			if (!IsWireValid())
				throw new InvalidDataException("Invalid geothermal vent state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				GeothermalVentSync.TryApply(this);
		}

		internal bool IsWireValid()
		{
			if (TargetNetId == 0 || !ValidFinite(RecentMass) || RecentMass < 0f ||
			    AvailableMaterial == null || AvailableMaterial.Count > MaxMaterialCount ||
			    (HasEmitterElement && !IsElementValid(EmitterElement)))
				return false;
			foreach (GeothermalElementState element in AvailableMaterial)
			{
				if (!IsElementValid(element))
					return false;
			}
			return true;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private static bool IsElementValid(GeothermalElementState element)
			=> element != null && element.Element != SimHashes.Vacuum &&
			   ValidFinite(element.Mass) && element.Mass >= 0f && element.Mass <= 1_000_000f &&
			   ValidFinite(element.Temperature) && element.Temperature >= 0f && element.Temperature <= 10000f &&
			   element.DiseaseCount >= 0;

		private static bool ValidFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

		private static void WriteElement(BinaryWriter writer, GeothermalElementState element)
		{
			writer.Write(element.IsSolid);
			writer.Write((int)element.Element);
			writer.Write(element.Mass);
			writer.Write(element.Temperature);
			writer.Write(element.DiseaseIndex);
			writer.Write(element.DiseaseCount);
		}

		private static GeothermalElementState ReadElement(BinaryReader reader)
			=> new()
			{
				IsSolid = reader.ReadBoolean(),
				Element = (SimHashes)reader.ReadInt32(),
				Mass = reader.ReadSingle(),
				Temperature = reader.ReadSingle(),
				DiseaseIndex = reader.ReadByte(),
				DiseaseCount = reader.ReadInt32()
			};
	}
}
