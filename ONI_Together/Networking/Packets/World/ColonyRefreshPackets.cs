using System;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Networking.Refresh;

namespace ONI_Together.Networking.Packets.World
{
    // The base is deliberately not an IPacket, so auto-registration includes only concrete packets.
    internal abstract class ColonyRefreshPacket : ISenderAwarePacket
    {
        public Guid RequestId;
        public ulong? SenderId { get; set; }
        protected void WriteId(BinaryWriter writer) => writer.Write(RequestId.ToByteArray());
        protected void ReadId(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            RequestId = new Guid(bytes);
        }
        public abstract void Serialize(BinaryWriter writer);
        public abstract void Deserialize(BinaryReader reader);
        public abstract void OnDispatched();
    }

    internal sealed class ColonyRefreshRequestPacket : ColonyRefreshPacket, IPacket
    {
        public bool Cancel;
        public ColonyRefreshRequestPacket() { }
        public override void Serialize(BinaryWriter writer) { WriteId(writer); writer.Write(Cancel); }
        public override void Deserialize(BinaryReader reader) { ReadId(reader); Cancel = reader.ReadBoolean(); }
        public override void OnDispatched() => ColonyRefreshCoordinator.Instance?.ReceiveRequest(this);
    }

    internal sealed class ColonyRefreshStatusPacket : ColonyRefreshPacket, IPacket
    {
        public RefreshState State;
        public int Done, Total, Skipped, RetrySeconds;
        public ColonyRefreshStatusPacket() { }
        public override void Serialize(BinaryWriter writer)
        {
            WriteId(writer); writer.Write((byte)State);
            writer.Write(Done); writer.Write(Total); writer.Write(Skipped); writer.Write(RetrySeconds);
        }
        public override void Deserialize(BinaryReader reader)
        {
            ReadId(reader); State = (RefreshState)reader.ReadByte();
            Done = reader.ReadInt32(); Total = reader.ReadInt32(); Skipped = reader.ReadInt32(); RetrySeconds = reader.ReadInt32();
            if (State < RefreshState.Accepted || State > RefreshState.Cancelled || Done < 0 || Total < Done || Total > 2000000
                || Skipped < 0 || RetrySeconds < 0 || RetrySeconds > 30) throw new InvalidDataException("Invalid refresh status");
        }
        public override void OnDispatched() => ColonyRefreshCoordinator.Instance?.ReceiveStatus(this);
    }

    internal sealed class ColonyRefreshBatchPacket : ColonyRefreshPacket, IPacket
    {
        public int Sequence, RecordLength, Offset;
        public RefreshRecordKind Kind;
        public byte[] Data;
        public ColonyRefreshBatchPacket() { }
        public override void Serialize(BinaryWriter writer)
        {
            WriteId(writer); writer.Write(Sequence); writer.Write((byte)Kind);
            writer.Write(RecordLength); writer.Write(Offset); writer.Write(Data.Length); writer.Write(Data);
        }
        public override void Deserialize(BinaryReader reader)
        {
            ReadId(reader); Sequence = reader.ReadInt32(); Kind = (RefreshRecordKind)reader.ReadByte();
            RecordLength = reader.ReadInt32(); Offset = reader.ReadInt32(); int length = reader.ReadInt32();
            if (Sequence < 0 || Kind > RefreshRecordKind.Logic || RecordLength <= 0 || RecordLength > RefreshLimits.RecordBytes
                || length <= 0 || length > RefreshLimits.FragmentBytes || Offset < 0 || Offset > RecordLength - length
                || length > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Invalid refresh batch");
            Data = reader.ReadBytes(length);
        }
        public override void OnDispatched() => ColonyRefreshCoordinator.Instance?.ReceiveBatch(this);
    }

    internal sealed class ColonyRefreshAckPacket : ColonyRefreshPacket, IPacket
    {
        public int Sequence;
        public bool Applied;
        public ColonyRefreshAckPacket() { }
        public override void Serialize(BinaryWriter writer) { WriteId(writer); writer.Write(Sequence); writer.Write(Applied); }
        public override void Deserialize(BinaryReader reader) { ReadId(reader); Sequence = reader.ReadInt32(); Applied = reader.ReadBoolean(); }
        public override void OnDispatched() => ColonyRefreshCoordinator.Instance?.ReceiveAck(this);
    }
}
