using ONI_Together.DebugTools;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared.Profiling;

namespace ONI_Together.Networking.Packets.Social
{

	public struct ImmigrantOptionEntry
	{
		private const int MaxTraits = 64;
		private const int MaxSkillAptitudes = 64;
		private const int MaxStartingLevels = 64;

		public ImmigrantOptionEntry()
		{

		}


		public static readonly ImmigrantOptionEntry INVALID = new ImmigrantOptionEntry() { EntryType = -1 };
		public bool IsValid => EntryType >= 0;

		public int EntryType = -1; //-1 for invalid, 0 for duplicant, 1 for care package
		public bool IsDuplicant => EntryType == 0;

		// Duplicant Data
		public string Name;
		public string PersonalityId;

		// Traits (stored as IDs)
		public List<string> TraitIds;
		public string StressTraitId;
		public string JoyTraitId;

		// Other stats
		public int VoiceIdx;
		public string StickerType;

		// Skill aptitudes (SkillGroup ID -> float)
		public Dictionary<string, float> SkillAptitudes;

		// Starting levels (Attribute ID -> int)
		public Dictionary<string, int> StartingLevels;

		// Care Package Data
		public string CarePackageId;
		public float Quantity;
		public string CarePackageFacadeId;

		public void Serialize(BinaryWriter writer)
		{
			using var _ = Profiler.Scope();

			if (EntryType != 0 && EntryType != 1)
				throw new InvalidDataException($"Invalid immigrant option type: {EntryType}");
			writer.Write(EntryType);
			if (EntryType == 0)
			{
				writer.Write(Name ?? "Unknown");
				writer.Write(PersonalityId ?? "Hassan");

				// Traits list
				int traitCount = TraitIds?.Count ?? 0;
				ValidateCount(traitCount, MaxTraits, "traits");
				writer.Write(traitCount);
				if (TraitIds != null)
				{
					foreach (var traitId in TraitIds)
					{
						writer.Write(traitId ?? "");
					}
				}

				// Special traits
				writer.Write(StressTraitId ?? "");
				writer.Write(JoyTraitId ?? "");

				// Other stats
				writer.Write(VoiceIdx);
				writer.Write(StickerType ?? "");

				// Skill aptitudes
				int aptCount = SkillAptitudes?.Count ?? 0;
				ValidateCount(aptCount, MaxSkillAptitudes, "skill aptitudes");
				writer.Write(aptCount);
				if (SkillAptitudes != null)
				{
					foreach (var kvp in SkillAptitudes)
					{
						writer.Write(kvp.Key ?? "");
						writer.Write(kvp.Value);
					}
				}

				// Starting levels
				int levelCount = StartingLevels?.Count ?? 0;
				ValidateCount(levelCount, MaxStartingLevels, "starting levels");
				writer.Write(levelCount);
				if (StartingLevels != null)
				{
					foreach (var kvp in StartingLevels)
					{
						writer.Write(kvp.Key ?? "");
						writer.Write(kvp.Value);
					}
				}
			}
			else if (EntryType == 1)
			{
				writer.Write(CarePackageId ?? "None");
				writer.Write(Quantity);
				writer.Write(CarePackageFacadeId ?? string.Empty);
			}

		}
		public static ImmigrantOptionEntry Deserialize(BinaryReader reader)
		{
			using var _ = Profiler.Scope();

			var opt = new ImmigrantOptionEntry();
			opt.EntryType = reader.ReadInt32();
			if (opt.EntryType != 0 && opt.EntryType != 1)
				throw new InvalidDataException($"Invalid immigrant option type: {opt.EntryType}");
			if (opt.EntryType == 0)
			{
				opt.Name = reader.ReadString();
				opt.PersonalityId = reader.ReadString();

				// Traits list
				int traitCount = reader.ReadInt32();
				ValidateCount(traitCount, MaxTraits, "traits");
				opt.TraitIds = new List<string>(traitCount);
				for (int t = 0; t < traitCount; t++)
				{
					opt.TraitIds.Add(reader.ReadString());
				}

				// Special traits
				opt.StressTraitId = reader.ReadString();
				opt.JoyTraitId = reader.ReadString();

				// Other stats
				opt.VoiceIdx = reader.ReadInt32();
				opt.StickerType = reader.ReadString();

				// Skill aptitudes
				int aptCount = reader.ReadInt32();
				ValidateCount(aptCount, MaxSkillAptitudes, "skill aptitudes");
				opt.SkillAptitudes = new Dictionary<string, float>(aptCount);
				for (int a = 0; a < aptCount; a++)
				{
					string key = reader.ReadString();
					float val = reader.ReadSingle();
					opt.SkillAptitudes[key] = val;
				}

				// Starting levels
				int levelCount = reader.ReadInt32();
				ValidateCount(levelCount, MaxStartingLevels, "starting levels");
				opt.StartingLevels = new Dictionary<string, int>(levelCount);
				for (int l = 0; l < levelCount; l++)
				{
					string key = reader.ReadString();
					int val = reader.ReadInt32();
					opt.StartingLevels[key] = val;
				}
			}
			else if (opt.EntryType == 1)
			{
				opt.CarePackageId = reader.ReadString();
				opt.Quantity = reader.ReadSingle();
				opt.CarePackageFacadeId = reader.ReadString();
			}
			return opt;
		}

		private static void ValidateCount(int count, int maximum, string field)
		{
			if (count < 0 || count > maximum)
				throw new InvalidDataException($"Invalid immigrant {field} count: {count}");
		}

