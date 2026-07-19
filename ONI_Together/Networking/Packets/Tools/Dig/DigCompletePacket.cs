using ONI_Together.Networking;
using ONI_Together.Networking.Packets.Architecture;
using System.IO;
using Shared.Profiling;
using UnityEngine;

public class DigCompletePacket : IPacket, Shared.Interfaces.Networking.IHostOnlyPacket
{
	public int Cell;
	public float Mass;
	public float Temperature;
	public ushort ElementIdx;
	public byte DiseaseIdx;
	public int DiseaseCount;

	public void Serialize(BinaryWriter writer)
	{
		using var _ = Profiler.Scope();

		writer.Write(Cell);
		writer.Write(Mass);
		writer.Write(Temperature);
		writer.Write(ElementIdx);
		writer.Write(DiseaseIdx);
		writer.Write(DiseaseCount);
	}

	public void Deserialize(BinaryReader reader)
	{
		using var _ = Profiler.Scope();

		Cell = reader.ReadInt32();
		Mass = reader.ReadSingle();
		Temperature = reader.ReadSingle();
		ElementIdx = reader.ReadUInt16();
		DiseaseIdx = reader.ReadByte();
		DiseaseCount = reader.ReadInt32();
	}

	public void OnDispatched()
	{
		using var _ = Profiler.Scope();

		if (MultiplayerSession.IsHost)
			return;

		if (!Grid.IsValidCell(Cell))
			return;

		// Destroy dig placers or tile visuals
		for (int i = 0; i < (int)Grid.SceneLayer.SceneMAX; i++)
		{
			GameObject obj = Grid.Objects[Cell, i];
			if (obj != null)
			{
				if (obj.HasTag(new Tag("DigPlacer")))
				{
					Util.KDestroyGameObject(obj);
				}
			}
		}

		// Spawn ore + FX from the dig
		//WorldDamage.Instance.OnDigComplete(Cell, Mass, Temperature, ElementIdx, DiseaseIdx, DiseaseCount);
		// Destroy cell via sim
		WorldDamage.Instance.DestroyCell(Cell);
		// Trigger on solid state changed
		WorldDamage.Instance.OnSolidStateChanged(Cell);
	}
}
