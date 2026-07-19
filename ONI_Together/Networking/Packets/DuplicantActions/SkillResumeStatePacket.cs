using System;
using System.Collections.Generic;
using System.IO;
using ONI_Together.Networking.Packets.Architecture;
using ONI_Together.Patches.Duplicant;
using Shared.Interfaces.Networking;

namespace ONI_Together.Networking.Packets.DuplicantActions;

public sealed class SkillResumeAptitudeData
{
	public int SkillGroupHash;
	public float Amount;
}

public sealed class SkillResumeHatData
{
	public string HatId = string.Empty;
	public bool IsUnlocked;
}

public sealed class SkillResumeStateData
{
	internal const int MaxSkillCount = 256;
	internal const int MaxAptitudeCount = 128;
	internal const int MaxHatCount = 256;
	internal const int MaxIdLength = 256;

	public int NetId;
	public ulong Revision;
	public float TotalExperience;
	public int AvailableSkillPoints;
	public List<string> MasteredSkillIds = new();
	public List<string> GrantedSkillIds = new();
	public List<SkillResumeAptitudeData> Aptitudes = new();
	public List<SkillResumeHatData> OwnedHats = new();
	public string CurrentHat = string.Empty;
	public string TargetHat = string.Empty;

	internal void Serialize(BinaryWriter writer)
	{
		if (!IsWireValid())
			throw new InvalidDataException("Invalid skill resume state");

		writer.Write(NetId);
		writer.Write(Revision);
		writer.Write(TotalExperience);
		writer.Write(AvailableSkillPoints);
		WriteIds(writer, MasteredSkillIds);
		WriteIds(writer, GrantedSkillIds);
		WriteAptitudes(writer);
		WriteHats(writer);
		writer.Write(CurrentHat);
		writer.Write(TargetHat);
	}

	internal static SkillResumeStateData Deserialize(BinaryReader reader)
	{
		var data = new SkillResumeStateData
		{
			NetId = reader.ReadInt32(),
			Revision = reader.ReadUInt64(),
			TotalExperience = reader.ReadSingle(),
			AvailableSkillPoints = reader.ReadInt32(),
			MasteredSkillIds = ReadIds(reader, MaxSkillCount),
			GrantedSkillIds = ReadIds(reader, MaxSkillCount),
			Aptitudes = ReadAptitudes(reader),
			OwnedHats = ReadHats(reader),
			CurrentHat = ReadId(reader, optional: true),
			TargetHat = ReadId(reader, optional: true)
		};
		if (!data.IsWireValid())
			throw new InvalidDataException("Invalid skill resume state");
		return data;
	}

	internal bool IsWireValid()
	{
		if (NetId == 0 || Revision == 0 || !IsFinite(TotalExperience) || TotalExperience < 0f ||
		    AvailableSkillPoints < 0 || !ValidOptionalId(CurrentHat) || !ValidOptionalId(TargetHat))
			return false;
		if (!ValidUniqueIds(MasteredSkillIds, MaxSkillCount) ||
		    !ValidUniqueIds(GrantedSkillIds, MaxSkillCount) || !ValidGrantedSubset())
			return false;
		return ValidAptitudes() && ValidHats();
	}

	private bool ValidGrantedSubset()
	{
		var mastered = new HashSet<string>(MasteredSkillIds, StringComparer.Ordinal);
		foreach (string skillId in GrantedSkillIds)
		{
			if (!mastered.Contains(skillId))
				return false;
		}
		return true;
	}

	private bool ValidAptitudes()
	{
		if (Aptitudes == null || Aptitudes.Count > MaxAptitudeCount)
			return false;
		var hashes = new HashSet<int>();
		foreach (SkillResumeAptitudeData aptitude in Aptitudes)
		{
			if (aptitude == null || !IsFinite(aptitude.Amount) || !hashes.Add(aptitude.SkillGroupHash))
				return false;
		}
		return true;
	}

	private bool ValidHats()
	{
		if (OwnedHats == null || OwnedHats.Count > MaxHatCount)
			return false;
		var ids = new HashSet<string>(StringComparer.Ordinal);
		foreach (SkillResumeHatData hat in OwnedHats)
		{
			if (hat == null || !ValidRequiredId(hat.HatId) || !ids.Add(hat.HatId))
				return false;
		}
		return true;
	}

