#if DEBUG
using System.IO;
using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.World
{
	public sealed class SoakTickRunPacket : IPacket, IHostOnlyPacket
	{
		public int RunId;
		public int SampleId;
		public int TickCount;
		public float StartTotalTime;
		public bool IsRepairBaselineWarmup;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(TickCount);
			writer.Write(StartTotalTime);
			writer.Write(IsRepairBaselineWarmup);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			TickCount = reader.ReadInt32();
			StartTotalTime = reader.ReadSingle();
			IsRepairBaselineWarmup = reader.ReadBoolean();
			Validate();
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveTickPrepare(this);
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, StartTotalTime);
			if (TickCount <= 0 || TickCount > SoakHashWire.MaxTickCount)
				throw new InvalidDataException("Invalid soak tick count");
		}
	}

	public sealed class SoakTickReadyAckPacket : IPacket
	{
		public int RunId;
		public int SampleId;
		public bool Ready;

		public void Serialize(BinaryWriter writer)
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(Ready);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			Ready = reader.ReadBoolean();
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveTickReadyAck(this, PacketHandler.CurrentContext);
		}
	}

	public sealed class SoakTickStartPacket : IPacket, IHostOnlyPacket
	{
		public int RunId;
		public int SampleId;

		public void Serialize(BinaryWriter writer)
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			writer.Write(RunId);
			writer.Write(SampleId);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveTickStart(RunId, SampleId);
		}
	}

	public sealed class SoakTickBarrierAckPacket : IPacket
	{
		public int RunId;
		public int SampleId;
		public int CompletedTicks;
		public bool StartedPaused;
		public bool IsPaused;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(CompletedTicks);
			writer.Write(StartedPaused);
			writer.Write(IsPaused);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			CompletedTicks = reader.ReadInt32();
			StartedPaused = reader.ReadBoolean();
			IsPaused = reader.ReadBoolean();
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveTickBarrierAck(this, PacketHandler.CurrentContext);
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			if (CompletedTicks < 0 || CompletedTicks > SoakHashWire.MaxTickCount)
				throw new InvalidDataException("Invalid completed soak tick count");
		}
	}

	public sealed class SoakTickCancelPacket : IPacket, IHostOnlyPacket
	{
		public int RunId;

		public void Serialize(BinaryWriter writer)
		{
			if (RunId <= 0)
				throw new InvalidDataException("Invalid cancelled soak run");
			writer.Write(RunId);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			if (RunId <= 0)
				throw new InvalidDataException("Invalid cancelled soak run");
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveTickCancel(RunId);
		}
	}

	public sealed class SoakSegmentFencePacket : IPacket, IHostOnlyPacket
	{
		public int RunId;
		public int SampleId;
		public int CompletedTicks;
		public long RepairSequenceCut;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(CompletedTicks);
			writer.Write(RepairSequenceCut);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			CompletedTicks = reader.ReadInt32();
			RepairSequenceCut = reader.ReadInt64();
			Validate();
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveSegmentFence(this);
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			if (CompletedTicks <= 0 || CompletedTicks > SoakHashWire.MaxTickCount
			    || RepairSequenceCut < 0)
				throw new InvalidDataException("Invalid completed soak fence tick count");
		}
	}

	public sealed class SoakSegmentFenceAckPacket : IPacket
	{
		public int RunId;
		public int SampleId;
		public int CompletedTicks;
		public long RepairSequenceCut;
		public bool KeyframeApplied;

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(CompletedTicks);
			writer.Write(RepairSequenceCut);
			writer.Write(KeyframeApplied);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			CompletedTicks = reader.ReadInt32();
			RepairSequenceCut = reader.ReadInt64();
			KeyframeApplied = reader.ReadBoolean();
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveSegmentFenceAck(this, PacketHandler.CurrentContext);
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, 0f);
			if (CompletedTicks <= 0 || CompletedTicks > SoakHashWire.MaxTickCount
			    || RepairSequenceCut < 0)
				throw new InvalidDataException("Invalid completed soak fence ACK tick count");
		}
	}

	public sealed class SoakHashCheckpointPacket : IPacket, IHostOnlyPacket
	{
		public int RunId;
		public int SampleId;
		public int CompletedTicks;
		public int Cycle;
		public float CycleTime;
		public int GridRecords;
		public int EntityLifecycleRecords;
		public int WorldMembershipRecords;
		public int StorageMembershipRecords;
		public int ClusterRocketRecords;
		public SoakLifecycleDiagnostics Lifecycle = new();
		public bool IsFinal;
		public byte[] GridHash = SoakHashWire.NewHash();
		public byte[] EntityLifecycleHash = SoakHashWire.NewHash();
		public byte[] WorldMembershipHash = SoakHashWire.NewHash();
		public byte[] StorageMembershipHash = SoakHashWire.NewHash();
		public byte[] ClusterRocketHash = SoakHashWire.NewHash();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(CompletedTicks);
			writer.Write(Cycle);
			writer.Write(CycleTime);
			writer.Write(GridRecords);
			writer.Write(EntityLifecycleRecords);
			writer.Write(WorldMembershipRecords);
			writer.Write(StorageMembershipRecords);
			writer.Write(ClusterRocketRecords);
			Lifecycle.Serialize(writer);
			writer.Write(IsFinal);
			writer.Write(GridHash);
			writer.Write(EntityLifecycleHash);
			writer.Write(WorldMembershipHash);
			writer.Write(StorageMembershipHash);
			writer.Write(ClusterRocketHash);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			CompletedTicks = reader.ReadInt32();
			Cycle = reader.ReadInt32();
			CycleTime = reader.ReadSingle();
			GridRecords = reader.ReadInt32();
			EntityLifecycleRecords = reader.ReadInt32();
			WorldMembershipRecords = reader.ReadInt32();
			StorageMembershipRecords = reader.ReadInt32();
			ClusterRocketRecords = reader.ReadInt32();
			Lifecycle = SoakLifecycleDiagnostics.Deserialize(reader);
			IsFinal = reader.ReadBoolean();
			GridHash = SoakHashWire.ReadHash(reader);
			EntityLifecycleHash = SoakHashWire.ReadHash(reader);
			WorldMembershipHash = SoakHashWire.ReadHash(reader);
			StorageMembershipHash = SoakHashWire.ReadHash(reader);
			ClusterRocketHash = SoakHashWire.ReadHash(reader);
			Validate();
		}

		public void OnDispatched()
		{
			if (!MultiplayerSession.IsHost)
				SoakStateHashProbe.SendHashReport(this);
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, Cycle * 600f + CycleTime);
			if (CompletedTicks <= 0 || CompletedTicks > SoakHashWire.MaxTickCount
				|| Cycle < 0 || CycleTime < 0f || CycleTime > 600f)
				throw new InvalidDataException("Invalid soak checkpoint time");
			SoakHashWire.ValidateState(GridRecords, GridHash);
			SoakHashWire.ValidateState(EntityLifecycleRecords, EntityLifecycleHash);
			SoakHashWire.ValidateState(WorldMembershipRecords, WorldMembershipHash);
			SoakHashWire.ValidateState(StorageMembershipRecords, StorageMembershipHash);
			SoakHashWire.ValidateState(ClusterRocketRecords, ClusterRocketHash);
			if (Lifecycle == null)
				throw new InvalidDataException("Missing soak lifecycle diagnostics");
			Lifecycle.Validate();
		}
	}

	public sealed class SoakHashReportPacket : IPacket
	{
		public int RunId;
		public int SampleId;
		public int CompletedTicks;
		public int Cycle;
		public float CycleTime;
		public int GridRecords;
		public int EntityLifecycleRecords;
		public int WorldMembershipRecords;
		public int StorageMembershipRecords;
		public int ClusterRocketRecords;
		public SoakLifecycleDiagnostics Lifecycle = new();
		public byte[] GridHash = SoakHashWire.NewHash();
		public byte[] EntityLifecycleHash = SoakHashWire.NewHash();
		public byte[] WorldMembershipHash = SoakHashWire.NewHash();
		public byte[] StorageMembershipHash = SoakHashWire.NewHash();
		public byte[] ClusterRocketHash = SoakHashWire.NewHash();

		public void Serialize(BinaryWriter writer)
		{
			Validate();
			writer.Write(RunId);
			writer.Write(SampleId);
			writer.Write(CompletedTicks);
			writer.Write(Cycle);
			writer.Write(CycleTime);
			writer.Write(GridRecords);
			writer.Write(EntityLifecycleRecords);
			writer.Write(WorldMembershipRecords);
			writer.Write(StorageMembershipRecords);
			writer.Write(ClusterRocketRecords);
			Lifecycle.Serialize(writer);
			writer.Write(GridHash);
			writer.Write(EntityLifecycleHash);
			writer.Write(WorldMembershipHash);
			writer.Write(StorageMembershipHash);
			writer.Write(ClusterRocketHash);
		}

		public void Deserialize(BinaryReader reader)
		{
			RunId = reader.ReadInt32();
			SampleId = reader.ReadInt32();
			CompletedTicks = reader.ReadInt32();
			Cycle = reader.ReadInt32();
			CycleTime = reader.ReadSingle();
			GridRecords = reader.ReadInt32();
			EntityLifecycleRecords = reader.ReadInt32();
			WorldMembershipRecords = reader.ReadInt32();
			StorageMembershipRecords = reader.ReadInt32();
			ClusterRocketRecords = reader.ReadInt32();
			Lifecycle = SoakLifecycleDiagnostics.Deserialize(reader);
			GridHash = SoakHashWire.ReadHash(reader);
			EntityLifecycleHash = SoakHashWire.ReadHash(reader);
			WorldMembershipHash = SoakHashWire.ReadHash(reader);
			StorageMembershipHash = SoakHashWire.ReadHash(reader);
			ClusterRocketHash = SoakHashWire.ReadHash(reader);
			Validate();
		}

		public void OnDispatched()
		{
			if (MultiplayerSession.IsHost)
				SoakStateHashProbe.ReceiveHashReport(this, PacketHandler.CurrentContext);
		}

		private void Validate()
		{
			SoakHashWire.ValidateMarker(RunId, SampleId, Cycle * 600f + CycleTime);
			if (CompletedTicks <= 0 || CompletedTicks > SoakHashWire.MaxTickCount
			    || Cycle < 0 || CycleTime < 0f || CycleTime > 600f)
				throw new InvalidDataException("Invalid soak report tick count");
			SoakHashWire.ValidateState(GridRecords, GridHash);
			SoakHashWire.ValidateState(EntityLifecycleRecords, EntityLifecycleHash);
			SoakHashWire.ValidateState(WorldMembershipRecords, WorldMembershipHash);
			SoakHashWire.ValidateState(StorageMembershipRecords, StorageMembershipHash);
			SoakHashWire.ValidateState(ClusterRocketRecords, ClusterRocketHash);
			if (Lifecycle == null)
				throw new InvalidDataException("Missing soak lifecycle diagnostics");
			Lifecycle.Validate();
		}
	}

	internal static class SoakHashWire
	{
		internal const int HashLength = 32;
		internal const int MaxTickCount = 60 * 60 * 20;
		private const int MaxRecords = 4 * 1024 * 1024;

		internal static byte[] NewHash() => new byte[HashLength];

		internal static byte[] ReadHash(BinaryReader reader)
		{
			byte[] hash = reader.ReadBytes(HashLength);
			if (hash.Length != HashLength)
				throw new EndOfStreamException("Soak hash payload is truncated");
			return hash;
		}

		internal static void ValidateMarker(int runId, int sampleId, float totalTime)
		{
			if (runId <= 0 || sampleId <= 0 || sampleId > 1000
				|| float.IsNaN(totalTime) || float.IsInfinity(totalTime) || totalTime < 0f)
			{
				throw new InvalidDataException("Invalid soak checkpoint marker");
			}
		}

		internal static void ValidateState(int records, byte[] hash)
		{
			if (records < 0 || records > MaxRecords || hash == null || hash.Length != HashLength)
			{
				throw new InvalidDataException("Invalid soak state hash payload");
			}
		}
	}
}
#endif