		internal bool ContentEquals(ImmigrantOptionEntry other)
		{
			if (EntryType != other.EntryType)
				return false;
			if (EntryType == 1)
				return CarePackageId == other.CarePackageId
				       && Quantity.Equals(other.Quantity)
				       && CarePackageFacadeId == other.CarePackageFacadeId;
			if (EntryType != 0)
				return false;

			return Name == other.Name
			       && PersonalityId == other.PersonalityId
			       && StressTraitId == other.StressTraitId
			       && JoyTraitId == other.JoyTraitId
			       && VoiceIdx == other.VoiceIdx
			       && StickerType == other.StickerType
			       && ListsEqual(TraitIds, other.TraitIds)
			       && DictionariesEqual(SkillAptitudes, other.SkillAptitudes)
			       && DictionariesEqual(StartingLevels, other.StartingLevels);
		}

		private static bool ListsEqual<T>(IReadOnlyList<T> left, IReadOnlyList<T> right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Count != right.Count) return false;
			for (int i = 0; i < left.Count; i++)
				if (!EqualityComparer<T>.Default.Equals(left[i], right[i])) return false;
			return true;
		}

		private static bool DictionariesEqual<TKey, TValue>(
			IReadOnlyDictionary<TKey, TValue> left,
			IReadOnlyDictionary<TKey, TValue> right)
		{
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Count != right.Count) return false;
			foreach (KeyValuePair<TKey, TValue> pair in left)
				if (!right.TryGetValue(pair.Key, out TValue value)
				    || !EqualityComparer<TValue>.Default.Equals(pair.Value, value)) return false;
			return true;
		}

		public static ImmigrantOptionEntry FromGameDeliverable(ITelepadDeliverable deliverable)
		{
			using var _ = Profiler.Scope();

			DebugConsole.Log("FromGameDeliverable type: " + (deliverable.GetType()));

			if (deliverable is CarePackageInfo ci)
			{
				return new()
				{
					EntryType = 1,
					CarePackageId = ci.id,
					Quantity = ci.quantity,
					CarePackageFacadeId = ci.facadeID ?? string.Empty
				};
			}
			else if (deliverable is CarePackageContainer.CarePackageInstanceData cpid)
			{
				return new()
				{
					EntryType = 1,
					CarePackageId = cpid.info.id,
					Quantity = cpid.info.quantity,
					CarePackageFacadeId = cpid.facadeID ?? string.Empty
				};
			}
			else if (deliverable is MinionStartingStats ms)
			{
				return new()
				{
					EntryType = 0,
					Name = ms.Name ?? string.Empty,
					PersonalityId = ms.personality.Id ?? string.Empty,
					TraitIds = ms.Traits.Select(t => t.Id).ToList(),
					StressTraitId = ms.stressTrait.Id ?? string.Empty,
					JoyTraitId = ms.joyTrait.Id ?? string.Empty,
					VoiceIdx = ms.voiceIdx ,
					StickerType = ms.stickerType ?? string.Empty,
					SkillAptitudes = ms.skillAptitudes.ToDictionary(kvp => kvp.Key.Id, kvp => kvp.Value),
					StartingLevels = ms.StartingLevels
				};
			}
			return INVALID;
		}
		public static void ListAllFieldValues(object s)
		{
			using var _ = Profiler.Scope();

			Console.WriteLine("Listing all fields of: " + s.ToString());

			foreach (var p in s.GetType().GetFields())
			{
				Console.WriteLine(p + ": " + p.GetValue(s));
			}
		}
		public ITelepadDeliverable ToGameDeliverable()
		{
			using var _ = Profiler.Scope();

			//Console.WriteLine("ToDeliverable: " + EntryType);
			//ListAllFieldValues(this);
			if (EntryType < 0)
				return null;
			if (EntryType == 1)
			{
				return new CarePackageInfo(CarePackageId, Quantity, null, CarePackageFacadeId);
			}
			else if (EntryType == 0)
			{
				Db db = Db.Get();
				var personality = Db.Get().Personalities.TryGet(PersonalityId);
				if (personality == null)
					personality = db.Personalities.resources.First();

				var traits = db.traits;
				var stats = new MinionStartingStats(personality);
				stats.Name = Name;
				stats.voiceIdx = VoiceIdx;
				stats.stickerType = StickerType;
				if (traits.TryGet(StressTraitId) != null)
					stats.stressTrait = traits.TryGet(StressTraitId);
				if (traits.TryGet(JoyTraitId) != null)
					stats.joyTrait = traits.TryGet(JoyTraitId);

				stats.Traits.Clear();
				foreach(var traitId in TraitIds)
				{
					var trait = traits.TryGet(traitId);
					if (trait != null)
						stats.Traits.Add(trait);
				}
				stats.StartingLevels = StartingLevels;
				stats.skillAptitudes.Clear();
				foreach(var kvp in SkillAptitudes)
				{
					var skillGroup = db.SkillGroups.TryGet(kvp.Key);
					if (skillGroup != null)
						stats.skillAptitudes[skillGroup] = kvp.Value;
				}
				return stats;
			}

			return null;
		}

		internal string GetId()
		{
			using var _ = Profiler.Scope();

			if(EntryType == 0)
			{
				return PersonalityId;
			}
			else if (EntryType == 1)
			{
				return CarePackageId;
			}
			return "Invalid";
		}
	}
}
