using ONI_Together.DebugTools;
using ONI_Together.Networking.Packets.Architecture;
using System;
using System.IO;
using Shared.Profiling;
using UnityEngine;

namespace ONI_Together.Networking.Packets.World
{
	public class FallingObjectPacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
	{
		public int Cell;
		public ushort ElementIndex;
		public float Mass;
		public float Temperature;
		public byte DiseaseIndex;
		public int DiseaseCount;
		public bool IsLiquid; // Helper to distinguish or debug

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			writer.Write(Cell);
			writer.Write(ElementIndex);
			writer.Write(Mass);
			writer.Write(Temperature);
			writer.Write(DiseaseIndex);
			writer.Write(DiseaseCount);
			writer.Write(IsLiquid);
		}

		public void Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			Cell = reader.ReadInt32();
			ElementIndex = reader.ReadUInt16();
			Mass = reader.ReadSingle();
			Temperature = reader.ReadSingle();
			DiseaseIndex = reader.ReadByte();
			DiseaseCount = reader.ReadInt32();
			IsLiquid = reader.ReadBoolean();
		}

		public void OnDispatched()
		{
			using var _ = Profiler.Scope();

			if (MultiplayerSession.IsHost)
			{
				// Host logic (send to others)
				PacketSender.SendToAllClients(this);
			}
			else
			{
				// Client logic (apply)
				Apply();
			}
		}

		private void Apply()
		{
			using var _ = Profiler.Scope();

			IsApplying = true;
			try
			{
				Vector3 pos = Grid.CellToPosCCC(Cell, Grid.SceneLayer.Ore);

				// FallingWater.AddParticle(int cell, ushort elementIdx, float mass, float temperature, byte diseaseIdx, int diseaseCount, bool skip_sound)
				// Need to verify signature or find the method.
				// Assuming standard Create/AddParticle method exists.
				// Based on standard ONI Modding: FallingWater.instance.AddParticle(...)

				if (FallingWater.instance != null)
				{
					FallingWater.instance.AddParticle(Cell, ElementIndex, Mass, Temperature, DiseaseIndex, DiseaseCount, true, false, false, false);
					// DebugConsole.Log($"[FallingObject] Spawned at {Cell} ({Mass}kg)");
				}
			}
			finally
			{
				IsApplying = false;
			}
		}

		public static bool IsApplying = false;
	}
}