	private static bool ValidUniqueIds(List<string> ids, int maxCount)
	{
		if (ids == null || ids.Count > maxCount)
			return false;
		var unique = new HashSet<string>(StringComparer.Ordinal);
		foreach (string id in ids)
		{
			if (!ValidRequiredId(id) || !unique.Add(id))
				return false;
		}
		return true;
	}

	private static void WriteIds(BinaryWriter writer, List<string> ids)
	{
		writer.Write(ids.Count);
		foreach (string id in ids)
			writer.Write(id);
	}

	private void WriteAptitudes(BinaryWriter writer)
	{
		writer.Write(Aptitudes.Count);
		foreach (SkillResumeAptitudeData aptitude in Aptitudes)
		{
			writer.Write(aptitude.SkillGroupHash);
			writer.Write(aptitude.Amount);
		}
	}

	private void WriteHats(BinaryWriter writer)
	{
		writer.Write(OwnedHats.Count);
		foreach (SkillResumeHatData hat in OwnedHats)
		{
			writer.Write(hat.HatId);
			writer.Write(hat.IsUnlocked);
		}
	}

	private static List<string> ReadIds(BinaryReader reader, int maxCount)
	{
		int count = ReadCount(reader, maxCount, "skill");
		var ids = new List<string>(count);
		for (int i = 0; i < count; i++)
			ids.Add(ReadId(reader, optional: false));
		return ids;
	}

	private static List<SkillResumeAptitudeData> ReadAptitudes(BinaryReader reader)
	{
		int count = ReadCount(reader, MaxAptitudeCount, "aptitude");
		var values = new List<SkillResumeAptitudeData>(count);
		for (int i = 0; i < count; i++)
		{
			values.Add(new SkillResumeAptitudeData
			{
				SkillGroupHash = reader.ReadInt32(),
				Amount = reader.ReadSingle()
			});
		}
		return values;
	}

	private static List<SkillResumeHatData> ReadHats(BinaryReader reader)
	{
		int count = ReadCount(reader, MaxHatCount, "hat");
		var hats = new List<SkillResumeHatData>(count);
		for (int i = 0; i < count; i++)
		{
			hats.Add(new SkillResumeHatData
			{
				HatId = ReadId(reader, optional: false),
				IsUnlocked = reader.ReadBoolean()
			});
		}
		return hats;
	}

	private static int ReadCount(BinaryReader reader, int maximum, string label)
	{
		int count = reader.ReadInt32();
		if (count < 0 || count > maximum)
			throw new InvalidDataException($"Invalid skill resume {label} count: {count}");
		return count;
	}

	private static string ReadId(BinaryReader reader, bool optional)
	{
		string value = reader.ReadString();
		if (optional ? !ValidOptionalId(value) : !ValidRequiredId(value))
			throw new InvalidDataException("Invalid skill resume identifier");
		return value;
	}

	private static bool ValidRequiredId(string value)
		=> !string.IsNullOrEmpty(value) && value.Length <= MaxIdLength;

	private static bool ValidOptionalId(string value)
		=> value != null && value.Length <= MaxIdLength;

	private static bool IsFinite(float value)
		=> !float.IsNaN(value) && !float.IsInfinity(value);
}

public sealed class SkillResumeStatePacket : IPacket, IHostOnlyPacket
{
	public SkillResumeStateData Data = new();

	public SkillResumeStatePacket()
	{
	}

	internal SkillResumeStatePacket(SkillResumeStateData data) => Data = data;

	public void Serialize(BinaryWriter writer) => Data.Serialize(writer);

	public void Deserialize(BinaryReader reader) => Data = SkillResumeStateData.Deserialize(reader);

	public void OnDispatched()
	{
		if (ShouldApply(MultiplayerSession.IsHost, PacketHandler.CurrentContext.SenderIsHost))
			SkillResumeSync.TryApplySnapshot(Data);
	}

	internal static bool ShouldApply(bool localIsHost, bool senderIsHost)
		=> !localIsHost && senderIsHost;
}
