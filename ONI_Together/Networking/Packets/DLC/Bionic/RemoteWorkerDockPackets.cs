using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.Bionic;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.Bionic
{
	public sealed class RemoteWorkerElementState
	{
		internal const float MaxTemperature = 10000f;
		internal const int MaxDiseaseCount = 1_000_000_000;

		public bool Present;
		public int ElementId;
		public float Mass;
		public float Temperature;
		public byte DiseaseIndex = byte.MaxValue;
		public int DiseaseCount;

		internal void Serialize(BinaryWriter writer)
		{
			writer.Write(Present);
			if (!Present)
				return;
			writer.Write(ElementId);
			writer.Write(Mass);
			writer.Write(Temperature);
			writer.Write(DiseaseIndex);
			writer.Write(DiseaseCount);
		}

		internal static RemoteWorkerElementState Deserialize(BinaryReader reader)
		{
			var state = new RemoteWorkerElementState { Present = reader.ReadBoolean() };
			if (!state.Present)
				return state;
			state.ElementId = reader.ReadInt32();
			state.Mass = reader.ReadSingle();
			state.Temperature = reader.ReadSingle();
			state.DiseaseIndex = reader.ReadByte();
			state.DiseaseCount = reader.ReadInt32();
			return state;
		}

		internal bool IsWireValid(float maximumMass)
			=> !Present || ElementId != 0 && IsBounded(Mass, maximumMass) && Mass > 0f &&
			   IsBounded(Temperature, MaxTemperature) && DiseaseCount >= 0 &&
			   DiseaseCount <= MaxDiseaseCount && (DiseaseCount > 0 || DiseaseIndex == byte.MaxValue);

		private static bool IsBounded(float value, float maximum)
			=> !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= maximum;
	}

	public sealed class RemoteWorkerDockStatePacket : IPacket, IHostOnlyPacket
	{
		internal const float MaxCharge = 60f;
		internal const float MaxWorkerResourceMass = 20.000002f;
		internal const float MaxDockResourceMass = 20000f;
		internal const float MaxWorkerMass = 1000f;

		public int DockNetId;
		public long Revision;
		public int WorkerNetId;
		public int TerminalNetId;
		public bool Docked;
		public bool PlayNewWorker;
		public bool ActivelyControlled;
		public bool ActivelyWorking;
		public bool Available;
		public float Charge;
		public RemoteWorkerElementState WorkerPrimary = new();
		public RemoteWorkerElementState WorkerOil = new();
		public RemoteWorkerElementState WorkerGunk = new();
		public RemoteWorkerElementState DockMaterial = new();
		public RemoteWorkerElementState DockOil = new();
		public RemoteWorkerElementState DockGunk = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid remote worker dock state");
			writer.Write(DockNetId);
			writer.Write(Revision);
			writer.Write(WorkerNetId);
			writer.Write(TerminalNetId);
			writer.Write(Docked);
			writer.Write(PlayNewWorker);
			writer.Write(ActivelyControlled);
			writer.Write(ActivelyWorking);
			writer.Write(Available);
			writer.Write(Charge);
			WorkerPrimary.Serialize(writer);
			WorkerOil.Serialize(writer);
			WorkerGunk.Serialize(writer);
			DockMaterial.Serialize(writer);
			DockOil.Serialize(writer);
			DockGunk.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			DockNetId = reader.ReadInt32();
			Revision = reader.ReadInt64();
			WorkerNetId = reader.ReadInt32();
			TerminalNetId = reader.ReadInt32();
			Docked = reader.ReadBoolean();
			PlayNewWorker = reader.ReadBoolean();
			ActivelyControlled = reader.ReadBoolean();
			ActivelyWorking = reader.ReadBoolean();
			Available = reader.ReadBoolean();
			Charge = reader.ReadSingle();
			WorkerPrimary = RemoteWorkerElementState.Deserialize(reader);
			WorkerOil = RemoteWorkerElementState.Deserialize(reader);
			WorkerGunk = RemoteWorkerElementState.Deserialize(reader);
			DockMaterial = RemoteWorkerElementState.Deserialize(reader);
			DockOil = RemoteWorkerElementState.Deserialize(reader);
			DockGunk = RemoteWorkerElementState.Deserialize(reader);
			if (!IsWireValid())
				throw new InvalidDataException("Invalid remote worker dock state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				RemoteWorkerDockSync.TryApplyOrQueue(this);
		}

		internal bool IsWireValid()
		{
			if (WorkerPrimary == null || WorkerOil == null || WorkerGunk == null || DockMaterial == null ||
			    DockOil == null || DockGunk == null || DockNetId == 0 || Revision <= 0 ||
			    WorkerNetId == DockNetId || TerminalNetId == DockNetId ||
			    (WorkerNetId != 0 && WorkerNetId == TerminalNetId) || !IsBounded(Charge, MaxCharge) ||
			    !WorkerPrimary.IsWireValid(MaxWorkerMass) ||
			    !WorkerOil.IsWireValid(MaxWorkerResourceMass) ||
			    !WorkerGunk.IsWireValid(MaxWorkerResourceMass) ||
			    !DockMaterial.IsWireValid(MaxDockResourceMass) ||
			    !DockOil.IsWireValid(MaxDockResourceMass) ||
			    !DockGunk.IsWireValid(MaxDockResourceMass))
				return false;
			if (WorkerNetId != 0)
				return WorkerPrimary.Present;
			return TerminalNetId == 0 && !Docked && !PlayNewWorker && !ActivelyControlled &&
			       !ActivelyWorking && !Available && Charge == 0f && !WorkerPrimary.Present &&
			       !WorkerOil.Present && !WorkerGunk.Present;
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;

		private static bool IsBounded(float value, float maximum)
			=> !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= maximum;
	}

	public sealed class RemoteWorkerDockSelectionRequestPacket : IPacket, IClientRelayable
	{
		public int TerminalNetId;
		public int ExpectedDockNetId;
		public int DesiredDockNetId;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid remote worker dock selection request");
			writer.Write(TerminalNetId);
			writer.Write(ExpectedDockNetId);
			writer.Write(DesiredDockNetId);
		}

		public void Deserialize(BinaryReader reader)
		{
			TerminalNetId = reader.ReadInt32();
			ExpectedDockNetId = reader.ReadInt32();
			DesiredDockNetId = reader.ReadInt32();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid remote worker dock selection request");
		}

		public void OnDispatched()
		{
			DispatchContext context = PacketHandler.CurrentContext;
			bool verified = MultiplayerSession.GetPlayer(context.SenderId)?.ProtocolVerified == true;
			if (ShouldAccept(MultiplayerSession.IsHost, context, verified) &&
			    RemoteWorkerDockSelectionSync.TryApplyRequest(this, out RemoteWorkerDockSelectionStatePacket state))
				PacketSender.SendToAllClients(state);
		}

		internal bool IsWireValid()
			=> TerminalNetId != 0 && ExpectedDockNetId != DesiredDockNetId &&
			   TerminalNetId != ExpectedDockNetId && TerminalNetId != DesiredDockNetId;

		internal static bool ShouldAccept(bool localIsHost, DispatchContext context, bool protocolVerified)
			=> localIsHost && !context.SenderIsHost && context.IsVerifiedHostBroadcast && protocolVerified;
	}

	public sealed class RemoteWorkerDockSelectionStatePacket : IPacket, IHostOnlyPacket
	{
		public int TerminalNetId;
		public int DockNetId;
		public long Revision;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid remote worker dock selection state");
			writer.Write(TerminalNetId);
			writer.Write(DockNetId);
			writer.Write(Revision);
		}

		public void Deserialize(BinaryReader reader)
		{
			TerminalNetId = reader.ReadInt32();
			DockNetId = reader.ReadInt32();
			Revision = reader.ReadInt64();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid remote worker dock selection state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				RemoteWorkerDockSelectionSync.TryApply(this);
		}

		internal bool IsWireValid() => TerminalNetId != 0 && DockNetId != TerminalNetId && Revision > 0;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}
}
