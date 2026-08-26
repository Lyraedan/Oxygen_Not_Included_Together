using ONI_Together.Networking.OxySync.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ONI_Together.Networking.Packets.DuplicantActions
{
	[Flags]
	public enum DuplicantVisualSnapshotFlags : byte
	{
		None = 0,
		FlipX = 1 << 0,
		FlipY = 1 << 1,
		Moving = 1 << 2,
		Teleport = 1 << 3,
	}

	/// <summary>
	/// Compact authoritative keyframe for one duplicant. X/Y use 1/256-cell
	/// fixed-point precision and Z uses the same precision in a signed short.
	/// This is more precise than the previous SyncVar epsilon while using ten
	/// bytes for the position instead of twelve.
	/// </summary>
	public struct DuplicantVisualSnapshot
	{
		public const float PositionScale = 256f;

		public int NetId;
		public uint Sequence;
		public Vector3 Position;
		public NavType NavType;
		public DuplicantVisualSnapshotFlags Flags;

		public bool FlipX => (Flags & DuplicantVisualSnapshotFlags.FlipX) != 0;
		public bool FlipY => (Flags & DuplicantVisualSnapshotFlags.FlipY) != 0;
		public bool IsMoving => (Flags & DuplicantVisualSnapshotFlags.Moving) != 0;
		public bool IsTeleport => (Flags & DuplicantVisualSnapshotFlags.Teleport) != 0;

		internal void Serialize(BinaryWriter writer)
		{
			writer.Write(NetId);
			writer.Write(Sequence);
			writer.Write(QuantizeInt(Position.x));
			writer.Write(QuantizeInt(Position.y));
			writer.Write(QuantizeShort(Position.z));
			writer.Write((byte)NavType);
			writer.Write((byte)Flags);
		}

		internal static DuplicantVisualSnapshot Deserialize(BinaryReader reader)
		{
			return new DuplicantVisualSnapshot
			{
				NetId = reader.ReadInt32(),
				Sequence = reader.ReadUInt32(),
				Position = new Vector3(
					Dequantize(reader.ReadInt32()),
					Dequantize(reader.ReadInt32()),
					Dequantize(reader.ReadInt16())),
				NavType = (NavType)reader.ReadByte(),
				Flags = (DuplicantVisualSnapshotFlags)reader.ReadByte(),
			};
		}

		private static int QuantizeInt(float value)
		{
			double scaled = Math.Round(value * PositionScale);
			if (scaled > int.MaxValue) return int.MaxValue;
			if (scaled < int.MinValue) return int.MinValue;
			return (int)scaled;
		}

		private static short QuantizeShort(float value)
		{
			int scaled = Mathf.RoundToInt(value * PositionScale);
			return (short)Mathf.Clamp(scaled, short.MinValue, short.MaxValue);
		}

		private static float Dequantize(int value) => value / PositionScale;
	}

	/// <summary>
	/// Carries many duplicant keyframes under one packet id and one timestamp.
	/// Forty entries stay below Riptide's 1000-byte application payload limit.
	/// </summary>
	public sealed class DuplicantVisualSnapshotBatchPacket : IPacket
	{
		public const int MaxEntries = 40;

		public long ServerTimestamp;
		public List<DuplicantVisualSnapshot> Snapshots = [];

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			if (Snapshots == null || Snapshots.Count > MaxEntries)
				throw new InvalidDataException($"Duplicant snapshot batch must contain 0-{MaxEntries} entries.");

			writer.Write(ServerTimestamp);
			writer.Write((byte)Snapshots.Count);
			for (int i = 0; i < Snapshots.Count; i++)
				Snapshots[i].Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			ServerTimestamp = reader.ReadInt64();
			int count = reader.ReadByte();
			if (count > MaxEntries)
				throw new InvalidDataException($"Duplicant snapshot batch count {count} exceeds {MaxEntries}.");

			Snapshots = new List<DuplicantVisualSnapshot>(count);
			for (int i = 0; i < count; i++)
				Snapshots.Add(DuplicantVisualSnapshot.Deserialize(reader));
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost || Snapshots == null)
				return;

			for (int i = 0; i < Snapshots.Count; i++)
			{
				var snapshot = Snapshots[i];
				if (!NetworkIdentityRegistry.TryGet(snapshot.NetId, out var entity))
					continue;

				if (entity.TryGetComponent<OxySyncEntityPositionHandler>(out var handler))
					handler.ReceiveVisualSnapshot(snapshot, ServerTimestamp);
			}
		}
	}
}
