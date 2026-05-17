using ONI_Together.Networking.Components;
using ONI_Together.Networking.Packets.Architecture;
using Shared.Profiling;
using System;
using System.Collections.Generic;
using System.IO;

namespace ONI_Together.Networking.Packets.Chores
{
	public class ChoreErrandsPacket : IPacket
	{
		public const int MaxEntries = 32;

		public int DupeNetId;
		public List<ErrandEntry> Entries = new();

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();
			writer.Write(DupeNetId);
			int count = Math.Min(Entries.Count, MaxEntries);
			writer.Write(count);
			for (int i = 0; i < count; i++)
				Entries[i].Serialize(writer);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();
			DupeNetId = reader.ReadInt32();
			int count = reader.ReadInt32();
			if (count < 0 || count > MaxEntries)
			{
				Entries = new List<ErrandEntry>();
				return;
			}
			Entries = new List<ErrandEntry>(count);
			for (int i = 0; i < count; i++)
				Entries.Add(ErrandEntry.Deserialize(reader));
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();
			if (!MultiplayerSession.IsClient) return;
			if (!NetworkIdentityRegistry.TryGet(DupeNetId, out var entity)) return;

			var receiver = entity.gameObject.GetComponent<ClientReceiver_ChoreErrands>();
			if (receiver == null) return;
			receiver.Apply(Entries);
		}
	}
}
