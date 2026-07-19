using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.DLC.SpacedOut;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DLC.SpacedOut
{
	public enum ArtifactSpawnSource : byte
	{
		Pedestal,
		Satellite
	}

	public sealed class ArtifactSpawnStatePacket : IPacket, IHostOnlyPacket
	{
		public int SourceNetId;
		public ArtifactSpawnSource Source;
		public bool Spawned;
		public int ArtifactNetId;
		public string ArtifactId = "";
		public bool ArtifactCharmed;
		public bool TerrestrialArtifact;
		public ArtifactSelectorStateData Selector = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact spawn state");
			writer.Write(SourceNetId);
			writer.Write((byte)Source);
			writer.Write(Spawned);
			writer.Write(ArtifactNetId);
			writer.Write(ArtifactId ?? "");
			writer.Write(ArtifactCharmed);
			writer.Write(TerrestrialArtifact);
			Selector.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			SourceNetId = reader.ReadInt32();
			Source = (ArtifactSpawnSource)reader.ReadByte();
			Spawned = reader.ReadBoolean();
			ArtifactNetId = reader.ReadInt32();
			ArtifactId = reader.ReadString();
			ArtifactCharmed = reader.ReadBoolean();
			TerrestrialArtifact = reader.ReadBoolean();
			Selector = ArtifactSelectorStateData.Deserialize(reader);
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact spawn state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				ArtifactGameplaySync.ApplyOrRetry(this);
		}

		internal bool IsWireValid()
		{
			if (SourceNetId == 0 || Source > ArtifactSpawnSource.Satellite ||
			    Selector?.IsWireValid() != true)
				return false;
			if (!Spawned)
				return ArtifactNetId == 0 && string.IsNullOrEmpty(ArtifactId) &&
				       !ArtifactCharmed && !TerrestrialArtifact;
			return ArtifactNetId != 0 && ArtifactPacketValidation.ValidId(ArtifactId);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}

	public sealed class ArtifactPoiOneTimeStatePacket : IPacket, IHostOnlyPacket
	{
		public int PoiNetId;
		public bool HasSpawnedResources;

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact POI one-time state");
			writer.Write(PoiNetId);
			writer.Write(HasSpawnedResources);
		}

		public void Deserialize(BinaryReader reader)
		{
			PoiNetId = reader.ReadInt32();
			HasSpawnedResources = reader.ReadBoolean();
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact POI one-time state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				ArtifactGameplaySync.ApplyOrRetry(this);
		}

		internal bool IsWireValid() => PoiNetId != 0;

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}

	public sealed class ArtifactAnalysisStatePacket : IPacket, IHostOnlyPacket
	{
		private const float MaxWorkTime = 1_000_000f;
		private const int MaxRevision = 1_000_000;

		public int StationNetId;
		public int Revision;
		public int WorkerNetId;
		public float WorkTimeRemaining;
		public int ArtifactNetId;
		public string ArtifactId = "";
		public bool ArtifactCharmed;
		public bool TerrestrialArtifact;
		public ArtifactSelectorStateData Selector = new();

		public void Serialize(BinaryWriter writer)
		{
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact analysis state");
			writer.Write(StationNetId);
			writer.Write(Revision);
			writer.Write(WorkerNetId);
			writer.Write(WorkTimeRemaining);
			writer.Write(ArtifactNetId);
			writer.Write(ArtifactId ?? "");
			writer.Write(ArtifactCharmed);
			writer.Write(TerrestrialArtifact);
			Selector.Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			StationNetId = reader.ReadInt32();
			Revision = reader.ReadInt32();
			WorkerNetId = reader.ReadInt32();
			WorkTimeRemaining = reader.ReadSingle();
			ArtifactNetId = reader.ReadInt32();
			ArtifactId = reader.ReadString();
			ArtifactCharmed = reader.ReadBoolean();
			TerrestrialArtifact = reader.ReadBoolean();
			Selector = ArtifactSelectorStateData.Deserialize(reader);
			if (!IsWireValid())
				throw new InvalidDataException("Invalid artifact analysis state");
		}

		public void OnDispatched()
		{
			if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
				ArtifactAnalysisSync.ApplyOrRetry(this);
		}

		internal bool IsWireValid()
		{
			if (StationNetId == 0 || Revision < 0 || Revision > MaxRevision ||
			    !ArtifactPacketValidation.IsFinite(WorkTimeRemaining) ||
			    WorkTimeRemaining < 0f || WorkTimeRemaining > MaxWorkTime ||
			    Selector?.IsWireValid() != true)
				return false;
			if (WorkerNetId != 0 && (ArtifactNetId == 0 || !ArtifactCharmed))
				return false;
			if (ArtifactNetId == 0)
				return string.IsNullOrEmpty(ArtifactId) && !ArtifactCharmed && !TerrestrialArtifact;
			return ArtifactPacketValidation.ValidId(ArtifactId);
		}

		internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
			=> !localIsHost && senderIsHost;
	}

	internal static class ArtifactPacketValidation
	{
		internal static bool ValidId(string id)
			=> !string.IsNullOrEmpty(id) && id.Length <= ArtifactSelectorStateData.MaxIdLength;

		internal static bool IsFinite(float value)
			=> !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
